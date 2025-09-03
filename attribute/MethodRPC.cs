using System;

namespace ArcaneNetworking;

/// <summary>
/// Add this to a method to send its values to (The Sever) if a client, and (All Clients) if you're a server
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MethodRPCAttribute : Attribute
{
    // Debug inspection
    public Channels Channel { get; }
    public uint[] Targets { get; }
    public bool ServerCommand { get; }

    public MethodRPCAttribute(
        Channels channel = Channels.Reliable, bool serverCommand = false)
    {
        Channel = channel;
        ServerCommand = serverCommand;
    }

}