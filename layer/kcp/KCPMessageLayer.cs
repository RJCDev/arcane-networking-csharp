using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ArcaneNetworkingSteam;
using Godot;
using Kcp;

namespace ArcaneNetworking
{
    public partial class KCPMessageLayer : MessageLayer
    {
        KCPClient KCPClient = new();
        KCPServer KCPServer = new();
        private UInt32 mNextUpdateTime = 0;

        public bool WriteDelay { get; set; }
        public bool AckNoDelay { get; set; }

        private DateTime startDt = DateTime.Now;
        const int logmask = KCP.IKCP_LOG_IN_ACK | KCP.IKCP_LOG_OUT_ACK | KCP.IKCP_LOG_IN_DATA | KCP.IKCP_LOG_OUT_DATA;

        
        public int Recv(byte[] data, int index, int length)
        {
            if (mRecvBuffer.ReadableBytes > 0) {
                var recvBytes = Math.Min(mRecvBuffer.ReadableBytes, length);
                Buffer.BlockCopy(mRecvBuffer.RawBuffer, mRecvBuffer.ReaderIndex, data, index, recvBytes);
                mRecvBuffer.ReaderIndex += recvBytes;

                if (mRecvBuffer.ReaderIndex == mRecvBuffer.WriterIndex) {
                    mRecvBuffer.Clear();
                }
                return recvBytes;
            }

            if (mSocket == null)
                return -1;

            if (!mSocket.Poll(0, SelectMode.SelectRead)) {
                return 0;
            }

            var rn = 0;
            try {
                rn = mSocket.Receive(mRecvBuffer.RawBuffer, mRecvBuffer.WriterIndex, mRecvBuffer.WritableBytes, SocketFlags.None);
            } catch(Exception ex) {
                Console.WriteLine(ex);
                rn = -1;
            }

            if (rn <= 0) {
                return rn;
            }
            mRecvBuffer.WriterIndex += rn;

            var inputN = mKCP.Input(mRecvBuffer.RawBuffer, mRecvBuffer.ReaderIndex, mRecvBuffer.ReadableBytes, true, AckNoDelay);
            if (inputN < 0) {
                mRecvBuffer.Clear();
                return inputN;
            }
            mRecvBuffer.Clear();

            for (;;) {
                var size = mKCP.PeekSize();
                if (size < 0) break;

                mRecvBuffer.EnsureWritableBytes(size);

                var n = mKCP.Recv(mRecvBuffer.RawBuffer, mRecvBuffer.WriterIndex, size);
                if (n > 0) mRecvBuffer.WriterIndex += n;
            }


            if (mRecvBuffer.ReadableBytes > 0) {
                return Recv(data, index, length);
            }

            return 0;
        }


        public override void StartServer(bool isHeadless) => KCPServer.StartServer();
        public override void StopServer()
        {
            throw new NotImplementedException();
        }

        public override void PollClient()
        {
            if (KCPClient.mSocket == null)
                return;

            KCPClient.SocketReceive();
            KCPClient.PollMessages();
        }

        public override void PollServer()
        {
            throw new NotImplementedException();
        }

        public override bool StartClient(NetworkConnection host)
        {
            throw new NotImplementedException();
        }

        public override void StopClient()
        {
            throw new NotImplementedException();
        }

        public override void SendTo(ArraySegment<byte> bytes, Channels channel, NetworkConnection target)
        {
            if (target == null) GD.PrintErr($"[KCP] User Didn't Specify connection to send to!");

            var convID = target.GetRemoteID();

            // Run invokes (send is for debug)
            if (convID != 0)
            {
                OnServerSend?.Invoke(bytes, convID);
            }
            else
            {

                OnClientSend?.Invoke(bytes);
            }
            
            if (mSocket != null)
                {
                    mSocket.Send(bytes.Array, bytes.Count, SocketFlags.None);
                }
        }
    }
}
