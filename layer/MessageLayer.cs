using Godot;
using System;

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
        public Action<uint> OnClientConnect;

        /// <summary>Called by client MessageLayer when the client connected to the server.</summary>
        public Action OnClientDisconnect;

        /// <summary>Called by client MessageLayer when the client sends data to the server.</summary>
        public Action<ArraySegment<byte>> OnClientSend;

        /// <summary>Called by MessageLayer when the client receieves from the server.</summary>
        public Action<ArraySegment<byte>> OnClientReceive;

        /// ON SERVER ->
        /// <summary>Called by server MessageLayer when a client is connected to the server.</summary>
        public Action<NetworkConnection> OnServerConnect;

        /// <summary>Called by server MessageLayer when a client disconnects to the server.</summary>
        public Action<uint> OnServerDisconnect;

        /// <summary>Called by server MessageLayer when the server sends data to a client.</summary>
        public Action<ArraySegment<byte>, uint> OnServerSend;

        /// <summary>Called by MessageLayer when the server receieves from a client.</summary>
        public Action<ArraySegment<byte>, uint> OnServerReceive;

        public override void _Process(double delta) => MessageHandler.Process();


        public abstract void StartServer(bool isHeadless);

        public abstract void StopServer();

        public abstract void Poll();
        /// <summary>
        /// Attempts a connection with an endpoint
        /// Here is where we pass in an empty NetworkConnection to our MessageLayer to attemp to connect
        /// </summary>
        public abstract bool StartClient(NetworkConnection host);

        /// <summary>
        /// Disconnects from another connection if they are valid
        /// </summary>
        /// <returns>Valid URI MessageLayer?</returns>
        public abstract void StopClient();

        /// SEND / RECEIVE \\\

        /// <summary>
        /// Sends raw data to the MessageLayer and routes it to the connections specified
        /// Channel Ids: 0 = RPCs, 1 = VOIP, 2 = Pings
        /// </summary>
        public abstract void SendToConnections(ArraySegment<byte> bytes, Channels channel, params uint[] connnectionsToSendTo);
        

    }

}