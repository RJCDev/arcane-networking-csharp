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
    // Allocate a small array of pointers for messages
    IntPtr[] steamMsgPtrs = new IntPtr[64]; // size = max messages per frame

    IntPtr steamSendPtr = Marshal.AllocHGlobal(65536); // size = max message size (64kb)

    public SteamClient SteamClient = new();
    public SteamServer SteamServer = new();

    public override bool Connect(NetworkConnection other)
    {
        // Local Server Connection
        if (other.GetEndpointAs<string>() == "localhost")
        {
            other.isAuthenticated = true;
            other.isLocalConnection = true;
        }
        // Connecting to other steam user connection
        else if (ulong.TryParse(other.GetEndPoint(), out ulong SteamId))
        {
            CSteamID serverID = new(SteamId);

            if (!serverID.IsValid())
            {
                GD.PrintErr("NetworkConnection other was NOT a valid SteamId");
                return false;
            }
        }
        
        // Send a handshake packet
        Client.Send(new ConnectionStatePacket() { connState = ConnectionState.Handshake }, Channels.Reliable);

        return true;
    }

    public override bool Disconnect(NetworkConnection other)
    {
        return true;
    }

    public override void Poll()
    {

        SteamServer.Poll();
        SteamClient.Poll();

        // Message will always be 1 packet
        int messageCount = SteamNetworkingMessages.ReceiveMessagesOnChannel(0, steamMsgPtrs, steamMsgPtrs.Length);

        for (int i = 0; i < messageCount; i++)
        {
            try
            {
                // Steam Networking Messages receive data as unmanaged memory, 
                // here we are retrieving the pointer from when we received the messages above
                var netMessage = Marshal.PtrToStructure<SteamNetworkingMessage_t>(steamMsgPtrs[i]);

                ArraySegment<byte> bytes = new byte[netMessage.m_cbSize];

                // Copy unmanaged to managed buffer
                Marshal.Copy(netMessage.m_pData, bytes.Array, 0, netMessage.m_cbSize);

                // Run invokes
                if (NetworkManager.AmIClient) OnClientReceive?.Invoke(bytes);
                if (NetworkManager.AmIServer) OnServerReceive?.Invoke(bytes, (uint)netMessage.m_identityPeer.GetSteamID().m_SteamID); // Cast to uint to get connection ID
            }
            catch
            {
                GD.PushWarning("Packet Was Invalid Or Empty!?");
            }
            finally
            {
                Marshal.DestroyStructure<SteamNetworkingMessage_t>(steamMsgPtrs[i]);
            }
        }
    }

    public override void Send(ArraySegment<byte> bytes, Channels sendType, params NetworkConnection[] connnectionsToSendTo)
    {
        foreach (NetworkConnection connection in connnectionsToSendTo)
        {
            // Check if local connection
            if (connection.isLocalConnection)
            {
                OnServerReceive?.Invoke(bytes, Client.serverConnection.GetID());
                continue;
            }

            // Not local connection, send
            foreach (var conn in connnectionsToSendTo)
            {
                // Run invokes (send is for debug)
                if (NetworkManager.AmIClient) OnClientSend?.Invoke(bytes);
                if (NetworkManager.AmIServer) OnServerSend?.Invoke(bytes, conn.GetID());
            }

            SteamNetworkingIdentity identity = new() { m_eType = ESteamNetworkingIdentityType.k_ESteamNetworkingIdentityType_SteamID };
            identity.SetSteamID((CSteamID)connection.GetEndpointAs<ulong>());

            if (identity.IsInvalid() || !identity.GetSteamID().IsValid()) continue;

            try
            {
                Marshal.Copy(bytes.Array, 0, steamSendPtr, bytes.Array.Length);

                // Flag 8 on steam is Reliable and 0 is Unreliable, convert it
                SteamNetworkingMessages.SendMessageToUser(ref identity, steamSendPtr, (uint)bytes.Array.Length, sendType == Channels.Unreliable ? 0 : 8, 0);
            }
            catch (Exception e)
            {
                GD.Print("(Steam Message Layer) Message Failed To Send!!");
                GD.Print(e.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(steamSendPtr);
            }
        }

    }

}
