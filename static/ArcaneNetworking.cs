using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using MessagePack;
using System.Security.Cryptography;

namespace ArcaneNetworking;

public delegate void RPCUnpackDelegate(NetworkReader reader, NetworkedComponent target);

/// <summary>
/// Storage singleton that takes data from the weaver and produces resources that can be
/// persistantly stored during runtime
/// </summary>
public static class ArcaneNetworking
{
    public static readonly Dictionary<int, Type> PacketTypes = new Dictionary<int, Type>();
    
    public static readonly Dictionary<int, RPCUnpackDelegate> RPCMethods = new Dictionary<int, RPCUnpackDelegate>();

    static ArcaneNetworking()
    {
        
    }

    internal static void RegisterPacket(int hash, Type type)
    {
        if (!PacketTypes.TryAdd(hash, type)) GD.PushWarning($"[Arcane Networking] Registered Packet: {type.Name} has duplicate: {hash}");
    }
    internal static void RegisterRPC(int hash, RPCUnpackDelegate del)
    {
        if (!RPCMethods.TryAdd(hash, del)) GD.PushWarning($"[Arcane Networking] Registered RPC: {del.Method.Name} has duplicate: {hash}");
    }
    
    internal static void Init()
    {

        GD.Print("[Arcane Networking] Arcane Networking Initialized!");
        Client.RegisterInvokes();
        Server.RegisterInvokes();
    }


}

