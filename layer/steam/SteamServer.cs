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
    internal HSteamNetPollGroup ClientPollGroup;
    internal Dictionary<uint, HSteamNetConnection> ClientsConnected = [];
    protected Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionCallback;


    public void StartServer(HSteamNetConnection localConnection = default)
    {
        ConnectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged); // Init Callbacks
    
        ServerListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null); // Create listen socket

        ClientPollGroup = SteamNetworkingSockets.CreatePollGroup(); // Create Poll Group for server

        if (localConnection != default)
        {
            uint steam32 = (uint)SteamUser.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID

            ClientsConnected.Add(steam32, localConnection);

            SteamNetworkingSockets.SetConnectionPollGroup(localConnection, ClientPollGroup);

        }

        GD.Print("[Steam Server] Server Started! ");


    }

    public void StopServer()
    {
        SteamNetworkingSockets.CloseListenSocket(ServerListenSocket);

        ConnectionCallback.Dispose();
    }

    public void InitLocal()
    {
        uint steam32 = (uint)SteamUser.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID

        NetworkConnection incoming = new(SteamUser.GetSteamID().m_SteamID.ToString(), steam32, null);

        GD.Print("[Steam Server] Setup Local Connection To Server!");

        MessageLayer.Active.OnServerConnect?.Invoke(incoming); // Invoke to the High-Level API that this Connection in the MessageLayer is connected
    }

    /// <summary>
    /// Called when the steam socket connection changes
    /// </summary>
    /// 
    void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
    {

        uint steam32 = (uint)info.m_info.m_identityRemote.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID

        // Accept the client
       
        
        switch (info.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:

                SteamNetworkingSockets.AcceptConnection(info.m_hConn);

                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
   
                if (info.m_info.m_hListenSocket == ServerListenSocket)
                {
                    SteamNetworkingSockets.SetConnectionPollGroup(info.m_hConn, ClientPollGroup);

                    NetworkConnection incoming = new(info.m_info.m_identityRemote.GetSteamID().ToString(), steam32, null);
                    ClientsConnected.Add(steam32, info.m_hConn);

                    GD.Print("[Steam Server] Accepted a Networking Session with a remote Client: " + info.m_info.m_identityRemote.GetSteamID());

                    MessageLayer.Active.OnServerConnect?.Invoke(incoming); // Invoke to the High-Level API that this Connection in the MessageLayer is connected

                }

                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:

                GD.PrintErr("Connection closed..." + info.m_info.m_identityRemote.GetSteamID());

                SteamNetworkingSockets.SetConnectionPollGroup(info.m_hConn, HSteamNetPollGroup.Invalid);
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);

                MessageLayer.Active.OnServerDisconnect?.Invoke(steam32);

                ClientsConnected.Remove(steam32);

                break;
        }
    }


    public void PollMessages(SteamMessageLayer layer)
    {
        int msgCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(ClientPollGroup, layer.ReceiveBuffer, layer.ReceiveBuffer.Length);

        for (int i = 0; i < msgCount; i++)
        {
            SteamNetworkingMessage_t netMessage =
                Marshal.PtrToStructure<SteamNetworkingMessage_t>(layer.ReceiveBuffer[i]);

            try
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(netMessage.m_cbSize);
                Marshal.Copy(netMessage.m_pData, buffer, 0, netMessage.m_cbSize);
                var segment = new ArraySegment<byte>(buffer, 0, netMessage.m_cbSize);

                MessageLayer.Active.OnServerReceive?.Invoke(
                    segment,
                    (uint)netMessage.m_identityPeer.GetSteamID().m_SteamID);

                //GD.Print("[SteamServer] Message Received! Count: " + msgCount);
            }
            catch (Exception e)
            {
                GD.PushWarning("[SteamServer] Packet Was Invalid Or Empty!?");
                GD.PrintErr(e);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(layer.ReceiveBuffer[i]); // Tell Steam to free the buffer
            }
        }
    
        
    }

}
