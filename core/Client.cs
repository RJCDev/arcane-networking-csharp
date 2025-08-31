using Godot;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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
    // Packet Handling
    static readonly Dictionary<ushort, Action<Packet, uint>> packetHandlers = [];

    /// List of networked object nodes that have references to their object in them, guids are keys
    public static Dictionary<uint, NetworkedNode> NetworkedNodes = new Dictionary<uint, NetworkedNode>();

    // Connection to the server
    public static NetworkConnection serverConnection = null;

    /// <summary>
    /// Registers a function to handle a packet of type T.
    /// </summary>
    public static void RegisterPacketHandler<T>(Action<T, uint> handler) where T : Packet
    {
        // Wrap the handler so it can fit into Action<Packet>
        packetHandlers[NetworkStorage.Singleton.PacketToID(typeof(T))] = (packet, connID) => handler((T)packet, connID);
    }
    internal static void PacketInvoke(ushort funcByte, Packet packet, uint fromConnection)
    {
        if (packetHandlers.TryGetValue(funcByte, out var handler))
        {
            handler(packet, fromConnection);
        }
        else
        {
            GD.PrintErr($"[Client] No handler registered for packet type {packet.GetType()}");
        }
    }
    internal static void RegisterInternalHandlers()
    {
        // Invokes
        MessageLayer.Active.OnClientConnect += OnClientConnected; // Client is authenticated
        MessageLayer.Active.OnClientDisconnect += OnClientDisconnect; // Client has disconnected
        MessageLayer.Active.OnClientReceive += OnClientReceive; // Client received bytes

        // Packet Handlers
        RegisterPacketHandler<SpawnNodePacket>(OnSpawn);
        RegisterPacketHandler<ModifyNodePacket>(OnModify);
        RegisterPacketHandler<PingPongPacket>(OnPingPong);
        RegisterPacketHandler<RPCPacket>(OnRPC);
        RegisterPacketHandler<LoadLevelPacket>(OnLoadLevel);
    }

    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, Channels channel = Channels.Reliable)
    {
        serverConnection.Send(packet, channel);

        //GD.Print("[Client] Send: " + packet.GetType());
    }

    static void OnClientConnected()
    {
        NetworkManager.AmIClient = true;
        serverConnection.isAuthenticated = true;

        GD.Print("[Client] Client Has Connected!");
    }
    static void OnClientDisconnect()
    {
        serverConnection = null;
        
        GD.Print("[Client] Client Has Disconnected..");
    }
    static void OnClientReceive(ArraySegment<byte> bytes)
    {
        //GD.Print("[Client] Receive Bytes: " + bytes.Array.Length);

        var reader = NetworkPool.GetReader(bytes.Array);

        //GD.Print("[Client] Recieve Length: " + bytes.Array.Length);
        
        if (reader.Read(out ushort packetHeader)) // Do we have a valid header?
        {
            if (reader.Read(out Packet packet, NetworkStorage.Singleton.IDToPacket(packetHeader))) // Invoke our packet handler
            {
                PacketInvoke(packetHeader, packet, 0);
            }

            else GD.PrintErr("Packet was invalid on client receive!");
        }
        else GD.PrintErr("Packet header was invalid on client receive!");

        NetworkPool.Recycle(reader);
    }

    /// <summary>
    /// Connect to the specified "host" and "port".
    /// If port is left as -1, we will not attempt to connect via a port, assumed to be DNS address
    /// The auth string can be used for the server to "Remember" the player if disconnected. On first connect this won't be needed as the server will generate it for you.
    /// </summary>
    public static void Connect(string host, int port = -1)
    {
        if (serverConnection != null)
        {
            GD.PrintErr("Cannot attempt another connection right now. Currently connecting / connected to server");
            return;
        }

        // Create Connection and store it (even if it isn't valid yet, we will store data about its authentication state)
        serverConnection = new(port == -1 ? host : host + ":" + port, 0);
        
        // Setup our MessageLayer to the server
        MessageLayer.Active.StartClient(serverConnection);    
    }

    public static void Disconnect()
    {
        foreach (var netObject in NetworkedNodes)
        {
            OnModify(new ModifyNodePacket() { NetID = netObject.Key, enabled = true, destroy = true }, 0);
        }

        MessageLayer.Active.StopClient();
        serverConnection = null;
    }
  
    ////////////////////////// Internal Packet Callbacks
    static void OnRPC(RPCPacket packet, uint fromConnection)
    {
        NetworkedNode node;
        
        // Get the NetworkedNode
        if (!NetworkedNodes.TryGetValue(packet.CallerNetID, out node))
        {
            GD.PrintErr("Caller Object GUID Is NOT found on the CLIENT!");
            return;
        }

        // Obtain the method using the ID
        MethodInfo method = NetworkStorage.Singleton.IDToMethod(packet.CallerMethodID);

        if (!Attribute.IsDefined(method, typeof(MethodRPCAttribute))) { GD.PrintErr("RPC Method IS NOT VALID"); return; } // Sanity Check

        // Run the RPC
        try
        {
            dynamic[] args = new dynamic[packet.Args.Count];
            // Attempt to parse args
            for (int i = 0; i < packet.Args.Count; i++) args[i] = MessagePackSerializer.Deserialize<dynamic>(packet.Args[i]);

            method.Invoke(node.NetworkedComponents[packet.CallerCompIndex], args);
        }
        catch (Exception e)
        {
            GD.PrintErr("[Client] Packet could not process method from packet! ");
            GD.PrintErr(e);
        }


        // If we got this far the packet is successful. If we are the server, check if we should relay to other clients
    }
    static void OnPingPong(PingPongPacket packet, uint fromConnection)
    {
        // Send back if it was a ping
        if (packet.PingPong == 0)
        {
            GD.Print("[Client] Sending Pong! " + Time.GetTicksMsec());
            serverConnection.Ping(true); // Send Pong if it was a Ping, if it was a Pong
        }
        else // This was a pong, we need to record the RTT
            serverConnection.rtt = Time.GetTicksMsec() - serverConnection.lastPingTime; 
    }
    static void OnSpawn(SpawnNodePacket packet, uint fromConnection)
        {
            Node spawnedObject;
            NetworkedNode netNode = null;

            //object already exists
            if (NetworkedNodes.ContainsKey(packet.NetID)) return;

            else
            {
                // We are not the server, instantiate
                if (!NetworkManager.AmIServer)
                {
                    spawnedObject = NetworkManager.manager.NetworkObjectPrefabs[(int)packet.prefabID].Instantiate<Node>();

                    if (netNode == null)
                    {
                        GD.PrintErr("Networked Node: " + packet.NetID + " Prefab ID: " + packet.prefabID + " Is Missing A NetworkedNode!!");
                        return;
                    }
                    // Finds its networked node, it should be a child of this spawned object
                    netNode = spawnedObject.FindChild<NetworkedNode>();

                    // Adds child to the root of the game world
                    NetworkManager.manager.GetTree().Root.AddChild(spawnedObject);
                }

                // We are the server as well as a client, don't instantiate twice, we can just get the info locally from the server
                else
                {
                    netNode = Server.NetworkedNodes[packet.NetID];
                }

                NetworkedNodes.Add(packet.NetID, netNode);
            }

            // Occupy Data
            netNode.NetID = packet.NetID;

        }

    static void OnLoadLevel(LoadLevelPacket packet, uint fromConnection)
    {
        try
        {
            NetworkManager.manager.WorldManager.LoadWorld(packet.LevelID, packet.UnloadLast);
        }
        catch (Exception e)
        {
            GD.PrintErr("Load World Packet Failed To Process");
            GD.PrintErr(e);
        }
    }

    /// <summary>
    /// Used for modifying a net object (disabling, destroying, etc.)
    /// </summary>
    public static void OnModify(ModifyNodePacket packet, uint fromConnection)
    {
        if (FindNetworkedNode(packet.NetID, out NetworkedNode netObject))
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
