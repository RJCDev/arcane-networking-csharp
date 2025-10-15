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
public abstract partial class NetworkedComponent : Node, INetworkLogger
{
    public int GetIndex() => NetworkedNode.NetworkedComponents.IndexOf(this);
    public virtual void SetAuthorityMode(AuthorityMode auth) { AuthorityMode = auth; _AuthoritySet(); }
    public virtual void _NetworkReady() {}
    public virtual void _NetworkDestroy() {}
    public virtual void _AuthoritySet() {}
    public virtual void _NetworkUpdate(double delta) {}

    public NetworkedNode NetworkedNode;

    [ExportGroup("Send Config")]
    [Export] protected AuthorityMode AuthorityMode = AuthorityMode.Server;
        
}