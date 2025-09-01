using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Godot.WebSocketPeer;

namespace ArcaneNetworking;

public abstract partial class NetworkedWorld : Node
{
    // Connections managed on this world, only the server has this info, clients don't need to care about other connections
    public Dictionary<ulong, NetworkConnection> ManagedConnections = new Dictionary<ulong, NetworkConnection>();

    /// <summary>
    /// Called when we are moved to this world on the client
    /// </summary>
    public abstract void ClientInit();

    /// <summary>
    /// Gets called on the server whenever a client is "Moved" to this level by the server
    /// </summary>
    /// <returns></returns>
    public void MoveConnection(NetworkConnection conn, NetworkedWorld from = null)
    {
        if (!NetworkManager.AmIServer) return;

        // Remove connection from previous world if it is not null
        from?.ManagedConnections.Remove(conn.GetID());

        // Tell all those clients in the other world to disable this person's player object now that they moved to a new world 
        if (conn.playerObject != null) // (if they have a player object)
            Server.Modify(conn.playerObject.GetNetNode(), false, false);

        // Add a new managed connection to this world
        ManagedConnections.Add(conn.GetID(), conn);
        
    }

    public abstract void Cleanup();

}
