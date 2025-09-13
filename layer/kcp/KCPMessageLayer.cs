using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ArcaneNetworkingSteam;
using Godot;
using kcp2k;

namespace ArcaneNetworking
{
    public partial class KCPMessageLayer : MessageLayer
    {
        [Export] KcpConfig KCPConfig = new();

        KcpClient KCPClient;
        KcpServer KCPServer;

        // Server Functions
        public override void StartServer(bool isHeadless)
        {
            KCPServer = new KcpServer(
                (id) =>
                {
                    // Create NetworkConnection
                    var endpoint = KCPServer.GetClientEndPoint(id);
                    GD.Print("[KCP] Client Connected! " + endpoint.Address.ToString() + " ID: " + id);
                    NetworkConnection incoming = new(endpoint.Address.ToString(), (ushort)endpoint.Port, id);
                    OnServerConnect?.Invoke(incoming);
                },
                (id, bytes, channel) => OnServerReceive?.Invoke(bytes, id), // Discard channel, doesn't really matter when incoming
                (id) => OnServerDisconnect?.Invoke(id),
                (id, error, message) =>
                {
                    GD.PrintErr("[KCP] Server Error: " + message);
                    OnServerError?.Invoke(id, (byte)error, message);
                },
                KCPConfig);

            KCPServer.Start(Port);

            GD.Print("[KCP] Server Started On Port: " + Port);

        }
        public override void PollServer() => KCPServer.Tick();
        public override void StopServer() => KCPServer.Stop();

        // Client Functions
        public override bool StartClient(NetworkConnection host)
        {
            KCPClient = new KcpClient(
                () =>
                {
                    GD.Print("[KCP] Connected To Server!: " + host.GetEndPoint());
                    OnClientConnect?.Invoke();
                },
                (message, channel) => OnClientReceive?.Invoke(message),
                () => OnClientDisconnect?.Invoke(),
                (error, message) =>
                {
                    GD.PrintErr("[KCP] Client Error: " + message);
                    OnClientError?.Invoke((byte)error, message);
                },
                 KCPConfig);

            KCPClient.Connect(host.GetEndPoint(), host.GetPort());

            return true;

        }
        public override void PollClient() => KCPClient.Tick();
        public override void StopClient() => KCPClient.Disconnect();

        public override void ServerDisconnect(NetworkConnection conn) => KCPServer.Disconnect(conn.GetRemoteID());

        public override void SendTo(ArraySegment<byte> bytes, Channels channel, NetworkConnection target)
        {
            if (target == null) GD.PrintErr("[KCP] User Didn't Specify connection to send to!");

            var remoteID = target.GetRemoteID();

            // Run invokes
            if (remoteID != 0) // Send as server
            {
                OnServerSend?.Invoke(bytes, remoteID);

                KCPServer.Send(remoteID, bytes, ToKCPChannel(channel));
            }
            else // Send as client (use authentication to see if we should send the first packet through the raw socket)
            {
                OnClientSend?.Invoke(bytes);

                KCPClient.Send(bytes, ToKCPChannel(channel));
            }

        }
        
        KcpChannel ToKCPChannel(Channels channel)
        {
            return channel switch
            {
                Channels.Reliable => KcpChannel.Reliable,
                Channels.Unreliable => KcpChannel.Unreliable,
                _ => default,
            };
        }
    }
}
