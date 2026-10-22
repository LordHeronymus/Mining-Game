#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

// "Als Standard vorschlagen" (F1 / Debug Settings / Tools → main-Sync, nur im Benutzerordner): Die eigenen GPS-Werte
// (GpsUserOverrides) werden auf die Standardwerte von origin/main gelegt, als GpsProfile.asset auf einen neuen Branch
// gps/vorschlag-<Zeit> committet, gepusht und als Pull Request gegen main geöffnet. Arbeitsordner, Index und main bleiben
// unberührt (eigener Index, commit-tree). Nach dem Merge kommen die Werte per main-Aktualisierung als Standard zurück;
// gleiche eigene Werte fallen dann von selbst aus der Überschreibung.
[InitializeOnLoad]
public static class GpsDefaultsProposal
{
    static Task<string> running;

    static GpsDefaultsProposal()
    {
        GpsUserOverrides.Propose = Start;
        EditorApplication.update += Poll;
    }

    public static void Start()
    {
        if (!GpsUserOverrides.Active) return;
        if (running != null) { Report("Vorschlag wird bereits erstellt …"); return; }
        if (MainAutoUpdate.Busy) { Report("main wird gerade aktualisiert, bitte gleich nochmals versuchen."); return; }
        GpsSettings.CommitPendingNow();
        var entries = GpsUserOverrides.Entries.ToList();
        if (entries.Count == 0) { Report("Keine eigenen Werte zum Vorschlagen."); return; }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        string path = MainSyncGit.GpsProfilePath;
        if (MainSyncGit.Run(root, "rev-parse HEAD", out string head) != 0 || MainSyncGit.Run(root, "rev-parse origin/main", out string target) != 0 || head != target)
        { Report("Erst main aktualisieren (Tools → main-Sync): Der Vorschlag muss auf dem neuesten origin/main beruhen."); return; }
        if (MainSyncGit.Run(root, "diff --quiet HEAD -- " + MainSyncGit.Quote(path), out _) != 0)
        { Report("GpsProfile.asset ist lokal geändert; erst main aktualisieren (Tools → main-Sync)."); return; }

        var profile = GpsSettings.Profile;
        var shipped = GpsSettings.ShippedDocument();
        var doc = GpsSettings.ShippedDocument();
        var stale = new List<GpsUserOverrides.Entry>();
        GpsUserOverrides.Apply(doc, profile, entries, stale);
        var used = entries.Where(entry => !stale.Contains(entry)).ToList();
        if (used.Count == 0) { Report("Die eigenen Werte passen nicht mehr zum Profil von main, nichts vorzuschlagen."); return; }
        var lines = used.Select(entry => Name(entry, shipped) + ": " + GpsUserOverrides.Describe(GpsUserOverrides.Find(shipped, profile, entry)) +
            " → " + GpsUserOverrides.Describe(entry.value)).ToList();
        if (!EditorUtility.DisplayDialog("Als Standard vorschlagen",
            "Diese eigenen Werte kommen als neue Standardwerte auf einen eigenen Branch und als Pull Request gegen main " +
            "(main selbst wird nicht geändert):\n\n" + string.Join("\n", lines.Take(15)) + (lines.Count > 15 ? "\n… und " + (lines.Count - 15) + " weitere" : ""),
            "Branch und PR erstellen", "Abbrechen")) return;

        string file;
        try { file = WriteProfile(root, profile, doc); }
        catch (Exception ex) { Report("Vorschlag nicht erstellt: " + ex.Message); return; }
        string branch = "gps/vorschlag-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        string title = "GPS-Standardwerte: " + used.Count + (used.Count == 1 ? " Wert" : " Werte") + " aus dem Benutzerordner";
        string body = "Neue Standardwerte im GPS-Profil, vorgeschlagen mit „Als Standard vorschlagen“ im Ordner `Mining Game`:\n\n" +
            string.Join("\n", lines.Select(line => "- " + line)) + "\n\nNach dem Merge holt die main-Aktualisierung die Werte als Standard zurück; " +
            "gleiche eigene Werte fallen dann von selbst aus `UserSettings/GpsOverrides.json`.";
        Report("Vorschlag wird erstellt (Branch " + branch + ") …");
        running = Task.Run(() => Publish(root, target, file, branch, title, body));
    }

