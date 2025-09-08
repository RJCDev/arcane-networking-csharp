using ArcaneNetworking;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArcaneNetworking;

public struct QueuedMessage()
{
    public Channels channel;
    public NetworkConnection[] connections;
    public NetworkWriter writer;

    public SendTime sendtime;
}

public static class MessageHandler
{
    public static Queue<QueuedMessage> MessageQueue = [];

    // Message processing timing
    static ulong lastProcessTime, lastPhysicsProcessTime, lastPingPongTime;

    // Enqueue single target
    public static void Enqueue(Channels channel, SendTime sendTime, NetworkWriter writer, NetworkConnection connection)
    => MessageQueue.Enqueue(new QueuedMessage() { writer = writer, channel = channel, connections = [connection], sendtime = sendTime });

    // Enqueue multi target
    public static void Enqueue(Channels channel, SendTime sendTime, NetworkWriter writer, params NetworkConnection[] connections)
    => MessageQueue.Enqueue(new QueuedMessage() { writer = writer, channel = channel, connections = connections, sendtime = sendTime });

    // Process loop
    public static void PhysicsProcess()
    {
        double msElapsed = Time.GetTicksMsec() - lastPhysicsProcessTime;

        if (msElapsed > 1.0f / NetworkManager.manager.NetworkRate * 1000.0f)
        {
            // We are now flushing
            // First send messagecount byte for the batch
            if (MessageQueue.Count > byte.MaxValue)
                throw new Exception("Batch message count excedes max header size!"); // TODO SPLIT BATCHES

            byte countByte = (byte)MessageQueue.Count;
            
            while (MessageQueue.Count > 0)
            {
                if (MessageQueue.Peek().sendtime == SendTime.Physics)
                {
                    var message = MessageQueue.Dequeue();

                    foreach (var connection in message.connections)
                    {
                        MessageLayer.Active.SendTo(new ArraySegment<byte>([countByte]), Channels.Reliable, connection); // Send messagecount byte
                        MessageLayer.Active.SendTo(message.writer.ToArraySegment(), message.channel, connection);  // Send writer bytes
                    }

                    NetworkPool.Recycle(message.writer);
                }
            }

            lastPhysicsProcessTime = Time.GetTicksMsec();
        }
    }
    public static void Process()
    {
        double msElapsed = Time.GetTicksMsec() - lastProcessTime;

        // Regular packets
        if (msElapsed > 1.0f / NetworkManager.manager.NetworkRate * 1000.0f)
        {
                while (MessageQueue.Count > 0)
                {
                    if (MessageQueue.Peek().sendtime == SendTime.Process)
                    {
                        var message = MessageQueue.Dequeue();

                        foreach (var connection in message.connections)
                            MessageLayer.Active.SendTo(message.writer.ToArraySegment(), message.channel, connection);

                        NetworkPool.Recycle(message.writer);
                    }
                }
           
            MessageLayer.Active.Poll();

            lastProcessTime = Time.GetTicksMsec();
        }



    }

    public static void PingPongs()
    {
         // At the interval set, attempt to check for packets, and also flush any packets in the queue
        double msElapsedPing = Time.GetTicksMsec() - lastPingPongTime;

        // Queue Ping Pong Packets
        if (msElapsedPing > NetworkManager.manager.PingPongFrequency)
        {

            lastPingPongTime = Time.GetTicksMsec();

            if (NetworkManager.AmIClient)
            {
                //GD.Print("[Client] Pinging At:" + Time.GetTicksMsec());

                Client.serverConnection.Ping(0);

            }
            if (NetworkManager.AmIServer)
            {
                foreach (var connection in Server.Connections)
                {
                    //GD.Print("[Server] Pinging At:" + Time.GetTicksMsec());

                    connection.Value.Ping(0);
                }
            }


        }
    }

}
