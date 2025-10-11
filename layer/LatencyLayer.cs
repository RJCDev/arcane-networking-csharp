
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ArcaneNetworking;

public enum Simulate
{
    Server,
    Client,
    Both,
}
[GlobalClass]
public partial class LatencyLayer : MessageLayer
{
    struct SimPacket
    {
        public byte[] message;
        public long sendTime;
        public Channels channel;
        public NetworkConnection target;
    }

    [Export] MessageLayer _underlying;

    bool _eventsHooked = false;
    RandomNumberGenerator rand = new();
    [Export(PropertyHint.Range, "0, 10, 0.05")] float ClientMaxSendDelaySeconds = 0;
    [Export(PropertyHint.Range, "0, 1, 0.05")] float ClientDropRate = 0;
    [Export(PropertyHint.Range, "0, 10, 0.05")] float ServerMaxSendDelaySeconds = 0;
    [Export(PropertyHint.Range, "0, 1, 0.05")] float ServerDropRate = 0;

    List<SimPacket> clientQueue = [];
    List<SimPacket> serverQueue = [];

    void SetClientDelay(float delay) => ClientMaxSendDelaySeconds = delay;
    void SetClientDropRate(float rate) => ClientDropRate = rate;

    // Link to underlying layer
    void HookEvents()
    {
        if (!_eventsHooked)
            _eventsHooked = true;
        
        // Client
        _underlying.OnClientConnect    = OnClientConnect;
        _underlying.OnClientReceive    = OnClientReceive;
        _underlying.OnClientDisconnect = OnClientDisconnect;
        _underlying.OnClientSend       = OnClientSend;
        _underlying.OnClientError      = OnClientError;

        // Server
        _underlying.OnServerConnect    = OnServerConnect;
        _underlying.OnServerReceive    = OnServerReceive;
        _underlying.OnServerDisconnect = OnServerDisconnect;
        _underlying.OnServerSend       = OnServerSend;
        _underlying.OnServerError      = OnServerError;

    }

    public override void PollClient()
    {  
        _underlying.PollClient();

        // Check if messages are ready to send, send after polling
        for (int i = clientQueue.Count - 1; i >= 0; i--)
        {
            SimPacket packet = clientQueue[i];
            
            if (NetworkTime.TickMS >= packet.sendTime)
            {
                _underlying.SendTo(packet.message, packet.channel, packet.target);

                clientQueue.Remove(packet);
            }
        }

    }
    public override void PollServer()
    {
        _underlying.PollServer();

        // Check if messages are ready to send, send after polling
        for (int i = serverQueue.Count - 1; i >= 0; i--)
        {
            SimPacket packet = serverQueue[i];

            if (NetworkTime.TickMS >= packet.sendTime)
            {
                _underlying.SendTo(packet.message, packet.channel, packet.target);
                
                serverQueue.Remove(packet);
            }
            
        }
    }
    public override void SendTo(ArraySegment<byte> bytes, Channels channel, NetworkConnection conn)
    {
        if (!conn.isAuthenticated) // Just send instantly if they arent authenticated yet.
        {
            _underlying.SendTo(bytes, channel, conn);
            return;
        }

        // Sending as client
        if (NetworkManager.AmIClient && conn == Client.serverConnection)
        {
            //GD.Print(rand.Randf() + " " + ClientDropRate + " " + channel);
            if (rand.Randf() < ClientDropRate && channel == Channels.Unreliable)
            {
                GD.PrintErr("[Client Simulation] Dropping Packet!");
                return;
            }
            else
            {
                // We need to allocate here to make sure this arraysegment doesn't get overwritten
                clientQueue.Add(
                    new()
                    {
                        message = [.. bytes],
                        channel = channel,
                        sendTime = NetworkTime.TickMS + (int)(ClientMaxSendDelaySeconds * 1000.0f),
                        target = conn
                    });
            }

        }
        // Sending as Server
        if (NetworkManager.AmIServer && Server.Connections.ContainsKey(conn.GetRemoteID()))
        {
            if (rand.Randf() < ServerDropRate && channel == Channels.Unreliable)
            {
                GD.PrintErr("[Server Simulation] Dropping Packet!");
                return;
            }
            else
            {
                // We need to allocate here to make sure this arraysegment doesn't get overwritten
                serverQueue.Add(
                    new()
                    {
                        message = [.. bytes],
                        channel = channel,
                        sendTime = NetworkTime.TickMS + (int)(ServerMaxSendDelaySeconds * 1000.0f),
                        target = conn
                    });
            }            
        }
    } 

    public override void ServerDisconnect(NetworkConnection conn) => _underlying.ServerDisconnect(conn);

    public override bool StartClient(NetworkConnection host)
    {
        HookEvents();
        return _underlying.StartClient(host);
    }

    public override void StartServer(bool isHeadless)
    {
        HookEvents();
        _underlying.StartServer(isHeadless);
    }

    public override void StopClient() => _underlying.StopClient();

    public override void StopServer() => _underlying.StopServer();




}
