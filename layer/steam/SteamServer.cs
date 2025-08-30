using ArcaneNetworking;
using Godot;
using Steamworks;
using System;
using System.Collections.Generic;

namespace ArcaneNetworkingSteam;

public class SteamServer
{

    Dictionary<uint, NetworkConnection> PendingConnections = [];
    //callbacks for connections
    protected Callback<SteamNetworkingMessagesSessionRequest_t> OnMessageSessionRequest;

    public SteamServer()
    {
        // Callbacks
        OnMessageSessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnConnectionReq);

        // Register the connection state packet to here so we can manage the connection here
        Server.RegisterPacketHandler<ConnectionStatePacket>(OnConnectionStatePacket);

    }

    /// <summary>
    /// Called on the server when Connect() is called on a client that is connecting to your server.
    /// We can authenticate our client here because this request will ALWAYS be the user it says it is, steam handles
    /// the authentication!
    /// </summary>
    void OnConnectionReq(SteamNetworkingMessagesSessionRequest_t request)
    {
        // Luckily we wont need any authentication from this user on the message layer, Steam accounts are encrypted through steam's back end
        // All we have to do to authenticate the client is make sure that this steam id (casted to a uint) has been stored on the server before

        uint steam32 = (uint)request.m_identityRemote.GetSteamID().m_SteamID;
        NetworkConnection incoming = new(request.m_identityRemote.GetSteamID().ToString(), steam32, null);

        PendingConnections.Add(steam32, incoming); // Cast to uint to get connection ID

        // If we are just the server, accept any incoming connections
        GD.PushWarning("Accepting a Networking Session with a client: " + request.m_identityRemote.GetSteamID().m_SteamID);

        SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);

    }

    public void OnConnectionStatePacket(ConnectionStatePacket packet, uint connID)
    {
        if (!PendingConnections.TryGetValue(connID, out NetworkConnection value)) return; // Something is wrong, as we should get a packet AFTER we accept a session

        // New user!
        if (packet.connState == ConnectionState.Handshake && !Server.Connections.ContainsKey(connID))
        {
            MessageLayer.Active.OnServerConnect?.Invoke(connID); // Invoke to the High-Level API that this Connection in the MessageLayer is connected

            PendingConnections.Remove(connID);

            GD.Print("User Connected via Steam! " + connID);
        }
        else if (packet.connState == ConnectionState.Disconnected)
        {
            // The client told us they disconnected
            MessageLayer.Active.OnServerDisconnect?.Invoke(connID);
        }
    }

    public void Poll()
    {
        foreach (var connection in Server.Connections)
        {
            SteamNetworkingIdentity identity = new() { m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID };
            identity.SetSteamID(new CSteamID(connection.Value.GetEndpointAs<ulong>()));

            SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out SteamNetConnectionInfo_t connectionInfo, out _);
            
            // Check if we are connected, if not invoke disconnect
            if (connectionInfo.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected
            || connectionInfo.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                MessageLayer.Active.OnServerDisconnect?.Invoke((uint)identity.GetSteamID().m_SteamID);
            }
        }
        
    }

}
