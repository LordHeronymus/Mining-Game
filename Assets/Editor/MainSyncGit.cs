#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

// Git-Schritte der main-Aktualisierung (MainAutoUpdate, Docs/Main-Auto-Update.md) ohne Unity-API: laufen im Hintergrund
// und lassen sich gegen ein temporäres Repository prüfen (Tests/MainSyncGitChecks.cs).
// Grundsatz: main folgt immer origin/main, verworfen wird nichts ohne Sicherung. Lokale Commits und lokal geänderte Dateien,
// die main ebenfalls ändert, kommen vorher als Commit auf den Sicherungs-Branch backup/mining-game-auto (eine Kette, jeder
// Eintrag mit HEAD und vorheriger Sicherung als Eltern). Andere lokale Änderungen bleiben unberührt stehen.
public static class MainSyncGit
{
    public const string GpsProfilePath = "Assets/Resources/Gameplay/GpsProfile.asset";
    public const string BackupBranch = "backup/mining-game-auto";
    const string BackupRef = "refs/heads/" + BackupBranch;

    public sealed class Status
    {
        public string error, busy;                 // git nicht nutzbar bzw. laufender Merge/Rebase
        public string branch, head, target, subject, mergeBase;
        public int behind, ahead;
        public List<string> dirty = new(), incoming = new(), conflicts = new();
        public bool gpsDirty, gpsInLocalCommits;
        public string gpsBaseYaml, gpsLocalYaml;   // Profiltexte, aus denen eigene GPS-Werte übernommen werden
        public bool NeedsUpdate => behind > 0 || ahead > 0 || gpsDirty;
    }

    public sealed class Outcome
    {
        public bool ok;
        public string error, backup, head;
        public List<string> restored = new();
    }

    public static Status Inspect(string root, bool fetch)
    {
        var status = new Status();
        if (Run(root, "rev-parse --abbrev-ref HEAD", out status.branch) != 0) { status.error = "git nicht verfügbar: " + status.branch; return status; }
        status.busy = Busy(root);
        if (status.branch != "main" || status.busy != null) return status;
        if (fetch && Run(root, "fetch -q origin main", out string fetched) != 0) { status.error = "git fetch fehlgeschlagen: " + fetched; return status; }
        if (Run(root, "rev-parse HEAD", out status.head) != 0 || Run(root, "rev-parse origin/main", out status.target) != 0)
        { status.error = "origin/main nicht gefunden"; return status; }
        Run(root, "log -1 --format=%s origin/main", out status.subject);
        Run(root, "rev-list --count HEAD..origin/main", out string behind); int.TryParse(behind, out status.behind);
        Run(root, "rev-list --count origin/main..HEAD", out string ahead); int.TryParse(ahead, out status.ahead);
        Run(root, "merge-base HEAD origin/main", out status.mergeBase);
        Run(root, "diff --name-only --no-renames -z HEAD", out string dirty);
        status.dirty = Split(dirty);
        if (status.head != status.target)
        {
            Run(root, "diff --name-status --no-renames -z HEAD origin/main", out string incoming);
            var parts = Split(incoming);
            for (int i = 0; i + 1 < parts.Count; i += 2)
            {
                string path = parts[i + 1];
                status.incoming.Add(path);
                // Neu auf main, liegt hier aber schon als unversionierte Datei: Git würde sie nicht überschreiben.
                bool blocksAsUntracked = parts[i] == "A" && File.Exists(Path.Combine(root, path)) && !status.dirty.Contains(path);
                if (blocksAsUntracked || status.dirty.Contains(path)) status.conflicts.Add(path);
            }
        }
        status.gpsDirty = status.dirty.Contains(GpsProfilePath);
        status.gpsInLocalCommits = status.ahead > 0 && !string.IsNullOrEmpty(status.mergeBase) &&
            Run(root, "diff --quiet " + status.mergeBase + " HEAD -- " + Quote(GpsProfilePath), out _) == 1;
        if (status.gpsDirty || status.gpsInLocalCommits)
        {
            // Basis: was der Benutzer als Standard hatte (vor eigenen Commits), lokal: was jetzt im Ordner liegt.
            Run(root, "show " + (status.gpsInLocalCommits ? status.mergeBase : "HEAD") + ":" + GpsProfilePath, out status.gpsBaseYaml, trim: false);
            try { status.gpsLocalYaml = File.ReadAllText(Path.Combine(root, GpsProfilePath)); } catch { status.gpsLocalYaml = null; }
        }
        return status;
    }

