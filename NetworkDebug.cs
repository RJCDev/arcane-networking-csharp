using ArcaneNetworking;
using Godot;
using System;

public partial class NetworkDebug : Node
{
    [Export] public Label RTTLabel, kbps, rwBuffers;
    [Export] public Label AmIClientLabel;
    [Export] public Label AmIServerLabel;
    [Export] public Label IsAuthenticatedLabel;
    

    static long bytesDwnCntr = 0, bytesUpCntr = 0;
    public static double KbpsDwn = 0, KbpsUp = 0;

    public static void OnPacketIn(ArraySegment<byte> data) => bytesDwnCntr += data.Count;
    public static void OnPacketOut(ArraySegment<byte> data) => bytesUpCntr += data.Count;

    public override void _Ready()
    {
        MessageLayer.Active.OnClientSend += OnPacketOut;
        MessageLayer.Active.OnClientReceive += OnPacketIn;
    }

    public void ClcltPckSz(double msElapsed)
    {
        if (msElapsed <= 0) return;

        // Bytes/sec
        double downBps = bytesDwnCntr * 1000.0 / msElapsed;
        double upBps = bytesUpCntr * 1000.0 / msElapsed;

        // Convert to kilobits/sec (divide by 1024, then multiply by 8)
        KbpsDwn = downBps * 8.0 / 1024.0;
        KbpsUp = upBps * 8.0 / 1024.0;

        // Reset counters
        bytesDwnCntr = 0;
        bytesUpCntr = 0;

    }

    public override void _PhysicsProcess(double delta)
    {
        if (Client.serverConnection == null) return;

        rwBuffers.Text = "Read Buffer: " + NetworkPool.GetReaderPoolSize() + "b |" + "Write Buffer: " + NetworkPool.GetWriterPoolSize() + "b"; 
        
        kbps.Text = "Up: " + KbpsUp + "kbps | Down: " + KbpsDwn + " kbps";
        RTTLabel.Text = Client.serverConnection.rtt.ToString() + " MS";
        AmIClientLabel.Text = "Client? " + NetworkManager.AmIClient.ToString();
        AmIServerLabel.Text = "Server? " + NetworkManager.AmIServer.ToString();
        IsAuthenticatedLabel.Text = "Authenticated? " + Client.serverConnection.isAuthenticated.ToString();

        ClcltPckSz(delta * 1000);
    }
}

