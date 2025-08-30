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

    public CSteamID MySteamID;   

    public SteamClient()
    {
        // Retrieve our steam ID
        MySteamID = SteamUser.GetSteamID();

        // Callbacks
        ConnectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
    }

    public void StartClient(NetworkConnection connection)
    {
        RemoteIdentity = new() { m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID };
        RemoteIdentity.SetSteamID(new CSteamID(connection.GetEndpointAs<ulong>()));

        ConnectionToServer = SteamNetworkingSockets.ConnectP2P(ref RemoteIdentity, 0, 0, null);

        GD.Print("[Steam Client] Connecting to... " + connection.GetEndpointAs<ulong>());
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
                    // We have connected!
                    SteamNetworkingSockets.AcceptConnection(info.m_hConn);

                    GD.PushWarning("[Steam Client] Connected To Remote Host: " + info.m_info.m_identityRemote.GetSteamID());
                }
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                GD.PrintErr("Connection closed remote..." + info.m_info.m_identityRemote.GetSteamID());
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
                break;
        }
    }

    public void PollMessages(SteamMessageLayer layer)
    {

        int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(ConnectionToServer, layer.ReceiveBuffer, layer.ReceiveBuffer.Length);

        for (int i = 0; i < msgCount; i++)
        {
            SteamNetworkingMessage_t netMessage =
                Marshal.PtrToStructure<SteamNetworkingMessage_t>(layer.ReceiveBuffer[i]);
            try
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(netMessage.m_cbSize);
                Marshal.Copy(netMessage.m_pData, buffer, 0, netMessage.m_cbSize);
                var segment = new ArraySegment<byte>(buffer, 0, netMessage.m_cbSize);
                
                if (NetworkManager.AmIClient)
                    MessageLayer.Active.OnClientReceive?.Invoke(segment);

                if (NetworkManager.AmIServer)
                    MessageLayer.Active.OnServerReceive?.Invoke(
                        segment,
                        (uint)netMessage.m_identityPeer.GetSteamID().m_SteamID);

                GD.Print("[SteamClient] Message Received");
            }
            catch (Exception e)
            {
                GD.PushWarning("[SteamClient] Packet Was Invalid Or Empty!?");
                GD.PrintErr(e);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(layer.ReceiveBuffer[i]); // Tell Steam to free the buffer
            }
        }
    }
    

}
