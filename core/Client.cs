using Godot;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ArcaneNetworking;

public enum ConnetionStatus
{
    Connected,
    Connecting,
    Disconnected,
    Diconnecting,
}

public partial class Client
{
    internal static readonly Dictionary<int, Action<Packet>> PacketInvokes = [];

    /// List of networked object nodes that have references to their object in them, guids are keys
    internal static readonly Dictionary<uint, NetworkedNode> NetworkedNodes = new Dictionary<uint, NetworkedNode>();

    // Connection to the server
    public static NetworkConnection serverConnection = null;

    public static Action OnClientConnected;
    public static Action OnClientAuthenticated;
    public static Action<NetworkedNode> OnClientSpawn;

    /// <summary>
    /// Registers a function to handle a packet of type T.
    /// </summary>
    public static void RegisterPacketHandler<T>(Action<T> handler) where T : Packet
    {
        // Wrap the handler so it can fit into Action<Packet>
        Type packetType = typeof(T);
        int packetHash = ExtensionMethods.StableHash(packetType.FullName);
        PacketInvokes[packetHash] = (packet) => handler((T)packet);

        GD.Print("[Client] Packet Handler Registered: " + packetHash + " " + packetType.FullName);
    }
    internal static void PacketInvoke(int packetHash, Packet packet)
    {
        if (PacketInvokes.TryGetValue(packetHash, out var handler))
        {
            handler(packet);
        }
        else
        {
            GD.PrintErr("[Client] No handler registered for packet type: " + packet.GetType() + " | Hash: " + packetHash);
        }
    }
    internal static void RegisterInternalHandlers()
    {
        // Invokes
        MessageLayer.Active.OnClientConnect += OnClientConnect; // Client is authenticated
        MessageLayer.Active.OnClientDisconnect += OnClientDisconnect; // Client has disconnected
        MessageLayer.Active.OnClientReceive += OnClientReceive; // Client received bytes

        // Packet Handlers
        RegisterPacketHandler<HandshakePacket>(OnHandshake);
        RegisterPacketHandler<SpawnNodePacket>(OnSpawn);
        RegisterPacketHandler<ModifyNodePacket>(OnModify);
        RegisterPacketHandler<PingPongPacket>(OnPingPong);

        GD.Print("[Client] Internal Handlers Registered");
    }

    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, Channels channel = Channels.Reliable)
    {
        GD.Print("[Client] Send: " + packet.GetType());

        serverConnection.Send(packet, channel);

    }

    static void OnClientConnect()
    {
        GD.Print("[Client] Client Has Connected!");

        serverConnection.SendHandshake();

        OnClientConnected?.Invoke();
    }

    static void OnClientDisconnect()
    {
        GD.Print("[Client] Client Has Disconnected..");
        serverConnection = null;
    }
    
    /// <summary>
    /// <para> Header always has [byte] for type first. (0 = Packet, 1 = RPC, 2 = Stream) </para>
    /// <para> Packet has [byte][int] for hash </para>
    /// <para> RPC Header has [byte][int] for hash </para>
    /// <para> Stream has [byte][data][data][data] etc. </para>
    /// </summary>
    static void OnClientReceive(ArraySegment<byte> bytes)
    {
        var reader = NetworkPool.GetReader(bytes);

        reader.ReadByte(out byte batchMsgCount); // Get batched message count
        
        for (int i = 0; i < batchMsgCount; i++)
        {
            if (NetworkPacker.ReadHeader(reader, out byte type, out int hash)) // Do we have a valid packet header?
            {
                //GD.Print("[Client] Header Valid! " + hash);

                switch (type)
                {
                    case 0: // Regular Packet

                        

                        if (reader.Read(out Packet packet, ArcaneNetworking.PacketTypes[hash])) // Invoke our packet handler
                        {
                            PacketInvoke(hash, packet);
                        }

                        break;


                    case 1: // RPC Packet

                        //GD.Print("[Client] RPC Packet");

                        if (reader.Read(out uint callerNetID) && reader.Read(out int callerCompIndex))
                        {
                            if (ArcaneNetworking.RPCMethods.TryGetValue(hash, out var unpack))
                            {
                                // Invoke Weaved Method, rest of the buffer is the arguments for the RPC, pass them to the delegate

                                //GD.Print("[Client] Unpacking RPC From .." + NetworkedNodes[callerNetID].OwnerID);

                                unpack(reader, NetworkedNodes[callerNetID].NetworkedComponents[callerCompIndex]);
                            }
                            else
                            {
                                GD.PrintErr("[Client] RPC Method Hash not found! " + hash);
                            }

                        }
                        else
                        {
                            GD.PrintErr("[Client] Could not read RPC Packet! " + hash);
                        }

                        break;
                }
            }
            else GD.PrintErr("[Client] Packet header was invalid on receive!");
        }
        
        NetworkPool.Recycle(reader);
    }

    /// <summary>
    /// Connect to the specified "host" and "port".
    /// If port is left as -1, we will not attempt to connect via a port, assumed to be DNS address
    /// The auth string can be used for the server to "Remember" the player if disconnected. On first connect this won't be needed as the server will generate it for you.
    /// </summary>
    public static void Connect(string host)
    {
        if (serverConnection != null)
        {
            GD.PrintErr("[Client] Cannot attempt another Connection right now. Currently Connecting / Connected to a Server!");
            return;
        }

        // Create Connection and store it (even if it isn't valid yet, we will store data about its authentication state)
        serverConnection = new(host, MessageLayer.Active.Port, 0);

        // Setup our MessageLayer to the server
        MessageLayer.Active.StartClient(serverConnection);    
        
        NetworkManager.AmIClient = true;
    }

    public static void Disconnect()
    {
        foreach (var netObject in NetworkedNodes)
        {
            OnModify(new ModifyNodePacket() { netID = netObject.Key, enabled = true, destroy = true });
        }

        MessageLayer.Active.StopClient();
        serverConnection = null;
    }

    public static void Process()
    {
        foreach (var batcher in serverConnection.Batchers)
        {
            // Send all batched messages
            while (batcher.Value.HasData())
            {
                if (!serverConnection.isAuthenticated) break;

                batcher.Value.Flush(out ArraySegment<byte> batch);
                MessageLayer.Active.SendTo(batch, batcher.Key, serverConnection);
            }
                
        }
    }

    ////////////////////////// Internal Packet Callbacks

    static void OnHandshake(HandshakePacket packet)
    {
        if (serverConnection.Encryption != null)
        {
            // TODO // ENCRYPTED AUTHENTICATION // TODO //   
        }

        GD.Print("[Client] Client Authenticated!");

        serverConnection.isAuthenticated = true;
        serverConnection.localID = packet.netID;

        OnClientAuthenticated?.Invoke();
    }
    static void OnPingPong(PingPongPacket packet)
    {
        // Send back if it was a ping
        if (packet.PingPong == 0)
        {
            //GD.Print("[Client] Sending Pong! " + Time.GetTicksMsec());
            serverConnection.Ping(1); // Send Pong if it was a Ping, if it was a Pong
        }
        else // This was a pong, we need to record the RTT
            serverConnection.rtt = Time.GetTicksMsec() - serverConnection.lastPingTime;
    }
    static void OnSpawn(SpawnNodePacket packet)
    {
        Node spawnedObject = null;
        NetworkedNode netNode = null;

        if (NetworkedNodes.ContainsKey(packet.netID))
        {
            GD.Print("[Client] We Already Have Networked Node: " + packet.netID);
            return; // Check if we already got this packet
        }
        else
        {
            // We are a client only, just spawn it normally
            if (NetworkManager.AmIClientOnly)
            {

                spawnedObject = NetworkManager.manager.NetworkObjectPrefabs[(int)packet.prefabID].Instantiate<Node>();

                GD.Print("[Client] Spawning Networked Node: " + packet.netID + " | Prefab ID: " + (int)packet.prefabID);

                // Finds its networked node, it should be a child of this spawned object (should be valid if the server told us)
                netNode = spawnedObject.FindChild<NetworkedNode>();

                // Occupy Data (it will be occupied already if we are a client and server)
                netNode.PrefabID = packet.prefabID;
                netNode.NetID = packet.netID;
                netNode.OwnerID = packet.ownerID;
         
                if (netNode == null)
                {
                    GD.PrintErr("Networked Node: " + packet.netID + " Prefab ID: " + packet.prefabID + " Is Missing A NetworkedNode!!");
                    return;
                }
            }
            // We are the server as well as a client, don't instantiate twice, we can just get the info locally from the server
            else if (!NetworkManager.AmIHeadless)
            {
                // Grab net node from server class
                netNode = Server.NetworkedNodes[packet.netID];
                spawnedObject = netNode.Node;
            }
            
            // Adds to the current loaded world
            NetworkManager.manager.GetTree().Root.AddChild(spawnedObject);

            // Set Transform
            if (spawnedObject is Node3D)
            {
                (spawnedObject as Node3D).Position = new Vector3(packet.position[0], packet.position[1], packet.position[2]);
                (spawnedObject as Node3D).Quaternion = new Quaternion(packet.rotation[0], packet.rotation[1], packet.rotation[2], packet.rotation[3]);
                (spawnedObject as Node3D).Scale = new Vector3(packet.scale[0], packet.scale[1], packet.scale[2]);
            }

            NetworkedNodes.Add(packet.netID, netNode);
        }
        netNode.Enabled = true; // Set Process enabled

        if (netNode.AmIOwner && packet.prefabID == NetworkManager.manager.PlayerPrefabID)
        {
            serverConnection.playerObject = spawnedObject; // Set your player object if its yours
            serverConnection.playerObject.Name = " [Conn ID: " + serverConnection.localID + "]";
        }
        
        OnClientSpawn?.Invoke(netNode);

        GD.Print("[Client] Spawned Networked Node: " + netNode.NetID);

    }

    /// <summary>
    /// Used for modifying a net object (disabling, destroying, etc.)
    /// </summary>
    public static void OnModify(ModifyNodePacket packet)
    {

        if (FindNetworkedNode(packet.netID, out NetworkedNode netObject))
        {
            // This means its visibility can be changed
            if (netObject.Node.HasMethod("show"))
                netObject.Node.Set("visible", packet.enabled);

            if (packet.destroy)
            {
                // Remove from tree if we want to remove this NetworkedObject (keep reference in list though)
                netObject.Node.GetParent().RemoveChild(netObject.Node);
            }
        }
    }

    public static bool FindNetworkedNode(uint netID, out NetworkedNode netObject)
    {
        if (!NetworkedNodes.TryGetValue(netID, out NetworkedNode networkObject))
        {
            GD.PrintErr("Error retrieving CLIENT NetworkedObject:" + netID);
            netObject = null;
            return false;
        }
        netObject = networkObject;
        return true;
    }

  
}
