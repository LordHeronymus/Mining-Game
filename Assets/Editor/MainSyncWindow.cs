#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools → main-Sync: Stand der automatischen main-Aktualisierung im Benutzerordner (MainAutoUpdate), was blockiert und warum,
// lokale Änderungen, eigene GPS-Werte und das Protokoll. Kurzfassung steht zusätzlich im Titel des Editorfensters.
public sealed class MainSyncWindow : EditorWindow
{
    Vector2 scroll;

    [MenuItem("Tools/main-Sync", false, 0)]
    public static void Open() { var window = GetWindow<MainSyncWindow>("main-Sync"); window.minSize = new Vector2(460, 360); window.Show(); }

    void OnEnable() { MainAutoUpdate.StatusChanged += Repaint; MainSyncLog.Changed += Repaint; GpsUserOverrides.Changed += Repaint; }
    void OnDisable() { MainAutoUpdate.StatusChanged -= Repaint; MainSyncLog.Changed -= Repaint; GpsUserOverrides.Changed -= Repaint; }

    void OnGUI()
    {
        var wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        if (!MainAutoUpdate.Tracked)
        {
            EditorGUILayout.HelpBox("Die automatische main-Aktualisierung läuft nur im Ordner \"Mining Game\" (Benutzerordner).\nDieser Ordner: " +
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")), MessageType.Info);
            return;
        }
        var (color, label) = MainAutoUpdate.State switch
        {
            "aktuell" => (new Color(.35f, .75f, .4f), "Aktuell"),
            "veraltet" => (new Color(.9f, .7f, .25f), "Neuer Stand wartet"),
            "ausgesetzt" => (new Color(.9f, .35f, .3f), "Blockiert"),
            "aus" => (Color.gray, "Ausgeschaltet"),
            _ => (Color.gray, "Wird geprüft"),
        };
        var header = GUILayoutUtility.GetRect(1, 30, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(header.x, header.y + 4, 6, 22), color);
        GUI.Label(new Rect(header.x + 14, header.y + 2, header.width - 14, 26), label, new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
        GUILayout.Label(MainAutoUpdate.StatusText, wrap);
        if (DateTime.TryParse(MainAutoUpdate.CheckedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedAt))
            GUILayout.Label("Zuletzt geprüft: " + checkedAt.ToLocalTime().ToString("HH:mm:ss") + (MainAutoUpdate.Busy ? "  (läuft …)" : ""), EditorStyles.miniLabel);
        if (MainAutoUpdate.State == "ausgesetzt")
            EditorGUILayout.HelpBox("Solange blockiert, zeigt dieser Editor nicht den neuesten Stand. Nach dem Beheben \"Jetzt aktualisieren\" drücken.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(MainAutoUpdate.Busy)) if (GUILayout.Button("Jetzt aktualisieren")) MainAutoUpdate.UpdateNow();
            bool enabled = GUILayout.Toggle(MainAutoUpdate.Enabled, "Automatisch", "Button");
            if (enabled != MainAutoUpdate.Enabled) EditorApplication.ExecuteMenuItem("Tools/main automatisch aktualisieren");
            if (GUILayout.Button("Protokoll öffnen")) { if (File.Exists(MainSyncLog.FilePath)) EditorUtility.OpenWithDefaultApp(MainSyncLog.FilePath); }
        }

        GUILayout.Space(8);
        GUILayout.Label("Eigene GPS-Werte", EditorStyles.boldLabel);
        int count = GpsUserOverrides.Count;
        GUILayout.Label(count == 0 ? "Keine. Änderungen im F1-/Debug-Fenster landen hier, nicht im versionierten Profil." :
            count + (count == 1 ? " eigener Wert" : " eigene Werte") + " in UserSettings/GpsOverrides.json (nur lokal, über den Standardwerten von main).", wrap);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Debug Settings öffnen")) GameplaySettingsWindow.Open();
            using (new EditorGUI.DisabledScope(count == 0)) if (GUILayout.Button("Als Standard vorschlagen")) GpsUserOverrides.Propose?.Invoke();
        }

        GUILayout.Space(8);
        GUILayout.Label("Lokale Änderungen", EditorStyles.boldLabel);
        var local = MainAutoUpdate.LocalChanges;
        GUILayout.Label(local.Count == 0 ? "Keine geänderten versionierten Dateien." :
            "Bleiben stehen, solange main sie nicht ändert; sonst werden sie gesichert (" + MainSyncGit.BackupBranch + ") und durch main ersetzt:\n  " +
            string.Join("\n  ", local.Take(8)) + (local.Count > 8 ? "\n  … (" + local.Count + ")" : ""), wrap);

        GUILayout.Space(8);
        GUILayout.Label("Protokoll", EditorStyles.boldLabel);
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (string line in MainSyncLog.Recent.Reverse().Take(60)) GUILayout.Label(line, new GUIStyle(EditorStyles.miniLabel) { wordWrap = true });
        GUILayout.EndScrollView();
    }
}
#endif
