#if TOOLS
using Godot;
using System;
using System.Diagnostics;
using System.IO;

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

    public override bool _Build()
    {
        var projectName = ProjectSettings.GetSetting("application/config/name").AsStringName();

        var targetDll = Path.Combine(".godot/mono/temp/bin/Debug/", $"{projectName}.dll");

        return Weave(targetDll);
    }

    public static bool Weave(string dllPath)
    {
        GD.Print($"[Arcane Networking] Weave Begin.. {dllPath} ");

        var projectDir = ProjectSettings.GlobalizePath("res://");
        
        if (!File.Exists(dllPath))
        {
            GD.PrintErr($"[Arcane Networking] Target DLL not found: {dllPath}");
            return false;
        }

        var weaverDll = ProjectSettings.GlobalizePath("res://addons/arcane-networking/plugin/weaver/weaver.dll");

        if (!File.Exists(weaverDll))
        {
            GD.PrintErr($"[Arcane Networking] Weaver not found: {weaverDll}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{weaverDll}\" -- \"{dllPath}\"",
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

        // if (!string.IsNullOrEmpty(stdout))
        //     GD.Print("[Arcane Networking] \n" + stdout);

        // if (!string.IsNullOrEmpty(stderr))
        //     GD.PrintErr("[Arcane Networking] \n" + stderr);

        GD.Print($"[Arcane Networking] Weaver finished with code {proc.ExitCode}");

        return proc.ExitCode == 0;
    }

}
#endif
