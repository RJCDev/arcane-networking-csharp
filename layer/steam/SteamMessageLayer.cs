using ArcaneNetworking;
using Godot;
using MessagePack;
using Steamworks;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ArcaneNetworkingSteam;

/// <summary>
/// Message layer that utilizes SteamNetworkingMessages.
/// Attempts to parse a URI's host as a steam ID,
/// Sends messages over the steam NAT relay 
/// </summary>
public partial class SteamMessageLayer : MessageLayer
{
    public override void _Ready()
    {
        SteamNetworkingUtils.InitRelayNetworkAccess();
    }

    public SteamClient SteamClient = new();
    public SteamServer SteamServer = new();

    internal IntPtr[] ReceiveBuffer = new nint[64];

    public override void StartServer(bool isHeadless)
    {
        // Local Server Connection
        if (!isHeadless)
        {
            SteamNetworkingIdentity _ = new();
            SteamNetworkingSockets.CreateSocketPair(out var ClientToServer, out var ServerToClient, false, ref _, ref _);

            SteamServer.StartServer(ServerToClient); // Start Local Server
            SteamClient.SetLocal(ClientToServer); // Set Local Client Connection To Server (No callbacks)

        }
        else SteamServer.StartServer();
    }

    public override void StopServer()
    {
        SteamServer.StopServer();
    }

    public override bool StartClient(NetworkConnection other)
    {
        if (other.GetEndpointAs<string>() == "localhost") // Local Connection
        {
            other.SetEndPoint(SteamUser.GetSteamID().m_SteamID.ToString());
            other.isLocalConnection = true;
            
            SteamClient.StartClient(other);
            SteamServer.InitLocal(); // Invoke client connection callback
        }
        else if (ulong.TryParse(other.GetEndPoint(), out ulong SteamId))
        {
            CSteamID serverID = new(SteamId);

            if (!serverID.IsValid())
            {
                GD.PrintErr("[Steam] NetworkConnection other was NOT a valid SteamID");
                return false;
            }
            SteamClient.StartClient(other);
        }

        return true;
    }

    public override void StopClient()
    {
        SteamClient.StopClient();
    }

    public override void Poll()
    {
        SteamServer.PollMessages(this);
        SteamClient.PollMessages(this);
    }

    public override void SendToConnections(ArraySegment<byte> bytes, Channels sendType, params uint[] connnectionsToSendTo)
    {
        var connectionsSending = connnectionsToSendTo;
        if (connectionsSending == null) GD.PrintErr($"[Steam] User Didn't Specify connections to send to!");
        if (connectionsSending.Length == 0) GD.PrintErr($"[Steam] User Didn't Specify connections to send to!");

        foreach (uint connID in connectionsSending)
        {
            // Run invokes (send is for debug)
            if (connID != 0) OnServerSend?.Invoke(bytes, connID);
            else OnClientSend?.Invoke(bytes);

            // Get SteamNetConnection handle to send to
            HSteamNetConnection steamConnectionToSend = connID == 0 ? SteamClient.ConnectionToServer : SteamServer.ClientsConnected[connID];

            GCHandle handle = default;
            try
            {
                // pin the backing array so GC won't move it
                handle = GCHandle.Alloc(bytes.Array, GCHandleType.Pinned);

                // get pointer to the offset inside the pinned array
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(bytes.Array, bytes.Offset);

                EResult result = SteamNetworkingSockets.SendMessageToConnection(steamConnectionToSend, // Send Message Over SteamNetworkingSockets
                    ptr,
                    (uint)bytes.Count,
                    sendType == Channels.Reliable ? 0 : 8,
                    out long msgNum
                );

                //GD.Print("[Steam] Sending: " + (uint)bytes.Count + "b To: " + connection.GetID());
                if (result != EResult.k_EResultOK) GD.PushWarning($"[Steam] Failed to send because: {result}");
            }
            catch (Exception e)
            {
                GD.Print(e.Message);
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

    }

}
