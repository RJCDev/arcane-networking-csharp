using Godot;
using MessagePack;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ArcaneNetworking;

public partial class Server : Node
{
    // Packet Handling
    static readonly Dictionary<int, Action<Packet, uint>> PacketInvokes = [];

    // Used to make sure ids are unique, incremented whenever a network object is registered, never reduces in value
    static uint CurrentNodeID = 0;

    // Connections to clients that are connected to this server
    public static Dictionary<uint, NetworkConnection> Connections = new Dictionary<uint, NetworkConnection>();

    /// Dictionary of lists of Networked nodes. Keys are NetworkConnection IDs of clients
    public static Dictionary<uint, NetworkedNode> NetworkedNodes = new Dictionary<uint, NetworkedNode>();

    public static Action<NetworkConnection> OnServerConnect;
    public static Action<NetworkConnection> OnServerAuthenticate;
    public static Action<NetworkConnection> OnServerDisconnect;

    public static Action<NetworkedNode> OnServerSpawn;

    public static NetworkConnection[] GetAllConnections() => [.. Connections.Values];
    public static uint[] GetConnsExcluding(params uint[] connectionIds) => [.. Connections.Keys.Except(connectionIds)];

    public static int GetConnectionCount() => Connections.Count;

    public static bool AllConnectionsAuthenticated => Connections.All(x => x.Value.isAuthenticated == true);

