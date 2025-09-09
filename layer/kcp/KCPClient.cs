using ArcaneNetworking;
using Steamworks;
using System;
using System.Net;
using System.Net.Sockets;

namespace Kcp;

public class KCPClient
{
    internal KCP mKCP = null;
    internal Socket mSocket;
    EndPoint RemoteAddress;
    internal byte[] rawRcv = new byte[1400];
    internal byte[] packetBuffer = new byte[4096];
    internal byte[] rawSnd = new byte[1400];
    protected Callback<SteamNetConnectionStatusChangedCallback_t> ConnectionCallback;

    public void StartClient(NetworkConnection connection)
    {
        IPHostEntry hostEntry = Dns.GetHostEntry(IPAddress.Parse(connection.GetEndPoint()));

        if (hostEntry.AddressList.Length == 0)
        {
            throw new Exception("Unable to resolve host: " + hostEntry);
        }

        var endpoint = hostEntry.AddressList[0];
        mSocket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        mSocket.Connect(endpoint, connection.GetPort());

        RemoteAddress = mSocket.RemoteEndPoint;

    }

    public void StopClient()
    {
       if (mSocket != null) {
            mSocket.Close();
            mSocket = null;
        }
    }

    public void Poll() => mKCP.Update();
    public void OnHandshake(HandshakePacket packet)
    {
       mKCP = new KCP(packet.ID, (data, size) =>
        {
            // Calls when its time to flush
            mSocket.SendTo(data, size, SocketFlags.None, RemoteAddress);
        });
    }

    public int Send(byte[] bytes, int index, int length)
    {
        if (mSocket == null)
            return -1;

        var waitsnd = mKCP.WaitSnd;
        if (waitsnd < mKCP.SndWnd && waitsnd < mKCP.RmtWnd)
        {

            var sendBytes = 0;
            do
            {
                var n = Math.Min((int)mKCP.Mss, length - sendBytes);
                mKCP.Send(bytes, index + sendBytes, n);
                sendBytes += n;
            } while (sendBytes < length);

            waitsnd = mKCP.WaitSnd;
            if (waitsnd >= mKCP.SndWnd || waitsnd >= mKCP.RmtWnd)
            {
                mKCP.Flush(false);
            }

            return length;
        }

        return 0;
    }

    public void SocketReceive()
    {
        // Put socket data in KCP
        if (mSocket.Available > 0)
        {
            int recv = mSocket.ReceiveFrom(rawRcv, ref RemoteAddress);
            if (recv > 0)
            {
                mKCP.Input(rawRcv, 0, recv, true, true);
            }
        }
    }
    public void PollMessages()
    {

        // tell KCP to update timers (resend, flush, ack, etc)
        mKCP.Update();

        // drain application messages
        int len;
        while ((len = mKCP.Recv(packetBuffer)) > 0) MessageLayer.Active.OnClientReceive?.Invoke(new ArraySegment<byte>(packetBuffer));
    }

}
