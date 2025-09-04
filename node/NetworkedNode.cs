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

    bool _enabled = false;
    [Export]
    public bool Enabled
    {
        get
        {
            return _enabled;
        }

        set
        {
            _enabled = value;
            ProcessMode = value ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
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

    public bool AmIOwner => OwnerID == Client.serverConnection.GetID();
    public uint OwnerID;
    public object[] OwnerMeta = new object[64];

    // Actions
    public Action<ulong, ulong> OnOwnerChanged;

    // Find all Networked Components
    public override void _EnterTree()
    {
        ChildEnteredTree += OnChildAdded;
    }

    // Destroy all Networked Components
    public override void _ExitTree()
    {
        ChildEnteredTree -= OnChildAdded;
    }

    void OnChildAdded(Node child)
    {
        // Make SURE we insert it at the correct index
        if (child is NetworkedComponent netComponent)
        {
            if (NetworkedComponents.Contains(netComponent)) return; // Don't add twice!

            GD.Print("[Networked Node] Networked Node: " + child.Name + " Was Registered In Networked Node: " + NetID);
            netComponent.NetworkedNode = this;
            NetworkedComponents.Insert(child.GetIndex(), netComponent);
        }
        else return;
    }

}