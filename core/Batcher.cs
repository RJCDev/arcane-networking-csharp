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

    public void Reset()
    {
        QueuedMessages.Clear();
        CurrBatch.Reset();
    }
    /// <summary>
    /// Resets the Batcher and flushes all data up to the max size
    /// </summary>
    public void Flush(out ArraySegment<byte> batchBytes)
    {
        // Make sure we don't exceed the 1300 byte limit per message
        int bytesCounted = 0;

        while (QueuedMessages.Count > 0)
        {
            // Get bytes counter
            int msgBytes = QueuedMessages.Peek().ToArraySegment().Count;
            int newBytesCounted = bytesCounted + msgBytes;

            if (msgBytes >= 1300)
            {
                GD.PushWarning("[Batcher] Message Size Excedes MTU!");
                QueuedMessages.Dequeue();
                continue; // Skip message
            }
            else if (newBytesCounted >= 1300)
            {
                break; // Send the Batch;
            }

            bytesCounted += msgBytes; // Add bytes its valid

            NetworkWriter msg = QueuedMessages.Dequeue();

            CurrBatch.WriteBytes(msg.ToArraySegment()); // Write the message
            
            NetworkPool.Recycle(msg); // We can now get rid of this NetworkWriter that originally wrote the packet
        }
        batchBytes = CurrBatch.ToArraySegment(); // Flush out

        if (batchBytes.Count == 0)
            throw new Exception("[Batcher] Batch has ZERO BYTES");

        CurrBatch.Reset(); // Reset

    }

}
