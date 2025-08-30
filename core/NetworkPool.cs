using Godot;
using System;
using System.Collections.Concurrent;

namespace ArcaneNetworking;

/// <summary>
/// Pool of readers and writers that can be accessed without creating addition memory allocations
/// </summary>
public static class NetworkPool
{
    static readonly ConcurrentBag<NetworkWriter> writerPool = [];
    static readonly ConcurrentBag<NetworkReader> readerPool = [];

    public static NetworkWriter GetWriter()
    {
        if (!writerPool.TryTake(out NetworkWriter writer))
        {
            writer = new NetworkWriter();
        }
        else
        {
            writer.Reset();
        }
        
        return writer;
    }

    public static NetworkReader GetReader(byte[] forBytes)
    {
        if (!readerPool.TryTake(out NetworkReader reader))
        {
            reader = new NetworkReader(forBytes);
        }
        else
        {
            reader.Reset(forBytes);
        }
        
        return reader;
    }
    
    public static NetworkReader GetReader()
    {
        if (!readerPool.TryTake(out NetworkReader reader))
        {
            reader = new NetworkReader();
        }
        else
        {
            reader.Reset();
        }
        
        return reader;
    }

    public static void Recycle(NetworkWriter writer)
    {
        writerPool.Add(writer);
        writer.Reset();
    }
    public static void Recycle(NetworkReader reader)
    {
        readerPool.Add(reader);
        reader.Reset();
    }


}