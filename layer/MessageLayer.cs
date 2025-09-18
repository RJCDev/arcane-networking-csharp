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
    [GlobalClass]
    [Icon("res://addons/arcane-networking/icon/network_layer.svg")]
    public abstract partial class MessageLayer : Node
    {
        [Export] public ushort Port;

        public static MessageLayer Active;
    
        /// <summary>Called when this client connection has throws an error, provided with a message.</summary>
        public Action<int, string> OnClientError;
        
        /// <summary>Called when a connection has throws an error, provided with a message.</summary>
        public Action<int, int, string> OnServerError;
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
        public Action<NetworkConnection> OnServerConnect;

        /// <summary>Called by server MessageLayer when a client disconnects to the server.</summary>
        public Action<int> OnServerDisconnect;

        /// <summary>Called by server MessageLayer when the server sends data to a client.</summary>
        public Action<ArraySegment<byte>, int> OnServerSend;

        /// <summary>Called by MessageLayer when the server receieves from a client.</summary>
        public Action<ArraySegment<byte>, int> OnServerReceive;

        public override void _Process(double delta) => NetworkTime.Process();

        public abstract void StartServer(bool isHeadless);

        public abstract void StopServer();

        public abstract void PollClient();
        public abstract void PollServer();

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

        /// <summary>
        /// Disconnects a client from the server if you are the server
        /// </summary>
        public abstract void ServerDisconnect(NetworkConnection conn);

        /// SEND / RECEIVE \\\

        /// <summary>
        /// Sends raw data to the MessageLayer and routes it to the connections specified
        /// </summary>
        public abstract void SendTo(ArraySegment<byte> bytes, Channels channel, NetworkConnection conn);

       
    }

}