using System;
using Godot;

namespace ArcaneNetworking;

public class WorldManager
{
    public static Node ServerWorld = null;

    public static Action OnWorldLoaded;

    public static void LoadOnlineWorld()
    {
        if (ServerWorld != null || NetworkManager.manager.OnlineScene == null) return; // If we already have a server world, just return

        ServerWorld = NetworkManager.manager.OnlineScene.Instantiate();
        NetworkManager.manager.GetTree().Root.AddChild(ServerWorld);

        OnWorldLoaded?.Invoke();

    }

    public static void UnloadOnlineWorld()
    {
        if (ServerWorld == null) return; // If we have no server world, just return

        ServerWorld.QueueFree();
        
    }

}