    // restoreExtra: weitere lokal geänderte Dateien, die ebenfalls gesichert und auf HEAD zurückgesetzt werden (GpsProfile.asset).
    public static Outcome Update(string root, Status status, string reason, IEnumerable<string> restoreExtra = null)
    {
        var outcome = new Outcome();
        var restore = status.conflicts.Concat(restoreExtra ?? Enumerable.Empty<string>()).Distinct().ToList();
        if (restore.Count > 0 || status.ahead > 0)
        {
            outcome.backup = Backup(root, status, restore, reason, out string error);
            if (outcome.backup == null) { outcome.error = "Sicherung fehlgeschlagen, nichts geändert: " + error; return outcome; }
        }
        foreach (string path in restore)
        {
            bool tracked = Run(root, "cat-file -e " + Quote("HEAD:" + path), out _) == 0;
            if (tracked)
            {
                if (Run(root, "checkout -q HEAD -- " + Quote(path), out string checkout) != 0) { outcome.error = "Zurücksetzen von " + path + " fehlgeschlagen: " + checkout; return outcome; }
            }
            else
            {
                Run(root, "rm -q --cached --ignore-unmatch -- " + Quote(path), out _);
                try { File.Delete(Path.Combine(root, path)); } catch (Exception ex) { outcome.error = path + " nicht löschbar: " + ex.Message; return outcome; }
            }
            outcome.restored.Add(path);
        }
        if (status.ahead > 0)
        {
            // Lokale Commits liegen jetzt in der Sicherung; main springt auf origin/main, übrige lokale Änderungen bleiben.
            if (Run(root, "reset -q --keep origin/main", out string reset) != 0) { outcome.error = "main nicht auf origin/main gesetzt: " + reset; return outcome; }
        }
        else if (status.behind > 0 && Run(root, "merge -q --ff-only origin/main", out string merge) != 0)
        { outcome.error = "Fast-Forward fehlgeschlagen: " + merge; return outcome; }
        Run(root, "rev-parse HEAD", out outcome.head);
        outcome.ok = outcome.head == status.target;
        if (!outcome.ok && outcome.error == null) outcome.error = "HEAD steht nach der Aktualisierung nicht auf origin/main";
        return outcome;
    }

    // Sicherungs-Commit über einen eigenen Index: Arbeitsordner, normaler Index und Stash bleiben unberührt.
    static string Backup(string root, Status status, List<string> files, string reason, out string error)
    {
        error = null;
        Run(root, "rev-parse --git-dir", out string gitDir);
        string index = Path.Combine(Path.IsPathRooted(gitDir) ? gitDir : Path.Combine(root, gitDir), "mining-sync-backup.index");
        string message = Path.ChangeExtension(index, ".msg");
        var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
        try
        {
            File.Delete(index);
            if (Run(root, "read-tree HEAD", out error, env) != 0) return null;
            if (files.Count > 0 && Run(root, "add -f -A -- " + string.Join(" ", files.Select(Quote)), out error, env) != 0) return null;
            if (Run(root, "write-tree", out string tree, env) != 0) { error = tree; return null; }
            bool hasPrevious = Run(root, "rev-parse -q --verify " + BackupRef, out string previous) == 0 && previous.Length > 0;
            var text = new StringBuilder("Sicherung vor main-Aktualisierung: " + reason + "\n\n");
            text.Append("Ordner: " + root + "\nmain vorher: " + status.head + "\norigin/main: " + status.target + "\n");
            if (status.ahead > 0) text.Append("Lokale Commits auf main: " + status.ahead + " (erster Elternteil dieses Commits)\n");
            if (files.Count > 0) text.Append("Lokal geänderte Dateien (Stand in diesem Commit):\n" + string.Join("\n", files.Select(file => "  " + file)) + "\n");
            File.WriteAllText(message, text.ToString(), new UTF8Encoding(false));
            if (Run(root, "commit-tree " + tree + " -p " + status.head + (hasPrevious ? " -p " + previous : "") + " -F " + Quote(message), out string commit) != 0)
            { error = commit; return null; }
            if (Run(root, "update-ref -m " + Quote("main-Sync Sicherung") + " " + BackupRef + " " + commit + (hasPrevious ? " " + previous : ""), out error) != 0) return null;
            return commit;
        }
        finally { try { File.Delete(index); File.Delete(message); } catch { } }
    }

    static string Busy(string root)
    {
        if (Run(root, "rev-parse --git-dir", out string gitDir) != 0) return null;
        string dir = Path.IsPathRooted(gitDir) ? gitDir : Path.Combine(root, gitDir);
        if (File.Exists(Path.Combine(dir, "MERGE_HEAD"))) return "ein Merge ist nicht abgeschlossen";
        if (Directory.Exists(Path.Combine(dir, "rebase-merge")) || Directory.Exists(Path.Combine(dir, "rebase-apply"))) return "ein Rebase ist nicht abgeschlossen";
        if (File.Exists(Path.Combine(dir, "CHERRY_PICK_HEAD")) || File.Exists(Path.Combine(dir, "REVERT_HEAD"))) return "ein Cherry-Pick/Revert ist nicht abgeschlossen";
        if (File.Exists(Path.Combine(dir, "index.lock"))) return "ein anderer Git-Befehl läuft (index.lock)";
        return null;
    }

    static List<string> Split(string text) => (text ?? "").Split('\0').Select(part => part.Trim('\r', '\n')).Where(part => part.Length > 0).ToList();
    public static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    public static int Run(string root, string args, out string output, IDictionary<string, string> env = null, int timeoutMs = 120000, bool trim = true, string executable = "git")
    {
        var psi = new ProcessStartInfo(executable, (executable == "git" ? "-C " + Quote(root) + " " : "") + args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["LC_ALL"] = "C"; // englische Git-Meldungen
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (env != null) foreach (var pair in env) psi.Environment[pair.Key] = pair.Value;
        try
        {
            using var process = Process.Start(psi);
            var error = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs)) { try { process.Kill(); } catch { } output = "Zeitlimit"; return -1; }
            output = trim ? stdout.Result.Trim() : stdout.Result;
            if (process.ExitCode != 0) output = (output + " " + error.Result).Trim();
            return process.ExitCode;
        }
        catch (Exception ex) { output = ex.Message; return -1; }
    }
}
#endif
