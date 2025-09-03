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

	public void LoadWorldClient(int levelID, bool unloadLast = true)
	{
		if (!NetworkManager.AmIClient) return;

		NetworkedWorld world = null;

		OnStartLoad?.Invoke(levelID);

		GD.Print("[Client][World Manager] Loading World " + levelID);

		if (NetworkManager.AmIClient && !NetworkManager.AmIServer)
		{
			// Add world to scene tree
			world = Worlds[levelID].scene.Instantiate<NetworkedWorld>();
			GetTree().Root.AddChild(world);
			LoadedWorlds.Add(world);
		}
		else world = LoadedWorlds[LoadedWorlds.Count]; // Get latest world loaded on server

		// Move player object to new world on client
		Client.serverConnection.playerObject.Reparent(world);

		world.ClientInit();

		GD.Print("[Client][World Manager] Loaded!!");

		OnFinishedLoad?.Invoke(levelID);
	}

	public void LoadWorldServer(NetworkConnection forConnection, int levelID, bool unloadLast = true)
	{
		if (!NetworkManager.AmIServer) return;

		OnStartLoad?.Invoke(levelID);

		GD.Print("[Server][World Manager] Loading World " + levelID);

		// Add world to scene tree
		NetworkedWorld world = Worlds[levelID].scene.Instantiate<NetworkedWorld>();
		GetTree().Root.AddChild(world);

		LoadedWorlds.Add(world);

		GD.Print("[Server][World Manager] Loaded!!");

		// Move player object to new world on client
		forConnection.playerObject.Reparent(world);

		GD.Print("[Server][World Manager] Moved Player ID: " + forConnection.GetID());

		// Only pass in the last active world if we wish to unload it and stop syncronizing connections from it // TODO
		world.MoveConnection(forConnection, null);		

	}


}