    /// <summary>
    /// Registers a function to handle a packet of type T.
    /// </summary>
    public static void RegisterPacketHandler<T>(Action<T, uint> handler) where T : Packet
    {
        // Wrap the handler so it can fit into Action<Packet>
        Type packetType = typeof(T);
        int packetHash = ExtensionMethods.StableHash(packetType.FullName);
        PacketInvokes[packetHash] = (packet, connID) => handler((T)packet, connID);
    }
    internal static void PacketInvoke(int packetHash, Packet packet, uint fromConnection)
    {
        if (PacketInvokes.TryGetValue(packetHash, out var handler))
        {
            handler(packet, fromConnection);
        }
        else
        {
            GD.PrintErr($"[Server] No handler registered for packet type {packet.GetType()}");
        }
    }
    internal static void RegisterInternalHandlers()
    {
        // Invokes
        MessageLayer.Active.OnServerConnect += OnServerClientConnect;
        MessageLayer.Active.OnServerDisconnect += OnServerClientDisconnect;
        MessageLayer.Active.OnServerReceive += OnServerReceive;

        // Packet Handlers
        RegisterPacketHandler<HandshakePacket>(OnHandshake);
        RegisterPacketHandler<SpawnNodePacket>((_, _) => { }); // Server Authorative. No need to receive OnSpawn because we won't process them anyways
        RegisterPacketHandler<ModifyNodePacket>(OnModify);
        RegisterPacketHandler<PingPongPacket>(OnPingPong);

        GD.Print("[Server] Internal Handlers Registered");
    }


    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, NetworkConnection conn, Channels channel = Channels.Reliable)
    {
        GD.Print("[Server] Send: " + packet.GetType() + " To: " + conn.GetRemoteID());

        conn.Send(packet, channel);
    }

    /// <summary>
    /// Sends to all clients connected
    /// </summary>
    public static void SendAll<T>(T packet, Channels channel = Channels.Reliable)
    {
        foreach (var client in Connections.Values) Send(packet, client, channel);
    }

    static void OnServerClientConnect(NetworkConnection connection)
    {
        GD.Print("[Server] Client Has Connected! (" + connection.GetEndPoint() + ")");

        Connections.Add(connection.GetRemoteID(), connection);

        OnServerConnect?.Invoke(connection);

    }
    static void OnServerClientDisconnect(uint connID)
    {
        OnServerDisconnect?.Invoke(Connections[connID]);
                
        RemoveClient(Connections[connID], NetworkManager.manager.DisconnectBehavior == DisconectBehavior.Destroy);

        GD.Print("[Server] Client Has Disconnected..");


    }
    static void OnServerReceive(ArraySegment<byte> bytes, uint connID)
    {
       //GD.Print("[Server] Recieve Length: " + bytes.Array.Length);

        var reader = NetworkPool.GetReader(bytes);

        reader.Read(out byte batchMsgCount); // Get batched message count
        
        for (int i = 0; i < batchMsgCount; i++)
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
                            PacketInvoke(hash, packet, connID);
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

                                unpack(reader, NetworkedNodes[callerNetID].NetworkedComponents[callerCompIndex]);
                            }
                            else
                            {
                                GD.PrintErr("[Server] RPC Method Hash not found! " + hash);
                            }

                        }
                        else
                        {
                            GD.PrintErr("[Server] Could not read RPC Packet! " + hash);
                        }

                        break;
                }
            }
            else GD.PrintErr("[Server] Packet header was invalid on receive!");
        }
        
        NetworkPool.Recycle(reader);
    }

    /// <summary>
    /// Starts the server
    /// if headless, then it will ONLY start the server
    /// else it will start the server and the client, and sort out the connections accordingly
    /// </summary>
    public static void Start(bool isHeadless)
    {
        if (NetworkManager.AmIServer == true)
        {
            GD.PrintErr("[Server] Cannot start another server!! Server is currently Active!");
            return;
        }

        MessageLayer.Active.StartServer(isHeadless);

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

    public static void Process()
    {
        foreach (var conn in Connections)
        {
            foreach (var batcher in conn.Value.Batchers)
            {
                // Send all batched messages
                while (batcher.Value.HasData())
                {
                    batcher.Value.Flush(out ArraySegment<byte> batch);
                    MessageLayer.Active.SendTo(batch, batcher.Key, conn.Value);
                }
            }
        }
    }


    ////////////////////////// Internal Packet Callbacks

    static void OnHandshake(HandshakePacket packet, uint fromConnection)
    {
        NetworkConnection conn = Connections[fromConnection];

        if (conn.Encryption != null)
        {
            // TODO // ENCRYPTED AUTHENTICATION // TODO //   
        }

        conn.isAuthenticated = true;

        OnServerAuthenticate?.Invoke(conn);

        Send(new HandshakePacket() { ID = fromConnection }, conn, Channels.Reliable);

        GD.Print("[Server] Client Authenticated!");

        AddClient(conn); // We are authenticated, add them to the game

    }

    static void OnPingPong(PingPongPacket packet, uint fromConnection)
    {
        // Send back if it was a ping
        if (packet.PingPong == 0)
        {
            //GD.Print("[Server] Sending Pong! " + Time.GetTicksMsec());
            Connections[fromConnection].Ping(1); // Send Pong if it was a Ping, if it was a Pong
        }
        else // This was a pong, we need to record the RTT
            Connections[fromConnection].rtt = Time.GetTicksMsec() - Connections[fromConnection].lastPingTime;
    } 

    static void OnModify(ModifyNodePacket packet, uint fromConnection)
    {

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

        // Occupy Data
        netNode.NetID = CurrentNodeID++;
        uint netOwner = owner != null ? owner.GetRemoteID() : 0;
        netNode.OwnerID = netOwner;
        netNode.OnOwnerChanged?.Invoke(netOwner, netOwner);

        var quat = basis.GetRotationQuaternion().Normalized();

        if (NetworkManager.AmIHeadless) // Check if we are headless, if we are a server + local client don't spawn yet, client will to keep the flow
        {
            // Now we can safely add to scene tree after values are set
            NetworkManager.manager.GetTree().Root.AddChild(spawnedObject);
            netNode.Enabled = true; // Set Process enabled

            // Set Transform
            if (spawnedObject is Node3D)
            {
                (spawnedObject as Node3D).Position = position;
                (spawnedObject as Node3D).GlobalBasis = basis;
            }      
        }
        
        NetworkedNodes.Add(netNode.NetID, netNode);

        SpawnNodePacket packet = new()
        {
            NetID = netNode.NetID,
            prefabID = prefabID,
            position = [position.X, position.Y, position.Z],
            rotation = [quat.X, quat.Y, quat.Z, quat.W],
            scale = [scale.X, scale.Y, scale.Z],
            ownerID = owner != null ? owner.GetRemoteID() : 0

        };


        GD.Print("[Server] Spawned Networked Node: " + netNode.NetID);

        OnServerSpawn?.Invoke(netNode);

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

    /// <summary>
    /// Physically adds a client to the server based on a valid NetworkConnection based on the player prefab,
    /// Sends them all active nodes that need to be spawned
    /// </summary>
    public static void AddClient(NetworkConnection connection)
    {
        if (!NetworkedNodes.ContainsKey(connection.GetRemoteID()))
        {
            // Tell them to spawn all net objects that we currenlty have on their client
            foreach (var node in NetworkedNodes)
            {
                SpawnNodePacket packet = new()
                {
                    NetID = node.Value.NetID,
                    prefabID = node.Value.PrefabID,
                    position = [0, 0, 0],
                    rotation = [0, 0, 0, 1],
                    scale = [1, 1, 1],
                    ownerID = node.Value.OwnerID

                };

                Send(packet, connection, Channels.Reliable);
            }


            if (NetworkManager.manager.PlayerPrefabID != -1)
            {
                // Instantiate the player prefab if not -1
                connection.playerObject = Spawn((uint)NetworkManager.manager.PlayerPrefabID, Vector3.Zero, Basis.Identity, Vector3.One, connection);
                connection.playerObject.Name = " [Conn ID: " + connection.GetRemoteID() + "]";
            }
        }
    }

    public static void RemoveClient(NetworkConnection connection, bool destroyObjects)
    {
        Connections.Remove(connection.GetRemoteID());

        foreach (var netObject in NetworkedNodes)
        {
            if (netObject.Value.OwnerID == connection.GetRemoteID()) // They own this object
                Modify(netObject.Value, !destroyObjects, destroyObjects);
        }

        // If these objects wont just become unspawns, then completely remove the memory from the list
        if (destroyObjects) NetworkedNodes.Remove(connection.GetRemoteID());

    }


}
