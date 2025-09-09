using ArcaneNetworking;
using Godot;
using System;
using System.Collections.Generic;

public class Batcher
{
    int MaxSize = 1500;
    NetworkWriter CurrBatch = new();

    public Queue<NetworkWriter> QueuedMessages = new();

    public void Push(NetworkWriter writer) => QueuedMessages.Enqueue(writer);

    /// <summary>
    /// Resets the Batcher and flushes all data up to the max size
    /// </summary>
    public ArraySegment<byte> Flush()
    {
        CurrBatch.Reset(); // Reset the currBatch processing

        while (QueuedMessages.Count > 0)
        {
            if (CurrBatch.Buffer.Length + QueuedMessages.Peek().Buffer.Length > MaxSize) // Flush now!
            {
                return CurrBatch.ToArraySegment();
            }

            CurrBatch.WriteBytes(QueuedMessages.Dequeue().ToArraySegment()); // Write the last message
        }

        return CurrBatch.ToArraySegment(); // Flush, we didn't make the max size but thats fine
    }
}
