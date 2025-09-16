using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArcaneNetworking;

public class NetworkTime
{
    // Message processing timing
    static ulong lastPingPongTime;

    static readonly Queue<ulong> rttSamples = [];

    public static void AddRTTSample(ulong sample)
    {
        rttSamples.Enqueue(sample);

        if (rttSamples.Count > 60)
            rttSamples.Dequeue();
    }

    public static ulong GetRTTAvg()
    {
        if (rttSamples.Count == 0) return 0;

        ulong avg = 0;
        uint count = (uint)rttSamples.Count;
        for (int i = 0; i < count; i++)
        {
            avg += rttSamples.ElementAt(i);
        }
        avg /= count;
        return avg;
    }
    
    // Process loop
    public static void Process()
    {
       if (NetworkManager.AmIClient)
        {
            MessageLayer.Active.PollClient();
            Client.Process();
        }
        if (NetworkManager.AmIServer)
        {
            MessageLayer.Active.PollServer();
            Server.Process();
        }
        
        // Process our ping pong events
        PingPongs();
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
