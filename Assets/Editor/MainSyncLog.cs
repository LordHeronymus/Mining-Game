#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Protokoll der main-Aktualisierung im Benutzerordner: Logs/main-sync.log (nicht versioniert, ab 1 MB nach .1 verschoben).
// Gleiche Meldungen hintereinander werden höchstens einmal pro Stunde geschrieben.
public static class MainSyncLog
{
    const int Keep = 200;
    static readonly List<string> recent = new();
    static bool loaded;
    static string last;
    static DateTime lastTime;
    public static event Action Changed;

    public static string FilePath => Path.Combine(MainAutoUpdate.ProjectRoot ?? Directory.GetCurrentDirectory(), "Logs", "main-sync.log");
    public static IReadOnlyList<string> Recent { get { Load(); return recent; } }

    public static void Write(string message, bool console = false)
    {
        if (message == last && DateTime.Now - lastTime < TimeSpan.FromHours(1)) return;
        last = message; lastTime = DateTime.Now;
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message.Replace("\r", "").Replace("\n", " | ");
        Load();
        recent.Add(line);
        if (recent.Count > Keep) recent.RemoveRange(0, recent.Count - Keep);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024) { File.Copy(FilePath, FilePath + ".1", true); File.Delete(FilePath); }
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception ex) { Debug.LogWarning("main-sync.log nicht schreibbar: " + ex.Message); }
        if (console) Debug.Log("main-Sync: " + message);
        Changed?.Invoke();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        try { if (File.Exists(FilePath)) recent.InsertRange(0, File.ReadAllLines(FilePath).Reverse().Take(Keep).Reverse()); }
        catch { }
    }
}
#endif
