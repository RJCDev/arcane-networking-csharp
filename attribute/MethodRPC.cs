using System;

namespace ArcaneNetworking;

/// <summary>
/// Add this to a method to send its values to the connections stated in sendToConnections.
/// If [Server & sendToConnections.Length == 0] => Will send to all client sendToConnections 
/// If [Client ONLY & sendToConnections.Length == 0] => Will send to server
/// If [Headless & sendToConnections.Length == 0] => Will send to server
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MethodRPCAttribute : Attribute
{
    // Debug inspection
    public Channels Channel { get; }
    public uint[] Targets { get; }
    public bool ServerCommand { get; }

    public MethodRPCAttribute(
        Channels channel = Channels.Reliable, bool serverCommand = false,
        params uint[] sendToConnections)
    {
        Channel = channel;
        ServerCommand = serverCommand;
        Targets = sendToConnections;
    }

}