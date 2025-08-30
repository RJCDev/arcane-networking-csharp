using Godot;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArcaneNetworking;

public partial class Server : Node
{
    // Packet Handling
    static readonly Dictionary<ushort, Action<Packet, uint>> packetHandlers = [];

    // Used to make sure ids are unique, incremented whenever a network object is registered, never reduces in value
    static uint CurrentNodeID = 0;

    // Connections to clients that are connected to this server
    public static Dictionary<uint, NetworkConnection> Connections = new Dictionary<uint, NetworkConnection>();

    /// Dictionary of lists of Networked nodes. Keys are NetworkConnection IDs of clients
    public static Dictionary<uint, NetworkedNode> NetworkedNodes = new Dictionary<uint, NetworkedNode>();

    public static int GetConnectionCount() => Connections.Count;

    public static bool AllConnectionsAuthenticated => Connections.All(x => x.Value.isAuthenticated == true);

     /// <summary>
    /// Registers a function to handle a packet of type T.
    /// </summary>
    public static void RegisterPacketHandler<T>(Action<T, uint> handler) where T : Packet
    {
        // Wrap the handler so it can fit into Action<Packet>
        packetHandlers[NetworkStorage.Singleton.PacketToID(typeof(T))] = (packet, connID) => handler((T)packet, connID);
    }
    static void PacketInvoke(ushort packetID, Packet packet, uint connID)
    {
        if (packetHandlers.TryGetValue(packetID, out var handler))
        {
            handler(packet, connID);
        }
        else
        {
            Console.WriteLine($"[Client] No handler registered for packet type {packet.GetType()}");
        }
    }
    internal static void RegisterInternalHandlers()
    {
        // Invokes
        MessageLayer.Active.OnServerConnect += OnServerConnect;
        MessageLayer.Active.OnServerDisconnect += OnServerDisconnect;
        MessageLayer.Active.OnServerReceive += OnServerReceive;

        // Packet Handlers
        RegisterPacketHandler<SpawnNodePacket>((_, _) => { }); // Server Authorative. No need to receive OnSpawn because we won't process them anyways
        RegisterPacketHandler<ModifyNodePacket>(OnModify);
        RegisterPacketHandler<PingPongPacket>(OnPing);
        RegisterPacketHandler<RPCPacket>(OnRPC);
        RegisterPacketHandler<LoadLevelPacket>(OnLoadLevel);
    }


    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, NetworkConnection conn, Channels channel = Channels.Reliable)
    {
        conn.Send(packet, channel);
        
         GD.Print("[Server] Send: " + packet.GetType());

    }

    /// <summary>
    /// Sends to all clients connected
    /// </summary>
    public static void SendAll<T>(T packet, Channels channel = Channels.Reliable)
    {
        foreach (var client in Connections.Values) Send(packet, client, channel);
    }

    /// <summary>
    /// Sends a packet to all clients in a specified world
    /// </summary>
    public static void SendAllWorld<T>(NetworkedWorld world, T packet, Channels channel = Channels.Reliable)
    {
        foreach (var client in world.ManagedConnections.Values) Send(packet, client, channel);
    }

    static void OnServerConnect(NetworkConnection connection)
    {
        AddClient(connection);

        GD.Print("[Server] Client Has Connected!");
    }
    static void OnServerDisconnect(uint connID)
    {
        RemoveClient(Connections[connID], NetworkManager.manager.DisconnectBehavior == DisconectBehavior.Destroy);

        GD.Print("[Server] Client Has Disconnected..");
    }
    static void OnServerReceive(ArraySegment<byte> bytes, uint connID)
    {
        GD.Print("[Server] Receive Bytes: " + bytes.Array.Length);
        
        var reader = NetworkPool.GetReader(bytes.Array);

        if (reader.Read(out ushort packetHeader)) // Do we have a valid header?
        {
            if (reader.Read(out Packet packet, NetworkStorage.Singleton.IDToPacket(packetHeader))) // Invoke our packet handler
            {
                PacketInvoke(packetHeader, packet, connID);
            }
                

            else GD.PrintErr("Packet was invalid on server receive!");
        }
        else GD.PrintErr("Packet header was invalid on server receive!");
    }


    /// <summary>
    /// Starts the server
    /// if headless, then it will ONLY start the server
    /// else it will start the server and the client, and sort out the connections accordingly
    /// </summary>
    public static void Start()
    {
        MessageLayer.Active.StartServer();

        NetworkManager.AmIServer = true;

        GD.Print("[Server] Server Has Started!");
    }
    public static void Stop()
    {
        foreach (var client in NetworkedNodes)
        {
            RemoveClient(Connections[client.Key], true);
        }

        Connections.Clear();

        MessageLayer.Active.StopServer();

        GD.Print("[Server] Server Has Stopped..");
    }

    ////////////////////////// Internal Packet Callbacks
    static void OnRPC(RPCPacket packet, uint fromConnection)
    {
        NetworkedNode node;

        if (!NetworkedNodes.TryGetValue(packet.CallerNetID, out node))
        {
            GD.PrintErr("Caller Object GUID Is NOT found on the SERVER!");
            return;
        }

        // Obtain the method using the ID
        MethodInfo method = NetworkStorage.Singleton.IDToMethod(packet.CallerMethodID);

        if (!Attribute.IsDefined(method, typeof(MethodRPCAttribute))) { GD.PrintErr("RPC Method IS NOT VALID"); return; } // Sanity Check

        // Run the RPC
        if (method != null)
        {
            method.Invoke(node, packet.Args);
        }
        else
        {
            GD.PrintErr("Packet could not process method from packet! ");
        }

        // Relay to Clients
        foreach (var conn in Connections.Values)

            conn.Send(packet, Channels.Reliable);

    }

    static void OnPing(PingPongPacket packet, uint fromConnection)
    {
        GD.Print("[Server] Recieved Ping!");
        Connections[fromConnection].rtt = Time.GetTicksMsec() - Connections[fromConnection].lastPingTime; // Get the round trip time
        Send(new PingPongPacket(), Connections[fromConnection]); // Resend PingPing
    } 

    static void OnModify(ModifyNodePacket packet, uint fromConnection)
    {

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
    /// Spawns a Node on the server and relays to all connections
    /// </summary>
    /// <returns>Node that was spawned</returns>
    public static Node Spawn(uint prefabID, Vector3 position, Basis basis, Vector3 scale, NetworkConnection owner = null)
    {
        Node spawnedObject = NetworkManager.manager.NetworkObjectPrefabs[(int)prefabID].Instantiate();
        NetworkedNode netNode;

        // Finds its networked node, it should be a child of this spawned object
        netNode = spawnedObject.FindChild<NetworkedNode>();

        if (netNode == null)
        {
            GD.PrintErr("Node ID: " + CurrentNodeID + " Prefab ID: " + prefabID + " Is Missing A Networked Node!!");
            spawnedObject.Free();
            return null;
        }

        NetworkedNodes.Add(CurrentNodeID, netNode);

        // Occupy Data
        netNode.NetID = CurrentNodeID++;

        uint netOwner = owner != null ? owner.GetID() : 0;

        netNode.OwnerID = netOwner;

        netNode.OnOwnerChanged?.Invoke(netOwner, netOwner);

        // Adds the spawned object to the game world if headless, else wait for client
        NetworkManager.manager.GetTree().Root.AddChild(spawnedObject);

        // Set Transform
        if (spawnedObject is Node3D)
        {
            (spawnedObject as Node3D).Position = position;
            (spawnedObject as Node3D).GlobalBasis = basis;
        }

        var quat = basis.GetRotationQuaternion().Normalized();

        SpawnNodePacket packet = new()
        {
            NetID = netNode.NetID,
            prefabID = prefabID,
            position = [position.X, position.Y, position.Z],
            rotation = [quat.X, quat.Y, quat.Z],
            scale = [scale.X, scale.Y, scale.Z]

        };

        // Relay to Clients
        SendAll(packet, Channels.Reliable);

        return spawnedObject;
    }

    /// <summary>
    /// Modifies the state of a Node on the server
    /// </summary>
    public static void Modify(NetworkedNode netNode, bool enabled = true, bool destroy = false)
    {
        // This means its visibility can be changed
        if (netNode.Node.HasMethod("show"))
            netNode.Node.Set("visible", enabled);

        if (destroy)
        {
            NetworkedNodes.Remove(netNode.NetID);

            // Will cleanup components as well
            netNode.Node.QueueFree();
        }

        ModifyNodePacket packet = new()
        {
            NetID = netNode.NetID,
            enabled = enabled,
            destroy = destroy,
        };

        SendAll(packet, Channels.Reliable);

    }

    // <summary>
    /// Physically adds a client to the server based on a valid NetworkConnection based on the player prefab
    /// </summary>
    public static void AddClient(NetworkConnection connection)
    {
        Connections.Add(connection.GetID(), connection);

        if (!NetworkedNodes.ContainsKey(connection.GetID()))
        {
            if (NetworkManager.manager.PlayerPrefabID != -1)
            {
                // Instantiate the player prefab if not -1
                connection.playerObject = Spawn((uint)NetworkManager.manager.PlayerPrefabID, Vector3.Zero, Basis.Identity, Vector3.One, connection);
            }
        }

    }

    public static void RemoveClient(NetworkConnection connection, bool destroyObjects)
    {
        Connections.Remove(connection.GetID());

        foreach (var netObject in NetworkedNodes)
        {
            if (netObject.Value.OwnerID == connection.GetID()) // They own this object
                Modify(netObject.Value, !destroyObjects, destroyObjects);
        }

        // If these objects wont just become unspawns, then completely remove the memory from the list
        if (destroyObjects) NetworkedNodes.Remove(connection.GetID());

    }

    public static void LoadLevel(NetworkConnection conn, int levelID, bool unloadLast = true)
    {
        // Tell this connection that to load the level on their client
        LoadLevelPacket packet = new()
        {
            LevelID = levelID,
            UnloadLast = unloadLast,
        };

        Send(packet, conn);
    }
    

}
