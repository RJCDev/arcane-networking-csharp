using System;

namespace ArcaneNetworking;

/// <summary>
/// When Called: Relays to the clients specified at the end of your method.
/// <para> For example: RelayExplosion(Vector3 position, params NetworkConnection[] sendTo)</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RelayAttribute : Attribute
{
    // Debug inspection
    public Channels Channel { get; }
    public bool ExcludeOwner { get; }    
    public RelayAttribute(Channels channel = Channels.Reliable, bool excludeOwner = false)
    {
        ExcludeOwner = excludeOwner;
        Channel = channel;
    }
}