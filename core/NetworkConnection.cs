using Godot;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ArcaneNetworking;

/// <summary>
/// A connection to a remote host that is identified by its connectionID, and its URI
/// </summary>
public partial class NetworkConnection(string endpoint, ushort port, int id, NetworkEncryption encryption = null)
{
    // If this connection is encrypted, hold the data here
    public NetworkEncryption Encryption = encryption;

    // Batcher for batching tiny packets together for efficiency (1 for each channel)
    public readonly Dictionary<Channels, Batcher> Batchers = new()
    {
        { Channels.Reliable, new() },
        { Channels.Unreliable, new() }
    };

    // The ID of this 2 way connection
    readonly int remoteID = id;
    ushort connectionPort = port;
    string connectionEndPoint = endpoint;

    public int localID;

    // The player object that is owned by this connection
    public Node playerObject = null;

    // This will be true if a client is accepted by the server
    public bool isAuthenticated = false;

    public bool isLocalConnection => connectionEndPoint == "127.0.0.1" || connectionEndPoint == "localhost";

    // pingTime is the last time we sent a ping since the game was started
    // The round trip time in ms of the network connection (populates by calling Ping())
    public long lastRTT, lastPingTime;

    public ushort GetPort() => connectionPort;
    public int GetRemoteID() => remoteID;
    internal void SetEndPoint(string endpoint) => connectionEndPoint = endpoint;
    public string GetEndPoint() => connectionEndPoint;
    public T GetEndpointAs<T>() => (T)Convert.ChangeType(connectionEndPoint, typeof(T));

    public void SendRaw(NetworkWriter writer, Channels channel)
    {
        if (!isAuthenticated) return;

        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        Batchers[channel].Push(writer);
    }

    public void SendHandshake(int netID = 0)
    {
        // if (!isAuthenticated) return; // Bypass
        NetworkWriter writer = NetworkPool.GetWriter();

        try
        {
            NetworkPacker.Pack(new HandshakePacket() { netID = netID }, writer);

            MessageLayer.Active.SendTo(writer.ToArraySegment(), Channels.Reliable, this); // Send
        }
        catch (Exception e)
        {
            GD.PrintErr(e.Message);
        }

        NetworkPool.Recycle(writer);
    }
    public void Send<T>(T packet, Channels channel, bool instant = false)
    {

        if (!isAuthenticated) return;

        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        //GD.Print("[NetworkConnection] GetWriter(): " + packet.GetType());

        var writer = NetworkPool.GetWriter();

        try
        {
            NetworkPacker.Pack(packet, writer);

            if (!instant) Batchers[channel].Push(writer);
            else MessageLayer.Active.SendTo(writer.ToArraySegment(), channel, this);

            //GD.Print("[NetworkConnection] Done! " + packet.GetType());
        }
        catch (Exception e)
        {
            GD.PrintErr("Error packing packet for connection: " + remoteID);
            if (e is MessagePackSerializationException) GD.PrintErr("Did you forget to assign [MessagePackObject] to your packet struct?");
            GD.PrintErr(e.Message);
        }

    }

    /// Ping connection
    public void Ping()
    {
        try
        {
            var writer = NetworkPool.GetWriter();

            NetworkPacker.Pack(new PingPacket()
            {
                sendTick = NetworkTime.LocalTimeMs() // Monotonic

            }, writer); // Pack pingpong

            MessageLayer.Active.SendTo(writer.ToArraySegment(), Channels.Reliable, this); // Send instantly

            NetworkPool.Recycle(writer);
        }
        catch (Exception e)
        {
            GD.PrintErr(e.Message);
        }

    }
    /// Send Pong To Connection
    public void Pong(long pingTime)
    {
        try
        {
            var writer = NetworkPool.GetWriter();

            NetworkPacker.Pack(new PongPacket()
            {
                pongSendTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                pingSendTick = pingTime

            }, writer); // Pack pingpong


            MessageLayer.Active.SendTo(writer.ToArraySegment(), Channels.Reliable, this); // Send instantly

            NetworkPool.Recycle(writer);
        }
        catch (Exception e)
        {
            GD.PrintErr(e.Message);
        }

    }
}
