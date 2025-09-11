using ArcaneNetworking;
using Godot;
using System;

public class Unbatcher
{
    // [typeByte][intHash][1300BytesHeader][messagechunk][1300BytesHeader][messageChunk]
    // [typeByte][intHash][500bytesHeader][message]
    // public bool ReadNextMessage(NetworkReader incoming, out byte type, out int hash)
    // {
    //     NetworkPacker.ReadHeader(incoming, out type, out hash);
    //     ResolveChunk(incoming);
    // }
    // public bool ResolveChunk(NetworkReader messageChunks)
    // {
    //     messageChunks.Read(out ushort lenHeader);
        
    //     for (int i = 0; i < lenHeader)
    //         messageChunks.ReadByte();
    // }
}
