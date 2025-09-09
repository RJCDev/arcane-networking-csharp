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
public partial class NetworkConnection(string endpoint, uint id, NetworkEncryption encryption = null)
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
    readonly uint remoteID = id;
    string connectionEndPoint = endpoint;

    public uint localID;

    // The player object that is owned by this connection
    public Node playerObject = null;

    // This will be true if a client is accepted by the server
    public bool isAuthenticated = false;

    public bool isLocalConnection = false;

    // pingTime is the last time we sent a ping since the game was started
    // The round trip time in ms of the network connection (populates by calling Ping())
    public ulong lastPingTime, rtt;

    public uint GetRemoteID() => remoteID;
    internal void SetEndPoint(string endpoint) => connectionEndPoint = endpoint;
    public string GetEndPoint() => connectionEndPoint;
    public T GetEndpointAs<T>() => (T)Convert.ChangeType(connectionEndPoint, typeof(T));

    public void SendRaw(NetworkWriter writer, Channels channel)
    {
        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        try
        {
            Batchers[channel].Push(writer);
        }
        catch (Exception e)
        {
            GD.PrintErr("Error writing raw bytes to connection: " + remoteID);
            GD.PrintErr(e.Message);
        }
    }

    public void Send<T>(T packet, Channels channel)
    {
        bool isEncrypted = Encryption != null; // check if we need to encrypt this packet

        //GD.Print("[NetworkConnection] GetWriter(): " + packet.GetType());

        var writer = NetworkPool.GetWriter();

        try
        {
            NetworkPacker.Pack(packet, writer);
            Batchers[channel].Push(writer);
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
    public void Ping(byte pingOrPong)
    {
        var writer = NetworkPool.GetWriter();
        writer.WriteBytes(new ArraySegment<byte>([1])); // Include msg count header
        NetworkPacker.Pack(new PingPongPacket() { PingPong = pingOrPong }, writer); // Pack pingpong

        lastPingTime = Time.GetTicksMsec();
        MessageLayer.Active.SendTo(writer.ToArraySegment(), Channels.Reliable, this); // Send instantly

        NetworkPool.Recycle(writer);

    }
}
