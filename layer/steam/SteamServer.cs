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
    internal Dictionary<int, HSteamNetConnection> ClientsConnected = [];
    protected Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionCallback;
    IntPtr[] ReceivePointers = new nint[64]; // Pointers to steamworks unmanaged messages on receive

    public void StartServer(HSteamNetConnection localConnection = default)
    {
        ConnectionCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged); // Init Callbacks
    
        ServerListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null); // Create listen socket

        ClientPollGroup = SteamNetworkingSockets.CreatePollGroup(); // Create Poll Group for server

        if (localConnection != default)
        {
            uint steam32 = (uint)SteamUser.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID

            ClientsConnected.Add((int)steam32, localConnection);

            SteamNetworkingSockets.SetConnectionPollGroup(localConnection, ClientPollGroup);

        }

        GD.Print("[Steam Server] Server Started! ");


    }

    public void StopServer()
    {
        SteamNetworkingSockets.CloseListenSocket(ServerListenSocket);

        ConnectionCallback.Dispose();
    }

    public void Disconnect(int connID)
    {
        GD.PrintErr("Connection forcefully closed..." + connID);

        var connection = ClientsConnected[connID];

        SteamNetworkingSockets.SetConnectionPollGroup(connection, HSteamNetPollGroup.Invalid);
        SteamNetworkingSockets.CloseConnection(connection, 0, null, false);

        MessageLayer.Active.OnServerDisconnect?.Invoke(connID);

        ClientsConnected.Remove(connID);
    }

    public void InitLocal()
    {
        uint steam32 = (uint)SteamUser.GetSteamID().m_SteamID; // Get 32 bit SteamID for connection ID

        NetworkConnection incoming = new("127.0.0.1", 0, (int)steam32);

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

                    NetworkConnection incoming = new(info.m_info.m_identityRemote.GetSteamID().ToString(), 0, (int)steam32, null);
                    ClientsConnected.Add((int)steam32, info.m_hConn);

                    GD.Print("[Steam Server] Accepted a Networking Session with a remote Client: " + info.m_info.m_identityRemote.GetSteamID());

                    MessageLayer.Active.OnServerConnect?.Invoke(incoming); // Invoke to the High-Level API that this Connection in the MessageLayer is connected

                }

                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:

                GD.PrintErr("Connection closed..." + info.m_info.m_identityRemote.GetSteamID());

                SteamNetworkingSockets.SetConnectionPollGroup(info.m_hConn, HSteamNetPollGroup.Invalid);
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);

                MessageLayer.Active.OnServerDisconnect?.Invoke((int)steam32);

                ClientsConnected.Remove((int)steam32);

                break;
        }
    }


    public void PollMessages(SteamMessageLayer layer)
    {
        int msgCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(ClientPollGroup, ReceivePointers, ReceivePointers.Length);

        for (int i = 0; i < msgCount; i++)
        {
            SteamNetworkingMessage_t netMessage =
                Marshal.PtrToStructure<SteamNetworkingMessage_t>(ReceivePointers[i]);

            try
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(netMessage.m_cbSize);
                Marshal.Copy(netMessage.m_pData, buffer, 0, netMessage.m_cbSize);
                var segment = new ArraySegment<byte>(buffer, 0, netMessage.m_cbSize);

                MessageLayer.Active.OnServerReceive?.Invoke(
                    segment,
                    (int)(uint)netMessage.m_identityPeer.GetSteamID().m_SteamID);

                //GD.Print("[SteamServer] Message Received! Count: " + msgCount);
            }
            catch (Exception e)
            {
                GD.PushWarning("[SteamServer] Packet Was Invalid Or Empty!?");
                GD.PrintErr(e);
            }
            finally
            {
                SteamNetworkingMessage_t.Release(ReceivePointers[i]); // Tell Steam to free the buffer
            }
        }
    
        
    }

}
