using ArcaneNetworking;
using Godot;
using System;
using System.Collections.Generic;

public class Batcher
{
    NetworkWriter CurrBatch = new();

    public Queue<NetworkWriter> QueuedMessages = new();

    public void Push(NetworkWriter writer) => QueuedMessages.Enqueue(writer);

    public bool HasData() => QueuedMessages.Count > 0;

    /// <summary>
    /// Resets the Batcher and flushes all data up to the max size
    /// </summary>
    public void Flush(out ArraySegment<byte> batchBytes)
    {
        byte count = (byte)Mathf.Min(byte.MaxValue - 1, QueuedMessages.Count);

        CurrBatch.Write(count); // Write batch Header (Message Count)
        
        for (int i = 0; i < count; i++)
        {
            NetworkWriter msg = QueuedMessages.Dequeue();
            var seg = msg.ToArraySegment();

            CurrBatch.WriteBytes(seg); // Write the message

            NetworkPool.Recycle(msg); // We can now get rid of this NetworkWriter that originally wrote the packet
        }
        
        //GD.Print("[Network Batcher] Flushed " + count + " Messages!");
        batchBytes = CurrBatch.ToArraySegment(); // Flush out

        CurrBatch.Reset(); // Reset

    }

}
