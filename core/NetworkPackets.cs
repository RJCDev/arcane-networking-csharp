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
    public struct PingPongPacket : Packet
    {
        ///<summary> 0 = Ping, 1 = Pong </summary>
        [Key(0)]
        public byte PingPong; 
    }

    public enum TypeByte : byte
    {
        Byte,
        UnsignedInt,
        Int,
        Long,
        Object
    }

    // Packet that calls a method with the specified arguents
    [MessagePackObject]
    public struct RPCPacket : Packet
    {
        // Caller NetworkObject guid
        [Key(0)]
        public uint CallerNetID;

        // Compoenent Index
        [Key(1)]
        public int CallerCompIndex;

        [Key(2)]
        public uint ArgCount;

        // Arguments are written into a buffer AFTER this packet has been serialized
        //public ArraySegment<byte> Args;

    }

    // Instantiates an object over the network
    [MessagePackObject]
    public struct SpawnNodePacket : Packet
    {
        [Key(0)]
        public uint NetID;

        [Key(1)]
        public uint prefabID;

        [Key(2)]
        public float[] position;

        [Key(3)]
        public float[] rotation;

        [Key(4)]
        public float[] scale;
    }

    [MessagePackObject]
    public struct ModifyNodePacket : Packet
    {
        [Key(0)]
        public uint NetID;

        [Key(1)]
        public bool enabled;

        [Key(2)]
        public bool destroy;
    }

    // Loads a world over the network
    public struct LoadLevelPacket : Packet
    {
        // Which world should we load?

        [Key(0)]
        public int LevelID;

        // Should we unload the previous world? (Disable packets from and to users from that world)
        [Key(1)]
        public bool UnloadLast;

    }

}

