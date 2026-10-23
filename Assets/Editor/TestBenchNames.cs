#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// Welche Arbeitsordner Testbaenke der Chats sind (Docs/Editor-Warteschlange.md): "<Projekt> 2" und "<Projekt> 3",
// nie ein Worktree unter .claude/worktrees. Gleiche Regel wie $BenchPattern in Tools/editor-queue.ps1 und
// Tools/editor-bench.ps1. editor-bench prueft beim Holen eines Stands auf Testbank 3, ob es diese Datei gibt:
// aeltere Branches kennen nur "<Projekt> 2" und liefen dort mit Ton und auf dem falschen Desktop.
public static class TestBenchNames
{
    static readonly Regex BenchName = new Regex(@" ([23])$");

    // Ordner des Projekts (bzw. des Besitzers bei Worktrees), sein Name und die Nummer der Testbank (0 = keine Testbank).
    public static string ProjectRoot { get; }
    public static string OwnerRoot { get; }
    public static string FolderName { get; }
    public static int Number { get; }
    public static bool IsBench => Number > 0;

    static TestBenchNames()
    {
        ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        var owner = Regex.Match(ProjectRoot, @"^(.*?)[\\/]\.claude[\\/]worktrees[\\/]");
        OwnerRoot = owner.Success ? owner.Groups[1].Value : ProjectRoot;
        FolderName = Path.GetFileName(OwnerRoot);
        var m = BenchName.Match(FolderName);
        Number = !owner.Success && m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    public static string QueueDir
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("EDITOR_QUEUE_DIR");
            return !string.IsNullOrEmpty(env) ? env : Path.Combine(Path.GetDirectoryName(OwnerRoot) ?? OwnerRoot, "EditorQueue");
        }
    }
}
#endif
