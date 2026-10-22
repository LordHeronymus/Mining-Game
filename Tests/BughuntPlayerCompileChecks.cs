using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

// Edit Mode. Compiles the player scripts (without UNITY_EDITOR) for the active standalone target into Temp,
// so editor-only members used by runtime code (e.g. GameSaveSystem.TestDirectory) break this check instead of a build.
public static class BughuntPlayerCompileChecks
{
    const string Report = "Temp/BughuntPlayerCompileChecks.txt";

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");
        var target = EditorUserBuildSettings.activeBuildTarget;
        var group = BuildPipeline.GetBuildTargetGroup(target);
        if (group != BuildTargetGroup.Standalone || !BuildPipeline.IsBuildTargetSupported(group, target))
        {
            target = Application.platform == RuntimePlatform.WindowsEditor ? BuildTarget.StandaloneWindows64 :
                Application.platform == RuntimePlatform.OSXEditor ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneLinux64;
            group = BuildTargetGroup.Standalone;
        }
        string output = Path.GetFullPath("Temp/BughuntPlayerCompile-" + DateTime.UtcNow.Ticks);
        try
        {
            var result = PlayerBuildInterface.CompilePlayerScripts(
                new ScriptCompilationSettings { target = target, group = group, options = ScriptCompilationOptions.None }, output);
            var assemblies = result.assemblies?.ToArray() ?? Array.Empty<string>();
            File.AppendAllText(Report, "Ziel " + target + ": " + assemblies.Length + " Assemblies\n");
            if (!assemblies.Any(name => string.Equals(name, "Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Spieler-Skripte kompilieren nicht (Assembly-CSharp.dll fehlt, Fehler im Log)");
            File.AppendAllText(Report, "PASS Spieler-Skripte ohne UNITY_EDITOR kompilieren (" + target + ")\n");
            return "PASS player scripts compile for " + target;
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error.Message + "\n");
            throw;
        }
        finally
        {
            try { if (Directory.Exists(output)) Directory.Delete(output, true); } catch (Exception) { }
        }
    }
}
