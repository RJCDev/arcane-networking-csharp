using ArcaneNetworking;
using Godot;
using Steamworks;
using System;

namespace ArcaneNetworkingSteam;

public class SteamClient
{
    public CSteamID MySteamID;

    public SteamClient()
    {
        // Retrieve our steam ID
        MySteamID = SteamUser.GetSteamID();

        // Register the connection state packet to here so we can manage the connection here
        Client.RegisterPacketHandler<ConnectionStatePacket>(OnConnectionStatePacket);
    }

    public void OnConnectionStatePacket(ConnectionStatePacket packet, uint connID)
    {
        // Server has accepted us
        if (packet.connState == ConnectionState.Handshake && !Server.Connections.ContainsKey(connID))
        {
            MessageLayer.Active.OnClientConnect?.Invoke(); // Invoke that the MessageLayer is connected
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
