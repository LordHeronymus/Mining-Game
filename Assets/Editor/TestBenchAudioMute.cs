#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Die Testbank der Chats ("<Projekt> 2", Docs/Editor-Warteschlange.md) laeuft stumm, damit Tests den
// Benutzer nicht stoeren. Ton nur fuer Audio-Tests: Tools/editor-bench.sh audio-on (oder Menue
// Tools/Testbank/Ton an) legt <Git Repos>/EditorQueue/<Testbank>.audio-on an; checkout, release und
// audio-off entfernen die Datei wieder. Der Editor des Benutzers ist davon nicht betroffen.
[InitializeOnLoad]
public static class TestBenchAudioMute
{
    const double CheckSeconds = 1;
    const string MenuRoot = "Tools/Testbank/";

    static readonly bool isBench;
    static readonly string markerPath;
    static double nextCheck;

    static TestBenchAudioMute()
    {
        if (Application.isBatchMode) return;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        var owner = Regex.Match(projectRoot, @"^(.*?)[\\/]\.claude[\\/]worktrees[\\/]");
        string ownerRoot = owner.Success ? owner.Groups[1].Value : projectRoot;
        string projectName = Path.GetFileName(ownerRoot);
        // Gleiche Regel wie Tools/editor-bench.ps1: die Testbank ist der Ordner "<Projekt> 2".
        isBench = !owner.Success && projectName.EndsWith(" 2", StringComparison.Ordinal);
        if (!isBench) return;
        string env = Environment.GetEnvironmentVariable("EDITOR_QUEUE_DIR");
        string queueDir = !string.IsNullOrEmpty(env) ? env : Path.Combine(Path.GetDirectoryName(ownerRoot) ?? ownerRoot, "EditorQueue");
        markerPath = Path.Combine(queueDir, projectName + ".audio-on");
        Apply();
        EditorApplication.update += OnUpdate;
        EditorApplication.playModeStateChanged += _ => Apply();
    }

    static bool AudioAllowed => markerPath != null && File.Exists(markerPath);

    static void OnUpdate()
    {
        double t = EditorApplication.timeSinceStartup;
        if (t < nextCheck) return;
        nextCheck = t + CheckSeconds;
        Apply();
    }

    static void Apply()
    {
        bool mute = !AudioAllowed;
        if (EditorUtility.audioMasterMute != mute) EditorUtility.audioMasterMute = mute;
    }

    [MenuItem(MenuRoot + "Ton an (Audio-Test)")]
    static void AudioOn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
        File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
        Apply();
    }

    [MenuItem(MenuRoot + "Ton an (Audio-Test)", true)]
    static bool AudioOnValid() => isBench && !AudioAllowed;

    [MenuItem(MenuRoot + "Ton aus")]
    static void AudioOff()
    {
        if (File.Exists(markerPath)) File.Delete(markerPath);
        Apply();
    }

    [MenuItem(MenuRoot + "Ton aus", true)]
    static bool AudioOffValid() => isBench && AudioAllowed;
}
#endif
