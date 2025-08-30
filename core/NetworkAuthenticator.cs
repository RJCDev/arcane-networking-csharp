using System;
using System.Collections.Generic;
using Godot;
using System.Security.Cryptography;
using System.Runtime.Intrinsics.Arm;

namespace ArcaneNetworking;

// Server Certificate comprised of =>
// Unix Time Server Was started (unguessable) 
// Host Adress

public class SHA1NetworkEncryption : NetworkEncryption
{
    readonly SHA1 Algorithm = SHA1.Create();

    public override byte[] Decrypt()
    {
        return [];
    }

    public override void Encrypt(string data)
    {
        Algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
    } 

    public override bool IsValid()
    {
        return true;
    }

}

public abstract class NetworkEncryption
{
    public abstract void Encrypt(string data);

    public abstract byte[] Decrypt();

    public abstract bool IsValid();
}

/// <summary>
/// Default network authenticator, it uses SHA256 to verify 
/// data transmited over the network. Can be overriden
/// </summary>
public abstract partial class NetworkAuthenticator : Node
{
    public Action<NetworkConnection> OnServerAuthenticate;
    public Action OnClientAuthenticate;

    protected virtual void OnServerStart() { }
    protected virtual void OnServerShutdown() { }

    /// <summary>
    /// Client was Rejected from the server
    /// </summary>
    protected void ClientRejected()
    {
        Client.serverConnection.isAuthenticated = false;
    }

    /// <summary>
    /// Client was Accepted into the server
    /// </summary>
    protected void ClientAccepted()
    {
        OnClientAuthenticate?.Invoke();
    }
}
