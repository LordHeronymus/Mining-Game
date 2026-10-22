#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Haelt den Ordner des Benutzers ("Mining Game") automatisch auf dem neuesten origin/main
// (Docs/Main-Auto-Update.md). Jede Minute im Leerlauf: git fetch, dann nur ein Fast-Forward,
// wenn main ausgecheckt ist, keine offene Szene ungespeichert ist und weder Play-Mode noch
// Kompilierung laufen. Lokal geaenderte Dateien bleiben erhalten; muesste Git eine davon
// ueberschreiben, bricht es ab. Danach laedt Unity die Assets neu.
// Waehrend Play-Mode/Kompilierung wird nur geprueft (fetch, nichts geholt): Ein neuer Stand
// erscheint als Hinweis im Game-Fenster und wird direkt nach dem Play-Stopp geholt.
// Den Stand ("main aktuell", "N neue Commits", Grund fuers Aussetzen) meldet EditorQueueActivity
// an die Editor-Warteschlange und damit an das PrivOrg-Modul.
// Testbank ("Mining Game 2") und Worktrees bleiben unberuehrt.
[InitializeOnLoad]
public static class MainAutoUpdate
{
    const string UserFolder = "Mining Game";
    const double IntervalSeconds = 60;
    const double AfterPlaySeconds = 2;        // nach dem Play-Stopp kurz warten, bis der Editor wieder ruhig ist
    const double NoticeRepeatSeconds = 120;   // Hinweis im Game-Fenster bei gleichem Stand hoechstens alle 2 min
    const string EnabledKey = "MiningGame.MainAutoUpdate.Enabled";
    const string MenuPath = "Tools/main automatisch aktualisieren";
    const string NowPath = "Tools/main jetzt aktualisieren";
    const string StateKey = "MiningGame.MainAutoUpdate.State";

    static readonly string projectRoot;
    static double nextCheck;
    static Task<Result> running;
    static string lastSkip;
    static int noticeBehind;
    static double lastNotice = double.NegativeInfinity;

    // Stand fuer die Editor-Warteschlange (EditorQueueActivity schreibt ihn in die Aktivitaetsdatei).
    public static bool Tracked => projectRoot != null;
    public static string State { get; private set; } = "";   // aktuell | veraltet | ausgesetzt | aus | "" (noch nicht geprueft)
    public static string StatusText { get; private set; } = "";
    public static int Behind { get; private set; } = -1;
    public static string CheckedUtc { get; private set; } = "";

    sealed class Result { public string state, text, skip; public int behind = -1; public bool updated; }

    static bool Enabled { get => EditorPrefs.GetBool(EnabledKey, true); set => EditorPrefs.SetBool(EnabledKey, value); }

