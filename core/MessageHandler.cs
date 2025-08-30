using ArcaneNetworking;
using Godot;
using System;
using System.Collections.Generic;

public struct QueuedMessage()
{
    public Channels channel;
    public NetworkConnection[] connections;
    public NetworkWriter writer;
}

public static class MessageHandler
{
    public static Queue<QueuedMessage> MessageQueue = [];

    // Message processing timing
    static ulong lastProcessTime, lastPingPongTime;

    public static void Enqueue(Channels channel, NetworkWriter writer, params NetworkConnection[] connections)
    => MessageQueue.Enqueue(new QueuedMessage() { writer = writer, channel = channel, connections = connections });

    // Process loop
    public static void Process()
    {
        double msElapsed = Time.GetTicksMsec() - lastProcessTime;

        // Regular packets
        if (msElapsed > NetworkManager.manager.NetworkRate)
        {
            while (MessageQueue.Count > 0)
            {
                GD.Print("[MessageManager] Flush... ");

                var message = MessageQueue.Dequeue();
                MessageLayer.Active.SendToConnections(message.writer.ToArraySegment(), message.channel, message.connections);

                GD.Print("[MessageManager] Flush Done! ");

                NetworkPool.Recycle(message.writer);
            }

            MessageLayer.Active.Poll();

            lastProcessTime = Time.GetTicksMsec();
        }

        // At the interval set, attempt to check for packets, and also flush any packets in the queue
        double msElapsedPing = Time.GetTicksMsec() - lastPingPongTime;

        // Queue Ping Pong Packets
        if (msElapsedPing > NetworkManager.manager.PingPongRate)
        {
            lastPingPongTime = Time.GetTicksMsec();

            if (NetworkManager.AmIClient)
            {
                Client.serverConnection.Ping();

                GD.Print("[Client] Pinging!");
            }
            if (NetworkManager.AmIServer)
            {
                foreach (var connection in Server.Connections)
                {
                    connection.Value.Ping();
                    GD.Print("[Server] Pinging!");
                }
            }


        }

    }

}
