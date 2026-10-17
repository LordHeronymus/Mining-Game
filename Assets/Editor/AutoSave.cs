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
    // Ausserhalb von Assets: Unity importiert die Backups nicht, Git ignoriert den Ordner.
    const string BackupFolder = "SceneBackups";
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
        if (!scene.IsValid() || !scene.isDirty || string.IsNullOrEmpty(scene.path)) return;

        if (!EditorSceneManager.SaveOpenScenes()) return;
        AssetDatabase.SaveAssets();

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
}
#endif
