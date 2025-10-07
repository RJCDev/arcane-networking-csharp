#if TOOLS
using Godot;
using System;

namespace ArcaneNetworking;

[Tool]
public partial class ArcaneNetworkingPlugin : EditorPlugin
{
    private ArcaneWeaver _exportPlugin;

    public override void _EnterTree()
    {
        _exportPlugin = new ArcaneWeaver();
        AddExportPlugin(_exportPlugin);
        GD.Print("[Arcane Networking] Weaver Plugin loaded.");
    }

    public override void _ExitTree()
    {
        RemoveExportPlugin(_exportPlugin);
        GD.Print("[Arcane Networking] Weaver Plugin unloaded.");
    }

}
#endif
