using Godot;
using Godot.Collections;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ArcaneNetworking;

/// <summary>
/// Weaver class that can store attribute data as well as registered packets after your assembly is built
/// </summary>
[Tool]
public partial class Weaver : EditorPlugin
{
    public override bool _Build()
    {
        try
        {
            // Fill Registry
            NetworkRegistry registry = new()
            {
                MethodRPCs = FindMethodRPCs(),
                PacketIDs = FindPacketIDs()
            };

            // Save Registry
            string path = "res://addons/arcane_networking/plugin/registry/NetworkRegistry.tres";

            registry.TakeOverPath(path); // Force write
            Error error = ResourceSaver.Save(registry, path);

            if (error != Error.Ok)
            {
                GD.PrintErr($"[Arcane Weaver] Failed To Save Registry!! {error}");
                return false;
            }

            

            GD.Print($"[Arcane Weaver] PacketRegistry generated at {path}");           

            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr("[Arcane Weaver] Failed To Build Registry!!");
            GD.PrintErr(e);

            return false;
        }
       
    }


    public override void _EnablePlugin()
    {
        // Create watcher for this directory
        string proj = ProjectSettings.GlobalizePath("res://");
        string buildDir = Path.Combine(proj, ".godot", "mono", "temp", "bin", "Debug");

        if (!ProjectSettings.HasSetting("autoload/NetworkStorage"))
        {
            AddAutoloadSingleton("NetworkStorage", "res://addons/arcane_networking/node/NetworkStorage.tscn"); // Make sure we don't duplicate
        }

    }

    public override void _DisablePlugin()
    {
        // Remove the autoload singleton for NetworkStorage
        RemoveAutoloadSingleton("NetworkStorage");
    }

    public Dictionary<ushort, string> FindMethodRPCs()
    {
        Dictionary<ushort, string> methodPairs = [];

        ushort methodID = 0;

       var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(NetworkedComponent).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .Distinct(); // not abstract

        foreach (var type in types)
        {
            // Obtain RPCAttribute Methods
            var methods = type.GetMethods().Where(y => y.GetCustomAttributes().OfType<MethodRPCAttribute>().Any()).OrderBy(m => m.Name);

            foreach (var method in methods)
            {
                GD.Print($"[Arcane Weaver] Saving RPC Method Definition: {type.FullName}.{method.Name}");
                // Assign ID's to methods
                methodPairs.Add(methodID, $"{type.FullName}.{method.Name}");
                methodID++;
            }

        }

        return methodPairs;
    }

    public Dictionary<ushort, string> FindPacketIDs()
    {
        Dictionary<ushort, string> packetPairs = [];
        ushort nextID = 0;
        
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(Packet).IsAssignableFrom(t) && !t.IsAbstract); // Check if its not abstract

        foreach (var type in types)
        {
            GD.Print("[Arcane Weaver] Saving Packet Definition: " + type.FullName);

            packetPairs.Add(nextID, type.FullName);
            nextID++;
        }

        return packetPairs;

    }
    
}
