using System;
using System.Collections.Generic;
using Godot;

namespace ArcaneNetworking;

public class WorldManager
{
    // Sorted list of Networked nodes. Keys are Net ID's of the nodes
    internal static readonly SortedList<uint, NetworkedNode> NetworkedNodes = [];

    public static Node ServerWorld = null;

    public static Action OnWorldLoaded;

    public static void LoadOnlineWorld()
    {
        if (ServerWorld != null) return; // If we already have a server world, just return

        // If we don't have an online scene, create one to add all the online nodes to
        if (NetworkManager.manager.OnlineScene == null)
        {
            ServerWorld = new Node3D { Name = "OnlineNodes" };
            NetworkManager.manager.AddSibling(ServerWorld);
        }
        
        ServerWorld = NetworkManager.manager.OnlineScene.Instantiate();
        NetworkManager.manager.GetTree().Root.AddChild(ServerWorld);

        // Whenever the world is ready, invoke OnWorldLoaded
        ServerWorld.Ready += () =>
        {
            OnWorldLoaded?.Invoke();
        };

    }

    public static void UnloadOnlineWorld()
    {
        if (ServerWorld == null) return; // If we have no server world, just return

        ServerWorld.QueueFree();
        ServerWorld = null;
    }
    

}
