#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Die Testbaenke der Chats ("<Projekt> 2" und "<Projekt> 3", TestBenchNames, Docs/Editor-Warteschlange.md) starten den Play-Mode mit "Play Unfocused":
// Mit "Play Focused" (Unity-Standard) holt Unity beim Play-Start das Game-Fenster nach vorne, und Windows springt
// auf den virtuellen Desktop der Testbank. Der Editor des Benutzers ist davon nicht betroffen. Was trotzdem nach vorne
// kommt, holt der Fokus-Waechter im Dienst der Warteschlange zurueck (Tools/editor-focus.ps1).
[InitializeOnLoad]
public static class TestBenchPlayUnfocused
{
    static readonly Type playModeView = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
    static readonly PropertyInfo behaviorProperty = playModeView?.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    const double CheckSeconds = 2;
    static double nextCheck;

    static TestBenchPlayUnfocused()
    {
        if (Application.isBatchMode || !TestBenchNames.IsBench) return;
        if (behaviorProperty == null || !behaviorProperty.PropertyType.IsEnum)
        {
            Debug.LogWarning("Testbank: PlayModeView.enterPlayModeBehavior nicht gefunden, Play-Mode startet mit Fokus.");
            return;
        }
        // delayCall laeuft im Hintergrund-Editor nicht verlaesslich; neu geoeffnete Game-Fenster ebenfalls erfassen.
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + CheckSeconds;
            Apply();
        };
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Apply(); };
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
