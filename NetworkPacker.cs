using Godot;
using System;

namespace ArcaneNetworking;

// Packs messages with their callback ids
public static class NetworkPacker
{
    public static void PackRPC(NetworkWriter writer, RPCPacket packet, object[] args)
    {
        writer.Write((ushort)args.Length); // Write argument count
        foreach (var arg in args) writer.Write(arg); // Write Argument Data
    }
    public static void UnpackRPC(NetworkReader reader, out object[] args)
    {
        try
        {
            reader.Read(out ushort argsCount); // Read argument count
            args = new object[argsCount]; // Initialize with size

            for (int i = 0; i < argsCount; i++) reader.Read(out args[i]); // Read Argument Data to object[]
            
        }
        catch (Exception e)
        {
            GD.PrintErr("Error unpacking RPC Arguments! Header or body was Corrupted!");
            GD.PrintErr(e.Message);
            args = [];

        }
    }

    public static void Pack<T>(T packet, NetworkWriter writer)
    {
        // Write header
        writer.Write(NetworkStorage.Singleton.PacketToID(typeof(T)));
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
