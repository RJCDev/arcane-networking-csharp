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
    string connectionEndPoint = endpoint;

    // The player object that is owned by this connection
    public Node playerObject = null;

    // This will be true if a client is accepted by the server
    public bool isAuthenticated = false;

    public bool isLocalConnection = false;

    // pingTime is the last time we sent a ping since the game was started
    // The round trip time in ms of the network connection (populates by calling Ping())
    public ulong lastPingTime, rtt;

    public uint GetID() => connectionID;
    internal void SetEndPoint(string endpoint) => connectionEndPoint = endpoint;
    public string GetEndPoint() => connectionEndPoint;
    public T GetEndpointAs<T>() => (T)Convert.ChangeType(connectionEndPoint, typeof(T));

    public void Send<T>(T packet, Channels channel, bool instant = false)
    {
        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        //GD.Print("[NetworkConnection] GetWriter(): " + packet.GetType());

        var NetworkWriter = NetworkPool.GetWriter();

        try
        {
            //GD.Print("[NetworkConnection] Pack.. " + packet.GetType());

            NetworkPacker.Pack(packet, NetworkWriter);

            //GD.Print("[NetworkConnection] Enqueue.. " + packet.GetType());

            if (instant)
            {
                MessageLayer.Active.SendToConnections(NetworkWriter.ToArraySegment(), Channels.Reliable, connectionID);
            }
            else
            {
                MessageHandler.Enqueue(channel, NetworkWriter, this);
            }
            

            //GD.Print("[NetworkConnection] Done! " + packet.GetType());
        }
        catch (Exception e)
        {
            GD.PrintErr("Error sending packet to connection: " + connectionID + " " + e.Message);
        }


    }

    /// Ping connection
    public void Ping(byte pingOrPong)
    {
        lastPingTime = Time.GetTicksMsec();
        Send(new PingPongPacket() { PingPong = pingOrPong, }, Channels.Reliable, true);
    
    }
}
