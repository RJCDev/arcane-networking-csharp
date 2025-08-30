using Godot;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using MessagePack;
using MessagePack.Resolvers;

namespace ArcaneNetworking
{
    public enum Channels : int
    {
        Unreliable,
        Reliable
    }
    /// <summary>
    /// Mid Level message layer that Can be overriden to allow for different networking solutions. 
    /// For example: Steam, Generic IP:Port Connections etc.
    /// </summary>
    public abstract partial class MessageLayer : Node
    {
        public static MessageLayer Active;

        /// ON CLIENT ->
        /// <summary>Called by client MessageLayer when the client is connected to the server.</summary>
        public Action OnClientConnect;

        /// <summary>Called by client MessageLayer when the client connected to the server.</summary>
        public Action OnClientDisconnect;

        /// <summary>Called by client MessageLayer when the client sends data to the server.</summary>
        public Action<ArraySegment<byte>> OnClientSend;

        /// <summary>Called by MessageLayer when the client receieves from the server.</summary>
        public Action<ArraySegment<byte>> OnClientReceive;

        /// ON SERVER ->
        /// <summary>Called by server MessageLayer when a client is connected to the server.</summary>
        public Action<uint> OnServerConnect;

        /// <summary>Called by server MessageLayer when a client disconnects to the server.</summary>
        public Action<uint> OnServerDisconnect;

        /// <summary>Called by server MessageLayer when the server sends data to a client.</summary>
        public Action<ArraySegment<byte>, uint> OnServerSend;

        /// <summary>Called by MessageLayer when the server receieves from a client.</summary>
        public Action<ArraySegment<byte>, uint> OnServerReceive;

        // Message processing timing
        ulong lastProcessTime, lastPingPongTime;

        // Process loop
        public override void _Process(double delta)
        {
            double msElapsed = Time.GetTicksMsec() - lastProcessTime;

            // Regular packets
            if (msElapsed > NetworkManager.manager.NetworkRate)
            {
                Poll();

                lastProcessTime = Time.GetTicksMsec();

                if (NetworkManager.manager.EnableDebug)
                {
                    NetworkDebug.ClcltPckSz(msElapsed);
                }
            }

            // At the interval set, attempt to check for packets, and also flush any packets in the queue
            double msElapsedPing = Time.GetTicksMsec() - lastPingPongTime;

            // Queue Ping Pong Packets
            if (msElapsedPing > NetworkManager.manager.PingPongRate)
            {
                lastPingPongTime = Time.GetTicksMsec();

                if (NetworkManager.AmIServer)
                {
                    foreach (var connection in Server.Connections)
                    {
                        connection.Value.Ping();
                    }
                }
                else if (NetworkManager.AmIClient)
                {
                    Client.serverConnection.Ping();
                }


            }

        }

        public abstract void Poll();
        /// <summary>
        /// Attempts a connection with an endpoint
        /// Here is where we pass in an empty NetworkConnection to our MessageLayer to attemp to connect
        /// </summary>
        public abstract bool Connect(NetworkConnection host);

        /// <summary>
        /// Disconnects from another connection if they are valid
        /// </summary>
        /// <returns>Valid URI MessageLayer?</returns>
        public abstract bool Disconnect(NetworkConnection other);

        /// SEND / RECEIVE \\\

        /// <summary>
        /// Sends raw data to the MessageLayer
        /// Channel Ids: 0 = RPCs, 1 = VOIP, 2 = Pings
        /// </summary>
        public abstract void Send(ArraySegment<byte> bytes, Channels channel, params NetworkConnection[] connnectionsToSendTo);

    }

}