    static MainAutoUpdate()
    {
        if (Application.isBatchMode) return;
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        if (Path.GetFileName(root) != UserFolder || !Directory.Exists(Path.Combine(root, ".git"))) return;
        projectRoot = root;
        nextCheck = EditorApplication.timeSinceStartup + 20;
        EditorApplication.update += OnUpdate;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) nextCheck = EditorApplication.timeSinceStartup + AfterPlaySeconds;
        };
        // Der Stand ueberlebt das Neuladen der Skripte nach einem Update (SessionState gilt bis zum Editor-Neustart).
        State = SessionState.GetString(StateKey, ""); StatusText = SessionState.GetString(StateKey + ".text", "");
        Behind = SessionState.GetInt(StateKey + ".behind", -1); CheckedUtc = SessionState.GetString(StateKey + ".checked", "");
        if (!Enabled) SetStatus("aus", "automatisches Aktualisieren ausgeschaltet", -1);
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        Debug.Log("main automatisch aktualisieren: " + (Enabled ? "an" : "aus"));
        if (Enabled) nextCheck = 0; else SetStatus("aus", "automatisches Aktualisieren ausgeschaltet", -1);
    }
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
            var result = running.IsFaulted
                ? new Result { state = "ausgesetzt", text = "Fehler: " + running.Exception?.GetBaseException().Message, skip = "Fehler: " + running.Exception?.GetBaseException().Message }
                : running.Result;
            running = null;
            Apply(result);
            return;
        }
        if (!Enabled || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + IntervalSeconds;
        Start(false);
    }

    static void Apply(Result result)
    {
        if (result.state != null) SetStatus(result.state, result.text, result.behind);
        if (result.updated)
        {
            lastSkip = null; noticeBehind = 0;
            Debug.Log("main automatisch aktualisiert, Assets werden neu geladen.");
            AssetDatabase.Refresh();
            return;
        }
        if (result.behind > 0 && EditorApplication.isPlayingOrWillChangePlaymode) NotifyPlaying(result.behind);
        if (result.skip != null) Skip(result.skip);
    }

    static bool Start(bool manual)
    {
        if (running != null) return false;
        string root = projectRoot;
        // Play-Mode oder Kompilierung: nur nachsehen, ob main neuer ist; geholt wird erst danach.
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            lastSkip = "Play-Mode oder Kompilierung läuft";
            running = Task.Run(() => Pull(root, manual, null, playing ? "wird nach dem Play-Stopp geholt" : "wird nach dem Kompilieren geholt"));
            return false;
        }
        string dirtyScene = null;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) { dirtyScene = SceneManager.GetSceneAt(i).name; break; }
        running = Task.Run(() => Pull(root, manual, dirtyScene == null ? null : "Szene " + dirtyScene + " ist nicht gespeichert", null));
        return true;
    }

    // Läuft im Hintergrund. blocked: Grund, warum nicht geholt werden darf (ungespeicherte Szene);
    // waitText: nur pruefen (Play-Mode/Kompilierung), der Text sagt, wann geholt wird.
    static Result Pull(string root, bool manual, string blocked, string waitText)
    {
        if (Git(root, "rev-parse --abbrev-ref HEAD", out string branch) != 0)
            return new Result { state = "ausgesetzt", text = "git nicht verfügbar", skip = "git nicht verfügbar" };
        if (branch != "main")
            return new Result { state = "ausgesetzt", text = "ausgecheckt ist " + branch + ", nicht main", skip = manual ? "ausgecheckt ist " + branch + ", nicht main" : null };
        if (Git(root, "fetch -q origin main", out string fetch) != 0)
            return new Result { state = "ausgesetzt", text = "git fetch fehlgeschlagen", skip = "git fetch fehlgeschlagen: " + fetch };
        Git(root, "rev-list --count HEAD..origin/main", out string behindText);
        int.TryParse(behindText, out int behind);
        if (behind == 0) return new Result { state = "aktuell", text = "main aktuell", behind = 0, skip = manual ? "bereits aktuell" : null };
        string commits = behind + (behind == 1 ? " neuer Commit" : " neue Commits") + " auf main";
        Git(root, "rev-list --count origin/main..HEAD", out string ahead);
        if (ahead != "0")
        {
            string reason = "lokales main hat eigene Commits (" + ahead + "), kein Fast-Forward";
            return new Result { state = "ausgesetzt", text = commits + ", nicht geholt: " + reason, behind = behind, skip = reason };
        }
        if (waitText != null) return new Result { state = "veraltet", text = commits + ", " + waitText, behind = behind };
        if (blocked != null) return new Result { state = "ausgesetzt", text = commits + ", nicht geholt: " + blocked, behind = behind, skip = blocked };
        // Lokale Änderungen blockieren nicht: Git bricht den Fast-Forward selbst ab, bevor es eine geänderte Datei überschreiben müsste.
        if (Git(root, "merge --ff-only -q origin/main", out string merge) == 0)
            return new Result { state = "aktuell", text = "main aktuell", behind = 0, updated = true };
        bool conflict = merge.Contains("would be overwritten") || merge.Contains("untracked working tree files");
        return new Result
        {
            state = "ausgesetzt", behind = behind,
            text = commits + ", nicht geholt: " + (conflict ? "neue Commits ändern lokal geänderte Dateien" : "Fast-Forward fehlgeschlagen"),
            skip = conflict ? "neue Commits ändern Dateien, die hier lokal geändert sind, nichts übernommen:\n" + merge : "Fast-Forward fehlgeschlagen: " + merge,
        };
    }

    // Hinweis oben im Game-Fenster: der Benutzer sieht, warum eine gemergte Änderung noch fehlt.
    static void NotifyPlaying(int behind)
    {
        double now = EditorApplication.timeSinceStartup;
        if (behind == noticeBehind && now - lastNotice < NoticeRepeatSeconds) return;
        if (behind != noticeBehind) Debug.Log("main: " + behind + (behind == 1 ? " neuer Commit" : " neue Commits") + ", wird nach dem Play-Stopp geholt.");
        noticeBehind = behind; lastNotice = now;
        var text = new GUIContent("Neuer Stand auf main (" + behind + (behind == 1 ? " Commit" : " Commits") + ")\nPlay beenden zum Aktualisieren");
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (window.GetType().Name == "GameView") window.ShowNotification(text, 8);
    }

    static void SetStatus(string state, string text, int behind)
    {
        CheckedUtc = DateTime.UtcNow.ToString("o"); SessionState.SetString(StateKey + ".checked", CheckedUtc);
        if (state == State && text == StatusText && behind == Behind) return;
        State = state; StatusText = text; Behind = behind;
        SessionState.SetString(StateKey, state); SessionState.SetString(StateKey + ".text", text); SessionState.SetInt(StateKey + ".behind", behind);
        EditorQueueActivity.RequestWrite();
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
        psi.Environment["LC_ALL"] = "C"; // englische Git-Meldungen, damit Pull() sie erkennt
        using var process = Process.Start(psi);
        var error = process.StandardError.ReadToEndAsync();
        output = process.StandardOutput.ReadToEnd().Trim();
        if (!process.WaitForExit(120000)) { try { process.Kill(); } catch { } output = "Zeitlimit"; return -1; }
        if (process.ExitCode != 0) output = (output + " " + error.Result).Trim();
        return process.ExitCode;
    }
}
#endif
