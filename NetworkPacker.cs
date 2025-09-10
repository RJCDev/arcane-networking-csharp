using Godot;
using System;

namespace ArcaneNetworking;

// Packs messages with their callback ids
public static class NetworkPacker
{
    public static void Pack<T>(T packet, NetworkWriter writer)
    {
        // Write header
        int hash = ExtensionMethods.StableHash(packet.GetType().FullName);
        writer.Write((byte)0);
        writer.Write(hash);
        writer.Write(packet);
    }
    
    public static bool ReadHeader(NetworkReader reader, out byte type, out int hash)
    {
        bool success = true;
        // Attempt to read packet message
        if (!reader.Read(out type)) success = false;
        if (!reader.Read(out hash)) success = false;
        
        if (!success)
        {
            type = default;
            hash = default;
            return false;
        }
        else return true;

    }


}
