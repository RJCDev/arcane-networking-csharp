using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using MessagePack;
using System.Security.Cryptography;

namespace ArcaneNetworking;

public struct RPCMethod
{
    public MethodInfo RPC;
    public Type[] ParameterTypes;
}

/// <summary>
/// Storage singleton that takes data from the weaver and produces resources that can be
/// persistantly stored during runtime
/// </summary>
public static class NetworkStorage
{

    public static readonly Dictionary<int, Type> PacketTypes = [];
    public static readonly Dictionary<int, Action<NetworkedComponent, int>> RPCMethods = [];

    static NetworkStorage() { }


    public static int StableHash(string hashString)
    {
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(hashString));
        return BitConverter.ToInt32(hash, 0);
    }


}

