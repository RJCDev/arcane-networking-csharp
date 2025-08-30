using Godot;

[GlobalClass]
public partial class WorldConfig : Resource
{
    [Export] public PackedScene scene;
    [Export] public string name;
    [Export] public int worldID;
}
