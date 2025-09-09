using Godot;
using Godot.Collections;
using System;

namespace ArcaneNetworking;

public enum DisconectBehavior
{
    Destroy,
    Unspawn
}

[GlobalClass]
[Icon("res://addons/arcane-networking/icon/network_manager.svg")]
public partial class NetworkManager : Node
{
    public static NetworkManager manager;

    /// <summary> Layer that is used for network messages </summary>
    [Export] MessageLayer MsgLayer;

    /// <summary> Objects that you wish to be networked MUST be present in this dictionary</summary>
    [Export] public Array<PackedScene> NetworkObjectPrefabs = new Array<PackedScene>();

    /// <summary> Client Player Prefab (Id in the networked objects list)</summary>
    [Export] public int PlayerPrefabID = -1;

    /// <summary> Should we enable the debug callbacks?</summary>
    [Export] public bool EnableDebug = false;

    /// <summary> The rate per second at which the server and clients will send data in ms</summary>
    [Export] public ulong NetworkRate = 60;

    /// <summary> The rate at which the server and clients will Ping Pong eachother, 0 would be same as NetworkRate</summary>
    [Export] public ulong PingPongFrequency = 600;

    /// <summary> TThe maximum amount of client connections our server can have at one time</summary>
    [Export] public int MaxConnections = 4;

    [Export] public int Port = 27156;

    /// <summary> How NetworkedObjects owned by a connection behave when the connection is disconnected</summary>
    [Export] public DisconectBehavior DisconnectBehavior = DisconectBehavior.Destroy;

    // Am I A client only?
    public static bool AmIClientOnly => AmIClient && !AmIServer;
    // Am I A client server combo?
    public static bool AmICombo => AmIClient && AmIServer;

    // Am I a headless server?
    public static bool AmIHeadless => !AmIClient && AmIServer;

    // Am I the server?
    public static bool AmIServer = false;

    // Am I a client?
    public static bool AmIClient = false;

    public NetworkManager()
    {
        manager ??= this;
    }

    // Helper methods
    public void StartServer(bool headless = false)
    {
        Server.Start(headless);
        if (!headless) Client.Connect("localhost");
    }
    public void Connect(string host, int port = -1) => Client.Connect(host, port);
    public void Connect(string host) => Client.Connect(host, -1);

    public override void _EnterTree()
    {

        if (MsgLayer != null)
        {
            MessageLayer.Active = MsgLayer; // Set Active Message Layer

            ArcaneNetworking.Init(); // Initialize our networking protocol
        }
        else
        {
            GD.PrintErr("No Valid Message Layer was found, defaulting to TCP!");
            // Set TCP layer
        }

    }



}
