#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System;
using System.Linq;

[InitializeOnLoad]
public static class AutoSave
{
    const double IntervalSec = 300;             // 5 Minuten
    const string BackupFolder = "Assets/_SceneBackups";
    const int MaxBackupsPerScene = 20;          // behalte die letzten 20
    static double next;

    static AutoSave()
    {
        next = EditorApplication.timeSinceStartup + IntervalSec;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + IntervalSec;

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || (!scene.isDirty && !AssetDatabase.IsOpenForEdit(""))) return;

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        if (!Directory.Exists(BackupFolder)) Directory.CreateDirectory(BackupFolder);
        var name = string.IsNullOrEmpty(scene.path) ? "UnsavedScene" : Path.GetFileNameWithoutExtension(scene.path);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = $"{BackupFolder}/{name}_{stamp}.unity";

        EditorSceneManager.SaveScene(scene, backupPath, true);

        var files = new DirectoryInfo(BackupFolder)
            .GetFiles($"{name}_*.unity")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        foreach (var old in files.Skip(MaxBackupsPerScene)) { try { old.Delete(); } catch { } }

        Debug.Log($"[AutoSave] Saved + backup: {backupPath}");
    }
}
#endif
