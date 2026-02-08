using System;

namespace ArcaneNetworking;

/// <summary>
/// When Called: Runs on this client (Unless runLocal is false) then gets sent to the server to run this method remotely.
/// <para> If requireAuthority is true, this node MUST be owned by the sending user for the server to exectute this method </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class CommandAttribute : Attribute
{
    // Debug inspection
    public Channels Channel { get; }
    public bool RequireAuthority { get; }

    public bool Instant { get; }  
    
    public CommandAttribute(Channels channel = Channels.Reliable, bool instant = false, bool requireAuthority = false)
    {
        Channel = channel;
        RequireAuthority = requireAuthority;
        Instant = instant;
    }
}