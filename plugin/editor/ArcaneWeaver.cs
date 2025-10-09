#if TOOLS
using Godot;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ArcaneNetworking;

[Tool]
public partial class ArcaneWeaver : EditorExportPlugin
{
    string[] features;
    bool isDebug;
    string path;
    uint flags;

    public override string _GetName() => "ArcaneWeaver";

    public override void _ExportBegin(string[] features, bool isDebug, string path, uint flags)
    {
        this.features = features;
        this.isDebug = isDebug;
        this.path = path;
        this.flags = flags;
    }
    public override void _ExportEnd()
    {        
        var exportDir = ProjectSettings.GlobalizePath(Path.GetDirectoryName(path));
        
        string dataDir = Directory.GetDirectories(exportDir)
            .FirstOrDefault(d => Path.GetFileName(d).StartsWith("data_"));

        var projectName = ProjectSettings.GetSetting("application/config/name").AsStringName();
        var targetDll = Path.Combine(dataDir, $"{projectName}.dll");

        ArcaneNetworkingPlugin.Weave(targetDll);
    }


}
#endif
