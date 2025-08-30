using System;
using Godot;
using Godot.Collections;

namespace ArcaneNetworking;

/// <summary>
/// A node that is syncronized across all clients. 
/// It can have child components that send RPC calls over the network, and is referenced internaly by its guid.
/// By itself, this node does nothing, it requires child components to send data.
/// </summary>
public partial class NetworkedNode : Node
{
    [ExportGroup("Network Identity")]

    /// The node that is under this NetworkedNode's control
    /// 
    public Node Node
    {
        get
        {
            Node parent = GetParent();

            if (parent != null)
            {
                return parent;
            }
            else
            {
                GD.PrintErr("Error Retrieving Tracking Node, this NetworkedNode has no parent!");
                return null;
            }
        }
    }

    [Export]
    public string IDString
    {
        get => NetID.ToString();
        set => NetID.ToString();
    }

    [Export]
    public string OwnerString
    {
        get => OwnerID.ToString();
        set => OwnerID.ToString();
    }

    public uint NetID;

    // Networked Components
    public Array<NetworkedComponent> NetworkedComponents = [];

    // 0 Means its owned by the server, if its not 0 we own it 
    // (We cannot see others netId's from the client so that means everything not owned by us is owned by the server, even if owned by other clients)
    public bool AmIOwner => OwnerID != 0;
    public uint OwnerID;
    public object[] OwnerMeta = new object[64];

    // Actions
    public Action<ulong, ulong> OnOwnerChanged;


    // Find all Networked Components
    public override void _EnterTree()
    {
        ChildEnteredTree += OnChildAdded;

        foreach (var child in GetChildren())
        {
            if (child is NetworkedComponent)
            {
                NetworkedComponents.Add(child as NetworkedComponent);
                (child as NetworkedComponent).NetworkedNode = this;
            }
        }
    }

    // Destroy all Networked Components
    public override void _ExitTree()
    {
        ChildEnteredTree -= OnChildAdded;

        foreach (var component in NetworkedComponents)
        {
            component.QueueFree();
        }
    }

    void OnChildAdded(Node child)
    {
        if (child is NetworkedComponent) NetworkedComponents.Add(child as NetworkedComponent);
        else return;
    }
}