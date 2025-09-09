using Godot;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ArcaneNetworking;

/// <summary>
/// Pool of readers and writers that can be accessed without creating addition memory allocations
/// </summary>
public static class NetworkPool
{
    static readonly ConcurrentBag<NetworkWriter> writerPool = [];
    static readonly ConcurrentBag<NetworkReader> readerPool = [];


    public static int GetWriterPoolSize()
    {
        int allBytes = 0;
        writerPool.ToList().ForEach(x => allBytes += x.Buffer.Length);
        return allBytes;
    } 

    public static int GetReaderPoolSize()
    {
        int allBytes = 0;
        readerPool.ToList().ForEach(x => allBytes += x.Buffer.Length);
        return allBytes;
    } 

    public static NetworkWriter GetWriter() // Should we reset the position and overwrite? or should we keep writing to this writer
    {
        if (!writerPool.TryTake(out NetworkWriter writer))
        {
            writer = new NetworkWriter();
            //GD.Print("[NetworkWriter] Obtaining NEW Network Writer");
        }
        else
        {

            writer.Reset();
            //GD.Print("[NetworkWriter] Obtaining Network Writer From Pool and RESETING POSITION.");
        }

        return writer;
    }

    public static NetworkReader GetReader(ArraySegment<byte> forBytes)
    {
        if (!readerPool.TryTake(out NetworkReader reader))
        {
            reader = new NetworkReader(forBytes);

            //GD.Print("[NetworkReader] Obtaining NEW Network Reader");
        }
        else
        {
            reader.Reset(forBytes);

            //GD.Print("[NetworkReader] Obtaining Network Reader From Pool. Length: " + forBytes.Length);
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