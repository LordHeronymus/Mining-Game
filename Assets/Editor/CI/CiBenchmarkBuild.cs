using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MiningGame.CI
{
    // Builds a Linux benchmark player for the cloud (Tests/CI/run-in-container.sh, suite "performance").
    // Same idea as Tests/BuildPerformanceBenchmark.cs (Windows/IL2CPP, local): MINING_BENCHMARK define and its
    // own product name, so PerformanceBenchmarkDriver runs the benchmark phases and the data stays separate.
    // Mono instead of IL2CPP: the GameCI base image ships Linux Mono build support only.
    public static class CiBenchmarkBuild
    {
        public const string PlayerName = "TiefenhallBenchmark";

        public static void Build()
        {
            string folder = Argument("-ciBuildPath") ?? "Artifacts/player";
            string product = PlayerSettings.productName;
            var standalone = NamedBuildTarget.Standalone;
            var backend = PlayerSettings.GetScriptingBackend(standalone);
            int code = 1;
            try
            {
                Directory.CreateDirectory(folder);
                PlayerSettings.productName = product + " Benchmark";
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                    locationPathName = Path.Combine(folder, PlayerName + ".x86_64"),
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { "MINING_BENCHMARK" }
                });
                var summary = report.summary;
                Debug.Log($"[CI] Benchmark build {summary.result}: {summary.totalTime.TotalSeconds:F0} s, {summary.totalSize / 1048576} MB, {summary.totalErrors} errors");
                code = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 2;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.SetScriptingBackend(standalone, backend);
            }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
