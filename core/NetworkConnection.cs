using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ArcaneNetworking;

/// <summary>
/// A connection to a remote host that is identified by its connectionID, and its URI
/// </summary>
public partial class NetworkConnection(string endpoint, uint id, NetworkEncryption encryption = null)
{
    // If this connection is encrypted, hold the data here
    public NetworkEncryption Encryption = encryption;

    // The ID of this 2 way connection
    readonly uint connectionID = id;
    readonly string connectionEndPoint = endpoint;

    // The player object that is owned by this connection
    public Node playerObject = null;

    // This will be true if a client is accepted by the server
    public bool isAuthenticated = false;

    public bool isLocalConnection = false;

    // pingTime is the last time we sent a ping since the game was started
    // The round trip time in ms of the network connection (populates by calling Ping())
    public ulong lastPingTime, rtt;

    public uint GetID() => connectionID;
    public string GetEndPoint() => connectionEndPoint;
    public T GetEndpointAs<T>() => (T)Convert.ChangeType(connectionEndPoint, typeof(T));

    public void Send<T>(T packet, Channels channel)
    {
        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        var NetworkWriter = NetworkPool.GetWriter();

        try
        {
            NetworkPacker.Pack(packet, NetworkWriter);

            MessageLayer.Active.Send(NetworkWriter.ToArraySegment(), channel);
        }
        catch (Exception e)
        {
            GD.PrintErr("Error sending packet to connection: " + connectionID + " " + e.Message);
        }
        finally
        {
            NetworkPool.Recycle(NetworkWriter);
        }


    }

    /// Ping connection
    public void Ping()
    {
        lastPingTime = Time.GetTicksMsec(); // Payload = [0] is ping, Payload = [1] is pong
        Send(new PingPongPacket() { }, Channels.Reliable);
    
    }

    internal static NetworkConnection[] GetAllConnections()
    {
        NetworkConnection[] allConnections = [.. Server.Connections.Values];

        if (NetworkManager.AmIClient) // Client
        {
            return [Client.serverConnection];
        }
        else if (NetworkManager.AmIHeadless) // Headless
        {
            return allConnections;
        }
        else // Server + Client
        {
            // Add clientconnection to end of servers
            Array.Resize(ref allConnections, allConnections.Length + 1);
            allConnections[allConnections.Length - 1] = Client.serverConnection;

            return allConnections;
        }
    }
}
