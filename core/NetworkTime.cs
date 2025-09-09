using Godot;

namespace ArcaneNetworking;

public class NetworkTime
{
    // Message processing timing
    static ulong lastProcessTime, lastPingPongTime;

    // Process loop
    public static void Process()
    {
        double msElapsed = Time.GetTicksMsec() - lastProcessTime;

        // Regular packets
        if (msElapsed > 1.0f / NetworkManager.manager.NetworkRate * 1000.0f)
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

            lastProcessTime = Time.GetTicksMsec();
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
