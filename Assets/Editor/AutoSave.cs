#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System;
using System.Linq;

[InitializeOnLoad]
public static class AutoSave
{
    const double IntervalSec = 300;             // 5 Minuten
    // Ausserhalb von Assets: Unity importiert die Backups nicht, Git ignoriert den Ordner.
    const string BackupFolder = "SceneBackups";
    const int MaxBackupsPerScene = 20;          // behalte die letzten 20
    static double next;
    static bool pauseReported;

    static AutoSave()
    {
        next = EditorApplication.timeSinceStartup + IntervalSec;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // Tick pauses during Play; without a new interval it would save right after LastPlayedMap restored the map.
    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) next = EditorApplication.timeSinceStartup + IntervalSec;
    }

    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + IntervalSec;

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isDirty || string.IsNullOrEmpty(scene.path)) return;
        // Saving the gameplay scene strips the map kept in the editor (LoadingSceneData): leave that scene to the user,
        // other open scenes and changed assets are still saved.
        var save = ScenesToSave(out bool held);
        if (held)
        {
            // Once per pause, so a session without scene backups does not go unnoticed.
            if (!pauseReported) Debug.Log("[AutoSave] SampleScene pausiert: Dort ist eine Map geladen (nach Play oder «Map im Editor generieren»). Speichern würde sie leeren; Ctrl+S speichert und leert sie. Andere Szenen und Assets speichert AutoSave weiter.");
            pauseReported = true;
        }
        else pauseReported = false;

        if (save.Length > 0 && !EditorSceneManager.SaveScenes(save)) return;
        AssetDatabase.SaveAssets();
        if (!save.Contains(scene)) return;

        Directory.CreateDirectory(BackupFolder);
        var name = Path.GetFileNameWithoutExtension(scene.path);
        var backupPath = $"{BackupFolder}/{name}_{DateTime.Now:yyyyMMdd_HHmmss}.unity";
        File.Copy(scene.path, backupPath, true);

        var files = new DirectoryInfo(BackupFolder)
            .GetFiles($"{name}_*.unity")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        foreach (var old in files.Skip(MaxBackupsPerScene)) { try { old.Delete(); } catch { } }

        Debug.Log($"[AutoSave] Saved + backup: {backupPath}");
    }

    // Dirty open scenes with a path, without the gameplay scene while it holds a map (held = true then).
    internal static Scene[] ScenesToSave(out bool held)
    {
        held = false;
        var save = new System.Collections.Generic.List<Scene>();
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var open = EditorSceneManager.GetSceneAt(i);
            if (!open.isLoaded || !open.isDirty || string.IsNullOrEmpty(open.path)) continue;
            if (open.path == LoadingSceneData.GameplayScene && HoldsMapPreview(open)) { held = true; continue; }
            save.Add(open);
        }
        return save.ToArray();
    }

    // Same layers as LoadingSceneData.Strip: kept after Play stop or generated via "Map im Editor generieren".
    internal static bool HoldsMapPreview(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var map in root.GetComponentsInChildren<MapGenerator>(true))
            if (HasPreviewTiles(map)) return true;
        return false;
    }

    internal static bool HasPreviewTiles(MapGenerator map) =>
        (map.Terrain && map.Terrain.GetUsedTilesCount() > 0) ||
        (map.OreOverlay && map.OreOverlay.GetUsedTilesCount() > 0) ||
        (map.ArtifactOverlay && map.ArtifactOverlay.GetUsedTilesCount() > 0);
}
#endif
