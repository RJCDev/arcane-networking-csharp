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
        if (msElapsed > 1.0d / NetworkManager.manager.NetworkRate * 1000.0f)
        {
            while (MessageQueue.Count > 0)
            {
                var message = MessageQueue.Dequeue();

                foreach (var connection in message.connections)
                    MessageLayer.Active.SendToConnections(message.writer.ToArraySegment(), message.channel, connection.GetRemoteID());

                NetworkPool.Recycle(message.writer);
            }

            MessageLayer.Active.Poll();

            lastProcessTime = Time.GetTicksMsec();
        }

        // At the interval set, attempt to check for packets, and also flush any packets in the queue
        double msElapsedPing = Time.GetTicksMsec() - lastPingPongTime;

        // Queue Ping Pong Packets
        if (msElapsedPing > 1.0d / NetworkManager.manager.PingPongRate * 1000.0f)
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
