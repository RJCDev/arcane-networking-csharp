using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace ArcaneNetworking;

/// <summary>
/// This interface is an optional interface for nodes that have networked nodes under them to use.
/// This will allow access to when the node is instantiated on the server, disabled, or destroyed
/// </summary>
public interface INetworkLogger
{
    public void _AuthoritySet();

    /// <summary>
    /// Called right after node is spawned on the server
    /// </summary>
    public void _NetworkReady();

    /// <summary>
    /// Called just before a network update is sent
    /// </summary>
    public void _NetworkUpdate(double delta);

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
public sealed partial class NetworkedNode : Node, INetworkLogger
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
    public uint PrefabID;

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

    public override void _Ready() => _NetworkReady();

    public void _AuthoritySet()
    {
        foreach (NetworkedComponent comp in NetworkedComponents)
        {
            comp._AuthoritySet();
        }
        if (Node is INetworkLogger node) node._AuthoritySet();

    }

    public void _NetworkReady()
    {
        foreach (NetworkedComponent comp in NetworkedComponents)
        {
            comp._NetworkReady();
        }
        if (Node is INetworkLogger node) node._NetworkReady();

    }

    public void _NetworkDestroy()
    {
        foreach (NetworkedComponent comp in NetworkedComponents)
        {
            comp._NetworkDestroy();
        } 
        if (Node is INetworkLogger node) node._NetworkDestroy();

    }

    public void _NetworkUpdate(double delta)
    {
        foreach (NetworkedComponent comp in NetworkedComponents)
        {
            comp._NetworkUpdate(delta);
        }
        if (Node is INetworkLogger node) node._NetworkUpdate(delta);

    }

    // Find all Networked Components
    public override void _EnterTree()
    {
        // If the NetID is 0 when it enters the tree, it was put in the scene by the user, and not by Server.Spawn()
        // We need to register it hashed by its path instead of by a random ID
        if (NetID == 0)
        {
            string path = GetPath().ToString();
            NetID = (uint)ExtensionMethods.StableHash(path);

            int collisionCount = 0;

            while (!WorldManager.NetworkedNodes.TryAdd(NetID, this)) // If there happens to be a collision
            {
                NetID = (uint)ExtensionMethods.StableHash(collisionCount + path);
                collisionCount++;
            }
        }

        GD.Print("[Networked Node] Networked Node: " + Node.Name + " | " + NetID + " Registered");

        ChildEnteredTree += OnChildAdded;
    }

    // Destroy all Networked Components
    public override void _ExitTree()
    {
        WorldManager.NetworkedNodes.Remove(NetID);

        ChildEnteredTree -= OnChildAdded;
    }

    void OnChildAdded(Node child)
    {
        if (child is NetworkedComponent netComponent)
        {
            if (NetworkedComponents.Contains(netComponent)) return; // Don't add twice!

            GD.Print("[Networked Node] Networked Component: " + child.Name + " Was Registered In Networked Node: " + NetID + " | " + Node.Name);
            netComponent.NetworkedNode = this;
           
            NetworkedComponents.Add(netComponent);
        }
        else return;
    }
}