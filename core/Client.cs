using Godot;
using System;
using System.Collections.Generic;

namespace ArcaneNetworking;

public class Client
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
    internal static bool PacketInvoke(int packetHash, Packet packet)
    {
        if (PacketInvokes.TryGetValue(packetHash, out var handler))
        {
            try
            {
                handler(packet);
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            GD.PrintErr($"[Client] No handler registered for packet type {packet.GetType()}");
            return false;
        }
    }

    internal static void RegisterInvokes()
    {
        // Invokes
        MessageLayer.Active.OnClientConnect += OnClientConnect; // Client is authenticated
        MessageLayer.Active.OnClientDisconnect += OnClientDisconnect; // Client has disconnected
        MessageLayer.Active.OnClientReceive += OnClientReceive; // Client received bytes

        GD.Print("[Client] Internal Invokes Registered");
    }

    internal static void RegisterInternalHandlers()
    {
        // Packet Handlers
        RegisterPacketHandler<HandshakePacket>(OnHandshake);
        RegisterPacketHandler<SpawnNodePacket>(OnSpawn);
        RegisterPacketHandler<ModifyNodePacket>(OnModify);
        RegisterPacketHandler<PongPacket>(OnPong);

        GD.Print("[Client] Internal Packet Handlers Registered");
    }

    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, Channels channel = Channels.Reliable, bool instant = false) where T : Packet
    {
        //GD.Print("[Client] Send: " + packet.GetType());

        serverConnection.Send(packet, channel, instant);
    }

    static void OnClientConnect()
    {
        GD.Print("[Client] Client Has Connected!");

        serverConnection.SendHandshake();

        OnClientConnected?.Invoke();
    }

    static void OnClientDisconnect()
    {
        NetworkedNodes.Clear();

        GD.Print("[Client] Client Has Disconnected..");
        NetworkTime.Reset();
        WorldManager.UnloadOnlineWorld();

        NetworkManager.AmIClient = false;
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

        //GD.Print("[Server] Recieve Length: bytes " + + bytes.Count + " " + batchMsgCount);

        while (reader.RemainingBytes > 0) // Read until end
        {
            // If we can't unpack this packet, just break, 
            // The batch is missaligned so we won't be able to read the rest of the batch
            // This shouldn't EVER happen, but we make sure not to get stuck in an infnite loop
            if (!Unpack(reader)) break;
            
        }
        
        NetworkPool.Recycle(reader);
    }

    static bool Unpack(NetworkReader reader)
    {
        if (NetworkPacker.ReadHeader(reader, out byte type, out int hash)) // Do we have a valid packet header?
        {
            //GD.Print("[Server] Header Valid! " + hash);

            switch (type)
            {
                case 0: // Regular Packet

                    //GD.Print("[Server] Regular Packet ");

                    if (reader.Read(out Packet packet, ArcaneNetworking.PacketTypes[hash])) // Invoke our packet handler
                    {
                        //GD.Print("[Server] Packet Valid! " + hash);
                        if (!PacketInvoke(hash, packet)) return false;
                    }

                    break;


                case 1: // RPC Packet

                    //GD.Print("[Client] RPC Packet");

                    if (reader.Read(out uint callerNetID) && reader.Read(out int callerCompIndex))
                    {
                        if (ArcaneNetworking.RPCMethods.TryGetValue(hash, out var unpack))
                        {
                            // Invoke Weaved Method, rest of the buffer is the arguments for the RPC, pass them to the delegate

                            //GD.PrintErr("[Server] Unpacking RPC..");
                            if (!NetworkedNodes.TryGetValue(callerNetID, out var netNode))
                            { 
                                // Silently return here, this can happen before we are authenticated
                                return false;
                            }
                            try
                            {
                                unpack(reader, netNode.NetworkedComponents[callerCompIndex]);
                            }
                            catch (Exception e)
                            {
                                GD.PrintErr("[Client] RPC  " + unpack.Method.ToString() + " Failed to Execute! " + e.Message);
                                return false;
                            }
                            
                        }
                        else
                        {
                            GD.PrintErr("[Client] RPC Method Hash not found! " + hash);
                            return false;
                        }

                    }
                    else
                    {
                        GD.PrintErr("[Client] Could not read RPC Packet! " + hash);
                        return false;
                    }

                    break;
            }
        }
        else
        {
            GD.PrintErr("[Client] Packet header was invalid on receive!");
            return false;
        }

        return true;
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

        RegisterInternalHandlers();
        
        // Create Connection and store it (even if it isn't valid yet, we will store data about its authentication state)
        serverConnection = new(host, MessageLayer.Active.Port, 0);

        // Setup our MessageLayer to the server
        MessageLayer.Active.StartClient(serverConnection);

        NetworkManager.AmIClient = true;

        GD.Print("[Client] Client Has Started!");
        
        //WorldManager.LoadOnlineWorld(); DO NOT spawn the world yet, wait for handshake as we ARE NOT authenticated here
    }

    public static void Disconnect()
    {
        MessageLayer.Active.StopClient();
    }

    public static void Process(double delta)
    {
        foreach (var netNode in NetworkedNodes)
            netNode.Value._NetworkUpdate(delta);

        foreach (var batcher in serverConnection.Batchers)
        {
            try
            {
                // Send all batched messages
                while (batcher.Value.HasData())
                {
                    if (!serverConnection.isAuthenticated) break;

                    batcher.Value.Flush(out ArraySegment<byte> batch);

                    MessageLayer.Active.SendTo(batch, batcher.Key, serverConnection);
                }
            }
            catch (Exception e)
            {
                GD.PrintErr(e.Message);
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

        GD.Print("[Client] Client Authenticated! ");

        serverConnection.isAuthenticated = true;
        serverConnection.localID = packet.yourConnID;

        // Instantiate world, we are now authenticated so we can safely do this.
        WorldManager.LoadOnlineWorld();

        OnClientAuthenticated?.Invoke();

    }

    static void OnPong(PongPacket packet)
    {
        long t0 = packet.pingSendTick; // client send (monotonic)

        long t1 = packet.pongSendTick; // server receive (Utc)

        long t2 = packet.pongSendTick; // server send (Utc) We have to assume here as the server can't tell us after it sent the packet

        long t3 = NetworkTime.LocalTimeMs(); // client receive (monotonic)

        serverConnection.lastRTT = t3 - t0;

        NetworkTime.AddTimeSample(t0, t1, t2, t3);
        NetworkTime.RTT.AddSample(serverConnection.lastRTT);
        
        serverConnection.Pong(packet.pongSendTick); // Pong the server with the send tick
    
    }
    static void OnSpawn(SpawnNodePacket packet)
    {
        Node spawnedObject = null;
        NetworkedNode netNode = null;
        
        if (NetworkedNodes.ContainsKey(packet.netID))
        {
            GD.Print("[Client] We Already Have Networked Node Or It was In The Scene Tree On The Client: " + packet.netID);
            return; // Check if we already got this packet
        }
        else
        {
            // We are a client only, just spawn it normally
            if (NetworkManager.AmIClientOnly)
            {

                spawnedObject = NetworkManager.manager.NetworkNodeScenes[(int)packet.prefabID].Instantiate<Node>();

                //GD.Print("[Client] Spawning Networked Node: " + packet.netID + " | Prefab ID: " + (int)packet.prefabID);

                // Finds its networked node, it should be a child of this spawned object (should be valid if the server told us)
                netNode = spawnedObject.FindChild<NetworkedNode>();

                // Occupy Data (it will be occupied already if we are a client and server)
                netNode.NetID = packet.netID;
                netNode.PrefabID = packet.prefabID;
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
            WorldManager.ServerWorld.AddChild(spawnedObject);

            // Set Transform
            if (spawnedObject is Node3D spawned3D)
            {
                //GD.Print("[Client] Fixing Position.... " + new Vector3(packet.position[0], packet.position[1], packet.position[2]));
                spawned3D.Position = new Vector3(packet.position[0], packet.position[1], packet.position[2]);
                spawned3D.Quaternion = new Quaternion(packet.rotation[0], packet.rotation[1], packet.rotation[2], packet.rotation[3]);
                spawned3D.Scale = new Vector3(packet.scale[0], packet.scale[1], packet.scale[2]);
            }


            NetworkedNodes.Add(packet.netID, netNode);
        }

        netNode.Enabled = true; // Set Process enabled

        if (netNode.AmIOwner && packet.prefabID == NetworkManager.manager.PlayerPrefabID)
        {
            serverConnection.playerObject = spawnedObject; // Set your player object if its yours
            serverConnection.playerObject.Name = " [Conn ID: " + serverConnection.localID + "]";
        }

        //GD.Print("[Client] Spawned Networked Node: " + netNode.NetID);

        OnClientSpawn?.Invoke(netNode);

    }

    /// <summary>
    /// Used for modifying a net object (disabling, destroying, etc.)
    /// </summary>
    public static void OnModify(ModifyNodePacket packet)
    {

        if (FindNetworkedNode(packet.netID, out NetworkedNode netObject))
        {
            netObject.OwnerID = packet.newOwner;

            // This means its visibility can be changed
            if (netObject.Node.HasMethod("show"))
                netObject.Node.Set("visible", packet.enabled);

            if (packet.destroy)
            {
                netObject._NetworkDestroy();

                // Remove from tree if we want to remove this NetworkedObject (keep reference in list though)
                netObject.Node.QueueFree();
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
