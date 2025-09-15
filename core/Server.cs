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
    static readonly Dictionary<int, Action<Packet, int>> PacketInvokes = [];

    // Used to make sure ids are unique, incremented whenever a network object is registered, never reduces in value
    static uint CurrentNodeID = 0;

    public static long TickMS => StartTimeMS == 0 ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - StartTimeMS; 
    internal static long StartTimeMS;

    // Connections to clients that are connected to this server
    public static Dictionary<int, NetworkConnection> Connections = new Dictionary<int, NetworkConnection>();
    public static NetworkConnection LocalConnection = null;

    /// Dictionary of lists of Networked nodes. Keys are NetworkConnection IDs of clients
    public static Dictionary<uint, NetworkedNode> NetworkedNodes = new Dictionary<uint, NetworkedNode>();

    public static Action<NetworkConnection> OnServerConnect;
    public static Action<NetworkConnection> OnServerAuthenticate;
    public static Action<NetworkConnection> OnServerDisconnect;

    public static Action<NetworkedNode> OnServerSpawn;

    public static NetworkConnection[] GetAllConnections() => [.. Connections.Values];

    public static int GetConnectionCount() => Connections.Count;

    public static bool AllConnectionsAuthenticated => Connections.All(x => x.Value.isAuthenticated == true);

    /// <summary>
    /// Registers a function to handle a packet of type T.
    /// </summary>
    public static void RegisterPacketHandler<T>(Action<T, int> handler) where T : Packet
    {
        // Wrap the handler so it can fit into Action<Packet>
        Type packetType = typeof(T);
        int packetHash = ExtensionMethods.StableHash(packetType.FullName);
        PacketInvokes[packetHash] = (packet, connID) => handler((T)packet, connID);
    }
    /// <summary>
    /// Internal invoke handler to run the unpack method for a packet registered with RegisterPacketHandler<T>()
    /// </summary>
    /// <returns></returns>
    internal static bool PacketInvoke(int packetHash, Packet packet, int fromConnection)
    {
        if (PacketInvokes.TryGetValue(packetHash, out var handler))
        {
            try
            {
                handler(packet, fromConnection);
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            GD.PrintErr($"[Server] No handler registered for packet type {packet.GetType()}");
            return false;
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
        RegisterPacketHandler<PingPongPacket>(OnPingPong);

        GD.Print("[Server] Internal Handlers Registered");
    }

    /// <summary>
    /// Send Logic for simple packets
    /// </summary>
    public static void Send<T>(T packet, NetworkConnection conn, Channels channel = Channels.Reliable, bool instant = false) where T : Packet
    {
        //GD.Print("[Server] Send: " + packet.GetType() + " To: " + conn.GetRemoteID());

        conn.Send(packet, channel, instant);
    }

    /// <summary>
    /// Sends to all clients connected
    /// </summary>
    public static void SendAll<T>(T packet, Channels channel = Channels.Reliable, bool instant = false) where T : Packet
    {
        foreach (var client in Connections)
            Send(packet, client.Value, channel, instant);
    }
    
    public static void SendAllExcept<T>(T packet, Channels channel = Channels.Reliable, bool instant = false, params int[] ignore) where T : Packet
    {
        foreach (var client in Connections)
            if (!ignore.Contains(client.Key))
                Send(packet, client.Value, channel, instant);
    }

    static void OnServerClientConnect(NetworkConnection connection)
    {
        GD.Print("[Server] Client Has Connected! (" + connection.GetEndPoint() + ")");

        // Filter local connection
        if (connection.isLocalConnection) LocalConnection = connection;

        Connections.Add(connection.GetRemoteID(), connection);

        OnServerConnect?.Invoke(connection);

    }
    static void OnServerClientDisconnect(int connID)
    {
        OnServerDisconnect?.Invoke(Connections[connID]);
                
        RemoveClient(Connections[connID], NetworkManager.manager.DisconnectBehavior == DisconectBehavior.Destroy);

        GD.Print("[Server] Client Has Disconnected..");


    }
    static void OnServerReceive(ArraySegment<byte> bytes, int connID)
    {
        var reader = NetworkPool.GetReader(bytes);

        //GD.Print("[Server] Recieve Length: bytes " + + bytes.Count + " " + batchMsgCount);

        while (reader.RemainingBytes > 0) // Read until end
        {
            // If we can't unpack this packet, just disconnect the client, they could be malicous
            if (!Unpack(reader, connID))
            {
                Disconnect(Connections[connID]);
                break;
            }
        }
        
        NetworkPool.Recycle(reader);
    }

    static bool Unpack(NetworkReader reader, int connID)
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
                        if (!PacketInvoke(hash, packet, connID)) return false;
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
                                GD.PrintErr("[Server] RPC pointed to invalid Networked Node! " + hash);
                                return false;
                            }
                            try
                            {
                                unpack(reader, netNode.NetworkedComponents[callerCompIndex]);
                            }
                            catch (Exception e)
                            {
                                GD.PrintErr("[Server] RPC Failed to Execute! " + e.Message);
                                return false;
                            }
                        }
                        else
                        {
                            GD.PrintErr("[Server] RPC Method Hash not found! " + hash);
                            return false;
                        }

                    }
                    else
                    {
                        GD.PrintErr("[Server] Could not read RPC Packet! " + hash);
                        return false;
                    }

                    break;
            }
        }
        else
        {
            GD.PrintErr("[Server] Packet header was invalid on receive!");
            return false;
        }

        return true;
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

        // Set the time the server started
        StartTimeMS = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Intiailize world ONLY if we are headless, we will intialize the world on the client if not
        if (isHeadless) WorldManager.LoadOnlineWorld();

        GD.Print("[Server] Server Has Started!");
    }
    public static void Stop()
    {
        foreach (var client in Connections)
        {
            RemoveClient(client.Value, true);
        }

        Connections.Clear();

        MessageLayer.Active.StopServer();

        NetworkManager.AmIServer = false;

        GD.Print("[Server] Server Has Stopped..");
    }

    public static void Process()
    {

        foreach (var conn in Connections)
        {
            foreach (var batcher in conn.Value.Batchers)
            {
                try
                {
                    // Send all batched messages
                    while (batcher.Value.HasData())
                    {
                        if (!conn.Value.isAuthenticated) break;

                        batcher.Value.Flush(out ArraySegment<byte> batch);
                        MessageLayer.Active.SendTo(batch, batcher.Key, conn.Value);
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr(e.Message);
                }
            }
        }
    }

    ////////////////////////// Internal Packet Callbacks

    static void OnHandshake(HandshakePacket packet, int fromConnection)
    {
        NetworkConnection conn = Connections[fromConnection];

        if (conn.Encryption != null)
        {
            // TODO // ENCRYPTED AUTHENTICATION // TODO //   
        }

        conn.isAuthenticated = true;
        AddClient(conn); // We are authenticated, add them to the game

        OnServerAuthenticate?.Invoke(conn);

        conn.SendHandshake(fromConnection);

        GD.Print("[Server] Client Authenticated!");

    }

    static void OnPingPong(PingPongPacket packet, int fromConnection)
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

    /// <summary>
    /// Spawns a Node on the server and relays to all connections
    /// </summary>
    /// <returns>Node that was spawned</returns>
    public static Node Spawn(uint prefabID, Vector3 position, Basis basis, Vector3 scale, NetworkConnection owner = null)
    {
        Node spawnedObject = NetworkManager.manager.NetworkNodeScenes[(int)prefabID].Instantiate();
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
        netNode.PrefabID = prefabID;
        int netOwner = owner != null ? owner.GetRemoteID() : 0;
        netNode.OwnerID = netOwner;

        netNode.OnOwnerChanged?.Invoke(netOwner, netOwner);

        var quat = basis.GetRotationQuaternion().Normalized();

        if (NetworkManager.AmIHeadless) // Check if we are headless, if we are a server + local client don't spawn yet, client will to keep the flow
        {
            // Now we can safely add to scene tree after values are set
            WorldManager.ServerWorld.AddChild(spawnedObject);

            netNode.Enabled = true; // Set Process enabled

            // Set Transform
            if (spawnedObject is Node3D)
            {
                (spawnedObject as Node3D).Position = position;
                (spawnedObject as Node3D).GlobalBasis = basis;
            }

            netNode._NetworkReady();
        }
        
        NetworkedNodes.Add(netNode.NetID, netNode);

        SpawnNodePacket packet = new()
        {
            netID = netNode.NetID,
            prefabID = prefabID,
            position = [position.X, position.Y, position.Z],
            rotation = [quat.X, quat.Y, quat.Z, quat.W],
            scale = [scale.X, scale.Y, scale.Z],
            ownerID = owner != null ? owner.GetRemoteID() : 0

        };

        //GD.Print("[Server] Spawned Networked Node: " + netNode.NetID);

        // Relay to Clients
        SendAll(packet, Channels.Reliable);

        OnServerSpawn?.Invoke(netNode);

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

        ModifyNodePacket packet = new()
        {
            netID = netNode.NetID,
            enabled = enabled,
            destroy = destroy,
        };

        SendAll(packet, Channels.Reliable);

        if (destroy)
        {
            NetworkedNodes.Remove(netNode.NetID);

            // Only if we are headless, if not then we will destroy when we get to the client
            if (NetworkManager.AmIHeadless)
            {
                netNode._NetworkDestroy();

                netNode.Node.QueueFree();
            } 
        }
    }

    public static void Disconnect(NetworkConnection conn)
    {
        MessageLayer.Active.ServerDisconnect(conn);

        RemoveClient(conn, NetworkManager.manager.DisconnectBehavior == DisconectBehavior.Destroy);
    }

    /// <summary>
    /// Physically adds a client to the server based on a valid NetworkConnection based on the player prefab,
    /// Sends them all active nodes that need to be spawned
    /// </summary>
    public static void AddClient(NetworkConnection connection)
    {
        foreach (var netNode in NetworkedNodes)
        {
            Node3D node3D = netNode.Value.Node is Node3D ? netNode.Value.Node as Node3D : null;

            SpawnNodePacket packet = new()
            {
                netID = netNode.Value.NetID,
                prefabID = netNode.Value.PrefabID,
                position = node3D != null ? [node3D.GlobalPosition.X, node3D.GlobalPosition.Y, node3D.GlobalPosition.Z] : [0, 0, 0],
                rotation = node3D != null ? [node3D.Quaternion.X, node3D.Quaternion.Y, node3D.Quaternion.Z, node3D.Quaternion.W] : [0, 0, 0, 1],
                scale = node3D != null ? [node3D.Scale.X, node3D.Scale.Y, node3D.Scale.Z] : [0, 0, 0],
                ownerID = netNode.Value.OwnerID

            };

            Send(packet, connection, Channels.Reliable);
        }


        if (NetworkManager.manager.PlayerPrefabID != -1)
        {
            // Instantiate the player prefab if not -1
            connection.playerObject = Spawn((uint)NetworkManager.manager.PlayerPrefabID, new Vector3(0f, 5f, 0f), Basis.Identity, Vector3.One, connection);
            connection.playerObject.Name = " [Conn ID: " + connection.GetRemoteID() + "]";
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

    }


}
