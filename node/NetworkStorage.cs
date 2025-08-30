using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using MessagePack;

namespace ArcaneNetworking;

/// <summary>
/// Storage singleton that takes data from the weaver and produces resources that can be
/// persistantly stored during runtime
/// </summary>
public partial class NetworkStorage : Node
{
    // Saved disk object to be loaded
    private NetworkRegistry _registry;

    // Runtime access loaded from _registry
    public static Map<ushort, Type> PacketTypes = new();
    public static Map<ushort, MethodInfo> RpcMethods = new();

    public static NetworkStorage Singleton;
    public NetworkStorage() => Singleton ??= this;

    public override void _EnterTree()
    {
       
        GD.Print("[Network Storage] Loading Runtime Registry...");
    
        try
        {
            _registry = GD.Load<NetworkRegistry>("res://addons/arcane_networking/plugin/registry/NetworkRegistry.tres");
            // Convert type names back into real System.Type
            foreach (var kv in _registry.PacketIDs)
            {
                var type = Type.GetType(kv.Value);

                if (type != null)
                {
                    PacketTypes.Add(kv.Key, type);

                    GD.Print("Packet ID: " + kv.Key + " For Packet Type: " + kv.Value + " Has Been Registered");
                }
            }

            foreach (var id in _registry.MethodRPCs)
            {
                string[] typeMethod = id.Value.Split(".");

                // Get the Type (search all loaded assemblies)
                var type = Type.GetType(typeMethod[0]);

                if (type == null)
                    throw new InvalidOperationException($"Could not find type '{typeMethod[0]}'");

                // Get the method
                var method = type.GetMethod(typeMethod[1],
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null)
                    throw new InvalidOperationException($"Could not find method '{typeMethod[1]}' on type '{typeMethod[0]}'");

                RpcMethods.Add(id.Key, method);
                
                
                GD.Print("Method ID: " + id.Key + " For Type: " + id.Value + " Has Been Registered");

            }

        }
        catch (Exception e)
        {
            GD.PrintErr("NetworkRegistry could not be loaded! Arcane Networking will not be able to retrieve IDs for RPCs or Packets.");
            GD.PrintErr(e);
            GetTree().Quit();
        }
    }

    public override void _Ready()
    {
        // Register internal handlers for packets
        Client.RegisterInternalHandlers();
        Server.RegisterInternalHandlers();
    }


    public MethodInfo IDToMethod(ushort rpcID) => RpcMethods.Forward[rpcID];
    public ushort MethodToID(MethodInfo method) => RpcMethods.Reverse[method];

    public Type IDToPacket(ushort packetID) => PacketTypes.Forward[packetID];
    public ushort PacketToID(Type packetType) => PacketTypes.Reverse[packetType];

}

