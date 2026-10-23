using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Edit Mode: builds a Windows IL2CPP player with the MINING_BENCHMARK define (Assets/GameObjects/GameManager/
// PerformanceBenchmarkDriver.cs). The player gets its own product name for the duration of the build, so its
// saves, meta profile, GPS file and PlayerPrefs never touch the real game's data. The project settings are
// restored afterwards. Output folder: first line of Temp/BuildPerformanceBenchmark.args; a second line
// "development" builds a development player instead (profiler capture with -benchmarkProfile <file>).
// Run the player with: "<folder>/Tiefenhall Benchmark.exe" -performanceBenchmark <report.txt> [-benchmarkVsync 1]
// After the build: check ProjectSettings for a diff and run AssetDatabase.SaveAssets so productName stays "Tiefenhall".
public static class BuildPerformanceBenchmark
{
    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit Mode erforderlich.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            throw new Exception("Aktives Build-Ziel ist " + EditorUserBuildSettings.activeBuildTarget + "; ein Wechsel würde Assets neu importieren.");
        var arguments = File.Exists("Temp/BuildPerformanceBenchmark.args") ? File.ReadAllLines("Temp/BuildPerformanceBenchmark.args") : Array.Empty<string>();
        string folder = arguments.Length > 0 ? arguments[0].Trim() : "";
        bool development = arguments.Length > 1 && arguments[1].Trim() == "development";
        if (string.IsNullOrEmpty(folder)) throw new Exception("Ausgabeordner fehlt in Temp/BuildPerformanceBenchmark.args.");
        string product = UnityEditor.PlayerSettings.productName;
        BuildReport result;
        try
        {
            UnityEditor.PlayerSettings.productName = product + " Benchmark";
            result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.Combine(folder, product + " Benchmark.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = new[] { "MINING_BENCHMARK" }
            });
        }
        finally { UnityEditor.PlayerSettings.productName = product; }
        var summary = result.summary;
        string errors = string.Join(" | ", result.steps.SelectMany(step => step.messages)
            .Where(message => message.type == UnityEngine.LogType.Error || message.type == UnityEngine.LogType.Exception).Select(message => message.content).Take(5));
        return $"{summary.result}: {summary.totalTime.TotalSeconds:F0} s, {summary.totalSize / 1048576} MB, Fehler {summary.totalErrors} {errors}";
    }
}
