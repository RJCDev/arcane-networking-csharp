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
        GD.Print($"[Arcane Networking] Weave Begin.. {path} ");

        var projectDir = ProjectSettings.GlobalizePath("res://");
        var projectName = ProjectSettings.GetSetting("application/config/name").AsStringName();
        var exportDir = ProjectSettings.GlobalizePath(Path.GetDirectoryName(path));
        
        if (!Path.IsPathRooted(exportDir))
            exportDir = Path.GetFullPath(Path.Combine(projectDir, exportDir));

        string dataDir = Directory.GetDirectories(exportDir)
            .FirstOrDefault(d => Path.GetFileName(d).StartsWith("data_"));

        var targetDll = Path.Combine(dataDir, $"{projectName}.dll");

        if (!File.Exists(targetDll))
        {
            GD.PrintErr($"[Arcane Networking] Target DLL not found: {targetDll}");
            return;
        }

        var weaverDll = ProjectSettings.GlobalizePath("res://addons/arcane-networking/plugin/weaver/weaver.dll");

        if (!File.Exists(weaverDll))
        {
            GD.PrintErr($"[Arcane Networking] Weaver not found: {weaverDll}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{weaverDll}\" -- \"{targetDll}\"",
            WorkingDirectory = projectDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var proc = new Process { StartInfo = psi };
        proc.Start();

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();

        proc.WaitForExit();

        if (!string.IsNullOrEmpty(stdout))
            GD.Print("[Arcane Networking] \n" + stdout);

        if (!string.IsNullOrEmpty(stderr))
            GD.PrintErr("[Arcane Networking] \n" + stderr);

        GD.Print($"[Arcane Networking] Weaver exited with code {proc.ExitCode}");
    }


}
#endif
