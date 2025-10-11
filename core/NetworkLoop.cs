using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArcaneNetworking;

public class NetworkLoop
{
    // Message processing timing
    static double updateTimer;
    static double pingPongTimer;

    public static void Poll()
    {
        if (NetworkManager.AmIClient)
        {
            MessageLayer.Active.PollClient();
        }


        if (NetworkManager.AmIServer)
        {
            MessageLayer.Active.PollServer();
        }
            
    }
    // Process loop
    public static void Process(double delta)
    {
        Poll();

        updateTimer += delta;
        double step = 1.0d / NetworkManager.manager.NetworkRate;

        while (updateTimer >= step)
        {
            updateTimer -= step;

            if (NetworkManager.AmIClient)
                Client.Process();

            if (NetworkManager.AmIServer)
                Server.Process();                

        }
        // Process our ping pong events
        PingPongs(delta);
    }

    static void PingPongs(double delta)
    {
        // At the interval set, attempt to check for packets, and also flush any packets in the queue
        pingPongTimer += delta;

        double step = NetworkManager.manager.PingFrequency / 1000.0d;

        // Queue Ping Pong Packets
        if (pingPongTimer >= step)
        {
            pingPongTimer = 0;

            if (NetworkManager.AmIClient)
            {
                //GD.Print("[Client] Pinging At:" + Time.GetTicksMsec());

                Client.serverConnection.Ping();

            }
           
        }
    }
}
