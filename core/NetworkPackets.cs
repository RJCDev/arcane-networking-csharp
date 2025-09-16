using Godot;
using MessagePack;
using MessagePack.Resolvers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Reflection;

namespace ArcaneNetworking
{
    public enum ModifyNode : byte
    {
        Instantiate,
        Modify,
        TransferOwner,
    }

    /// <summary>
    /// Contains packet types
    /// Arcane Networking can understand.
    /// </summary>
    public interface Packet;
    
    [MessagePackObject]
    public struct HandshakePacket : Packet
    {
        [Key(0)] // Your Local ID
        public int netID;

        [Key(1)]
        public long sendTime;

        [Key(2)] // Authentication Payload
        public ArraySegment<byte> AuthPayload;
    }
    
    public enum ConnectionState : byte
    {
        Handshake,
        Disconnected,
    }

    /// Packet that is sent when we first connect to a connection
    [MessagePackObject]
    public struct ConnectionStatePacket : Packet
    {
        // The state change
        [Key(0)]
        public ConnectionState connState;

        /// Auth payload
        [Key(1)]
        public byte[] payload;
    }

    // Ping Pong packet
    [MessagePackObject]
    public struct PingPacket : Packet
    {
        ///<summary> Tick the ping was sent at </summary>
        [Key(0)]
        public long sendTick;
    }

    // Ping Pong packet
    [MessagePackObject]
    public struct PongPacket : Packet
    {
        ///<summary> Tick the original ping was sent at </summary>
        [Key(0)]
        public long pingTick;

        ///<summary> Tick the pong is being sent </summary>
        [Key(1)]
        public long sendTick;
    }
    
    // Instantiates an object over the network
    [MessagePackObject]
    public struct SpawnNodePacket : Packet
    {
        [Key(0)]
        public uint netID;

        [Key(1)]
        public uint prefabID;

        [Key(2)]
        public int ownerID;

        [Key(3)]
        public float[] position;

        [Key(4)]
        public float[] rotation;

        [Key(5)]
        public float[] scale;
        

    }

    [MessagePackObject]
    public struct ModifyNodePacket : Packet
    {
        [Key(0)]
        public uint netID;

        [Key(1)]
        public bool enabled;

        [Key(2)]
        public bool destroy;

        [Key(3)]
        public int newOwner;
    }

}

