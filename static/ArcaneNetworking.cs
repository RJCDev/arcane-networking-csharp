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
        PacketTypes.Add(hash, type);
        GD.Print("[Arcane Networking] Registered Packet: " + hash + " | Type: " + type.FullName);
    }
    internal static void RegisterRPC(int hash, RPCUnpackDelegate del)
    {
        RPCMethods.Add(hash, del);
        GD.Print("[Arcane Networking] Registered RPC: " + hash + " | Invoker: " + del.Method.Name);
    }
    internal static void Init()
    {
        GD.Print("[Arcane Networking] Arcane Networking Initialized!");
        Client.RegisterInternalHandlers();
        Server.RegisterInternalHandlers();
    }


}

