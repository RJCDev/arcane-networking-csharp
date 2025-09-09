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
        byte count = (byte)Mathf.Min(byte.MaxValue, QueuedMessages.Count);
        
        CurrBatch.WriteByte(count); // Write batch Header (Message Count)

        for (int i = 0; i < count; i++)
        {
            NetworkWriter msg = QueuedMessages.Dequeue();

            CurrBatch.WriteBytes(msg.ToArraySegment()); // Write the message

            NetworkPool.Recycle(msg); // We can now get rid of this NetworkWriter that originally wrote the packet
        }

        batchBytes = CurrBatch.ToArraySegment(); // Flush out

        CurrBatch.Reset(); // Reset

    }

}
