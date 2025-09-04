using ArcaneNetworking;
using Godot;
using System;
using System.Collections;

public partial class NetworkDebug : Node
{
    [Export] public Label FPS, RTTLabel, kbps, rwBuffers;
    [Export] public Label AmIClientLabel;
    [Export] public Label AmIServerLabel;
    [Export] public Label IsAuthenticatedLabel;
    

    static Queue bytesDownCounter, bytesUpCounter;
    public static double KbpsDwn = 0, KbpsUp = 0;


    double timeCounter = 0;

    public static void OnClientPacketIn(ArraySegment<byte> data) => bytesDownCounter.Enqueue(data.Count);

    public static void OnClientPacketOut(ArraySegment<byte> data) => bytesUpCounter.Enqueue(data.Count);

    public override void _Ready()
    {
        MessageLayer.Active.OnClientSend += OnClientPacketOut;
        MessageLayer.Active.OnClientReceive += OnClientPacketIn;

        bytesDownCounter = new Queue();
        bytesUpCounter = new Queue();
    }

    public void ClcltPckSz()
    {
        if (timeCounter < 1.0) return; // Less than 1 second, do nothing

        // Convert to kilobits/sec
        int bytesDownAverage = 0;
        int bytesDownSamples = bytesDownCounter.Count;

        if (bytesDownSamples > 0)
        {
            while (bytesDownCounter.Count > 0) bytesDownAverage += (int)bytesDownCounter.Dequeue();
            bytesDownAverage /= bytesDownSamples;
        }

        int bytesUpAverage = 0;
        int bytesUpSamples = bytesUpCounter.Count;
            
        if (bytesUpSamples > 0)
        {
            while (bytesUpCounter.Count > 0) bytesUpAverage += (int)bytesUpCounter.Dequeue();
            bytesUpAverage /= bytesUpSamples;
        }

        KbpsDwn = bytesDownAverage / 1024.0;
        KbpsUp = bytesUpAverage / 1024.0;

        timeCounter = 0.0;

    }

    public override void _PhysicsProcess(double delta)
    {
        if (Client.serverConnection == null) return;

        FPS.Text = "FPS: " + ((int)Engine.GetFramesPerSecond()).ToString();
        rwBuffers.Text = "RdBfr: " + NetworkPool.GetReaderPoolSize() + "b |" + "WrtBfr: " + NetworkPool.GetWriterPoolSize() + "b";

        kbps.Text = "Up: " + Math.Round(KbpsUp, 4) + "kbps | Down: " + Math.Round(KbpsDwn, 4) + "kbps";
        RTTLabel.Text = Client.serverConnection.rtt.ToString() + " MS";
        AmIClientLabel.Text = "Client? " + NetworkManager.AmIClient.ToString();
        AmIServerLabel.Text = "Server? " + NetworkManager.AmIServer.ToString();
        IsAuthenticatedLabel.Text = "Authenticated? " + Client.serverConnection.isAuthenticated.ToString();

        timeCounter+=delta;
        ClcltPckSz();
    }
}

