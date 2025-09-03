using Godot;
using System;

namespace ArcaneNetworking;

// Packs messages with their callback ids
public static class NetworkPacker
{
    public static void Pack<T>(T packet, NetworkWriter writer)
    {
        // Write header
        int packetHash = ExtensionMethods.StableHash(packet.GetType().FullName);
        writer.Write((byte)0);
        writer.Write(packetHash);
        writer.Write(packet);
    }

    public static ushort Unpack<T>(NetworkReader reader, out T packet)
    {
        try
        {
            // Attempt to read packet message
            reader.Read(out ushort packetID);
            reader.Read(out packet);

            return packetID;
        }
        catch (Exception e)
        {
            GD.PrintErr("Error unpacking Packet! Header or body was Corrupted!");
            GD.PrintErr(e.Message);
            packet = default;
            return 0;
        }

    }



}
