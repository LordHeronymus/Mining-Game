using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

// Edit-mode checks for the target behaviour of the level system (Docs/Progression.md):
// S1 one main progression across all worlds, S2 level-ups and payouts reach the disk at once (a kill loses nothing, and
// reloading an older world save neither loses nor repays anything), S3 challenges pay XP into the main progression exactly once.
// A "crash" copies the profile files as they are on disk (no Save, no world save) and reads them in a new session.
// Every profile lives in Temp/Progression-<ticks> (MetaProgression.TestDirectory); statics and GPS values are restored.
// Report: Temp/ProgressionChecks.txt.
public static class ProgressionChecks
{
    const string Report = "Temp/ProgressionChecks.txt";
    [Serializable] sealed class Envelope { public int version = 1; public string payload, sha256; }
    static readonly List<string> lines = new();
    static int passed, failed, crashes, seq;
    static string root;
    static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc); // a Wednesday

    public static object Main()
    {
        lines.Clear(); passed = failed = crashes = seq = 0;
        Directory.CreateDirectory("Temp");
        if (Application.isPlaying) return Finish("FAIL Edit Mode only: stop Play Mode before running ProgressionChecks");
        if (MetaProgression.CurrentRun != null) return Finish("FAIL a run is active; run ProgressionChecks without a loaded world");
        string previousDirectory = MetaProgression.TestDirectory;
        var previousClock = MetaProgression.ChallengeTestUtc;
        var previousSettings = MetaProgression.ChallengeSettingsOverride;
        var previousZone = MetaProgression.ChallengeTimeZoneOverride;
        var previousMeta = GpsSettings.Preferences.metaProgression;
        root = Path.GetFullPath("Temp/Progression-" + DateTime.UtcNow.Ticks);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try {
            GpsSettings.Preferences.metaProgression = new MetaProgressionSettings { efficiencyMaxBonus = 0f };
            Section("one progression", OneProgression);
            Section("immediate level-up", ImmediateLevelUp);
            Section("deferred write", DeferredWrite);
            Section("payouts", Payouts);
            Section("older world save", OlderWorldSave);
            Section("objectives", Objectives);
            Section("saves without tallies", SavesWithoutTallies);
            Section("foreign payout markers", ForeignPayoutMarkers);
            Section("read-only profile", ReadOnlyProfile);
            Section("run end", RunEnd);
            Section("journal", Journal);
            Section("save warning", SaveWarning);
            Section("catalog freeze", CatalogFreeze);
        } finally {
            try { MetaProgression.EndRun(); } catch (Exception e) { Check(false, "cleanup: EndRun threw " + Describe(e)); }
            try { MetaProgression.TestDirectory = previousDirectory; } catch (Exception e) { Check(false, "cleanup: restoring TestDirectory threw " + Describe(e)); }
            MetaProgression.ChallengeTestUtc = previousClock;
            MetaProgression.ChallengeSettingsOverride = previousSettings;
            MetaProgression.ChallengeTimeZoneOverride = previousZone;
            GpsSettings.Preferences.metaProgression = previousMeta;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception e) { lines.Add("INFO temporary profiles kept in " + root + ": " + e.Message); }
        }
        Check(MetaProgression.TestDirectory == previousDirectory && MetaProgression.ChallengeTestUtc == previousClock &&
            MetaProgression.ChallengeSettingsOverride == previousSettings && MetaProgression.ChallengeTimeZoneOverride == previousZone, "cleanup: statics restored");
        Check(watch.Elapsed.TotalSeconds < 120, "cleanup: runtime below 120 s (" + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
        return Finish(null);
    }

    static string Finish(string abort)
    {
        if (abort != null) { failed++; lines.Insert(0, abort); }
        string summary = failed == 0 ? "PASS " + passed + ": progression (one progression, immediate persistence, exactly-once payouts, replay, robustness)"
            : "FAIL " + failed + " of " + (passed + failed) + " progression checks";
        File.WriteAllText(Report, summary + "\n" + string.Join("\n", lines) + "\n");
        if (failed == 0) return summary;
        string list = string.Join(" | ", lines.Where(line => line.StartsWith("FAIL ")).Select(line => line.Length > 165 ? line.Substring(5, 160) + "…" : line.Substring(5)));
        return summary + ": " + (list.Length > 2300 ? list.Substring(0, 2300) + " …" : list);
    }

    // ---------- helpers ----------
    static void Check(bool ok, string label)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "PASS " : "FAIL ") + label.Replace('\n', ' ').Replace('\r', ' '));
    }
    static void Info(string text) => lines.Add("INFO " + text);
    static void Section(string name, Action body) { try { body(); } catch (Exception e) { Check(false, name + ": aborted by " + Describe(e)); } }
    static string Describe(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        var frame = new System.Diagnostics.StackTrace(e, false).GetFrames()?.FirstOrDefault(f => f.GetMethod()?.DeclaringType != null);
        return e.GetType().Name + ": " + e.Message + (frame != null ? " at " + frame.GetMethod().DeclaringType.Name + "." + frame.GetMethod().Name : "");
    }
    static MetaProfile P => MetaProgression.Profile;
    static MetaRunState Run => MetaProgression.CurrentRun;
    static long Total => MetaProgression.TotalXp;
    static long Counter(string id) => P.lifetimeCounters?.Find(c => c != null && c.id == id)?.value ?? 0;
    // The integrity formula ValidateProfile enforces on every load.
    static long Formula(MetaProfile p) => p.periodRewardXp + p.runs.Sum(r => (long)Math.Floor(r.progressXp + r.efficiencyXp) + r.challengeXp) +
        p.completedMilestones.Sum(id => (long)(MetaProgressionCatalog.Challenge(id)?.xp ?? 0));
    static long RunXp(MetaRunState r) => (long)Math.Floor(r.progressXp + r.efficiencyXp) + r.challengeXp + r.milestoneXp + r.rewardXp;

    static void Fresh(string name)
    {
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = Path.Combine(root, name);
        MetaProgression.ChallengeTestUtc = Now;
        MetaProgression.ChallengeSettingsOverride = new ChallengeSettings();
        MetaProgression.ChallengeTimeZoneOverride = TimeZoneInfo.Utc;
        Check(Run == null && Total == 0 && P.runs.Count == 0, name + ": starts with an isolated empty profile");
    }
    // A finished world that holds exactly this much XP; no objectives or milestones are completed by it.
    static void Seed(string id, long xp)
    {
        MetaProgression.BeginRun(id, new MetaRunState { runId = id, progressXp = xp });
        MetaProgression.EndRun();
    }
    static MetaProfile ReadProfile(string path) => JsonUtility.FromJson<MetaProfile>(JsonUtility.FromJson<Envelope>(File.ReadAllText(path)).payload);
    // Lets only a write that was already started finish (as the per-frame pump of the runtime does), then copies the files.
    static MetaProfile Crash()
    {
        WaitForWriter();
        string copy = Path.Combine(root, "crash-" + (++crashes));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(MetaProgression.DirectoryPath)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        string main = Path.Combine(copy, "meta-progression.json");
        return File.Exists(main) ? ReadProfile(main) : null;
    }
    static string LastCrash => Path.Combine(root, "crash-" + crashes);
    static void WaitForWriter()
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (MetaProgression.IsSaving) {
            MetaProgression.PumpSaves();
            if (DateTime.UtcNow > timeout) throw new TimeoutException("profile writer did not finish");
            Thread.Sleep(1);
        }
    }
    static readonly string[] Drivable = { "blocks", "cells", "trees", "grass", "sales" };
    // The open round task with a simple metric that is closest to its target.
    static MetaChallengeTask FindTask(out ChallengePeriod period, out int index)
    {
        MetaChallengeTask best = null; period = ChallengePeriod.Daily; index = -1;
        foreach (var candidate in new[] { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly }) {
            MetaProgression.GetChallenges(candidate);
            var round = P.challengeRounds.Find(r => r != null && r.period == (int)candidate);
            if (round?.tasks == null) continue;
            for (int i = 0; i < round.tasks.Count; i++) {
                var task = round.tasks[i];
                if (task == null || task.done || task.tier == (int)ChallengeTier.Project || task.subject >= 0 || task.mode != (int)ChallengeMode.Add ||
                    Array.IndexOf(Drivable, task.metric) < 0) continue;
                if (best == null || task.target - task.progress < best.target - best.progress) { best = task; period = candidate; index = i; }
            }
        }
        return best;
    }
    static MetaChallengeTask StoredTask(MetaProfile profile, ChallengePeriod period, int index) =>
        profile?.challengeRounds?.Find(r => r != null && r.period == (int)period)?.tasks?.ElementAtOrDefault(index);
    // Feeds the metric with events numbered from 'first', so a second call with the same 'first' replays identical events.
    static void Drive(string metric, long amount, int first)
    {
        if (metric == "sales") { MetaProgression.RecordSale((int)Math.Min(int.MaxValue, amount)); return; }
        for (int i = first; i < first + amount; i++) {
            switch (metric) {
                case "blocks": MetaProgression.RecordMinedBlock(new Vector3Int(40000 + i, -(5 + i % 20), 0), 1, false); break;
                case "cells": MetaProgression.RecordRevealedCell(1000 + i % 2000, 1 + i / 2000); break;
                case "trees": case "grass": MetaProgression.RecordSurfaceHarvest(metric, "drive-" + i); break;
            }
        }
    }

    // ---------- S1: one main progression ----------
    static void OneProgression()
    {
        Fresh("one");
        Check(typeof(MetaRunState).GetFields().All(f => f.Name.IndexOf("level", StringComparison.OrdinalIgnoreCase) < 0) &&
            typeof(RunSaveState).GetFields().All(f => f.Name.IndexOf("level", StringComparison.OrdinalIgnoreCase) < 0),
            "S1: neither the run ledger nor the world save has a level of its own");
        MetaProgression.BeginRun("world-a"); MetaProgression.RecordResource(Item.Gold, 40, "1:-60"); MetaProgression.RecordDepth(100);
        var worldA = MetaProgression.CaptureRunState(); long afterA = Total;
        MetaProgression.EndRun();
        MetaProgression.BeginRun("world-b"); MetaProgression.RecordResource(Item.Gold, 40, "1:-60"); MetaProgression.RecordDepth(100);
        long afterB = Total;
        Check(afterA > 0 && afterB > afterA, "S1: a second world adds to the same main progression (" + afterA + " -> " + afterB + ")");
        Check(P.runs.Count == 2 && Total == Formula(P) && MetaProgression.Level == MetaProgressionCatalog.LevelForXp(Total), "S1: TotalXp is the sum of all world ledgers and the level follows it");
        MetaProgression.EndRun(); MetaProgression.BeginRun("world-a", worldA);
        Check(Total == afterB, "S1: loading the first world again keeps the XP of both worlds");
        MetaProgression.EndRun(); MetaProgression.Reload();
        Check(Total == afterB && MetaProgression.LastError == null, "S1: a new session reads the same shared total");
    }

    // ---------- S2: level-ups reach the disk at once ----------
    static void ImmediateLevelUp()
    {
        Fresh("levelup");
        Seed("seed", MetaProgressionCatalog.XpForLevel(5) - 10);
        Check(MetaProgression.Level == 4, "S2: seed is level 4, 10 XP below level 5");
        MetaProgression.BeginRun("levelup-world");
        Check(MetaProgression.Save() && !MetaProgression.IsSaving, "S2: no write is pending before the level-up");
        MetaProgression.RecordResource(Item.Gold, 1, "3:-40");
        Check(MetaProgression.Level == 5, "S2: one gold (20 XP) reaches level 5");
        Check(MetaProgression.IsSaving, "S2: the level-up starts a profile write at once (no Save, no world save)");
        var disk = Crash();
        Check(disk != null && MetaProgressionCatalog.LevelForXp(disk.totalXp) == 5 && disk.totalXp == Total, "S2: after a kill the disk already holds level 5");
        MetaProgression.Save();
        int before = MetaProgression.Level;
        MetaProgression.RecordResource(Item.Ultronium, 200, "4:-900");
        Check(MetaProgression.Level >= before + 2, "S2: one big find crosses several levels (" + before + " -> " + MetaProgression.Level + ")");
        Check(MetaProgression.IsSaving, "S2: several level-ups at once also write at once");
        disk = Crash();
        Check(disk != null && disk.totalXp == Total && disk.runs.Any(r => r.runId == "levelup-world" && Math.Abs(r.progressXp - Run.progressXp) < 1e-6), "S2: after a kill the disk holds every new level and the run ledger");
        long total = Total; int level = MetaProgression.Level;
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = LastCrash;
        Check(Total == total && MetaProgression.Level == level && MetaProgression.LastError == null, "S2: a new session on the crashed files starts at the reached level " + level);
    }

    // A level-up while the background writer is busy is written as soon as it is done, by the per-frame pump alone
    // (menus, shop and workbench block input, so TickActive does not run there).
    static void DeferredWrite()
    {
        Fresh("deferred");
        Seed("seed", MetaProgressionCatalog.XpForLevel(3) - 5);
        MetaProgression.BeginRun("deferred-world"); MetaProgression.Save();
        MetaProgression.RecordResource(Item.Coal, 1, "1:-10");
        Check(MetaProgression.RequestSave() && MetaProgression.IsSaving && MetaProgression.Level == 2, "S2: a background write of level 2 is running");
        MetaProgression.RecordResource(Item.Gold, 1, "2:-10");
        Check(MetaProgression.Level == 3, "S2: level-up while the writer is busy");
        WaitForWriter();
        var disk = ReadProfile(MetaProgression.ProfilePath);
        Check(MetaProgressionCatalog.LevelForXp(disk.totalXp) == 3 && disk.totalXp == Total, "S2: PumpSaves writes the deferred level-up right after the first write");
    }

    // ---------- S3: challenges pay into the main progression, exactly once ----------
    static void Payouts()
    {
        Fresh("payouts");
        MetaProgression.BeginRun("payout-world");
        var task = FindTask(out var period, out int index);
        Check(task != null, "S3: the round of " + Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " has an open task with a simple metric");
        if (task != null) {
            var older = MetaProgression.CaptureRunState();
            MetaProgression.Save();
            long period0 = P.periodRewardXp, total0 = Total;
            int first = ++seq * 100000;
            Drive(task.metric, task.target - task.progress, first);
            Check(task.done && task.paid == task.xp && task.xp > 0, "S3: " + period + " task " + task.id + " (" + task.target + " " + task.metric + ") is paid with " + task.xp + " XP");
            Check(P.periodRewardXp - period0 >= task.xp && Total - total0 == P.periodRewardXp - period0, "S3: the task XP lands in the main progression (+" + (Total - total0) + ")");
            Check(MetaProgression.IsSaving, "S2: the payout starts a profile write at once");
            var disk = Crash();
            var stored = StoredTask(disk, period, index);
            Check(stored != null && stored.done && stored.paid == task.xp && disk.periodRewardXp == P.periodRewardXp && disk.totalXp == Total, "S2: after a kill the disk holds the paid task");
            long paidPeriod = P.periodRewardXp;
            // A second sale is new revenue, not a replay; cells, blocks and plants carry their own ids.
            if (task.metric != "sales") {
                Drive(task.metric, task.target, first);
                Check(task.paid == task.xp && P.periodRewardXp == paidPeriod, "S3: replaying the same events pays nothing more");
            }
            MetaProgression.BeginRun("payout-world", older);
            Drive(task.metric, task.target, first);
            Check(StoredTask(P, period, index)?.paid == task.xp && P.periodRewardXp == paidPeriod, "S3: after loading the world save from before the task, the replay pays nothing");
            MetaProgression.Save(); MetaProgression.Reload();
            var again = StoredTask(P, period, index);
            Check(again != null && again.done && again.paid == task.xp && P.periodRewardXp == paidPeriod && Total == Formula(P), "S3: after a new session the task stays paid exactly once");
            MetaProgression.BeginRun("payout-world", older);
        }
        // A lifetime milestone stage: Handelsfürst I (50 000 $ sales, 1000 XP).
        MetaProgression.Save();
        long p0 = P.periodRewardXp, t0 = Total, r0 = Run.rewardXp;
        MetaProgression.RecordSale(50000);
        var award = P.milestoneAwards.Find(a => a.id == "ms-sales-1");
        Check(award != null && award.xp == 1000 && Total - t0 == P.periodRewardXp - p0 && P.periodRewardXp - p0 >= 1000, "S3: milestone stage ms-sales-1 pays 1000 XP into the main progression");
        Check(MetaProgression.IsSaving, "S2: the milestone payout starts a profile write at once");
        var after = Crash();
        Check(after != null && after.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1 && after.totalXp == Total, "S2: after a kill the disk holds the milestone payout");
        Check(Run.rewardXp - r0 == P.periodRewardXp - p0 && Run.earnedXp == RunXp(Run), "S3: the run result counts the challenge XP of this run (rewardXp " + Run.rewardXp + ")");
        MetaProgression.RecordSale(50000);
        Check(P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1, "S3: the stage is never paid twice");
    }

    // ---------- S2: an older world save after a crash: nothing lost, nothing paid twice ----------
    static void OlderWorldSave()
    {
        Fresh("older");
        MetaProgression.BeginRun("older-world");
        var older = MetaProgression.CaptureRunState();
        void Play() {
            MetaProgression.RecordDepth(300); MetaProgression.RecordResource(Item.Iron, 30, "7:-120"); MetaProgression.RecordExploration(2, 5, 60);
            MetaProgression.RecordDiscovery("artifact:7:-130"); MetaProgression.RecordSale(5000);
            MetaProgression.RecordMinedBlock(new Vector3Int(7, -120, 0), 2, false); MetaProgression.RecordRevealedCell(57, 120);
            MetaProgression.RecordCraftedAmount(Item.Torche, 4); MetaProgression.RecordSurfaceHarvest("trees", "older-tree");
        }
        Play();
        long total = Total, period = P.periodRewardXp; int level = MetaProgression.Level;
        Check(total > 0 && level >= 1, "S2: setup earned " + total + " XP");
        MetaProgression.Save(); Crash();
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = LastCrash;
        MetaProgression.BeginRun("older-world", older);
        Check(Total == total && MetaProgression.Level == level, "S2: loading the world save from before these events keeps every XP and the level");
        Play();
        Check(Total == total && P.periodRewardXp == period, "S2: replaying the same events after the reload pays nothing twice");
        MetaProgression.RecordResource(Item.Iron, 5, "8:-121");
        Check(Total > total, "S2: new events after the reload still pay");
        var branch = MetaProgression.CaptureRunState();
        MetaProgression.BeginRun("older-world", older);
        MetaProgression.RecordResource(Item.Iron, 5, "8:-121");
        long afterBranches = Total;
        MetaProgression.BeginRun("older-world", branch);
        MetaProgression.RecordResource(Item.Iron, 5, "8:-121");
        Check(Total == afterBranches && Total == Formula(P), "S2: alternating two copies of the same world pays each cell once");
    }

    // Run objectives (three per world) pay into run.challengeXp exactly once per world.
    static void Objectives()
    {
        Fresh("objectives");
        var plan = new MetaRunState { runId = "obj-a" }; plan.objectiveIds.AddRange(new[] { "run-depth", "run-map", "run-crafter" });
        MetaProgression.BeginRun("obj-a", plan);
        var older = MetaProgression.CaptureRunState();
        MetaProgression.RecordDepth(150);
        Check(Run.completedObjectives.SequenceEqual(new[] { "run-depth" }) && Run.challengeXp == 280, "S3: run-depth pays its 280 XP into the run");
        Check(Total == Formula(P) && MetaProgression.IsSaving, "S3: objective XP is part of the main progression and written at once");
        MetaProgression.RecordDepth(160);
        Check(Run.challengeXp == 280, "S3: reaching the objective again pays nothing");
        MetaProgression.BeginRun("obj-a", older);
        Check(Run.challengeXp == 280 && Run.completedObjectives.Contains("run-depth"), "S2: the world save from before the objective neither repays nor loses it");
        MetaProgression.Save(); MetaProgression.Reload(); MetaProgression.BeginRun("obj-a", older); MetaProgression.RecordDepth(150);
        Check(Run.challengeXp == 280 && Total == Formula(P), "S3: after a new session the objective stays paid once");
        long before = Total;
        var other = new MetaRunState { runId = "obj-b" }; other.objectiveIds.AddRange(new[] { "run-depth", "run-map", "run-crafter" });
        MetaProgression.BeginRun("obj-b", other); MetaProgression.RecordDepth(150);
        Check(Run.challengeXp == 280 && Total - before >= 280, "S3: another world earns its own objectives");
        MetaProgression.EndRun();
        bool early = false, late = false;
        for (int i = 0; i < 40; i++) { MetaProgression.BeginRun("gate-" + i); early |= Run.objectiveIds.Contains("run-exotic"); MetaProgression.EndRun(); }
        Check(MetaProgression.Level < 5 && !early, "S3: run-exotic is never drawn below level 5");
        Seed("seed-6", MetaProgressionCatalog.XpForLevel(6));
        for (int i = 0; i < 40; i++) { MetaProgression.BeginRun("gate5-" + i); late |= Run.objectiveIds.Contains("run-exotic"); MetaProgression.EndRun(); }
        Check(MetaProgression.Level >= 5 && late, "S3: run-exotic can be drawn from level 5");
    }

    // Worlds saved without run tallies (saves from before the metaprogression, or by older builds) stand where their own
    // tp markers say: selling restored stock again after reloading pays nothing (findings F-11/F-12).
    static void SavesWithoutTallies()
    {
        Fresh("tallies");
        const string id = "legacy-1-2-3-4";
        MetaProgression.BeginRun(id, new MetaRunState { runId = id });
        MetaProgression.RecordSale(3000);
        Check(Counter("sales") == 3000, "replay: the first sale after loading a save without progression counts");
        MetaProgression.EndRun();
        MetaProgression.BeginRun(id, new MetaRunState { runId = id });
        MetaProgression.RecordSale(3000);
        Check(Counter("sales") == 3000, "replay: selling the same stock again after reloading that save pays nothing");
        MetaProgression.RecordSale(500);
        Check(Counter("sales") == 3500, "replay: sales beyond the earlier peak count again");
        var current = MetaProgression.CaptureRunState(); current.tallies.Clear();
        MetaProgression.BeginRun(id, current);
        MetaProgression.RecordSale(700);
        Check(Counter("sales") == 4200, "replay: an older build's save that carries the newest markers does not stall new sales");
    }

    // A payout marker that only a world save carries (the profile was lost or never saved it) is not trusted: the reward
    // stays open instead of counting as paid without XP (finding G-03, also F-17).
    static void ForeignPayoutMarkers()
    {
        Fresh("markers-a");
        MetaProgression.BeginRun("marker-world"); MetaProgression.RecordSale(50000);
        var world = MetaProgression.CaptureRunState();
        Check(P.milestoneAwards.Any(a => a.id == "ms-sales-1") && world.resourceSources.Contains("cm:ms-sales-1"), "markers: setup world save carries the cm marker of a paid stage");
        Fresh("markers-b");
        MetaProgression.BeginRun("marker-world", world);
        Check(!Run.resourceSources.Contains("cm:ms-sales-1") && !Run.resourceSources.Any(k => k.StartsWith("cr:", StringComparison.Ordinal)) &&
            !P.milestoneAwards.Any(a => a.id == "ms-sales-1"), "markers: payout markers only the world save has are dropped");
        MetaProgression.Save(); MetaProgression.Reload();
        Check(!P.milestoneAwards.Any(a => a.id == "ms-sales-1") && P.periodRewardXp == 0 && Total == Formula(P), "markers: a new session does not rebuild the stage as paid without XP");
        MetaProgression.BeginRun("marker-world", world);
        long before = Total;
        MetaProgression.RecordSale(50000);
        Check(P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1 && Total - before >= 1000, "markers: the stage is earned again in the new profile, exactly once");
        MetaProgression.EndRun();
        Fresh("markers-c");
        MetaProgression.BeginRun("own-world"); MetaProgression.RecordSale(50000);
        var own = MetaProgression.CaptureRunState();
        MetaProgression.BeginRun("own-world", own);
        Check(Run.resourceSources.Contains("cm:ms-sales-1") && P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1, "markers: the profile keeps its own markers when its world save is loaded");
    }

    // A read-only profile (newer version) still records into the run ledger, so the world save keeps the progress
    // (finding F-08); death still ends the run so the result screen opens (F-02); the pause menu warns (G-02).
    static void ReadOnlyProfile()
    {
        Fresh("readonly");
        MetaProgression.BeginRun("ro-world"); var save = MetaProgression.CaptureRunState(); MetaProgression.EndRun();
        string path = MetaProgression.ProfilePath, keep = File.ReadAllText(path), future = "{\"version\":999,\"payload\":\"future\",\"sha256\":\"future\"}";
        Check(MetaProgression.ProfileWarning == null, "read-only: a writable profile shows no warning");
        File.WriteAllText(path, future); MetaProgression.Reload();
        Check(MetaProgression.ProfileWarning != null, "read-only: the pause menu warns that progress is not saved");
        MetaProgression.BeginRun("ro-world", save);
        MetaProgression.RecordDepth(300); MetaProgression.RecordResource(Item.Gold, 10, "9:-280");
        var world = MetaProgression.CaptureRunState();
        Check(world.progressXp > 0 && world.maxDepth == 300 && world.resources.Any(r => r.item == (int)Item.Gold && r.count == 10), "read-only: progress still reaches the world save");
        MetaProgression.RecordRunFinished(false);
        Check(Run.ended, "read-only: death ends the run, so the result screen opens");
        Check(File.ReadAllText(path) == future, "read-only: the newer profile file stays untouched");
        MetaProgression.EndRun();
        File.WriteAllText(path, keep); MetaProgression.Reload();
        MetaProgression.BeginRun("ro-world", world);
        Check(Run.maxDepth == 300 && Run.progressXp == world.progressXp && Total == Formula(P) && Total >= (long)Math.Floor(world.progressXp), "read-only: loading that world later brings the progress into the profile");
        Check(MetaProgression.ProfileWarning == null, "read-only: no warning once the profile is writable again");
    }

    // A victory without rewards (debug modes; in Edit Mode rewards are always off) ends the run without first-victory (F-02).
    static void RunEnd()
    {
        Fresh("runend");
        MetaProgression.BeginRun("victory-world");
        MetaProgressionRuntime.RecordVictory();
        Check(Run.ended && !Run.won && !P.completedMilestones.Contains("first-victory"), "run end: a victory without rewards ends the run without first-victory");
        long before = Total;
        MetaProgression.RecordDepth(500);
        Check(Total == before, "run end: an ended run earns nothing more");
        MetaProgression.BeginRun("victory-world-2"); MetaProgression.MarkRunEnded(); MetaProgression.MarkRunEnded();
        Check(Run.ended && Total == before, "run end: MarkRunEnded is idempotent and pays nothing");
    }

    // Journal (.tmp) handling: an interrupted first write must not lock the profile (F-16), and a complete journal is kept
    // when only replacing the main file fails, so the next session recovers it (G-01).
    static void Journal()
    {
        Fresh("journal-only");
        Directory.CreateDirectory(MetaProgression.DirectoryPath);
        File.WriteAllText(MetaProgression.ProfilePath + ".tmp", "{\"version\":1,\"payload\":\"{\\\"revis");
        MetaProgression.Reload();
        MetaProgression.BeginRun("journal-world"); MetaProgression.RecordDepth(100);
        Check(MetaProgression.Save() && MetaProgression.LastError == null && MetaProgression.ProfileWarning == null, "journal: a cut-off .tmp alone does not lock the profile");
        long total = Total;
        MetaProgression.EndRun(); MetaProgression.Reload();
        Check(Total == total && total > 0, "journal: the new profile persists normally");

        Fresh("journal-kept");
        MetaProgression.BeginRun("kept-world"); MetaProgression.Save();
        string path = MetaProgression.ProfilePath;
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        Directory.CreateDirectory(path + ".bak"); // replacing the main file now fails after the journal was written
        MetaProgression.RecordDepth(250);
        total = Total;
        if (MetaProgression.Save()) Info("journal: File.Replace accepted a directory as backup here, the kept-journal case is not reachable on this platform");
        else {
            Check(File.Exists(path + ".tmp") && ReadProfile(path + ".tmp").totalXp == total, "journal: the complete journal stays when replacing the main file fails");
            Check(MetaProgression.ProfileWarning != null, "journal: the failed write is shown as a warning");
            MetaProgression.EndRun(); MetaProgression.Reload();
            Check(Total == total && MetaProgression.RecoveredFromBackup, "journal: the next session recovers the journal");
        }
        MetaProgression.EndRun(); MetaProgression.Reload();
        Directory.Delete(path + ".bak", true);
    }

    static void SaveWarning()
    {
        Fresh("warning");
        string obstruction = Path.Combine(root, "warning-file");
        File.WriteAllText(obstruction, "preserve");
        MetaProgression.TestDirectory = obstruction;
        MetaProgression.BeginRun("warning-world");
        WaitForWriter();
        Check(MetaProgression.ProfileWarning != null && !string.IsNullOrEmpty(MetaProgression.LastError), "warning: a profile that cannot be written shows a warning (" + MetaProgression.ProfileWarning + ")");
        Check(File.ReadAllText(obstruction) == "preserve", "warning: the obstructing file is untouched");
        MetaProgression.EndRun();
    }

    // Profiles and world saves are validated against these catalog values. Lowering or removing any of them makes
    // existing profiles invalid, so a change needs a migration first (finding F-04, Docs/Progression.md).
    const string FrozenCatalog =
        "U:health|0|20|0.025|2|5;energy|0|20|0.025|2|5;money|0|20|10|1|5;movement|0|10|0.01|2|3;jump|0|10|0.008|2|3;mining|0|20|0.01|2|5;" +
        "reach|1|20|0.015|1|5;capacity|1|25|0.4|1|5;torches|1|10|1|2|3;ladders|1|10|1|2|3\n" +
        "C:depth-100|250|1;depth-250|600|1;depth-500|1200|1;depth-1000|2400|1;exotic-first|400|1;gem-variety|800|1;first-victory|3000|1;" +
        "lifetime-ores|4000|1;lifetime-coal|1500|1;lifetime-copper|1800|1;lifetime-iron|2000|1;lifetime-silver|2500|1;lifetime-gold|3000|1;" +
        "lifetime-regions|2000|1;lifetime-discoveries|3000|1;lifetime-victories|8000|1;lifetime-exotics|1600|1;run-ores|180|0;run-miner|220|0;" +
        "run-crafter|180|0;run-map|240|0;run-depth|280|0;run-discovery|240|0;run-deep|500|0;run-rare|400|0;run-exotic|400|0\n" +
        "L:1000|6000|25|1000|200|5|5866493\n" +
        "R:Coal=4;Copper=6;Diamond=120;Emerald=80;Gold=20;Iron=9;Mythril=95;Obsidian=45;OrangeGarnet=50;Platinum=28;Ruby=65;Silver=14;Titanium=32;Tungsten=40;Ultronium=160";
    static void CatalogFreeze()
    {
        var c = CultureInfo.InvariantCulture;
        string upgrades = string.Join(";", MetaProgressionCatalog.Upgrades.Select(u => u.id + "|" + (u.comfort ? 1 : 0) + "|" + u.maxRank + "|" + u.step.ToString(c) + "|" + u.baseCost + "|" + u.costInterval));
        string challenges = string.Join(";", MetaProgressionCatalog.Challenges.Select(x => x.id + "|" + x.xp + "|" + (x.permanent ? 1 : 0)));
        string curve = string.Join("|", MetaProgressionCatalog.InitialLevelCost, MetaProgressionCatalog.PlateauLevelCost, MetaProgressionCatalog.CurveWidth.ToString(c),
            MetaProgressionCatalog.MaxLevel, MetaProgressionCatalog.PowerLevelCap, MetaProgressionCatalog.ComfortLevelInterval, MetaProgressionCatalog.XpForLevel(MetaProgressionCatalog.MaxLevel));
        string weights = string.Join(";", Enum.GetValues(typeof(Item)).Cast<Item>().Distinct().Where(item => MetaProgressionCatalog.ResourceWeight(item) > 0f)
            .Select(item => item.ToString()).OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => name + "=" + MetaProgressionCatalog.ResourceWeight((Item)Enum.Parse(typeof(Item), name)).ToString(c)));
        string actual = "U:" + upgrades + "\nC:" + challenges + "\nL:" + curve + "\nR:" + weights;
        Check(actual == FrozenCatalog, "catalog: ids, XP, ranks, steps, costs, level curve and ore weights are unchanged" + (actual == FrozenCatalog ? "" : " — actual: " + actual.Replace("\n", " // ")));
    }
}
