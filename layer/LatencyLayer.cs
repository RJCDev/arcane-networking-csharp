using ArcaneNetworking;
using Godot;
using System;
using System.Threading.Tasks;

public enum Simulate
{
    Server,
    Client,
    Both,
}
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
        Underlying.OnClientConnect    += () => OnClientConnect?.Invoke();
        Underlying.OnClientReceive    += (msg) => OnClientReceive?.Invoke(msg);
        Underlying.OnClientDisconnect += () => OnClientDisconnect?.Invoke();
        Underlying.OnClientSend       += (msg) => OnClientSend?.Invoke(msg);
        Underlying.OnClientError      += (code, msg) => OnClientError?.Invoke(code, msg);

        // Server
        Underlying.OnServerConnect    += (conn) => OnServerConnect?.Invoke(conn);
        Underlying.OnServerReceive    += (conn, msg) => OnServerReceive?.Invoke(conn, msg);
        Underlying.OnServerDisconnect += (conn) => OnServerDisconnect?.Invoke(conn);
        Underlying.OnServerSend       += (conn, msg) => OnServerSend?.Invoke(conn, msg);
        Underlying.OnServerError      += (conn, code, msg) => OnServerError?.Invoke(conn, code, msg);

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
