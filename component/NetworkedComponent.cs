using System;
using Godot;

namespace ArcaneNetworking;

public enum AuthorityMode
{
    Client,
    Server,
}

public enum SendTime
{
    Process,
    Physics,
}

/// <summary>
/// A component that works under a networked node. It can send RPCs over the network.
/// </summary>
[GlobalClass, Icon("res://addons/arcane-networking/icon/networked_component.svg")]
public abstract partial class NetworkedComponent : Node
{
    public int GetIndex() => NetworkedNode.NetworkedComponents.IndexOf(this);

    public NetworkedNode NetworkedNode;

    [ExportGroup("Send Config")]
    [Export] public AuthorityMode AuthorityMode = AuthorityMode.Server;
    [Export] public SendTime SendTime = SendTime.Physics;

        
}