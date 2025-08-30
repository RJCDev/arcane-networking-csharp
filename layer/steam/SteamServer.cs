using ArcaneNetworking;
using Godot;
using Steamworks;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ArcaneNetworkingSteam;

public class SteamServer
{
    internal HSteamListenSocket ServerListenSocket;
    internal Dictionary<uint, HSteamNetConnection> ClientsConnected = [];
    protected Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionCallback;

    public SteamServer()
    {
        // Callbacks
        ConnectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

    }

    public void StartServer()
    {
        ServerListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);

        GD.Print("[Steam Server] Server Started! ");
    }

    public void StopServer()
    {
        SteamNetworkingSockets.CloseListenSocket(ServerListenSocket);
    }
    /// <summary>
    /// Called when the steam socket connection changes
    /// </summary>
    /// 
    void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
    {
        uint steam32 = (uint)info.m_info.m_identityRemote.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID
        
        switch (info.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
            
                if (info.m_info.m_hListenSocket == ServerListenSocket)
                {
                    // Accept the client
                    SteamNetworkingSockets.AcceptConnection(info.m_hConn);

                    NetworkConnection incoming = new(info.m_info.m_identityRemote.GetSteamID().ToString(), steam32, null);
                    ClientsConnected.Add(steam32, info.m_hConn);

                    GD.PushWarning("[Steam Server] Accepting a Networking Session with a remote Client: " + info.m_info.m_identityRemote.GetSteamID());

                    MessageLayer.Active.OnServerConnect?.Invoke(incoming); // Invoke to the High-Level API that this Connection in the MessageLayer is connected

                }

                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:

                GD.PrintErr("Connection closed..." + info.m_info.m_identityRemote.GetSteamID());

                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
                MessageLayer.Active.OnServerDisconnect?.Invoke(steam32);

                ClientsConnected.Remove(steam32);

                break;
        }
    }


    public void PollMessages(SteamMessageLayer layer)
    {
        foreach (var connection in ClientsConnected)
        {
            int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(ClientsConnected[connection.Key], layer.ReceiveBuffer, layer.ReceiveBuffer.Length);

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

}
