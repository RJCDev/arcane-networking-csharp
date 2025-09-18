using ArcaneNetworking;
using Godot;
using Steamworks;
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace ArcaneNetworkingSteam;

public class SteamClient
{
    internal HSteamNetConnection ConnectionToServer;
    SteamNetworkingIdentity RemoteIdentity;

    protected Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionCallback;

    public void SetLocal(HSteamNetConnection conn) => ConnectionToServer = conn;

    IntPtr[] ReceivePointers = new nint[64]; // Pointers to steamworks unmanaged messages on receive


    public void StartClient(NetworkConnection connection)
    {
        if (ConnectionToServer == default) // We have no internal connection, we are Searching for remote server
        {
            RemoteIdentity = new() { m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID };
            RemoteIdentity.SetSteamID(new CSteamID(connection.GetEndpointAs<ulong>()));

            GD.Print("[Steam Client] Connecting to... " + connection.GetEndpointAs<ulong>());

            ConnectionToServer = SteamNetworkingSockets.ConnectP2P(ref RemoteIdentity, 0, 0, null);

            ConnectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged); // Init Callback (VERY IMPORTANT ONLY IF REMOTE)
            // Else we will get a recuring loop when someone tries to join our "Server" as we are accepting both client and server callbacks on this connection

        }
        else // Local Server, simulate callbacks
        {
            GD.Print("[Steam Client] Local Client Setup!");

            MessageLayer.Active.OnClientConnect?.Invoke();
        }

    }

    public void StopClient()
    {
        SteamNetworkingSockets.CloseConnection(ConnectionToServer, 0, "", true);
        ConnectionToServer = default;
    }

    /// <summary>
    /// Called when the steam socket connection changes
    /// </summary>
    /// 
    void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
    {
        switch (info.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:

                if (info.m_info.m_identityRemote.GetSteamID() == RemoteIdentity.GetSteamID())
                {
                    GD.PushWarning("[Steam Client] Connected To Remote Host: " + info.m_info.m_identityRemote.GetSteamID());

                    MessageLayer.Active.OnClientConnect?.Invoke();
                }
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer: // No errors, they just disconnected us
                OnCloseConnection(info); 
                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally: // THere was a problem, throw an error
                MessageLayer.Active.OnClientError?.Invoke(info.m_info.m_eEndReason, info.m_info.m_szEndDebug);
                OnCloseConnection(info);
                break;
        }
    }
    void OnCloseConnection(SteamNetConnectionStatusChangedCallback_t info)
    {
        GD.PrintErr("Connection closed remote..." + info.m_info.m_identityRemote.GetSteamID());
        SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
    }

    public void PollMessages(SteamMessageLayer layer)
    {
        int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(ConnectionToServer, ReceivePointers, ReceivePointers.Length);

        for (int i = 0; i < msgCount; i++)
        {
            SteamNetworkingMessage_t netMessage =
                Marshal.PtrToStructure<SteamNetworkingMessage_t>(ReceivePointers[i]);

            try
            {

                byte[] buffer = ArrayPool<byte>.Shared.Rent(netMessage.m_cbSize);
                Marshal.Copy(netMessage.m_pData, buffer, 0, netMessage.m_cbSize);
                var segment = new ArraySegment<byte>(buffer, 0, netMessage.m_cbSize); // Create segment from message pointer

                MessageLayer.Active.OnClientReceive?.Invoke(segment);

                //GD.Print("[SteamClient] Message Received! Count: " + msgCount);
            }
            catch (Exception e)
            {
                GD.PushWarning("[SteamClient] Packet Was Invalid Or Empty!?");
                GD.PrintErr(e);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(ReceivePointers[i]); // Tell Steam to free the buffer
            }
        }
    }
    

}
