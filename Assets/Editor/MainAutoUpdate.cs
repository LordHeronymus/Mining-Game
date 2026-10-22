#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Haelt den Ordner des Benutzers ("Mining Game") automatisch auf dem neuesten origin/main (Docs/Main-Auto-Update.md).
// Jede Minute im Leerlauf: git fetch und nachsehen (MainSyncGit.Inspect, Hintergrund). Ist main neuer oder weicht das
// lokale main ab, wird im Edit Mode aktualisiert (MainSyncGit.Update, Hintergrund), nie waehrend Play-Mode oder Kompilierung:
// - Lokale Commits auf main und lokal geaenderte Dateien, die main ebenfalls aendert, kommen zuerst als Sicherung auf
//   backup/mining-game-auto; dann folgt main origin/main. Andere lokale Aenderungen bleiben stehen.
// - Eigene GPS-Werte in GpsProfile.asset (lokal geaendert oder in lokalen Commits) gehen vorher in die nicht versionierte
//   Ueberschreibungs-Schicht (GpsUserOverrides); das Profil selbst wird danach auf den Stand von main gesetzt.
// - Ist eine offene, unveraenderte Szene betroffen, wird sie fuer die Aktualisierung kurz geschlossen und danach neu
//   geoeffnet (kein "modified externally"-Dialog). Eine Szene mit ungespeicherten Aenderungen, die main aendert, blockiert.
// - Danach AssetDatabase.Refresh (Skripte kompilieren, Assets neu laden) und GPS neu einlesen.
// Waehrend Play-Mode/Kompilierung wird nur geprueft: Ein neuer Stand erscheint als Hinweis im Game-Fenster und wird direkt
// nach dem Play-Stopp geholt. Stand: Fenstertitel, Tools → main-Sync, Editor-Warteschlange/PrivOrg (EditorQueueActivity);
// Protokoll: Logs/main-sync.log. Testbank ("Mining Game 2") und Worktrees bleiben unberuehrt.
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
    static Task<MainSyncGit.Status> inspecting;
    static Task<MainSyncGit.Outcome> updating;
    static bool manualRun;
    static string lastSkip;
    static int noticeBehind;
    static double lastNotice = double.NegativeInfinity;
    static MainSyncGit.Status pending;
    static SceneSetup[] closedScenes;
    static bool gpsTouched;

    // Stand fuer Editor-Warteschlange und Statusfenster.
    public static bool Tracked => projectRoot != null;
    public static string ProjectRoot => projectRoot;
    public static bool Busy => inspecting != null || updating != null;
    public static string State { get; private set; } = "";   // aktuell | veraltet | ausgesetzt | aus | "" (noch nicht geprueft)
    public static string StatusText { get; private set; } = "";
    public static int Behind { get; private set; } = -1;
    public static string CheckedUtc { get; private set; } = "";
    public static string Head { get; private set; } = "";
    public static string Subject { get; private set; } = "";
    public static IReadOnlyList<string> LocalChanges { get; private set; } = Array.Empty<string>();
    public static event Action StatusChanged;

    public static bool Enabled { get => EditorPrefs.GetBool(EnabledKey, true); set => EditorPrefs.SetBool(EnabledKey, value); }

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
            // Waehrend Git Dateien tauscht, darf kein Play starten (halb aktualisierter Stand).
            if (state == PlayModeStateChange.ExitingEditMode && updating != null)
            {
                EditorApplication.isPlaying = false;
                Notify("main wird gerade aktualisiert – Play gleich nochmals starten");
            }
        };
        EditorApplication.updateMainWindowTitle += descriptor => { if (projectRoot != null) descriptor.title += "   ·   " + ShortStatus; };
        // Der Stand ueberlebt das Neuladen der Skripte nach einem Update (SessionState gilt bis zum Editor-Neustart).
        State = SessionState.GetString(StateKey, ""); StatusText = SessionState.GetString(StateKey + ".text", "");
        Behind = SessionState.GetInt(StateKey + ".behind", -1); CheckedUtc = SessionState.GetString(StateKey + ".checked", "");
        Head = SessionState.GetString(StateKey + ".head", ""); Subject = SessionState.GetString(StateKey + ".subject", "");
        if (!Enabled) SetStatus("aus", "automatisches Aktualisieren ausgeschaltet", -1);
        EditorApplication.delayCall += EditorApplication.UpdateMainWindowTitle;
    }

    public static string ShortStatus => State switch
    {
        "aktuell" => "main aktuell @ " + Short(Head),
        "veraltet" => "main: " + Behind + (Behind == 1 ? " neuer Commit" : " neue Commits") + " wartet",
        "ausgesetzt" => "main blockiert – Tools → main-Sync",
        "aus" => "main-Sync aus",
        _ => "main wird geprüft",
    };
    public static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(7, sha.Length));

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        MainSyncLog.Write("Automatisches Aktualisieren " + (Enabled ? "eingeschaltet" : "ausgeschaltet"));
        if (Enabled) nextCheck = 0; else SetStatus("aus", "automatisches Aktualisieren ausgeschaltet", -1);
    }
    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return projectRoot != null; }

    [MenuItem(NowPath)]
    public static void UpdateNow() { lastSkip = null; if (!Start(true)) Debug.Log("main aktualisieren: läuft bereits"); }
    [MenuItem(NowPath, true)]
    static bool UpdateNowValidate() => projectRoot != null;

    static void OnUpdate()
    {
        if (inspecting != null)
        {
            if (!inspecting.IsCompleted) return;
            var status = inspecting.IsFaulted ? new MainSyncGit.Status { error = "Fehler: " + inspecting.Exception?.GetBaseException().Message } : inspecting.Result;
            inspecting = null;
            Inspected(status);
            return;
        }
        if (updating != null)
        {
            if (!updating.IsCompleted) return;
            var outcome = updating.IsFaulted ? new MainSyncGit.Outcome { error = "Fehler: " + updating.Exception?.GetBaseException().Message } : updating.Result;
            updating = null;
            Updated(outcome);
            return;
        }
        if (!Enabled || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + IntervalSeconds;
        Start(false);
    }

    static bool Start(bool manual)
    {
        if (Busy) return false;
        manualRun = manual;
        string root = projectRoot;
        inspecting = Task.Run(() => MainSyncGit.Inspect(root, true));
        return true;
    }

    static bool EditorBusy => EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;

    // Hauptthread: entscheiden, ob und wie aktualisiert wird.
    static void Inspected(MainSyncGit.Status status)
    {
        if (status.error != null) { Block(status.error, -1); return; }
        if (status.branch != "main") { Block("ausgecheckt ist " + status.branch + ", nicht main", -1, quiet: !manualRun); return; }
        if (status.busy != null) { Block(status.busy, -1); return; }
        Head = status.head; Subject = status.subject;
        SessionState.SetString(StateKey + ".head", Head); SessionState.SetString(StateKey + ".subject", Subject);
        LocalChanges = status.dirty.Where(path => !status.conflicts.Contains(path) && path != MainSyncGit.GpsProfilePath).ToList();
        if (!status.NeedsUpdate) { Current(manualRun ? "bereits aktuell" : null); return; }

        string commits = status.behind + (status.behind == 1 ? " neuer Commit" : " neue Commits") + " auf main";
        if (EditorBusy)
        {
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            string text = (status.behind > 0 ? commits : "lokales main weicht ab") + ", " + (playing ? "wird nach dem Play-Stopp geholt" : "wird nach dem Kompilieren geholt");
            SetStatus("veraltet", text, status.behind);
            if (status.behind > 0 && playing) NotifyPlaying(status.behind);
            return;
        }
        // Ungespeicherte Szene, die main aendert: nicht ueberschreiben, das waere stiller Datenverlust.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty && status.incoming.Contains(scene.path))
            {
                Block(commits + ", nicht geholt: Szene " + scene.name + " hat ungespeicherte Änderungen und wird von main geändert (speichern oder verwerfen)", status.behind);
                Notify("main wartet: Szene " + scene.name + " speichern oder verwerfen");
                return;
            }
        }
        gpsTouched = status.gpsDirty || status.gpsInLocalCommits || status.incoming.Contains(MainSyncGit.GpsProfilePath);
        // Offene GPS-Änderungen zuerst speichern, solange das Profil noch dem geladenen Stand entspricht.
        if (gpsTouched) { GpsSettings.CommitPendingNow(); if (GpsSettings.HasUnsavedChanges) GpsSettings.Save(out _); }
        var extra = new List<string>();
        if (status.gpsDirty || status.gpsInLocalCommits)
        {
            if (TakeOverGpsValues(status)) { if (status.gpsDirty) extra.Add(MainSyncGit.GpsProfilePath); }
            else if (!status.conflicts.Contains(MainSyncGit.GpsProfilePath) && status.behind == 0 && status.ahead == 0) { gpsTouched = false; Current(null); return; }
        }
        CloseAffectedScenes(status);
        pending = status;
        string root = projectRoot;
        string reason = (status.behind > 0 ? commits : "main angleichen") + (status.ahead > 0 ? ", " + status.ahead + " lokale Commits" : "");
        SetStatus("veraltet", (status.behind > 0 ? commits : "lokales main weicht ab") + ", wird jetzt geholt", status.behind);
        updating = Task.Run(() => MainSyncGit.Update(root, status, reason, extra));
    }

    // Hauptthread nach den Git-Schritten: Unity neu laden, Szenen wieder oeffnen, protokollieren.
    static void Updated(MainSyncGit.Outcome outcome)
    {
        var status = pending; pending = null;
        if (outcome.backup != null)
        {
            var parts = new List<string>();
            if (status.ahead > 0) parts.Add(status.ahead + (status.ahead == 1 ? " lokaler Commit" : " lokale Commits"));
            if (outcome.restored.Count > 0) parts.Add("lokale Fassung von " + string.Join(", ", outcome.restored));
            MainSyncLog.Write("Gesichert auf " + MainSyncGit.BackupBranch + " @ " + Short(outcome.backup) + ": " + string.Join("; ", parts), true);
        }
        if (!outcome.ok)
        {
            // Teilweise geänderte Dateien (z. B. zurückgesetztes GpsProfile) trotzdem neu einlesen.
            AssetDatabase.Refresh();
            ReopenScenes();
            if (gpsTouched && !EditorApplication.isPlayingOrWillChangePlaymode) GpsSettings.ReloadFromProfile();
            gpsTouched = false; GpsUserOverrides.Hold = false;
            Block("Aktualisierung fehlgeschlagen: " + outcome.error, status.behind);
            return;
        }
        Head = outcome.head; Subject = status.subject;
        SessionState.SetString(StateKey + ".head", Head); SessionState.SetString(StateKey + ".subject", Subject);
        if (status.head != status.target)
            MainSyncLog.Write("Aktualisiert " + Short(status.head) + " → " + Short(outcome.head) + " (" + status.behind + (status.behind == 1 ? " Commit" : " Commits") + "): " + Subject, true);
        lastSkip = null; noticeBehind = 0;
        AssetDatabase.Refresh();
        ReopenScenes();
        if (gpsTouched && !EditorApplication.isPlayingOrWillChangePlaymode) GpsSettings.ReloadFromProfile();
        gpsTouched = false; GpsUserOverrides.Hold = false;
        Current(null);
        if (outcome.backup != null) Notify("main aktualisiert – lokale Änderungen gesichert (" + MainSyncGit.BackupBranch + ")");
    }

    // Eigene GPS-Werte aus GpsProfile.asset (lokale Aenderung oder lokale Commits) in die Ueberschreibungs-Schicht uebernehmen.
    // Bestehende eigene Werte gehen vor: Sie lagen schon ueber dem lokalen Profil und waren damit das, was der Benutzer sah.
    static bool TakeOverGpsValues(MainSyncGit.Status status)
    {
        if (!GpsUserOverrides.Active) return false;
        GpsDocument local, baseline;
        try { local = GpsUserOverrides.ParseProfileYaml(status.gpsLocalYaml); baseline = GpsUserOverrides.ParseProfileYaml(status.gpsBaseYaml); }
        catch (Exception ex) { local = baseline = null; MainSyncLog.Write("GpsProfile.asset nicht lesbar: " + ex.Message); }
        if (local == null || baseline == null)
        {
            MainSyncLog.Write("Eigene GPS-Werte nicht übernommen: GpsProfile.asset nicht lesbar. Die lokale Fassung bleibt bzw. kommt in die Sicherung.", true);
            return false;
        }
        var found = GpsUserOverrides.Diff(local, baseline, GpsSettings.Profile);
        var merged = GpsUserOverrides.Merge(GpsUserOverrides.Entries, found);
        int added = merged.Count - GpsUserOverrides.Count;
        try { GpsUserOverrides.Write(merged); }
        catch (Exception ex) { MainSyncLog.Write("Eigene GPS-Werte nicht gespeichert: " + ex.Message, true); return false; }
        // Bis das Profil zurückgesetzt und neu geladen ist, stehen diese Werte noch im geladenen Profil; ein Speichern
        // dazwischen darf sie nicht als "gleich wie Standard" aus der Datei werfen.
        GpsUserOverrides.Hold = true;
        if (added > 0)
            MainSyncLog.Write(added + (added == 1 ? " eigener GPS-Wert" : " eigene GPS-Werte") + " aus GpsProfile.asset übernommen: " +
                string.Join(", ", found.Take(12).Select(entry => entry.record.Split(':').Last() + "/" + entry.path + " = " + GpsUserOverrides.Describe(entry.value))) +
                (found.Count > 12 ? " …" : ""), true);
        return true;
    }

    static void CloseAffectedScenes(MainSyncGit.Status status)
    {
        closedScenes = null;
        bool affected = false, dirty = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            affected |= status.incoming.Contains(scene.path) || status.conflicts.Contains(scene.path);
            dirty |= scene.isDirty;
        }
        // Mit einer anderen ungespeicherten Szene offen nichts schliessen; Unity fragt dann selbst nach dem Neuladen.
        if (!affected || dirty) return;
        closedScenes = EditorSceneManager.GetSceneManagerSetup();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
    static void ReopenScenes()
    {
        if (closedScenes == null) return;
        var setup = closedScenes; closedScenes = null;
        try { if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        catch (Exception ex) { Debug.LogWarning("Szene nach der Aktualisierung nicht wieder geöffnet: " + ex.Message); }
    }

    static void Current(string message)
    {
        string text = "aktuell auf origin/main @ " + Short(Head) + (string.IsNullOrEmpty(Subject) ? "" : " – " + Subject);
        if (EditorUtility.scriptCompilationFailed) text += " (Achtung: Skripte haben Kompilierfehler)";
        SetStatus("aktuell", text, 0);
        if (message != null) Debug.Log("main aktualisieren: " + message);
    }

    static void Block(string reason, int behind, bool quiet = false)
    {
        SetStatus("ausgesetzt", reason, behind);
        if (quiet || reason == lastSkip) return;
        lastSkip = reason;
        MainSyncLog.Write("Ausgesetzt: " + reason, true);
    }

    // Hinweis oben im Game-Fenster: der Benutzer sieht, warum eine gemergte Änderung noch fehlt.
    static void NotifyPlaying(int behind)
    {
        double now = EditorApplication.timeSinceStartup;
        if (behind == noticeBehind && now - lastNotice < NoticeRepeatSeconds) return;
        if (behind != noticeBehind) MainSyncLog.Write("main: " + behind + (behind == 1 ? " neuer Commit" : " neue Commits") + ", wird nach dem Play-Stopp geholt.");
        noticeBehind = behind; lastNotice = now;
        Notify("Neuer Stand auf main (" + behind + (behind == 1 ? " Commit" : " Commits") + ")\nPlay beenden zum Aktualisieren");
    }
    static void Notify(string text)
    {
        var content = new GUIContent(text);
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (window.GetType().Name == "GameView" || window is SceneView) window.ShowNotification(content, 8);
    }

    static void SetStatus(string state, string text, int behind)
    {
        CheckedUtc = DateTime.UtcNow.ToString("o"); SessionState.SetString(StateKey + ".checked", CheckedUtc);
        if (state != State || text != StatusText || behind != Behind)
        {
            // Zustandswechsel ins Protokoll; Gründe fürs Aussetzen schreibt Block selbst.
            if (state != State && state != "ausgesetzt") MainSyncLog.Write("Stand: " + text);
            State = state; StatusText = text; Behind = behind;
            SessionState.SetString(StateKey, state); SessionState.SetString(StateKey + ".text", text); SessionState.SetInt(StateKey + ".behind", behind);
            EditorQueueActivity.RequestWrite();
            EditorApplication.UpdateMainWindowTitle();
        }
        StatusChanged?.Invoke();
    }
}
#endif
