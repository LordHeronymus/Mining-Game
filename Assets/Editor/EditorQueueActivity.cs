#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Objektives Aktivitaetssignal fuer die Editor-Warteschlange (Docs/Editor-Warteschlange.md).
// Schreibt <Git Repos>/EditorQueue/<Projektordner>.activity.json: letzte Editor-Aktivitaet
// (Play-Mode-Wechsel, Kompilierung, Szene geoeffnet/geaendert/gespeichert, Undo, Auswahl,
// laufende Play-Session) mit Zeitstempel. Tools/editor-queue.ps1 verdraengt einen Inhaber nur,
// wenn dieses Signal Ruhe zeigt.
[InitializeOnLoad]
public static class EditorQueueActivity
{
    const double HeartbeatSeconds = 30;   // Lebenszeichen des Editors, auch ohne Aktivitaet
    const double MinWriteSeconds = 5;     // haeufige Ereignisse hoechstens alle 5 s schreiben
    const string MenuRoot = "Tools/Editor-Warteschlange/";

    static readonly string projectRoot;
    static readonly string projectName;
    static readonly string queueDir;
    static readonly string activityPath;

    static DateTime lastActivityUtc = DateTime.UtcNow;
    static string lastEvent = "Skripte geladen";
    static bool dirty = true;
    static double lastWrite = double.NegativeInfinity;
    static bool closed;
    static bool warned;

    [Serializable]
    class ActivityData
    {
        public string project;
        public string projectPath;
        public string updated;
        public string lastActivity;
        public string lastEvent;
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
        public bool closed;
        public int processId;
    }

    static EditorQueueActivity()
    {
        if (Application.isBatchMode) return;

        projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        // Claude-Worktrees unter <Projekt>/.claude/worktrees/... gehoeren zum Editor des Projektordners.
        var owner = Regex.Match(projectRoot, @"^(.*?)[\\/]\.claude[\\/]worktrees[\\/]");
        string ownerRoot = owner.Success ? owner.Groups[1].Value : projectRoot;
        projectName = Path.GetFileName(ownerRoot);
        string env = Environment.GetEnvironmentVariable("EDITOR_QUEUE_DIR");
        queueDir = !string.IsNullOrEmpty(env) ? env : Path.Combine(Path.GetDirectoryName(ownerRoot) ?? ownerRoot, "EditorQueue");
        activityPath = Path.Combine(queueDir, projectName + ".activity.json");

        if (EditorApplication.isPlayingOrWillChangePlaymode) lastEvent = "Skripte geladen (Play-Mode)";

        EditorApplication.update += OnUpdate;
        // Der lokale Dienst fuer PrivOrg startet mit dem ersten Editor (nicht mit Windows).
        EditorApplication.delayCall += EnsureQueueService;
        EditorApplication.playModeStateChanged += s => Mark("Play-Mode: " + s, true);
        EditorApplication.pauseStateChanged += s => Mark("Pause: " + s, true);
        EditorApplication.projectChanged += () => Mark("Projekt geaendert");
        EditorApplication.quitting += OnQuit;
        CompilationPipeline.compilationStarted += _ => Mark("Kompilierung gestartet", true);
        CompilationPipeline.compilationFinished += _ => Mark("Kompilierung beendet", true);
        EditorSceneManager.sceneOpened += (scene, _) => Mark("Szene geoeffnet: " + scene.name);
        EditorSceneManager.sceneSaved += scene => Mark("Szene gespeichert: " + scene.name);
        EditorSceneManager.sceneDirtied += scene => Mark("Szene geaendert: " + scene.name);
        Undo.undoRedoPerformed += () => Mark("Undo/Redo");
        Undo.postprocessModifications += mods => { Mark("Eigenschaft geaendert"); return mods; };
        Selection.selectionChanged += () => Mark("Auswahl geaendert");
    }

    const int QueueServicePort = 47615;

