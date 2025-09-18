
using Godot;
using System;

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
    [Export] MessageLayer Underlying;

    bool _eventsHooked = false;
    RandomNumberGenerator rand = new();
    [Export(PropertyHint.Range, "0, 1, 0.05")] float ClientDropRate = 0;
    [Export(PropertyHint.Range, "0, 1, 0.05")] float ServerDropRate = 0;

    // Link to underlying layer
    void HookEvents()
    {
        if (!_eventsHooked)
         _eventsHooked = true;
        
        // Client
        Underlying.OnClientConnect    = OnClientConnect;
        Underlying.OnClientReceive    = OnClientReceive;
        Underlying.OnClientDisconnect = OnClientDisconnect;
        Underlying.OnClientSend       = OnClientSend;
        Underlying.OnClientError      = OnClientError;

        // Server
        Underlying.OnServerConnect    = OnServerConnect;
        Underlying.OnServerReceive    = OnServerReceive;
        Underlying.OnServerDisconnect = OnServerDisconnect;
        Underlying.OnServerSend       = OnServerSend;
        Underlying.OnServerError      = OnServerError;

    }

    public override void PollClient() => Underlying.PollClient();

    public override void PollServer() => Underlying.PollServer();

    public override void SendTo(ArraySegment<byte> bytes, Channels channel, NetworkConnection conn)
    {
        // Sending as client
        if (NetworkManager.AmIClient && conn == Client.serverConnection)
        {
            if (rand.Randf() < ClientDropRate && conn.isAuthenticated && channel != Channels.Reliable)
            {
                GD.PrintErr("[Client Simulation] Dropping Packet!");
            }
            else Underlying.SendTo(bytes, channel, conn);
        }
        // Sending as Server
        if (NetworkManager.AmIServer && Server.Connections.ContainsKey(conn.GetRemoteID()))
        {
            if (rand.Randf() < ServerDropRate && conn.isAuthenticated && channel != Channels.Reliable)
            {
                GD.PrintErr("[Server Simulation] Dropping Packet!");
            }
            else Underlying.SendTo(bytes, channel, conn);
        }
    } 

    public override void ServerDisconnect(NetworkConnection conn) => Underlying.ServerDisconnect(conn);

    public override bool StartClient(NetworkConnection host)
    {
        HookEvents();
        return Underlying.StartClient(host);
    }

    public override void StartServer(bool isHeadless)
    {
        HookEvents();
        Underlying.StartServer(isHeadless);
    }

    public override void StopClient() => Underlying.StopClient();

    public override void StopServer() => Underlying.StopServer();




}
