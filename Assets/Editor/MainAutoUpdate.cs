#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Haelt den Ordner des Benutzers ("Mining Game") automatisch auf dem neuesten origin/main
// (Docs/Main-Auto-Update.md). Jede Minute im Leerlauf: git fetch, dann nur ein Fast-Forward,
// wenn main ausgecheckt ist, keine getrackten Dateien geaendert sind, keine offene Szene
// ungespeichert ist und weder Play-Mode noch Kompilierung laufen. Danach laedt Unity die Assets neu.
// Testbank ("Mining Game 2") und Worktrees bleiben unberuehrt.
[InitializeOnLoad]
public static class MainAutoUpdate
{
    const string UserFolder = "Mining Game";
    const double IntervalSeconds = 60;
    const string EnabledKey = "MiningGame.MainAutoUpdate.Enabled";
    const string MenuPath = "Tools/main automatisch aktualisieren";
    const string NowPath = "Tools/main jetzt aktualisieren";

    static readonly string projectRoot;
    static double nextCheck;
    static Task<string> running;
    static string lastSkip;

    static bool Enabled { get => EditorPrefs.GetBool(EnabledKey, true); set => EditorPrefs.SetBool(EnabledKey, value); }

    static MainAutoUpdate()
    {
        if (Application.isBatchMode) return;
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        if (Path.GetFileName(root) != UserFolder || !Directory.Exists(Path.Combine(root, ".git"))) return;
        projectRoot = root;
        nextCheck = EditorApplication.timeSinceStartup + 20;
        EditorApplication.update += OnUpdate;
    }

    [MenuItem(MenuPath)]
    static void Toggle() { Enabled = !Enabled; Debug.Log("main automatisch aktualisieren: " + (Enabled ? "an" : "aus")); }
    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return projectRoot != null; }

    [MenuItem(NowPath)]
    static void UpdateNow() { lastSkip = null; if (!Start(true)) Debug.Log("main aktualisieren: " + (lastSkip ?? "läuft bereits")); }
    [MenuItem(NowPath, true)]
    static bool UpdateNowValidate() => projectRoot != null;

    static void OnUpdate()
    {
        if (running != null)
        {
            if (!running.IsCompleted) return;
            string result = running.IsFaulted ? "Fehler: " + running.Exception?.GetBaseException().Message : running.Result;
            running = null;
            if (result == "updated") { lastSkip = null; Debug.Log("main automatisch aktualisiert, Assets werden neu geladen."); AssetDatabase.Refresh(); }
            else if (result != null) Skip(result);
            return;
        }
        if (!Enabled || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + IntervalSeconds;
        Start(false);
    }

    static bool Start(bool manual)
    {
        if (running != null) return false;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        { lastSkip = "Play-Mode oder Kompilierung läuft"; return false; }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) { Skip("Szene " + SceneManager.GetSceneAt(i).name + " ist nicht gespeichert"); return false; }
        string root = projectRoot;
        running = Task.Run(() => Pull(root, manual));
        return true;
    }

    // Läuft im Hintergrund; gibt "updated", null (nichts zu tun) oder einen Grund zum Aussetzen zurück.
    static string Pull(string root, bool manual)
    {
        if (Git(root, "rev-parse --abbrev-ref HEAD", out string branch) != 0) return "git nicht verfügbar";
        if (branch != "main") return manual ? "ausgecheckt ist " + branch + ", nicht main" : null;
        if (Git(root, "fetch -q origin main", out string fetch) != 0) return "git fetch fehlgeschlagen: " + fetch;
        Git(root, "rev-list --count HEAD..origin/main", out string behind);
        if (behind == "0") return manual ? "bereits aktuell" : null;
        Git(root, "rev-list --count origin/main..HEAD", out string ahead);
        if (ahead != "0") return "lokales main hat eigene Commits (" + ahead + "), kein Fast-Forward";
        Git(root, "status --porcelain --untracked-files=no", out string changes);
        if (changes.Length > 0) return "geänderte Dateien im Ordner, nichts übernommen:\n" + changes;
        return Git(root, "merge --ff-only -q origin/main", out string merge) == 0 ? "updated" : "Fast-Forward fehlgeschlagen: " + merge;
    }

    static void Skip(string reason)
    {
        if (reason == lastSkip) return;
        lastSkip = reason;
        Debug.Log("main automatisch aktualisieren ausgesetzt: " + reason);
    }

    static int Git(string root, string args, out string output)
    {
        var psi = new ProcessStartInfo("git", "-C \"" + root + "\" " + args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi);
        var error = process.StandardError.ReadToEndAsync();
        output = process.StandardOutput.ReadToEnd().Trim();
        if (!process.WaitForExit(120000)) { try { process.Kill(); } catch { } output = "Zeitlimit"; return -1; }
        if (process.ExitCode != 0) output = (output + " " + error.Result).Trim();
        return process.ExitCode;
    }
}
#endif