    // Unity schreibt das Profil (gleiche YAML-Faltung wie beim normalen Speichern) in eine temporäre Datei; Kopf (fileID,
    // Skript) kommt unverändert aus dem bisherigen Profil, damit Verweise auf das Asset gültig bleiben.
    public static string WriteProfile(string root, GpsProfile profile, GpsDocument doc)
    {
        string temp = Path.Combine(root, "Temp", "GpsProfile-Vorschlag.asset");
        Directory.CreateDirectory(Path.GetDirectoryName(temp));
        string json = GpsSettings.AssetJson(doc);
        var copy = Object.Instantiate(profile);
        try
        {
            copy.name = profile.name; copy.documentJson = json;
            InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { copy }, temp, true);
        }
        finally { Object.DestroyImmediate(copy); }
        string written = File.ReadAllText(temp).Replace("\r\n", "\n");
        string original = File.ReadAllText(Path.Combine(root, MainSyncGit.GpsProfilePath)).Replace("\r\n", "\n");
        const string Marker = "\n  documentJson: ";
        int newStart = written.IndexOf(Marker, StringComparison.Ordinal), oldStart = original.IndexOf(Marker, StringComparison.Ordinal);
        if (newStart < 0 || oldStart < 0) throw new InvalidDataException("Profiltext unerwartet aufgebaut.");
        string text = original.Substring(0, oldStart) + written.Substring(newStart);
        var check = GpsUserOverrides.ParseProfileYaml(text);
        if (check == null || JsonUtility.ToJson(check) != JsonUtility.ToJson(JsonUtility.FromJson<GpsDocument>(json)))
            throw new InvalidDataException("Geschriebenes Profil liest sich nicht zurück.");
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        return temp;
    }

    public static string Publish(string root, string baseCommit, string file, string branch, string title, string body, bool openPullRequest = true)
    {
        string path = MainSyncGit.GpsProfilePath;
        MainSyncGit.Run(root, "rev-parse --git-dir", out string gitDir);
        string dir = Path.IsPathRooted(gitDir) ? gitDir : Path.Combine(root, gitDir);
        string index = Path.Combine(dir, "gps-proposal.index"), message = Path.Combine(dir, "gps-proposal.msg"), bodyFile = Path.Combine(dir, "gps-proposal.body");
        var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
        try
        {
            File.Delete(index);
            if (MainSyncGit.Run(root, "read-tree " + baseCommit, out string error, env) != 0) return "Vorschlag fehlgeschlagen (read-tree): " + error;
            if (MainSyncGit.Run(root, "hash-object -w --path=" + MainSyncGit.Quote(path) + " " + MainSyncGit.Quote(file), out string blob) != 0) return "Vorschlag fehlgeschlagen (hash-object): " + blob;
            if (MainSyncGit.Run(root, "update-index --cacheinfo 100644," + blob + "," + MainSyncGit.Quote(path), out error, env) != 0) return "Vorschlag fehlgeschlagen (update-index): " + error;
            if (MainSyncGit.Run(root, "write-tree", out string tree, env) != 0) return "Vorschlag fehlgeschlagen (write-tree): " + tree;
            MainSyncGit.Run(root, "rev-parse " + baseCommit + "^{tree}", out string baseTree);
            if (tree == baseTree) return "Keine Änderung gegenüber main, kein Vorschlag nötig.";
            File.WriteAllText(message, title + "\n\n" + body + "\n", new UTF8Encoding(false));
            if (MainSyncGit.Run(root, "commit-tree " + tree + " -p " + baseCommit + " -F " + MainSyncGit.Quote(message), out string commit) != 0) return "Vorschlag fehlgeschlagen (commit-tree): " + commit;
            if (MainSyncGit.Run(root, "push -q origin " + commit + ":refs/heads/" + branch, out string push, timeoutMs: 300000) != 0)
                return "Commit " + commit.Substring(0, 7) + " erstellt, aber Push fehlgeschlagen: " + push;
            if (!openPullRequest) return "Branch " + branch + " gepusht.";
            File.WriteAllText(bodyFile, body, new UTF8Encoding(false));
            if (MainSyncGit.Run(root, "pr create --base main --head " + branch + " --title " + MainSyncGit.Quote(title) + " --body-file " + MainSyncGit.Quote(bodyFile),
                out string pr, timeoutMs: 120000, executable: "gh") == 0)
                return "Vorschlag als PR erstellt: " + pr.Split('\n').Last().Trim();
            MainSyncGit.Run(root, "remote get-url origin", out string remote);
            string web = remote.Replace("git@github.com:", "https://github.com/").Replace(".git", "");
            return "Branch " + branch + " gepusht; PR bitte auf GitHub öffnen: " + web + "/compare/main..." + branch + " (gh: " + pr + ")";
        }
        catch (Exception ex) { return "Vorschlag fehlgeschlagen: " + ex.Message; }
        finally { try { File.Delete(index); File.Delete(message); File.Delete(bodyFile); } catch { } }
    }

    static void Poll()
    {
        if (running == null || !running.IsCompleted) return;
        string result = running.IsFaulted ? "Vorschlag fehlgeschlagen: " + running.Exception?.GetBaseException().Message : running.Result;
        running = null;
        Report(result);
    }

    static string Name(GpsUserOverrides.Entry entry, GpsDocument shipped)
    {
        string owner = entry.record == "preferences" ? "Einstellungen" : entry.record == "tests" ? "Testeinstellungen" :
            shipped.records.Find(record => record.key == entry.record)?.name;
        if (string.IsNullOrEmpty(owner)) owner = entry.record.Split(':').Skip(1).FirstOrDefault()?.Split('.').Last() ?? entry.record;
        return owner + " / " + entry.path;
    }

    static void Report(string message)
    {
        Debug.Log("GPS-Vorschlag: " + message);
        if (MainAutoUpdate.Tracked) MainSyncLog.Write("GPS-Vorschlag: " + message);
        GpsUserOverrides.Notify(message);
    }
}
#endif
