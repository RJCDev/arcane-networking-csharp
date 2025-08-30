using Godot;
using Godot.Collections;
using Steamworks;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ArcaneNetworking;

public partial class WorldManager : Node
{
	// Loading Actions Hooks
	public Action<int> OnStartLoad;
	public Action<int> OnFinishedLoad;

	// World Configs
	[Export] public Array<WorldConfig> Worlds;

	// Worlds currently loaded, they can be active or inactive
	public Array<NetworkedWorld> LoadedWorlds = new Array<NetworkedWorld>();

	// THe world currently active on this client
	public NetworkedWorld ActiveWorld = null;

	/// <summary>
	/// Unloads all of the networked worlds
	/// </summary>
	public void UnloadAllWorlds()
	{
		OnStartLoad?.Invoke(-1);

		// Remove active networked worlds
		foreach (var world in LoadedWorlds) world.Cleanup();
		LoadedWorlds.Clear();

		OnFinishedLoad?.Invoke(0);
	}

	public void LoadWorld(int levelID, bool unloadLast = true, NetworkConnection forConnection = null)
	{
		OnStartLoad?.Invoke(levelID);

		// Add world to scene tree
		NetworkedWorld world = Worlds[levelID].scene.Instantiate<NetworkedWorld>();
		GetTree().Root.AddChild(world);
		
		LoadedWorlds.Add(world);

		// Move player object to new world on client
		(Client.serverConnection.playerObject as Node3D).Reparent(world);
		
		if (NetworkManager.AmIServer) // Move client to new world on server
		{
			// Only pass in the last active world if we wish to unload it and stop syncronizing connections from it
			world.MoveConnection(forConnection, unloadLast ? ActiveWorld : null);
		}
		if (NetworkManager.AmIClient) 
		{
			world.ClientInit();
		}
		
		OnFinishedLoad?.Invoke(levelID);

	}


}
