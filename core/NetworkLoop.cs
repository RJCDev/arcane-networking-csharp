using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArcaneNetworking;

public class NetworkLoop
{
    // Message processing timing
    static ulong lastPingPongTime;
    
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
        if (msElapsedPing > NetworkManager.manager.PingFrequency)
        {
            lastPingPongTime = Time.GetTicksMsec();

            if (NetworkManager.AmIClient)
            {
                //GD.Print("[Client] Pinging At:" + Time.GetTicksMsec());

                Client.serverConnection.Ping();

            }
        }
    }
}
