using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Git-Schritte der main-Aktualisierung (MainSyncGit) gegen temporäre Repositories unter Temp/ (Edit Mode, ändert nichts am
// Projekt): "origin" (bare), "work" (pusht wie ein Chat nach main) und "user" (spielt den Ordner "Mining Game").
// Fälle: nur neuer, sauber → Fast-Forward; fremde lokale Änderung bleibt; lokale Änderung, die main auch ändert → gesichert
// und ersetzt; lokaler Commit auf main → gesichert, main = origin/main; unversionierte Datei, die main neu bringt; GPS-Profil
// zurücksetzen; laufender Merge blockiert; Sicherungen bilden eine Kette; GPS-Vorschlag landet als eigener Branch auf origin.
public static class MainSyncGitChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks.Add(label); }
    static string root, origin, work, user;

    public static object Main()
    {
        checks.Clear();
        root = Path.GetFullPath("Temp/MainSyncGitChecks-" + DateTime.UtcNow.Ticks);
        origin = Path.Combine(root, "origin.git"); work = Path.Combine(root, "work"); user = Path.Combine(root, "user");
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init -q --bare -b main " + Q(origin));
            Git(root, "clone -q " + Q(origin) + " " + Q(work));
            Configure(work);
            Write(work, "a.txt", "a1"); Write(work, "b.txt", "b1");
            Write(work, MainSyncGit.GpsProfilePath, File.ReadAllText(MainSyncGit.GpsProfilePath));
            Commit(work, "start");
            Git(root, "clone -q " + Q(origin) + " " + Q(user));
            Configure(user);

            // 1. Nur neuer Stand, sauber.
            Push(work, "a.txt", "a2");
            var status = MainSyncGit.Inspect(user, true);
            Check(status.error == null && status.branch == "main" && status.behind == 1 && status.ahead == 0 && status.conflicts.Count == 0, "1: ein neuer Commit erkannt");
            var outcome = MainSyncGit.Update(user, status, "Test 1");
            Check(outcome.ok && outcome.backup == null && Read(user, "a.txt") == "a2", "1: Fast-Forward ohne Sicherung");

            // 2. Lokale Änderung, die main nicht betrifft, bleibt stehen.
            Write(user, "b.txt", "b-lokal");
            Push(work, "a.txt", "a3");
            status = MainSyncGit.Inspect(user, true);
            Check(status.conflicts.Count == 0 && status.dirty.Contains("b.txt"), "2: lokale Änderung ohne Konflikt");
            outcome = MainSyncGit.Update(user, status, "Test 2");
            Check(outcome.ok && outcome.backup == null && Read(user, "a.txt") == "a3" && Read(user, "b.txt") == "b-lokal", "2: aktualisiert, lokale Änderung behalten");

            // 3. Lokale Änderung an einer Datei, die main auch ändert: gesichert, dann main.
            Write(user, "a.txt", "a-lokal");
            Push(work, "a.txt", "a4");
            status = MainSyncGit.Inspect(user, true);
            Check(status.conflicts.SequenceEqual(new[] { "a.txt" }), "3: Konflikt erkannt");
            outcome = MainSyncGit.Update(user, status, "Test 3");
            Check(outcome.ok && outcome.backup != null && Read(user, "a.txt") == "a4" && Read(user, "b.txt") == "b-lokal", "3: main übernommen, andere Änderung bleibt");
            Check(Show(user, MainSyncGit.BackupBranch + ":a.txt") == "a-lokal", "3: lokale Fassung in der Sicherung");
            string firstBackup = outcome.backup;

            // 4. Lokaler Commit auf main (wie 7f79e15): gesichert, main springt auf origin/main.
            Write(user, "c.txt", "lokal"); Git(user, "add c.txt"); Git(user, "commit -q -m lokal");
            string local = Out(user, "rev-parse HEAD");
            Push(work, "a.txt", "a5");
            status = MainSyncGit.Inspect(user, true);
            Check(status.ahead == 1 && status.behind == 1, "4: lokaler Commit und neuer Commit erkannt");
            outcome = MainSyncGit.Update(user, status, "Test 4");
            Check(outcome.ok && Out(user, "rev-parse HEAD") == Out(user, "rev-parse origin/main") && Read(user, "a.txt") == "a5", "4: main = origin/main");
            Check(!File.Exists(Path.Combine(user, "c.txt")) && Read(user, "b.txt") == "b-lokal", "4: Commit-Inhalt weg, lokale Änderung bleibt");
            Check(Out(user, "rev-parse " + MainSyncGit.BackupBranch + "^1") == local, "4: lokaler Commit ist erster Elternteil der Sicherung");
            Check(Out(user, "rev-parse " + MainSyncGit.BackupBranch + "^2") == firstBackup, "4: Sicherungen bilden eine Kette");

            // 5. Unversionierte Datei, die main neu hinzufügt.
            Write(user, "d.txt", "d-lokal");
            Push(work, "d.txt", "d-main");
            status = MainSyncGit.Inspect(user, true);
            Check(status.conflicts.Contains("d.txt"), "5: unversionierte Datei blockiert erkannt");
            outcome = MainSyncGit.Update(user, status, "Test 5");
            Check(outcome.ok && Read(user, "d.txt") == "d-main" && Show(user, MainSyncGit.BackupBranch + ":d.txt") == "d-lokal", "5: gesichert und durch main ersetzt");

            // 6. Lokal geändertes GPS-Profil ohne neuen Stand: Text für die Übernahme, danach zurückgesetzt.
            string profile = Path.Combine(user, MainSyncGit.GpsProfilePath);
            File.AppendAllText(profile, "\n# lokal\n");
            status = MainSyncGit.Inspect(user, true);
            Check(status.gpsDirty && status.NeedsUpdate && status.gpsLocalYaml != null && status.gpsLocalYaml.Contains("# lokal") &&
                status.gpsBaseYaml != null && !status.gpsBaseYaml.Contains("# lokal"), "6: GPS-Profil lokal geändert, beide Texte da");
            Check(GpsUserOverrides.ParseProfileYaml(status.gpsBaseYaml) != null, "6: Profiltext aus Git lesbar");
            outcome = MainSyncGit.Update(user, status, "Test 6", new[] { MainSyncGit.GpsProfilePath });
            Check(outcome.ok && !File.ReadAllText(profile).Contains("# lokal") && Show(user, MainSyncGit.BackupBranch + ":" + MainSyncGit.GpsProfilePath).Contains("# lokal"),
                "6: Profil zurückgesetzt, lokale Fassung gesichert");

            // 7. Laufender Merge: nichts anfassen.
            string mergeHead = Path.Combine(user, ".git", "MERGE_HEAD");
            File.WriteAllText(mergeHead, Out(user, "rev-parse HEAD"));
            status = MainSyncGit.Inspect(user, true);
            File.Delete(mergeHead);
            Check(status.busy != null && !status.NeedsUpdate, "7: offener Merge blockiert");

            // 8. GPS-Vorschlag: eigener Branch auf origin, Arbeitsordner und main unverändert.
            string head = Out(user, "rev-parse HEAD");
            string proposal = Path.Combine(root, "Vorschlag.asset");
            File.WriteAllText(proposal, File.ReadAllText(profile) + "# vorschlag\n");
            string porcelain = Out(user, "status --porcelain");
            string message = GpsDefaultsProposal.Publish(user, head, proposal, "gps/vorschlag-test", "Test", "Body", openPullRequest: false);
            Check(Out(origin, "rev-parse gps/vorschlag-test^") == head && Out(origin, "show gps/vorschlag-test:" + MainSyncGit.GpsProfilePath).Contains("# vorschlag"),
                "8: Vorschlag als Branch gepusht (" + message + ")");
            Check(Out(user, "rev-parse HEAD") == head && Out(user, "status --porcelain") == porcelain && !File.ReadAllText(profile).Contains("# vorschlag"),
                "8: Benutzerordner unverändert");
            return new { count = checks.Count, checks, proposal = message };
        }
        finally { Delete(root); }
    }

    static void Configure(string repo)
    {
        Git(repo, "config user.email test@example.com"); Git(repo, "config user.name Test");
        Git(repo, "config core.autocrlf false"); Git(repo, "config commit.gpgsign false");
    }
    static void Push(string repo, string file, string content) { Write(repo, file, content); Commit(repo, "set " + file); }
    static void Commit(string repo, string message) { Git(repo, "add -A"); Git(repo, "commit -q -m " + Q(message)); Git(repo, "push -q origin HEAD:main"); }
    static void Write(string repo, string file, string content)
    {
        string path = Path.Combine(repo, file); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content);
    }
    static string Read(string repo, string file) => File.ReadAllText(Path.Combine(repo, file));
    static string Show(string repo, string spec) => Out(repo, "show " + Q(spec));
    static string Out(string repo, string args) { Git(repo, args, out string output); return output; }
    static void Git(string repo, string args) { Git(repo, args, out _); }
    static void Git(string repo, string args, out string output)
    {
        if (MainSyncGit.Run(repo, args, out output) != 0) throw new Exception("git " + args + ": " + output);
    }
    static string Q(string value) => MainSyncGit.Quote(value);
    static void Delete(string path)
    {
        try
        {
            foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
            Directory.Delete(path, true);
        }
        catch { }
    }
}
