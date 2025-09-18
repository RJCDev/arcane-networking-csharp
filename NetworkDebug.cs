using ArcaneNetworking;
using Godot;
using System;
using System.Collections;

[GlobalClass]
[Icon("res://addons/arcane-networking/icon/network_debug.svg")]
public partial class NetworkDebug : Control
{    [Export] TextEdit Endpoint;
    [Export] Label ServerTick, FPS, RTTLabel, kbps, rwBuffers;
    [Export] Label AmIClientLabel;
    [Export] Label AmIServerLabel;
    [Export] Label IsAuthenticatedLabel;

    [Export] VBoxContainer ButtonsBox;



    static readonly Queue bytesDownCounter = new();
    static readonly Queue bytesUpCounter = new();

    public static double KbpsDwn = 0, KbpsUp = 0;


    double timeCounter = 0;

    public static void OnClientPacketIn(ArraySegment<byte> data) => bytesDownCounter.Enqueue(data.Count);

    public static void OnClientPacketOut(ArraySegment<byte> data) => bytesUpCounter.Enqueue(data.Count);

    public static void OnServerPacketIn(ArraySegment<byte> data, int conn) => bytesDownCounter.Enqueue(data.Count);

    public static void OnServerPacketOut(ArraySegment<byte> data, int conn) => bytesUpCounter.Enqueue(data.Count);

    public override void _Ready()
    {
        MessageLayer.Active.OnClientSend += OnClientPacketOut;
        MessageLayer.Active.OnClientReceive += OnClientPacketIn;
        MessageLayer.Active.OnServerSend += OnServerPacketOut;
        MessageLayer.Active.OnServerReceive += OnServerPacketIn;

        MessageLayer.Active.OnClientConnect += OnClientConnect;
        MessageLayer.Active.OnClientDisconnect += OnClientDisconnect;
    }

    void OnClientConnect() => ButtonsBox.Hide();
    void OnClientDisconnect() => ButtonsBox.Show();

    void StartServer(bool headless) => NetworkManager.manager.StartServer(headless);
    void StartClient() => NetworkManager.manager.Connect(Endpoint.Text);

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

        KbpsDwn = bytesDownAverage * 8 / 1000.0; // * 8 For 8 bits in one byte
        KbpsUp = bytesUpAverage * 8 / 1000.0; // * 8 For 8 bits in one byte

        timeCounter = 0.0;

    }

    public override void _PhysicsProcess(double delta)
    {
        ServerTick.Text = "Server Tick " + (NetworkManager.AmIClient ? Client.TickMS.ToString() : Server.TickMS.ToString());
        FPS.Text = "FPS: " + ((int)Engine.GetFramesPerSecond()).ToString();
        rwBuffers.Text = "RdBfr: " + NetworkPool.GetReaderPoolSize() + "b |" + "WrtBfr: " + NetworkPool.GetWriterPoolSize() + "b";

        kbps.Text = "Up: " + Math.Round(KbpsUp, 4) + "kbps | Down: " + Math.Round(KbpsDwn, 4) + "kbps";
        
        AmIClientLabel.Text = "Client? " + NetworkManager.AmIClient.ToString();
        AmIServerLabel.Text = "Server? " + NetworkManager.AmIServer.ToString();

        if (Client.serverConnection != null && Client.serverConnection.isAuthenticated)
        {
            RTTLabel.Text = Client.serverConnection.lastRTT.ToString() + " MS";
            IsAuthenticatedLabel.Text = "Authenticated? " + Client.serverConnection.isAuthenticated.ToString();
        } 

        timeCounter += delta;
        ClcltPckSz();
    }
}

