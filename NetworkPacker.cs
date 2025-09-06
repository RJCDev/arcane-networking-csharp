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
        try
        {
            // Attempt to read packet message
            reader.Read(out type);
            reader.Read(out hash);
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr("Error unpacking Packet! Header was Corrupted!");
            GD.PrintErr(e.Message);
            type = byte.MaxValue;
            hash = int.MinValue;
            return false;
        }

    }


}
