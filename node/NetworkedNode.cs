using System;
using Godot;
using Godot.Collections;

namespace ArcaneNetworking;

/// <summary>
/// This interface is an optional interface for nodes that have networked nodes under them to use.
/// This will allow access to when the node is instantiated on the server, disabled, or destroyed
/// </summary>
public interface INetworkLogger
{
    /// <summary>
    /// Called right after node is spawned on the server
    /// </summary>
    public void _NetworkReady();

    /// <summary>
    /// Called just before node is destroyed on the server
    /// </summary>
    public void _NetworkDestroy();
}
/// <summary>
/// A node that is syncronized across all clients. 
/// It can have child components that send RPC calls over the network, and is referenced internaly by its guid.
/// By itself, this node does nothing, it requires child components to send data.
/// </summary>
[GlobalClass]
[Icon("res://addons/arcane-networking/icon/networked_node.svg")]
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
            SetProcess(_enabled);
            SetPhysicsProcess(_enabled);
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

    public uint PrefabID; // Keep track of this so we can duplicate it easier / send it easier

    // Networked Components
    public Array<NetworkedComponent> NetworkedComponents = [];

    public bool AmIOwner
    {
        get
        {
            // If its headless, check if we own it by owner id 0
            if (NetworkManager.AmIHeadless)
            {
                return OwnerID == 0;
            }
            else if (NetworkManager.AmIClient)
            {
                // We are JUST client
                if (!NetworkManager.AmIServer)
                    return OwnerID == Client.serverConnection.localID;
                    
                // We are BOTH, check if the server OR local client own this node
                else
                    return OwnerID == 0 || OwnerID == Client.serverConnection.localID;
            }
            return false;
            
        }
       
    }
    public int OwnerID;
    public object[] OwnerMeta = new object[64];

    // Actions
    public Action<int, int> OnOwnerChanged;

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

            //GD.Print("[Networked Node] Networked Node: " + child.Name + " Was Registered In Networked Node: " + NetID);
            netComponent.NetworkedNode = this;
            NetworkedComponents.Insert(child.GetIndex(), netComponent);
        }
        else return;
    }

}