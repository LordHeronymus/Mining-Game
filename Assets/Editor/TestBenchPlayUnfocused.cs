#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Die Testbank der Chats ("<Projekt> 2", Docs/Editor-Warteschlange.md) startet den Play-Mode mit "Play Unfocused":
// Mit "Play Focused" (Unity-Standard) holt Unity beim Play-Start das Game-Fenster nach vorne, und Windows springt
// auf den virtuellen Desktop der Testbank. Der Editor des Benutzers ist davon nicht betroffen. Was trotzdem nach vorne
// kommt, holt der Fokus-Waechter im Dienst der Warteschlange zurueck (Tools/editor-focus.ps1).
[InitializeOnLoad]
public static class TestBenchPlayUnfocused
{
    static readonly Type playModeView = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
    static readonly PropertyInfo behaviorProperty = playModeView?.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    static TestBenchPlayUnfocused()
    {
        if (Application.isBatchMode || !IsBench()) return;
        if (behaviorProperty == null || !behaviorProperty.PropertyType.IsEnum)
        {
            Debug.LogWarning("Testbank: PlayModeView.enterPlayModeBehavior nicht gefunden, Play-Mode startet mit Fokus.");
            return;
        }
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Apply(); };
    }

    // Gleiche Regel wie Tools/editor-bench.ps1 und TestBenchAudioMute: die Testbank ist der Ordner "<Projekt> 2".
    static bool IsBench()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        bool worktree = Regex.IsMatch(projectRoot, @"[\\/]\.claude[\\/]worktrees[\\/]");
        return !worktree && Path.GetFileName(projectRoot).EndsWith(" 2", StringComparison.Ordinal);
    }

    static void Apply()
    {
        object unfocused;
        try { unfocused = Enum.Parse(behaviorProperty.PropertyType, "PlayUnfocused"); }
        catch (ArgumentException) { return; }
        foreach (var view in Resources.FindObjectsOfTypeAll(playModeView))
            if (!Equals(behaviorProperty.GetValue(view), unfocused)) behaviorProperty.SetValue(view, unfocused);
    }
}
#endif