    // Startet Tools/editor-queue-server.ps1 ueber die Verknuepfung, die "-Install" in EditorQueue/bin anlegt,
    // falls er noch nicht laeuft. Ein zweiter Editor findet den laufenden Dienst und tut nichts; ein doppelter
    // Start beendet sich selbst, weil der Port belegt ist.
    static void EnsureQueueService()
    {
        try
        {
            string shortcut = Path.Combine(queueDir, "bin", "editor-queue-server.lnk");
            if (!File.Exists(shortcut) || QueueServiceRunning()) return;
            Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            Debug.Log("Editor-Warteschlange: lokaler Dienst fuer PrivOrg gestartet (http://localhost:" + QueueServicePort + ").");
        }
        catch (Exception e)
        {
            Debug.LogWarning("Editor-Warteschlange: lokaler Dienst konnte nicht gestartet werden: " + e.Message);
        }
    }

    static bool QueueServiceRunning()
    {
        try
        {
            using (var client = new System.Net.Sockets.TcpClient())
            {
                var connect = client.ConnectAsync("127.0.0.1", QueueServicePort);
                return connect.Wait(300) && client.Connected;
            }
        }
        catch { return false; }
    }

    static void Mark(string evt, bool immediate = false)
    {
        lastEvent = evt;
        lastActivityUtc = DateTime.UtcNow;
        dirty = true;
        if (immediate) Write();
    }

    static void OnUpdate()
    {
        // Eine laufende Play-Session oder Kompilierung zaehlt durchgehend als Aktivitaet.
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            lastActivityUtc = DateTime.UtcNow;

        double t = EditorApplication.timeSinceStartup;
        if ((dirty && t - lastWrite >= MinWriteSeconds) || t - lastWrite >= HeartbeatSeconds)
            Write();
    }

    static void OnQuit()
    {
        closed = true;
        lastEvent = "Editor beendet";
        Write();
    }

    static void Write()
    {
        if (activityPath == null) return;
        lastWrite = EditorApplication.timeSinceStartup;
        var data = new ActivityData
        {
            project = projectName,
            projectPath = projectRoot,
            updated = DateTime.UtcNow.ToString("o"),
            lastActivity = lastActivityUtc.ToString("o"),
            lastEvent = lastEvent,
            isPlaying = EditorApplication.isPlayingOrWillChangePlaymode,
            isPaused = EditorApplication.isPaused,
            isCompiling = EditorApplication.isCompiling || EditorApplication.isUpdating,
            closed = closed,
            processId = Process.GetCurrentProcess().Id,
        };
        try
        {
            Directory.CreateDirectory(queueDir);
            string tmp = activityPath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            if (File.Exists(activityPath)) File.Replace(tmp, activityPath, null);
            else File.Move(tmp, activityPath);
            dirty = false;
        }
        catch (Exception e)
        {
            // Liest gerade jemand die Datei, klappt es beim naechsten Update.
            if (!warned) Debug.LogWarning("EditorQueueActivity: " + e.Message);
            warned = true;
        }
    }

    // --- Menue: Benutzer meldet sich ohne Terminal an/ab ---------------------------

    [MenuItem(MenuRoot + "Status anzeigen")]
    static void MenuStatus() => RunQueue("status");

    [MenuItem(MenuRoot + "Als Benutzer anmelden")]
    static void MenuJoin() => RunQueue("join -User -Purpose \"Benutzer im Editor\"");

    [MenuItem(MenuRoot + "Lebenszeichen (Benutzer)")]
    static void MenuHeartbeat() => RunQueue("heartbeat -User");

    [MenuItem(MenuRoot + "Als Benutzer abmelden")]
    static void MenuLeave() => RunQueue("leave -User");

    static void RunQueue(string args)
    {
        Mark("Warteschlange: " + args.Split(' ')[0], true);
        string script = Path.Combine(projectRoot ?? "", "Tools", "editor-queue.ps1");
        if (!File.Exists(script))
        {
            EditorUtility.DisplayDialog("Editor-Warteschlange", "Skript fehlt: " + script, "OK");
            return;
        }
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {args} -Project \"{projectName}\" -QueueDir \"{queueDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string output;
        try
        {
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
            }
        }
        catch (Exception e)
        {
            output = "Fehler: " + e.Message;
        }
        Debug.Log("Editor-Warteschlange:\n" + output);
        EditorUtility.DisplayDialog("Editor-Warteschlange", output.Trim(), "OK");
    }
}
#endif
