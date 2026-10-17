using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;

public static class MetaProgressionPlayerCompile
{
    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Compile player scripts in Edit Mode.");
        string output = Path.GetFullPath("Temp/MetaProgressionPlayerAssemblies");
        Directory.CreateDirectory(output);
        var target = EditorUserBuildSettings.activeBuildTarget;
        var settings = new ScriptCompilationSettings { target = target, group = BuildPipeline.GetBuildTargetGroup(target) };
        var result = PlayerBuildInterface.CompilePlayerScripts(settings, output);
        if (result.assemblies == null || result.assemblies.Count == 0) throw new Exception("Player script compilation returned no assemblies.");
        File.WriteAllText("Temp/MetaProgressionPlayerCompile.txt", "PASS " + result.assemblies.Count + " player assemblies; " + target + "\n");
        return new { assemblies = result.assemblies.Count, target = target.ToString(), output };
    }
}
