using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class NetworkRegistry : Resource
{
    [Export] public Dictionary<ushort, string> MethodRPCs = [];

    [Export] public Dictionary<ushort, string> PacketIDs = [];
}
