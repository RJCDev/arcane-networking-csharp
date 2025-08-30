using ArcaneNetworking;
using Godot;
using MessagePack;
using Steamworks;
using System;
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

    public override void StartServer()
    {
        SteamServer.StartServer();
    }
    public override void StopServer()
    {
        SteamServer.StopServer();
    }


    public override bool StartClient(NetworkConnection other)
    {
        // Local Server Connection
        if (other.GetEndpointAs<string>() == "localhost")
        {
            other.SetEndPoint(SteamUser.GetSteamID().m_SteamID.ToString()); // Set the endpoint to your steamID
            uint steam32 = (uint)other.GetEndpointAs<ulong>(); // Get 32 bit SteamID for connection ID
            other.isLocalConnection = true;

            NetworkConnection localServerToLocalClient = new(other.GetEndPoint(), steam32);

            // Create steam socket pair
            SteamNetworkingIdentity _ = new();
            SteamNetworkingSockets.CreateSocketPair(out SteamClient.ConnectionToServer, out var LocalConnection, true, ref _ , ref _);

            SteamServer.ClientsConnected.Add(steam32, LocalConnection);

            Active.OnServerConnect?.Invoke(localServerToLocalClient);
            Active.OnClientConnect?.Invoke();

            return true;

        }
        // Connecting to other steam user connection
        if (ulong.TryParse(other.GetEndPoint(), out ulong SteamId))
        {
            CSteamID serverID = new(SteamId);

            if (!serverID.IsValid())
            {
                GD.PrintErr("[Steam MessageLayer] NetworkConnection other was NOT a valid SteamID");
                return false;
            }
        }

        SteamClient.StartClient(other);

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

    public override void SendToConnections(ArraySegment<byte> bytes, Channels sendType, params NetworkConnection[] connnectionsToSendTo)
    {
        foreach (NetworkConnection connection in connnectionsToSendTo)
        {

            // Run invokes (send is for debug)
            if (NetworkManager.AmIServer) OnServerSend?.Invoke(bytes, connection.GetID());
            if (NetworkManager.AmIClient) OnClientSend?.Invoke(bytes);

            GCHandle handle = default;
            try
            {
                // pin the backing array so GC won't move it
                handle = GCHandle.Alloc(bytes.Array, GCHandleType.Pinned);

                // get pointer to the offset inside the pinned array
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(bytes.Array, bytes.Offset);

                // call Steam
                EResult result = SteamNetworkingSockets.SendMessageToConnection(NetworkManager.AmIClient ? SteamClient.ConnectionToServer : SteamServer.ClientsConnected[connection.GetID()]
                    , ptr,
                    (uint)bytes.Count,
                    sendType == Channels.Reliable ? 0 : 8,
                    out long msgNum

                );

                if (result == EResult.k_EResultOK)
                GD.Print($"[Steam MessageLayer] Sent {bytes.Count} bytes (msgNum {msgNum})");
                else
                    GD.PushWarning($"[Steam MessageLayer] Failed to send, result: {result}");
            }
            catch (Exception e)
            {
                GD.Print("[Steam MessageLayer] Message Failed To Send!!");
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
