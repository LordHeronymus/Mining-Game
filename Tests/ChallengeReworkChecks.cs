using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Edit-mode integration checks for the challenge rework (spec v2): rounds, recording, persistence, replay guards,
// clock and time zones, lifetime milestones, downgrade compatibility, legacy migration and robustness.
// Every profile lives in Temp/ChallengeRework-<ticks> (MetaProgression.TestDirectory); the user's profile and
// saves are never written and all statics are restored. Report: Temp/ChallengeReworkChecks.txt.
public static class ChallengeReworkChecks
{
    const string Report = "Temp/ChallengeReworkChecks.txt";
    [Serializable] sealed class Envelope { public int version = 1; public string payload, sha256; }
    [Serializable] sealed class RunTallies { public string runId; public List<MetaRunTally> tallies = new(); }
    [Serializable] sealed class State
    {
        public List<MetaChallengeRound> rounds = new();
        public List<MetaMilestoneAward> awards = new();
        public List<MetaLifetimeCounter> counters = new();
        public List<RunTallies> tallies = new();
    }
    struct Ledger { public long period, paid, awards, total; }
    sealed class Expect { public string id, metric; public int mode, subject = -1; public bool atLeast; }
    sealed class ExpTask { public int index, target, progress, paid, xp, tier; public bool done; public string name; public Expect map; }
    sealed class LegacyFixture
    {
        public MetaProfile profile;
        public MetaChallengePeriodState daily, weekly;
        public readonly List<ExpTask> dailyTasks = new(), weeklyTasks = new();
        public int layersMask, sightedMask;
    }

    static readonly List<string> lines = new();
    static int passed, failed, seq;
    static string root;
    static readonly Dictionary<string, int> placed = new(), upgradeLevels = new();
    static readonly HashSet<string> gearUsed = new();
    static readonly DateTime T0 = Utc(2026, 10, 6, 10); // Tuesday
    static readonly int[] LayerStart = { 0, 30, 150, 450, 900 };
    static readonly Item[] OreOrder = { Item.Coal, Item.Copper, Item.Iron, Item.Silver, Item.OrangeGarnet, Item.Gold, Item.Titanium, Item.Platinum, Item.Tungsten, Item.Diamond, Item.Ruby, Item.Ultronium };
    static readonly Item[] GearItems = { Item.Backpack, Item.LoadBelt, Item.ReinforcedBackpack, Item.HeavyDutyBoots, Item.SpringGreaves, Item.LoadFrame, Item.Exoskeleton,
        Item.CrystalPendant, Item.CopperEnergyBracelet, Item.EnergyStorageVial, Item.RuneBelt, Item.CrystalHeart, Item.CrystalHarness, Item.TravelMonolith };

    // The 26 catalog ids older builds validate (MetaProgressionCatalog.Challenges); ids and XP must never change.
    static readonly Dictionary<string, int> LegacyMilestones = new() {
        ["depth-100"] = 250, ["depth-250"] = 600, ["depth-500"] = 1200, ["depth-1000"] = 2400, ["exotic-first"] = 400, ["gem-variety"] = 800, ["first-victory"] = 3000,
        ["lifetime-ores"] = 4000, ["lifetime-coal"] = 1500, ["lifetime-copper"] = 1800, ["lifetime-iron"] = 2000, ["lifetime-silver"] = 2500, ["lifetime-gold"] = 3000,
        ["lifetime-regions"] = 2000, ["lifetime-discoveries"] = 3000, ["lifetime-victories"] = 8000, ["lifetime-exotics"] = 1600 };
    static readonly Dictionary<string, int> RunObjectives = new() {
        ["run-ores"] = 180, ["run-miner"] = 220, ["run-crafter"] = 180, ["run-map"] = 240, ["run-depth"] = 280, ["run-discovery"] = 240, ["run-deep"] = 500, ["run-rare"] = 400, ["run-exotic"] = 400 };

    // Legacy period tables (MetaChallengePeriods.cs before the rework), used to build and verify migration fixtures.
    static readonly string[] DailyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "Schwarze Adern", "Eisenherz", "Silbermond", "Goldfieber", "Unbetretene Pfade", "Geheimnisse im Stein", "Fremde Künste", "Himmelsleiter", "Grosse Ausbeute", "Feuer der Schmiede",
        "Platinglanz", "Titankraft", "Obsidianfluch", "Goldener Handschlag", "Ruf der Wälder", "Grüne Ernte", "Unter der Grasnarbe", "Relikte der Vorzeit", "Spuren der Vorzeit", "Steinernes Erwachen", "In der Bergesmitte",
        "XXL · Herz des Berges", "XXL · Ruf des Abgrunds", "XXL · Erzrausch", "Funkelnde Beute", "Glut des Granats", "Rubinfeuer", "Smaragdtraum", "Diamantenglanz",
        "XXL · Edelsteinrausch", "XXL · Kristalljagd", "XXL · Reliktenjäger", "XXL · Reliktvermächtnis", "XXL · Ultroniumrausch", "Die nächste Klinge", "Werkzeugschmiede", "Für den Aufbruch", "Funken des Lebens", "Schultern aus Stahl", "Donner der Schmiede", "Heilende Vorräte", "Grenzgänger", "Verborgene Weiten", "Ruf des Altars", "Spuren der Alten", "Licht und Tiefe" };
    static readonly string[] WeeklyNames = { "Schätze des Berges", "Ruf der Tiefe", "Meister am Amboss", "Kupferglut", "Goldene Geschäfte", "Funken im Dunkel",
        "König der Kohle", "Eiserne Dynastie", "Silberne Träume", "Goldene Ära", "Jenseits der Karten", "Geheimnisse des Berges", "Fremde Künste", "Wege zum Himmel", "Grosse Ausbeute", "Feuer der Schmiede" };
    static readonly int[] DailyTargets = { 50, 100, 5, 25, 1000, 3, 25, 25, 25, 25, 6, 1, 1, 5, 250, 15, 25, 25, 30, 2500, 10, 25, 150, 5, 3, 200, 250, 2000, 2000, 500, 30, 25, 25, 25, 25, 300, 150, 75, 25, 100, 1, 2, 1, 1, 1, 5, 3, 2, 250, 1, 2, 1 };
    static readonly int[] DailyLargeTargets = { 250, 300, 15, 100, 5000, 15, 100, 100, 100, 100, 18, 3, 3, 25, 1250, 45, 100, 100, 100, 7500, 40, 100, 500, 25, 10, 600, 750, 2000, 2000, 500, 100, 100, 100, 100, 100, 300, 150, 75, 25, 100, 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
    static readonly int[] LegacyDailyRewards = { 150, 200, 150, 100, 200, 100, 100, 150, 180, 200, 180, 200, 300, 120, 350, 250, 220, 240, 250, 350, 500, 500, 500, 500, 500, 500, 500, 3000, 3000, 3000, 500, 500, 500, 500, 500, 3000, 3000, 3000, 3000, 3000, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500 };
    static readonly int[] WeeklyTargets = { 500, 750, 25, 150, 10000, 20, 300, 200, 80, 50, 50, 5, 3, 30, 2500, 100 };
    static readonly int[] WeeklyRewards = { 600, 1000, 800, 500, 1000, 500, 400, 600, 750, 900, 800, 1000, 1400, 500, 2000, 1600 };
    static readonly int[] SpecificOreIndices = { 6, 3, 7, 8, 9, 16, 17, 18 };
    static readonly int[] PreviousOreSmall = { 30, 20, 20, 8, 5, 5, 5, 8 }, PreviousOreLarge = { 150, 100, 100, 24, 15, 15, 15, 24 };
    static readonly int[] XxlOreItems = { 3, 0, 1, 4, 2, 8, 28, 31 }, GemItems = { 32, 40, 36, 33 };
    static readonly int[] Lengths = { 6, 16, 20, 22, 23, 24, 25, 26, 27, 29, 30, 31, 35, 40, 45, 47, 52 };
    // Valid legacy daily selections (group mix, unique metrics, at most one XXL); together they cover all 52 indices.
    static readonly int[][] Selections = {
        new[] { 0, 1, 2, 3, 4, 5, 10, 13 }, new[] { 24, 51, 0, 3, 2, 5, 4, 12 }, new[] { 47, 50, 6, 7, 40, 41, 4, 29 }, new[] { 36, 0, 2, 5, 1, 10, 4, 13 },
        new[] { 16, 17, 18, 6, 2, 10, 4, 12 }, new[] { 22, 23, 25, 26, 2, 11, 4, 30 }, new[] { 39, 31, 32, 34, 40, 48, 49, 4 }, new[] { 27, 43, 44, 45, 11, 23, 19, 21 },
        new[] { 38, 46, 42, 15, 1, 10, 4, 0 }, new[] { 35, 14, 2, 1, 10, 4, 33, 26 }, new[] { 37, 20, 45, 46, 41, 24, 47, 19 }, new[] { 28, 22, 2, 51, 50, 4, 13, 5 },
        new[] { 8, 9, 3, 2, 10, 11, 4, 12 } };

    public static object Main()
    {
        lines.Clear(); passed = failed = 0; seq = 0; placed.Clear(); upgradeLevels.Clear(); gearUsed.Clear();
        Directory.CreateDirectory("Temp");
        if (Application.isPlaying) return Finish("FAIL Edit Mode only: stop Play Mode before running ChallengeReworkChecks");
        if (MetaProgression.CurrentRun != null) return Finish("FAIL a run is active; run ChallengeReworkChecks without a loaded world");
        string previousDirectory = MetaProgression.TestDirectory;
        var previousClock = MetaProgression.ChallengeTestUtc;
        var previousSettings = MetaProgression.ChallengeSettingsOverride;
        var previousZone = MetaProgression.ChallengeTimeZoneOverride;
        root = Path.GetFullPath("Temp/ChallengeRework-" + DateTime.UtcNow.Ticks);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try {
            Section("catalog", Catalog);
            Section("rounds", Rounds);
            Section("recording", Recording);
            Section("persistence", Persistence);
            Section("replay", Replay);
            Section("clock", Clock);
            Section("time zones", TimeZones);
            Section("milestones", Milestones);
            Section("downgrade cycle", DowngradeCycle);
            Section("migration", Migration);
            Section("robustness", Robustness);
        } finally {
            try { MetaProgression.EndRun(); } catch (Exception e) { Check(false, "cleanup: EndRun threw " + Describe(e)); }
            try { MetaProgression.TestDirectory = previousDirectory; } catch (Exception e) { Check(false, "cleanup: restoring TestDirectory threw " + Describe(e)); }
            MetaProgression.ChallengeTestUtc = previousClock;
            MetaProgression.ChallengeSettingsOverride = previousSettings;
            MetaProgression.ChallengeTimeZoneOverride = previousZone;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception e) { lines.Add("NOTE temporary profiles kept in " + root + ": " + e.Message); }
        }
        Check(MetaProgression.TestDirectory == previousDirectory && MetaProgression.ChallengeTestUtc == previousClock &&
            MetaProgression.ChallengeSettingsOverride == previousSettings && MetaProgression.ChallengeTimeZoneOverride == previousZone, "cleanup: statics restored");
        Check(watch.Elapsed.TotalSeconds < 60, "cleanup: runtime below 60 s (" + watch.Elapsed.TotalSeconds.ToString("0.0") + " s)");
        return Finish(null);
    }

    static string Finish(string abort)
    {
        if (abort != null) { failed++; lines.Insert(0, abort); }
        string summary = failed == 0 ? "PASS " + passed + ": challenge rework (rounds, recording, persistence, replay, clock, milestones, downgrade, migration, robustness)"
            : "FAIL " + failed + " of " + (passed + failed) + " challenge rework checks";
        File.WriteAllText(Report, summary + "\n" + string.Join("\n", lines) + "\n");
        if (failed == 0) return summary;
        // The CI live status only keeps the returned text (about 2500 characters), so list every failure briefly.
        string list = string.Join(" | ", lines.Where(line => line.StartsWith("FAIL ")).Select(line => line.Length > 125 ? line.Substring(5, 120) + "…" : line.Substring(5)));
        return summary + ": " + (list.Length > 2300 ? list.Substring(0, 2300) + " …" : list);
    }

    // ---------- helpers ----------
    static void Check(bool ok, string label)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "PASS " : "FAIL ") + label.Replace('\n', ' ').Replace('\r', ' '));
    }
    static void Section(string name, Action body) { try { body(); } catch (Exception e) { Check(false, name + ": aborted by " + Describe(e)); } }
    static string Describe(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        var frame = new System.Diagnostics.StackTrace(e, false).GetFrames()?.FirstOrDefault(f => f.GetMethod()?.DeclaringType != null);
        return e.GetType().Name + ": " + e.Message + (frame != null ? " at " + frame.GetMethod().DeclaringType.Name + "." + frame.GetMethod().Name : "");
    }
    static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) => new(year, month, day, hour, minute, second, DateTimeKind.Utc);
    static string Show(long ticks) => ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm") + "Z" : ticks.ToString();
    static MetaProfile P => MetaProgression.Profile;
    static MetaChallengeRound Stored(int period) => P.challengeRounds?.Find(r => r != null && r.period == period);
    static MetaChallengeRound R(int period) { MetaProgression.GetChallenges((ChallengePeriod)period); return Stored(period); }
    static long Paid(MetaChallengeRound round) => round?.tasks == null ? 0 : round.tasks.Where(t => t != null).Sum(t => (long)t.paid) + (round.bonusPaid ? round.bonusXp : 0);
    static long PaidAll() => P.challengeRounds.Sum(Paid);
    static long AwardXp(MetaProfile profile) => profile.milestoneAwards?.Where(a => a != null).Sum(a => (long)a.xp) ?? 0;
    static long Counter(string id) => P.lifetimeCounters?.Find(c => c != null && c.id == id)?.value ?? 0;
    static int Completed(MetaChallengeRound round) => round?.tasks?.Count(t => t != null && t.done) ?? 0;
    static int CompletedAll() => P.challengeRounds.Sum(Completed);
    static int Mask(MetaChallengeRound round, string metric) => round?.masks?.Find(m => m != null && m.metric == metric)?.mask ?? 0;
    static long LegacyTotal(MetaProfile p) => p.periodRewardXp + p.runs.Sum(r => (long)Math.Floor(r.progressXp + r.efficiencyXp) + r.challengeXp) +
        p.completedMilestones.Sum(id => (long)(MetaProgressionCatalog.Challenge(id)?.xp ?? 0));
    static Ledger Snap() => new() { period = P.periodRewardXp, paid = PaidAll(), awards = AwardXp(P), total = MetaProgression.TotalXp };
    static void Consistent(Ledger before, string label)
    {
        var now = Snap();
        Check(now.period - before.period == (now.paid - before.paid) + (now.awards - before.awards),
            label + ": periodRewardXp grows exactly by round payouts and stage awards (+" + (now.period - before.period) + " vs +" + (now.paid - before.paid) + " +" + (now.awards - before.awards) + ")");
        Check(MetaProgression.TotalXp == LegacyTotal(P) && now.total - before.total >= now.period - before.period, label + ": TotalXp follows the legacy formula and includes the new XP");
    }
    static string Sha(string text) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", ""); }
    static void WriteEnvelope(string path, string payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(new Envelope { payload = payload, sha256 = Sha(payload) }));
    }
    static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    static string StateJson(MetaProfile p)
    {
        var state = new State {
            rounds = (p.challengeRounds ?? new List<MetaChallengeRound>()).Where(r => r != null).OrderBy(r => r.period).ToList(),
            awards = p.milestoneAwards ?? new List<MetaMilestoneAward>(), counters = p.lifetimeCounters ?? new List<MetaLifetimeCounter>() };
        foreach (var run in p.runs) state.tallies.Add(new RunTallies { runId = run.runId, tallies = run.tallies ?? new List<MetaRunTally>() });
        return JsonUtility.ToJson(state);
    }
    static string RoundsJson(MetaProfile p) => JsonUtility.ToJson(new State { rounds = (p.challengeRounds ?? new List<MetaChallengeRound>()).Where(r => r != null).OrderBy(r => r.period).ToList() });

    static MethodInfo Private(string name, int parameters) => typeof(MetaProgression).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameters) ?? throw new MissingMethodException("MetaProgression." + name);
    static bool ValidateProfile(MetaProfile p) => (bool)Private("ValidateProfile", 1).Invoke(null, new object[] { p });
    static bool ValidatePeriods(MetaProfile p) => (bool)Private("ValidatePeriods", 1).Invoke(null, new object[] { p });
    static MetaProfile CopyProfile(MetaProfile p) => (MetaProfile)Private("CopyProfile", 1).Invoke(null, new object[] { p });
    static MetaProfile TryRead(string path)
    {
        var args = new object[] { path, null, false };
        return (bool)Private("TryRead", 3).Invoke(null, args) ? (MetaProfile)args[1] : null;
    }
    static bool SafeValidate(MetaProfile p, out string error)
    {
        error = "";
        try { return ValidateProfile(p); } catch (Exception e) { error = " (threw " + Describe(e) + ")"; return false; }
    }

    static ChallengeSettings S1()
    {
        var s = new ChallengeSettings { diligenceDays = 2, diligenceDailyTasks = 1 };
        s.daily.countEasy = 2; s.daily.countMedium = 1; s.daily.countHard = 1;
        s.weekly.countEasy = 1; s.weekly.countMedium = 2; s.weekly.countHard = 1;
        s.monthly.countEasy = 1; s.monthly.countMedium = 1; s.monthly.countHard = 0;
        return s;
    }
    static void Fresh(string name, DateTime now, ChallengeSettings settings, TimeZoneInfo zone = null)
    {
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = Path.Combine(root, name);
        MetaProgression.ChallengeTestUtc = now;
        MetaProgression.ChallengeSettingsOverride = settings;
        MetaProgression.ChallengeTimeZoneOverride = zone ?? TimeZoneInfo.Utc;
        Check(MetaProgression.CurrentRun == null && MetaProgression.TotalXp == 0 && (P.challengeRounds?.Count ?? 0) == 0 && P.runs.Count == 0, name + ": starts with an isolated empty profile");
    }

    // ---------- gameplay drivers (metric -> Record* API per spec section 4) ----------
    static Vector3Int Cell(int layer)
    {
        seq++;
        return new Vector3Int(seq, -(LayerStart[Mathf.Clamp(layer, 1, 5) - 1] + 2 + seq % 20), 0);
    }
    static string Src() => "rework-source-" + (++seq);
    static string RunId => MetaProgression.CurrentRun?.runId ?? "";
    static string TaskName(MetaChallengeRound round, int index)
    {
        var t = round.tasks[index];
        return (ChallengePeriod)round.period + "[" + index + "] " + t.id + "/" + t.metric + (t.subject >= 0 ? "/" + t.subject : "") + " " + t.progress + "/" + t.target;
    }
    static long Cost(MetaChallengeTask t)
    {
        switch (t.metric) {
            case "depth": case "altars": case "torches-placed": case "ladders-placed": case "crafted": case "tools": case "dynamite": case "medkits":
            case "torches-crafted": case "ladders-crafted": case "exotics": case "sales": case "run-sales": case "sale-single": return 1;
            case "resources": case "ore": case "gems": return 3; // gameplay XP side effect
            case "regions": case "discoveries": case "ore-variety": case "depth-gain": return 2L * t.target + 3;
            case "diligence": case "unobtainable": return long.MaxValue;
            default: return t.target + 1L;
        }
    }
    static int Cheapest(MetaChallengeRound round)
    {
        int best = -1; long cost = long.MaxValue;
        for (int i = 0; i < round.tasks.Count; i++) {
            var t = round.tasks[i];
            if (t == null || t.done || t.tier == (int)ChallengeTier.Project || Cost(t) >= cost) continue;
            best = i; cost = Cost(t);
        }
        return best;
    }
    // Brings task progress to `value` (<= target) through the gameplay API of its metric.
    static bool Reach(int period, int index, long value)
    {
        var t = Stored(period).tasks[index];
        value = Math.Min(value, t.target);
        long need = t.mode == (int)ChallengeMode.Max ? (value > t.progress ? 1 : 0) : value - t.progress;
        return need <= 0 || Emit(period, index, (int)Math.Min(int.MaxValue, need), value);
    }
    // Emits `amount` units of the task's metric (Max metrics: the value), whether or not the task is done.
    static bool Emit(int period, int index, int amount, long value)
    {
        var round = Stored(period); var t = round.tasks[index];
        var s = MetaProgression.CurrentChallengeSettings;
        int n = amount;
        switch (t.metric) {
            case "blocks": for (int i = 0; i < n; i++) MetaProgression.RecordMinedBlock(Cell(1), 1, false); return true;
            case "layer-blocks": { int layer = Mathf.Clamp(t.subject, 1, 5); for (int i = 0; i < n; i++) MetaProgression.RecordMinedBlock(Cell(layer), layer, false); return true; }
            case "blasted": for (int i = 0; i < n; i++) MetaProgression.RecordMinedBlock(Cell(1), 1, true); return true;
            case "resources": MetaProgression.RecordResource(Item.Coal, n, Src()); return true;
            case "ore": MetaProgression.RecordResource(t.subject >= 0 ? (Item)t.subject : Item.Coal, n, Src()); return true;
            case "gems": MetaProgression.RecordResource(Item.Diamond, n, Src()); return true;
            case "ore-variety":
                foreach (var ore in OreOrder) {
                    if (n <= 0) break;
                    int bit = ChallengeCatalog.OreBit((int)ore);
                    if (bit >= 0 && (Mask(Stored(period), t.metric) & (1 << bit)) != 0) continue;
                    MetaProgression.RecordResource(ore, 1, Src()); n--;
                }
                return n <= 0;
            case "veins": for (int i = 0; i < n; i++) MetaProgression.RecordVeinCleared(t.subject >= 0 ? t.subject : (int)Item.Silver, Cell(1), 3); return true;
            case "trees": case "grass": for (int i = 0; i < n; i++) MetaProgression.RecordSurfaceHarvest(t.metric, Src()); return true;
            case "depth": MetaProgression.RecordDepthReached((int)value); return true;
            case "depth-gain": MetaProgression.RecordDepth(MetaProgression.CurrentRun.maxDepth + n); return true;
            case "regions": for (int i = 0; i < n; i++) MetaProgression.RecordExploration(++seq, 3, 10); return true;
            case "cells": for (int i = 0; i < n; i++) { seq++; MetaProgression.RecordRevealedCell(seq % 100000, 1 + seq / 100000); } return true;
            case "layers":
                for (int layer = 1; layer <= 5 && n > 0; layer++)
                    if ((Mask(Stored(period), t.metric) & (1 << (layer - 1))) == 0) { MetaProgression.RecordLayerVisited(layer); n--; }
                return n <= 0;
            case "discoveries": for (int i = 0; i < n; i++) MetaProgression.RecordDiscovery("rework-discovery-" + (++seq)); return true;
            case "altars": MetaProgression.RecordAltarReached(); return true;
            case "round-trips": {
                int layer = Mathf.Clamp(Math.Max(2, t.subject), 2, 5);
                for (int i = 0; i < n; i++) MetaProgression.RecordReturn(Math.Max(s.returnMinDepth, LayerStart[layer - 1] + 5), layer, false, s.tripMinNewBlocks);
                return true;
            }
            case "returns": for (int i = 0; i < n; i++) MetaProgression.RecordReturn(Math.Max(s.returnMinDepth, 35), 2, false, s.tripMinNewBlocks); return true;
            case "full-returns": for (int i = 0; i < n; i++) MetaProgression.RecordReturn(Math.Max(s.returnMinDepth, 35), 2, true, s.tripMinNewBlocks); return true;
            case "torches-placed": case "ladders-placed": {
                string key = RunId + "|" + t.metric;
                placed.TryGetValue(key, out int count); count += n; placed[key] = count;
                MetaProgression.RecordPlacedCount(t.metric, count); return true;
            }
            case "crafted": MetaProgression.RecordCraftedAmount(Item.BridgePart, n); return true;
            case "tools": MetaProgression.RecordCraftedAmount(Item.Axe, n); return true;
            case "dynamite": MetaProgression.RecordCraftedAmount(Item.Dynamite, n); return true;
            case "medkits": MetaProgression.RecordCraftedAmount(Item.Medkit, n); return true;
            case "torches-crafted": MetaProgression.RecordCraftedAmount(Item.Torche, n); return true;
            case "ladders-crafted": MetaProgression.RecordCraftedAmount(Item.Ladder, n); return true;
            case "exotics": MetaProgression.RecordCraftedAmount(Item.LavaLamp, n, true, n); return true;
            case "gear":
                foreach (var item in GearItems) {
                    if (n <= 0) break;
                    if (!gearUsed.Add(RunId + "|" + item)) continue;
                    MetaProgression.RecordCraftedAmount(item, 1); n--;
                }
                return n <= 0;
            case "upgrades": {
                string metric = t.subject == 1 ? "energy-upgrades" : t.subject == 2 ? "carrying-upgrades" : "pickaxe-upgrades", key = RunId + "|" + metric;
                upgradeLevels.TryGetValue(key, out int level); level = Math.Max(1, level);
                for (int i = 0; i < n; i++, level++) MetaProgression.RecordCraftUpgrade(metric, level, level + 1);
                upgradeLevels[key] = level; return true;
            }
            case "sales": case "run-sales": MetaProgression.RecordSale(n); return true;
            case "sale-single": MetaProgression.RecordSale((int)value); return true;
            case "artifacts": for (int i = 0; i < n; i++) MetaProgression.RecordArtifact(Cell(1), "Artifact_01"); return true;
            case "artifact": for (int i = 0; i < n; i++) MetaProgression.RecordArtifact(Cell(1), "Artifact_" + Mathf.Clamp(t.subject, 1, 10).ToString("D2")); return true;
            case "artifact-variety": case "artifact-sighted":
                for (int type = 1; type <= 10 && n > 0; type++) {
                    if ((Mask(Stored(period), t.metric) & (1 << (type - 1))) != 0) continue;
                    if (t.metric == "artifact-variety") MetaProgression.RecordArtifact(Cell(1), "Artifact_" + type.ToString("D2"));
                    else MetaProgression.RecordArtifactSight(Cell(1), "Artifact_" + type.ToString("D2"));
                    n--;
                }
                return n <= 0;
            default: return false;
        }
    }

    // ---------- paid markers ----------
    static IEnumerable<string> RunSources() => MetaProgression.CurrentRun?.resourceSources ?? Enumerable.Empty<string>();
    static string[] RoundPrefixes(MetaChallengeRound round) => new[] { "cr:" + round.period + ":" + round.startUtc + ":", "cr:" + (ChallengePeriod)round.period + ":" + round.startUtc + ":" };
    static string TaskKey(MetaChallengeRound round, int index) => round.tasks[index].id + "/" + round.tasks[index].subject + "/" + index;
    static bool TaskMarker(IEnumerable<string> sources, MetaChallengeRound round, int index)
    {
        var expected = RoundPrefixes(round).Select(prefix => prefix + TaskKey(round, index) + ":t").ToArray();
        return sources.Any(expected.Contains);
    }
    static int StageMarkers(IEnumerable<string> sources, MetaChallengeRound round, int index)
    {
        var prefixes = RoundPrefixes(round).Select(prefix => prefix + TaskKey(round, index) + ":s").ToArray();
        return sources.Count(source => prefixes.Any(source.StartsWith));
    }
    static bool BonusMarker(IEnumerable<string> sources, MetaChallengeRound round)
    {
        var prefixes = RoundPrefixes(round);
        return sources.Any(source => prefixes.Any(prefix => source.StartsWith(prefix) && (source == prefix + "b" || source.EndsWith(":b"))));
    }

    // ---------- 0. catalog facts the checks rely on ----------
    static void Catalog()
    {
        var wrong = LegacyMilestones.Concat(RunObjectives).Where(pair => !(MetaProgressionCatalog.Challenge(pair.Key) is MetaChallengeDefinition c && c.xp == pair.Value &&
            c.permanent == LegacyMilestones.ContainsKey(pair.Key))).Select(pair => pair.Key).ToList();
        Check(wrong.Count == 0, "catalog: the 26 legacy milestone/objective ids keep their XP and permanence (" + string.Join(", ", wrong) + ")");
        var lines = ChallengeCatalog.Milestones;
        Check(lines != null && lines.Count == 25, "catalog: 25 lifetime milestone lines (" + (lines?.Count ?? 0) + ")");
        if (lines == null || lines.Count == 0) return;
        var stages = lines.SelectMany(l => l.stages).ToList();
        Check(stages.Select(s => s.id).Distinct().Count() == stages.Count, "catalog: stage ids unique across lines");
        var legacy = stages.Where(s => s.legacy).ToList();
        Check(legacy.Count == LegacyMilestones.Count && legacy.All(s => LegacyMilestones.ContainsKey(s.id)), "catalog: legacy stages are exactly the 17 existing milestone ids");
        Check(legacy.All(s => MetaProgressionCatalog.Challenge(s.id) is MetaChallengeDefinition c && c.target == s.target && c.xp == s.xp), "catalog: legacy stages keep target and XP");
        Check(lines.All(l => l.stages.Length >= 1 && l.stages.Length <= 4 && Enumerable.Range(1, l.stages.Length - 1).All(i => l.stages[i].target > l.stages[i - 1].target)),
            "catalog: 1-4 stages per line with ascending targets");
        var sales = lines.FirstOrDefault(l => l.id == "line-sales"); var coal = lines.FirstOrDefault(l => l.id == "line-coal");
        Check(sales != null && sales.counter && sales.metric == "sales" && sales.stages.Select(s => s.id + ":" + s.target + ":" + s.xp).SequenceEqual(new[] { "ms-sales-1:50000:1000", "ms-sales-2:1000000:2500", "ms-sales-3:10000000:5000" }),
            "catalog: line-sales stages as in spec 7.5");
        Check(coal != null && !coal.counter && coal.metric == "total-coal" && coal.stages.Select(s => s.id + ":" + s.target + ":" + s.xp + ":" + s.legacy).SequenceEqual(new[] { "lifetime-coal:2000:1500:True", "ms-coal-2:10000:3000:False", "ms-coal-3:40000:6000:False" }),
            "catalog: line-coal mixes the legacy stage and new stages");
        Check(lines.Any(l => l.id == "line-tasks" && l.counter && l.metric == "tasks-done") && lines.Any(l => l.id == "line-gems" && !l.counter && l.metric == "total-gems"), "catalog: tasks-done and total-gems lines");
        var dil = ChallengeCatalog.Template("diligence");
        Check(dil != null && dil.family == ChallengeFamily.Special && dil.mode == ChallengeMode.Distinct, "catalog: diligence template (Special, Distinct)");
        Check(new[] { "legacy-depth-gain", "legacy-artifact-sighted", "legacy-impossible" }.All(id => ChallengeCatalog.Template(id) is ChallengeTemplate t && (t.retired || t.weight == 0)),
            "catalog: retired legacy templates exist and are never drawn");
        Check(new[] { "variety", "resources", "crafts", "regions", "depth", "discoveries", "exotics", "rare", "victory" }.All(m => !string.IsNullOrWhiteSpace(ChallengeCatalog.Unit(null, m, -1)) &&
            !string.IsNullOrWhiteSpace(ChallengeCatalog.Icon(null, m, -1))), "catalog: units and icons for run-objective metrics without template");
        Check(OreOrder.Select((item, i) => ChallengeCatalog.OreBit((int)item) == i).All(ok => ok), "catalog: ore-variety bits in spec order");
        Check(new[] { 1, 2, 3, 4, 5 }.Select(ChallengeCatalog.AvailableOres).SequenceEqual(new[] { 3, 4, 7, 11, 12 }) &&
            new[] { 1, 2, 3, 4, 5 }.Select(ChallengeCatalog.AvailableArtifacts).SequenceEqual(new[] { 2, 3, 5, 9, 10 }), "catalog: available ores/artifacts per layer");
    }

    // ---------- 1. rounds ----------
    static string ViewId(MetaChallengeRound round, int index)
    {
        string Base(MetaChallengeTask t) => (ChallengePeriod)round.period + "-" + t.id + (t.subject >= 0 ? "-" + t.subject : "");
        string id = Base(round.tasks[index]);
        return Enumerable.Range(0, index).Any(j => round.tasks[j].tier != (int)ChallengeTier.Project && Base(round.tasks[j]) == id) ? id + "-" + index : id;
    }
    static string Activity(MetaChallengeTask t) { var template = ChallengeCatalog.Template(t.id); return string.IsNullOrEmpty(template?.activity) ? t.id : template.activity; }
    static int Family(MetaChallengeTask t) => (int)(ChallengeCatalog.Template(t.id)?.family ?? ChallengeFamily.Special);
    static void RoundShape(string label, MetaChallengeRound round, MetaChallengeProgress[] views, ChallengePeriodSettings ps, ChallengeSettings s)
    {
        if (round == null) { Check(false, label + ": round missing"); return; }
        var period = (ChallengePeriod)round.period;
        var tasks = round.tasks.Where(t => t.tier != (int)ChallengeTier.Project).ToList();
        for (int tier = 0; tier < 3; tier++)
            Check(tasks.Count(t => t.tier == tier) == ps.Count(tier), label + ": tier " + tier + " count " + tasks.Count(t => t.tier == tier) + " == settings " + ps.Count(tier));
        Check(tasks.All(t => t.tier >= 0 && t.tier <= 2), label + ": no XXL below xxlMinLayer");
        Check(tasks.All(t => t.xp == ps.Xp(t.tier)), label + ": task XP from settings (" + string.Join(",", tasks.Select(t => t.tier + ":" + t.xp)) + ")");
        Check(round.bonusXp == ps.completionBonusXp && !round.bonusPaid, label + ": completion bonus " + ps.completionBonusXp + " from settings");
        Check(tasks.All(t => t.target > 0 && t.progress == 0 && !t.done && t.paid == 0 && !string.IsNullOrEmpty(t.id) && !string.IsNullOrEmpty(t.metric) && !string.IsNullOrEmpty(t.name)),
            label + ": fresh tasks have id, name, metric, a positive target and no progress");
        Check(Enumerable.Range(1, Math.Max(0, round.tasks.Count - 1)).All(i => round.tasks[i - 1].tier == (int)ChallengeTier.Project || round.tasks[i].tier >= round.tasks[i - 1].tier) &&
            round.tasks.Skip(1).All(t => t.tier != (int)ChallengeTier.Project), label + ": project first, then tiers ascending");
        var activities = round.tasks.Select(Activity).ToList();
        Check(activities.Distinct().Count() == activities.Count, label + ": activities unique incl. project (" + string.Join(",", activities) + ")");
        int[] caps = period == ChallengePeriod.Daily ? new[] { 3, 2, 2, 1, 1 } : period == ChallengePeriod.Weekly ? new[] { 3, 3, 2, 1, 2 } : new[] { 2, 2, 2, 1, 1 };
        Check(Enumerable.Range(0, 5).All(f => tasks.Count(t => Family(t) == f) <= caps[f]), label + ": family caps respected");
        Check(views.Length == tasks.Count && views.All(v => v.kind == 0), label + ": GetChallenges lists every task except the project (" + views.Length + ")");
        var expectedIds = Enumerable.Range(0, round.tasks.Count).Where(i => round.tasks[i].tier != (int)ChallengeTier.Project).Select(i => ViewId(round, i)).ToList();
        Check(views.Select(v => v.id).OrderBy(x => x).SequenceEqual(expectedIds.OrderBy(x => x)), label + ": view ids = period-template(-subject) (" + string.Join(",", views.Select(v => v.id)) + ")");
        bool viewsMatch = true;
        for (int i = 0; i < round.tasks.Count; i++) {
            var t = round.tasks[i]; if (t.tier == (int)ChallengeTier.Project) continue;
            var v = views.FirstOrDefault(x => x.id == ViewId(round, i));
            viewsMatch &= v != null && v.target == t.target && v.xp == t.xp && v.current == t.progress && v.completed == t.done && v.tier == t.tier;
        }
        Check(viewsMatch, label + ": views mirror target, XP, progress and tier");
        Check(views.All(v => !string.IsNullOrWhiteSpace(v.name) && !string.IsNullOrWhiteSpace(v.unit) && !string.IsNullOrWhiteSpace(v.icon) && !v.name.Contains('ß') && !(v.unit ?? "").Contains('ß')),
            label + ": views carry name, unit and icon in Swiss spelling");
        var bonus = MetaProgression.ChallengeBonus(period);
        Check(bonus != null && bonus.done == 0 && bonus.required == round.tasks.Count && bonus.xp == round.bonusXp && !bonus.awarded, label + ": ChallengeBonus 0/" + round.tasks.Count);
    }

    static void Rounds()
    {
        var s = S1();
        Fresh("rounds", T0, s);
        var dv = MetaProgression.GetChallenges(ChallengePeriod.Daily);
        var wv = MetaProgression.GetChallenges(ChallengePeriod.Weekly);
        var mv = MetaProgression.GetChallenges(ChallengePeriod.Monthly);
        var day = Stored(0); var week = Stored(1); var month = Stored(3);
        Check(P.challengeRounds.Count == 3 && day != null && week != null && month != null, "rounds: GetChallenges stores exactly one round each for Daily, Weekly and Monthly");
        Check(P.challengePeriods.Count == 0, "rounds: no legacy challengePeriods are written");
        if (day == null || week == null || month == null) return;
        Check(day.format == 2 && week.format == 2 && month.format == 2, "rounds: drawn rounds use format 2");
        Check(day.startUtc == Utc(2026, 10, 6).Ticks && day.endUtc == Utc(2026, 10, 7).Ticks && day.dayIndex == 1, "rounds: daily Tue 06.10. 00:00Z-07.10., dayIndex 1 (" + Show(day.startUtc) + ", " + day.dayIndex + ")");
        Check(week.startUtc == Utc(2026, 10, 5).Ticks && week.endUtc == Utc(2026, 10, 12).Ticks, "rounds: weekly Monday to Monday (" + Show(week.startUtc) + ")");
        Check(month.startUtc == Utc(2026, 10, 1).Ticks && month.endUtc == Utc(2026, 11, 1).Ticks, "rounds: monthly 1st to 1st (" + Show(month.startUtc) + ")");
        RoundShape("rounds daily", day, dv, s.daily, s);
        RoundShape("rounds weekly", week, wv, s.weekly, s);
        RoundShape("rounds monthly", month, mv, s.monthly, s);
        var dil = week.tasks.Find(t => t.id == "diligence");
        Check(dil != null && dil.metric == "diligence" && dil.mode == (int)ChallengeMode.Distinct && dil.tier == 1 && dil.target == s.diligenceDays && dil.xp == s.weekly.xpMedium,
            "rounds: weekly holds the diligence task in a Medium slot (target " + s.diligenceDays + ", " + s.weekly.xpMedium + " XP)");
        Check(day.tasks.Concat(month.tasks).All(t => t.id != "diligence"), "rounds: diligence only in the weekly round");
        var project = month.tasks.FirstOrDefault();
        var view = MetaProgression.GetMonthlyProject();
        Check(project != null && project.tier == (int)ChallengeTier.Project && project.stageTargets != null && project.stageTargets.Length == 3 &&
            project.stageTargets[0] > 0 && project.stageTargets[1] > project.stageTargets[0] && project.stageTargets[2] > project.stageTargets[1] && project.target == project.stageTargets[2] &&
            project.stageXp != null && project.stageXp.SequenceEqual(new[] { s.projectStage1Xp, s.projectStage2Xp, s.projectStage3Xp }) && project.xp == 0 && project.stagesPaid == 0,
            "rounds: monthly project first with 3 ascending stages and stage XP from settings");
        Check(view != null && project != null && view.kind == 1 && view.tier == 4 && view.id == "Monthly-" + project.id && view.target == project.stageTargets[2] &&
            view.stageTargets.SequenceEqual(project.stageTargets) && view.stageXp.SequenceEqual(project.stageXp) && view.stagesDone == 0 && view.xp == project.stageXp[0] && !view.completed,
            "rounds: GetMonthlyProject mirrors the project (kind 1, tier 4, next stage XP)");
        Check(MetaProgression.ChallengeRemaining(ChallengePeriod.Daily) == TimeSpan.FromHours(14) && MetaProgression.ChallengeRemaining(ChallengePeriod.Weekly) == TimeSpan.FromHours(5 * 24 + 14) &&
            MetaProgression.ChallengeRemaining(ChallengePeriod.Monthly) == TimeSpan.FromHours(25 * 24 + 14), "rounds: ChallengeRemaining counts down to each round end");
        Check(MetaProgression.ChallengePeriodEnd(ChallengePeriod.Daily) == day.endUtc && MetaProgression.ChallengePeriodEnd(ChallengePeriod.Weekly) == week.endUtc &&
            MetaProgression.ChallengePeriodEnd(ChallengePeriod.Monthly) == month.endUtc, "rounds: ChallengePeriodEnd equals the stored round end");
        Check(MetaProgression.ChallengeRemaining(ChallengePeriod.Lifetime) == TimeSpan.Zero && MetaProgression.ChallengePeriodEnd(ChallengePeriod.Lifetime) == 0, "rounds: Lifetime has no countdown");

        var life = MetaProgression.GetChallenges(ChallengePeriod.Lifetime);
        var lines = ChallengeCatalog.Milestones;
        Check(life.Length == lines.Count && life.All(v => v.kind == 2) && life.Select(v => v.id).SequenceEqual(lines.Select(l => l.id)),
            "rounds: Lifetime without run lists one kind-2 view per milestone line (" + life.Length + ")");
        var coalView = life.FirstOrDefault(v => v.id == "line-coal"); var coalLine = lines.FirstOrDefault(l => l.id == "line-coal");
        Check(coalView != null && coalLine != null && coalView.medal == 0 && coalView.stage == 0 && coalView.stages == 3 && coalView.target == 2000 && coalView.xp == 1500 &&
            coalView.current == 0 && !coalView.completed && coalView.unit == coalLine.unit && coalView.icon == coalLine.icon, "rounds: line-coal view shows the legacy first stage");
        var depthView = life.FirstOrDefault(v => v.id == "line-depth");
        Check(depthView != null && depthView.stages == 4 && depthView.target == 100 && depthView.xp == 250, "rounds: line-depth has the four legacy depth stages");
        var lifeBonus = MetaProgression.ChallengeBonus(ChallengePeriod.Lifetime);
        Check(lifeBonus != null && lifeBonus.done == 0 && lifeBonus.required == lines.Sum(l => l.stages.Length) && lifeBonus.xp == 0, "rounds: lifetime bonus counts stages (0/" + lines.Sum(l => l.stages.Length) + ")");

        string before = RoundsJson(P);
        MetaProgression.BeginRun("test-run-rounds");
        life = MetaProgression.GetChallenges(ChallengePeriod.Lifetime);
        var objectives = life.Where(v => v.kind == 3).ToArray();
        Check(life.Count(v => v.kind == 2) == lines.Count && objectives.Length == 3 && objectives.Select(v => v.id).SequenceEqual(MetaProgression.CurrentRun.objectiveIds),
            "rounds: with a run, Lifetime appends the 3 run objectives as kind 3");
        Check(objectives.All(v => !string.IsNullOrWhiteSpace(v.unit) && !string.IsNullOrWhiteSpace(v.icon) && v.target > 0 && v.xp > 0), "rounds: run objectives carry unit, icon, target and XP");
        var legacy = MetaProgression.GetChallenges();
        Check(legacy.Length == 20 && legacy.All(v => LegacyMilestones.ContainsKey(v.id) || RunObjectives.ContainsKey(v.id)), "rounds: GetChallenges() keeps its legacy meaning (17 milestones + 3 objectives)");
        MetaProgression.GetChallenges(ChallengePeriod.Daily); MetaProgression.GetChallenges(ChallengePeriod.Weekly); MetaProgression.GetChallenges(ChallengePeriod.Monthly);
        Check(RoundsJson(P) == before, "rounds: starting a run never redraws stored rounds");
        Check(P.challengePeriods.Count == 0, "rounds: challengePeriods stays empty after all API use");
    }

    // ---------- 2. recording ----------
    static void Recording()
    {
        var s = S1();
        Fresh("recording", T0, s);
        MetaProgression.BeginRun("test-run-recording");
        R(0); R(1); R(3);
        int count = Stored(0).tasks.Count;
        Check(count == s.daily.countEasy + s.daily.countMedium + s.daily.countHard, "recording: daily round drawn with a run (" + count + " tasks)");
        bool firstDone = false;
        for (int i = 0; i < count; i++) {
            if (Stored(0).tasks[i].done) { Check(true, "recording: " + TaskName(Stored(0), i) + " completed by a shared metric"); continue; }
            var t = Stored(0).tasks[i];
            string label = "recording " + TaskName(Stored(0), i);
            var before = Snap();
            if (t.target > 1) {
                if (!Reach(0, i, t.target - 1)) { Check(false, label + ": no driver for this metric"); continue; }
                t = Stored(0).tasks[i];
                Check(t.progress == t.target - 1 && !t.done && t.paid == 0, label + ": one short of the target is not paid (" + t.progress + "/" + t.target + ")");
            }
            if (!Reach(0, i, t.target)) { Check(false, label + ": no driver for this metric"); continue; }
            var round = Stored(0); t = round.tasks[i];
            Check(t.progress == t.target && t.done && t.paid == t.xp && t.xp > 0, label + ": reaching the target completes and pays " + t.xp + " XP");
            Check(TaskMarker(RunSources(), round, i), label + ": paid marker cr:<period>:<start>:" + TaskKey(round, i) + ":t in the run ledger");
            Check(P.periodRewardXp - before.period >= t.xp, label + ": XP added to periodRewardXp");
            Consistent(before, label);
            Check(Counter("tasks-done") == CompletedAll(), label + ": lifetime counter tasks-done = completed tasks (" + Counter("tasks-done") + ")");
            var bonus = MetaProgression.ChallengeBonus(ChallengePeriod.Daily);
            Check(bonus.done == Completed(round) && bonus.required == count && bonus.xp == s.daily.completionBonusXp && bonus.awarded == round.bonusPaid,
                label + ": ChallengeBonus " + bonus.done + "/" + bonus.required);
            if (!firstDone) {
                firstDone = true;
                var week = Stored(1); var dil = week.tasks.Find(x => x.id == "diligence");
                Check(round.diligenceCredited && dil != null && dil.progress == 1 && (Mask(week, "diligence") & (1 << round.dayIndex)) != 0,
                    "recording: first completed daily task credits the weekly diligence task (diligenceDailyTasks 1)");
            }
        }
        var day = Stored(0);
        Check(day.tasks.All(t => t.done) && day.bonusPaid && MetaProgression.ChallengeBonus(ChallengePeriod.Daily).awarded, "recording: all daily tasks done pays the completion bonus");
        Check(BonusMarker(RunSources(), day), "recording: bonus paid marker in the run ledger");
        long dayPaid = Paid(day); var ledger = Snap();
        MetaProgression.RecordSale(100); MetaProgression.RecordCraftedAmount(Item.BridgePart, 2); MetaProgression.RecordMinedBlock(Cell(1), 1, false);
        Check(Paid(Stored(0)) == dayPaid && dayPaid == day.tasks.Sum(t => (long)t.xp) + s.daily.completionBonusXp, "recording: the daily bonus is paid exactly once");
        Consistent(ledger, "recording: after the bonus");

        var month = Stored(3); int pi = month.tasks.FindIndex(t => t.tier == (int)ChallengeTier.Project);
        Check(pi == 0, "recording: monthly project present");
        if (pi < 0) return;
        var project = month.tasks[pi];
        string plabel = "recording project " + project.id + "/" + project.metric;
        // A daily task of the same metric may already have advanced the project (e.g. depth), so only test the gap when it exists.
        if (project.stageTargets[0] > 1 && Stored(3).tasks[pi].progress < project.stageTargets[0] - 1 && Reach(3, pi, project.stageTargets[0] - 1))
            Check(Stored(3).tasks[pi].stagesPaid == 0 && Stored(3).tasks[pi].paid == 0, plabel + ": below stage 1 nothing is paid");
        for (int k = 0; k < 3; k++) {
            var before = Snap();
            if (!Reach(3, pi, project.stageTargets[k])) { Check(false, plabel + ": no driver"); break; }
            var p = Stored(3).tasks[pi];
            int expectedPaid = project.stageXp.Take(k + 1).Sum();
            Check(p.stagesPaid == k + 1 && p.paid == expectedPaid && p.done == (k == 2) && p.progress >= project.stageTargets[k],
                plabel + ": stage " + (k + 1) + " pays " + project.stageXp[k] + " (paid " + p.paid + ", stages " + p.stagesPaid + ")");
            Check(StageMarkers(RunSources(), Stored(3), pi) == k + 1, plabel + ": " + (k + 1) + " stage marker(s) cr:...:s<stage>");
            Consistent(before, plabel + " stage " + (k + 1));
            var view = MetaProgression.GetMonthlyProject();
            Check(view != null && view.stagesDone == k + 1 && view.current == p.progress && view.completed == p.done && view.xp == project.stageXp[Math.Min(2, k + 1)],
                plabel + ": GetMonthlyProject shows stage " + (k + 1));
        }
        Check(Counter("tasks-done") == CompletedAll(), "recording: a finished project counts once for tasks-done");
        Check(MetaProgression.ChallengeBonus(ChallengePeriod.Monthly).done == Completed(Stored(3)), "recording: monthly bonus progress counts the project");
        Check(P.challengePeriods.Count == 0, "recording: challengePeriods stays empty");
        DowngradeInvariants("recording");
    }

    // ---------- 3. persistence ----------
    static void Persistence()
    {
        Fresh("persistence", T0, S1());
        string runId = "test-run-persistence";
        MetaProgression.BeginRun(runId);
        R(0); R(1); R(3);
        int k = Cheapest(Stored(0));
        if (k >= 0) Reach(0, k, Stored(0).tasks[k].target);
        int partial = Enumerable.Range(0, Stored(0).tasks.Count).FirstOrDefault(i => !Stored(0).tasks[i].done && Stored(0).tasks[i].target > 1 && Cost(Stored(0).tasks[i]) < 400);
        if (!Stored(0).tasks[partial].done) Reach(0, partial, Stored(0).tasks[partial].target - 1);
        MetaProgression.RecordSale(60000);
        MetaProgression.RecordCraftedAmount(Item.BridgePart, 600);
        for (int i = 0; i < 3; i++) MetaProgression.RecordMinedBlock(Cell(2), 2, false);
        MetaProgression.RecordPlacedCount("torches-placed", 4);
        Check(Stored(0).tasks.Any(t => t.done) && P.milestoneAwards.Count >= 2 && P.lifetimeCounters.Count > 0 && (MetaProgression.CurrentRun.tallies?.Count ?? 0) > 0,
            "persistence: setup produced paid tasks, awards, lifetime counters and run tallies");
        string state = StateJson(P); long period = P.periodRewardXp, total = MetaProgression.TotalXp;
        var dayBefore = Clone(Stored(0));
        Check(MetaProgression.Save() && MetaProgression.LastError == null, "persistence: Save succeeds");
        var read = TryRead(MetaProgression.ProfilePath);
        Check(read != null && StateJson(read) == state && read.periodRewardXp == period && read.totalXp == total, "persistence: TryRead accepts the written file with identical challenge data");
        MetaProgression.Reload();
        Check(MetaProgression.LastError == null && StateJson(P) == state && P.periodRewardXp == period && MetaProgression.TotalXp == total,
            "persistence: Reload keeps rounds, progress, paid flags, awards, counters, tallies and XP");
        var day = R(0);
        Check(day != null && day.startUtc == dayBefore.startUtc && JsonUtility.ToJson(day) == JsonUtility.ToJson(dayBefore), "persistence: the reloaded round is reused, not redrawn");
        Check(P.challengePeriods.Count == 0, "persistence: challengePeriods stays empty");

        var copy = CopyProfile(P);
        Check(copy != null && StateJson(copy) == StateJson(P) && copy.periodRewardXp == P.periodRewardXp, "persistence: CopyProfile copies challengeRounds, milestoneAwards, lifetimeCounters and run tallies");
        string live = StateJson(P);
        var cr = copy.challengeRounds.First(r => r.period == 0);
        cr.tasks[0].progress = Math.Max(0, cr.tasks[0].progress - 1); cr.tasks[0].name = "changed"; cr.masks.Add(new MetaChallengeMask { metric = "probe", mask = 1 });
        cr.runValues.Add(new MetaChallengeRunValue { metric = "probe", runId = "x", value = 1 });
        var cp = copy.challengeRounds.First(r => r.period == 3).tasks[0];
        if (cp.stageTargets != null && cp.stageTargets.Length > 0) cp.stageTargets[0]++;
        if (cp.stageXp != null && cp.stageXp.Length > 0) cp.stageXp[0]++;
        copy.challengeRounds.Add(new MetaChallengeRound { period = 1 });
        copy.milestoneAwards[0].xp++; copy.milestoneAwards.Add(new MetaMilestoneAward { id = "probe", xp = 1 });
        copy.lifetimeCounters[0].value++; copy.lifetimeCounters.Add(new MetaLifetimeCounter { id = "probe", value = 1 });
        var copyRun = copy.runs.First(r => r.runId == runId);
        if (copyRun.tallies.Count > 0) { copyRun.tallies[0].value++; copyRun.tallies[0].peak++; }
        copyRun.tallies.Add(new MetaRunTally { id = "probe", value = 1, peak = 1 });
        Check(StateJson(P) == live, "persistence: mutating the copy never changes the live profile (deep copy)");
    }

    // ---------- 4. double counting and replay ----------
    static MetaChallengeTask Probe(string metric, int mode = 0, int subject = -1, bool atLeast = false) => new() {
        id = "probe-" + metric + (subject >= 0 ? "-" + subject : ""), name = "Probe", unit = "Probe", metric = metric, mode = mode, subject = subject, atLeast = atLeast,
        tier = 0, target = 1000000000, xp = 0 };
    static int Diligence() => Stored(1)?.tasks.Find(t => t.id == "diligence")?.progress ?? -1;
    static long Pr(string id) => Stored(3)?.tasks.Find(t => t.id == id)?.progress ?? -1;

    static void Replay()
    {
        var s = S1();
        Fresh("replay", T0, s);
        string runId = "test-run-replay";
        MetaProgression.BeginRun(runId);
        R(0); R(1); var month = R(3);
        // Probe tasks (xp 0, unreachable target) observe exactly what each gameplay call emits, independent of the draw.
        var probes = new[] { Probe("resources"), Probe("blocks"), Probe("layer-blocks", 0, 2), Probe("sales"), Probe("run-sales", (int)ChallengeMode.Run),
            Probe("sale-single", (int)ChallengeMode.Max), Probe("crafted"), Probe("veins"), Probe("torches-placed"), Probe("returns"), Probe("full-returns"), Probe("round-trips", 0, 2, true) };
        month.tasks.AddRange(probes);
        Check(month.tasks.Count <= 16, "replay: probe tasks fit into a format-2 round (" + month.tasks.Count + ")");
        var start = Snap();

        long r0 = Pr("probe-resources");
        MetaProgression.RecordResource(Item.Coal, 5, "replay-coal-1");
        Check(Pr("probe-resources") == r0 + 5, "replay: RecordResource credits a new source (+5)");
        MetaProgression.RecordResource(Item.Coal, 5, "replay-coal-1");
        Check(Pr("probe-resources") == r0 + 5, "replay: the same resource source counts once");
        MetaProgression.RecordResource(Item.Coal, 5, "replay-coal-2");
        Check(Pr("probe-resources") == r0 + 10, "replay: another source counts again");

        int credited = MetaProgression.CreditedBlocks; long b0 = Pr("probe-blocks"), l0 = Pr("probe-layer-blocks-2");
        var cell = new Vector3Int(90001, -40, 0);
        MetaProgression.RecordMinedBlock(cell, 2, false);
        Check(Pr("probe-blocks") == b0 + 1 && Pr("probe-layer-blocks-2") == l0 + 1 && MetaProgression.CreditedBlocks == credited + 1, "replay: a new mined cell counts for blocks, its layer and CreditedBlocks");
        MetaProgression.RecordMinedBlock(cell, 2, false); MetaProgression.RecordMinedBlock(cell, 2, true);
        Check(Pr("probe-blocks") == b0 + 1 && MetaProgression.CreditedBlocks == credited + 1, "replay: the same cell counts once (also when blasted later)");
        Check(MetaProgression.CurrentRun.resourceSources.Contains("mined:90001:-40"), "replay: mined cells use the ledger key mined:x:y");
        var snapshot = MetaProgression.CaptureRunState();
        snapshot.resourceSources.AddRange(new[] { "l2-block:90002:-40", "l3-block:90003:-200", "xxl-layer:4:90004:-500", "xxl-layer:5:90005:-950", "surface-block:90006:0" });
        MetaProgression.BeginRun(runId, snapshot);
        credited = MetaProgression.CreditedBlocks; b0 = Pr("probe-blocks");
        MetaProgression.RecordMinedBlock(new Vector3Int(90002, -40, 0), 2, false);
        MetaProgression.RecordMinedBlock(new Vector3Int(90003, -200, 0), 3, false);
        MetaProgression.RecordMinedBlock(new Vector3Int(90004, -500, 0), 4, false);
        MetaProgression.RecordMinedBlock(new Vector3Int(90005, -950, 0), 5, false);
        MetaProgression.RecordMinedBlock(new Vector3Int(90006, 0, 0), 1, false);
        Check(Pr("probe-blocks") == b0 && MetaProgression.CreditedBlocks == credited, "replay: cells with legacy keys (l2/l3/xxl-layer/surface-block) are not credited again");
        MetaProgression.RecordMinedBlock(new Vector3Int(90007, -40, 0), 2, false);
        Check(Pr("probe-blocks") == b0 + 1 && MetaProgression.CreditedBlocks == credited + 1, "replay: a cell without legacy key still counts");

        long v0 = Pr("probe-veins"), lv0 = Counter("veins");
        MetaProgression.RecordVeinCleared((int)Item.Iron, new Vector3Int(91000, -60, 0), 4);
        MetaProgression.RecordVeinCleared((int)Item.Iron, new Vector3Int(91000, -60, 0), 4);
        Check(Pr("probe-veins") == v0 + 1 && Counter("veins") == lv0 + 1, "replay: a vein key counts once (challenge and lifetime counter)");
        MetaProgression.RecordVeinCleared((int)Item.Iron, new Vector3Int(91001, -60, 0), 4);
        Check(Pr("probe-veins") == v0 + 2 && MetaProgression.HasMinedOre(Item.Coal, new Vector3Int(-1, 1, 0)) == false, "replay: another vein counts; unknown ore cells are not mined");

        long t0 = Pr("probe-torches-placed"), lt0 = Counter("torches-placed");
        MetaProgression.RecordPlacedCount("torches-placed", 5);
        Check(Pr("probe-torches-placed") == t0 + 5, "replay: placing 5 torches credits 5");
        MetaProgression.SyncPlacedCount("torches-placed", 2); MetaProgression.RecordPlacedCount("torches-placed", 5);
        Check(Pr("probe-torches-placed") == t0 + 5, "replay: picking up and re-placing up to the old count credits nothing");
        MetaProgression.RecordPlacedCount("torches-placed", 7);
        Check(Pr("probe-torches-placed") == t0 + 7, "replay: exceeding the high-water mark credits only the difference (+2)");
        MetaProgression.SyncPlacedCount("torches-placed", 12); MetaProgression.RecordPlacedCount("torches-placed", 12);
        Check(Pr("probe-torches-placed") == t0 + 7, "replay: SyncPlacedCount raises the mark without credit");
        MetaProgression.RecordPlacedCount("torches-placed", 13);
        Check(Pr("probe-torches-placed") == t0 + 8 && Counter("torches-placed") == lt0 + 8, "replay: torches-placed lifetime counter follows the credited amount");

        int m = s.tripMinNewBlocks;
        long ret0 = Pr("probe-returns"), full0 = Pr("probe-full-returns"), trip0 = Pr("probe-round-trips-2"), lret0 = Counter("returns"), lfull0 = Counter("full-returns");
        MetaProgression.RecordReturn(200, 3, true, m - 1);
        Check(Pr("probe-returns") == ret0 && Pr("probe-full-returns") == full0 && Pr("probe-round-trips-2") == trip0 && Counter("returns") == lret0 && Counter("full-returns") == lfull0,
            "replay: a trip below tripMinNewBlocks counts nothing");
        MetaProgression.RecordReturn(200, 3, true, m);
        Check(Pr("probe-returns") == ret0 + 1 && Pr("probe-full-returns") == full0 + 1 && Pr("probe-round-trips-2") == trip0 + 1 && Counter("returns") == lret0 + 1 && Counter("full-returns") == lfull0 + 1,
            "replay: a qualifying deep full trip counts returns, full-returns and round-trips (peak layer 3 >= 2)");
        MetaProgression.RecordReturn(s.returnMinDepth - 1, 1, false, m);
        Check(Pr("probe-returns") == ret0 + 1 && Pr("probe-round-trips-2") == trip0 + 1, "replay: a shallow layer-1 trip counts neither returns nor round-trips");
        MetaProgression.RecordReturn(s.returnMinDepth, 2, false, m);
        Check(Pr("probe-returns") == ret0 + 2 && Pr("probe-round-trips-2") == trip0 + 2 && Pr("probe-full-returns") == full0 + 1, "replay: returnMinDepth itself qualifies");

        var older = MetaProgression.CaptureRunState();
        long s0 = Pr("probe-sales"), c0 = Pr("probe-crafted"), ls0 = Counter("sales"), lc0 = Counter("crafted");
        MetaProgression.RecordSale(1000); MetaProgression.RecordCraftedAmount(Item.BridgePart, 7);
        Check(Pr("probe-sales") == s0 + 1000 && Pr("probe-crafted") == c0 + 7 && Counter("sales") == ls0 + 1000 && Counter("crafted") == lc0 + 7, "replay: first sale and craft are credited");
        MetaProgression.BeginRun(runId, older);
        MetaProgression.RecordSale(1000); MetaProgression.RecordCraftedAmount(Item.BridgePart, 7);
        Check(Pr("probe-sales") == s0 + 1000 && Pr("probe-crafted") == c0 + 7 && Counter("sales") == ls0 + 1000 && Counter("crafted") == lc0 + 7,
            "replay: repeating sale and craft after loading the older snapshot credits nothing (tally peak)");
        MetaProgression.RecordSale(500); MetaProgression.RecordCraftedAmount(Item.BridgePart, 3);
        Check(Pr("probe-sales") == s0 + 1500 && Pr("probe-crafted") == c0 + 10, "replay: activity beyond the peak is credited again");
        var tally = MetaProgression.CurrentRun.tallies?.Find(x => x != null && x.id == "sales");
        Check(tally != null && tally.value == 1500 && tally.peak == 1500, "replay: sales tally value follows the world, peak never drops (" + tally?.value + "/" + tally?.peak + ")");
        var older2 = MetaProgression.CaptureRunState();
        MetaProgression.RecordSale(5000);
        Check(Pr("probe-sale-single") == 5000, "replay: sale-single records the credited single sale (5000)");
        MetaProgression.BeginRun(runId, older2);
        MetaProgression.RecordSale(6000);
        Check(Pr("probe-sale-single") == 5000 && Pr("probe-sales") == s0 + 7500, "replay: sale-single uses the replay-guarded credit (a 6000 replay sale credits 1000)");
        Check(Pr("probe-run-sales") == 7500, "replay: run-sales holds the credited total of this run (" + Pr("probe-run-sales") + ")");
        MetaProgression.BeginRun(runId + "-b");
        MetaProgression.RecordSale(300);
        Check(Pr("probe-run-sales") == 7500 && Pr("probe-sales") == s0 + 7800, "replay: another run keeps its own run-sales value");
        MetaProgression.BeginRun(runId);
        MetaProgression.RecordSale(100);
        Check(Pr("probe-run-sales") == 7600, "replay: switching back continues the first run's value");
        Check(Counter("sales") == ls0 + 7900, "replay: sales lifetime counter equals the credited proceeds");

        // Older builds rewrite the profile without run tallies but keep the ledger: the "tp:" peak markers still guard the replay.
        static List<string> PeakMarkers(string id) => RunSources().Where(x => x.StartsWith("tp:" + id + ":") && x.LastIndexOf(':') == 3 + id.Length).ToList();
        static long Peak(string id) => MetaProgression.CurrentRun.tallies?.Find(x => x != null && x.id == id)?.peak ?? -1;
        var world = MetaProgression.CaptureRunState();
        long sw = Pr("probe-sales"), cw = Pr("probe-crafted"), lsw = Counter("sales"), tw = Pr("probe-torches-placed");
        MetaProgression.RecordSale(4000); MetaProgression.RecordCraftedAmount(Item.BridgePart, 6); MetaProgression.RecordPlacedCount("torches-placed", 15);
        Check(Pr("probe-sales") == sw + 4000 && Pr("probe-crafted") == cw + 6 && Pr("probe-torches-placed") == tw + 2, "replay: sale, craft and placement after the world save are credited");
        var guarded = new[] { "sales", "crafted", "placed:torches-placed" };
        var peaks = guarded.ToDictionary(id => id, Peak);
        var wrong = guarded.Where(id => !(PeakMarkers(id).Count == 1 && PeakMarkers(id)[0] == "tp:" + id + ":" + peaks[id])).ToList();
        Check(wrong.Count == 0 && peaks["sales"] > 0 && peaks["placed:torches-placed"] == 15, "replay: each tally keeps exactly one tp marker holding its peak (" + string.Join(", ", wrong) + ")");
        var mainWorld = Clone(world); mainWorld.tallies.Clear();
        for (int pass = 0; pass < 3; pass++) {
            // 0: this build's save from before the 4000 sale. 1: an older build's save (no tallies) from before it.
            // 2: an older build's save written after it: older builds keep the ledger, so it carries the profile's tp markers.
            MetaRunState snapshotWorld = pass == 0 ? world : pass == 1 ? mainWorld : MetaProgression.CaptureRunState();
            if (pass == 2) snapshotWorld.tallies.Clear();
            bool fromOlder = pass > 0, current = pass == 2;
            string which = !fromOlder ? "this build's" : current ? "a current older build's" : "an older build's";
            long s1 = Pr("probe-sales"), c1 = Pr("probe-crafted"), t1 = Pr("probe-torches-placed"), l1 = Counter("sales");
            MetaProgression.CurrentRun.tallies.Clear(); // the older build rewrote the profile without them
            MetaProgression.BeginRun(runId, snapshotWorld);
            MetaProgression.RecordSale(4000); MetaProgression.RecordCraftedAmount(Item.BridgePart, 6); MetaProgression.RecordPlacedCount("torches-placed", 15);
            // A save without tallies stands where its own tp markers say: replaying the sale after an old save credits
            // nothing, while a save that already holds the newest markers counts new sales and crafts at once.
            // Placements stay guarded by the world's torch count.
            long sale = current ? 4000 : 0, craft = current ? 6 : 0;
            Check(Pr("probe-sales") == s1 + sale && Pr("probe-crafted") == c1 + craft && Pr("probe-torches-placed") == t1 && Counter("sales") == l1 + sale,
                current ? "replay: after a current older build's world save new sales and crafts count at once, placements stay guarded"
                      : "replay: after " + which + " world save from before the sale, replaying it credits nothing (tp markers)");
            peaks["sales"] += sale; peaks["crafted"] += craft;
            wrong = guarded.Where(id => Peak(id) != peaks[id] || !(PeakMarkers(id).Count == 1 && PeakMarkers(id)[0] == "tp:" + id + ":" + peaks[id])).ToList();
            Check(wrong.Count == 0, "replay: " + which + " world save: peaks restored from the markers, one tp marker each (" + string.Join(", ", wrong) + ")");
        }
        Check(MetaProgression.CurrentRun.tallies.Exists(x => x != null && x.id == "tallies") && !RunSources().Any(x => x.StartsWith("tp:tallies:")),
            "replay: the run keeps the marker tally that identifies saves of this build, without a tp marker");
        Consistent(start, "replay");
        string probesJson = JsonUtility.ToJson(new State { rounds = new List<MetaChallengeRound> { Stored(3) } });
        Check(MetaProgression.Save(), "replay: save");
        MetaProgression.Reload();
        Check(JsonUtility.ToJson(new State { rounds = new List<MetaChallengeRound> { R(3) } }) == probesJson, "replay: tasks with unknown template ids survive Save/Reload");
        MetaProgression.BeginRun(runId);
        DowngradeInvariants("replay");
    }

    // ---------- 5. clock ----------
    static TimeZoneInfo Fixed(int hours) => TimeZoneInfo.CreateCustomTimeZone("Test/Fixed" + hours, TimeSpan.FromHours(hours), "Test " + hours, "Test " + hours);
    static TimeZoneInfo Cet() => TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "Test CET", "CET", "CEST", new[] {
        TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)) });
    // Standard -4 h, daylight -3 h, switching at local midnight: 29.03.2026 00:00 is invalid, 25.10.2026 00:00 ambiguous.
    static TimeZoneInfo MidnightZone() => TimeZoneInfo.CreateCustomTimeZone("Test/Midnight", TimeSpan.FromHours(-4), "Test Midnight", "TMS", "TMD", new[] {
        TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday)) });

    static void Clock()
    {
        var s = S1();
        Fresh("clock", T0, s);
        MetaProgression.BeginRun("test-run-clock");
        var d0 = R(0); var w0 = R(1); var m0 = R(3);
        long dStart = d0.startUtc, wStart = w0.startUtc, mStart = m0.startUtc;
        string project0 = m0.tasks.Find(t => t.tier == (int)ChallengeTier.Project)?.id;
        int k = Cheapest(d0);
        Reach(0, k, d0.tasks[k].target);
        var paid = Clone(Stored(0).tasks[k]);
        Check(paid.done && paid.paid == paid.xp, "clock: " + TaskName(Stored(0), k) + " paid on Tuesday");
        int dil = Stored(1).tasks.FindIndex(t => t.id == "diligence");
        Check(dil >= 0 && Diligence() == 1 && (Mask(Stored(1), "diligence") & (1 << 1)) != 0, "clock: diligence credited for Tuesday (bit 1)");
        var ledger = Snap();

        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 5, 10);
        var back = R(0);
        Check(back.startUtc == dStart && back.tasks[k].done && back.tasks[k].paid == paid.xp && P.periodRewardXp == ledger.period && P.challengeRounds.Count(r => r.period == 0) == 1,
            "clock: moving the clock back keeps the round and its paid task");
        var remaining = MetaProgression.ChallengeRemaining(ChallengePeriod.Daily);
        Check(remaining > TimeSpan.Zero && remaining <= TimeSpan.FromHours(14), "clock: the countdown never grows when the clock goes back (" + remaining + ")");
        var before = Snap();
        Emit(0, k, 1, Stored(0).tasks[k].target);
        Check(Stored(0).tasks[k].paid == paid.xp && Diligence() == 1, "clock: recording more of a paid task's metric pays nothing twice and diligence stays at 1");
        Consistent(before, "clock: backwards");

        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 7, 10);
        var d1 = R(0);
        Check(d1.startUtc == Utc(2026, 10, 7).Ticks && d1.endUtc == Utc(2026, 10, 8).Ticks && d1.dayIndex == 2, "clock: next day draws a new daily round (Wednesday, dayIndex 2)");
        Check(P.challengeRounds.Count(r => r.period == 0) == 1 && d1.tasks.Count == s.daily.countEasy + s.daily.countMedium + s.daily.countHard &&
            d1.tasks.All(t => t.progress == 0 && !t.done && t.paid == 0) && !d1.bonusPaid && !d1.diligenceCredited, "clock: the new day starts fresh with the configured task count despite avoidance");
        Check(d1.tasks.Select(Activity).Distinct().Count() == d1.tasks.Count, "clock: activities stay unique in the new day");
        Check(R(1).startUtc == wStart && Diligence() == 1 && R(3).startUtc == mStart, "clock: weekly and monthly rounds unchanged on Wednesday");
        before = Snap();
        k = Cheapest(Stored(0));
        Reach(0, k, Stored(0).tasks[k].target);
        var dt = Stored(1).tasks.Find(t => t.id == "diligence");
        Check(dt != null && dt.progress == 2 && dt.done && dt.paid == s.weekly.xpMedium, "clock: a completed task on a second day finishes diligence (" + dt?.progress + "/" + dt?.target + ", " + dt?.paid + " XP)");
        Check(dil >= 0 && TaskMarker(RunSources(), Stored(1), dil), "clock: diligence paid marker");
        Consistent(before, "clock: Wednesday");

        MetaProgression.ChallengeTimeZoneOverride = Fixed(-12);
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 8, 11);
        var tz = R(0);
        Check(tz.startUtc == Utc(2026, 10, 8).Ticks && tz.endUtc == Utc(2026, 10, 8, 12).Ticks, "clock: a time-zone change westwards starts the next round at the previous end (" + Show(tz.startUtc) + " - " + Show(tz.endUtc) + ")");
        Check(R(1).startUtc == wStart, "clock: the weekly round survives the time-zone change");
        MetaProgression.ChallengeTimeZoneOverride = TimeZoneInfo.Utc;
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 8, 12);
        tz = R(0);
        Check(tz.startUtc == Utc(2026, 10, 8, 12).Ticks && tz.endUtc == Utc(2026, 10, 9).Ticks, "clock: switching back continues without overlap (" + Show(tz.startUtc) + " - " + Show(tz.endUtc) + ")");

        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 11, 23);
        Check(R(0).startUtc == Utc(2026, 10, 11).Ticks && Stored(0).dayIndex == 6 && R(1).startUtc == wStart, "clock: weekly unchanged on Sunday night");
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 12);
        var w1 = R(1);
        Check(w1.startUtc == Utc(2026, 10, 12).Ticks && w1.endUtc == Utc(2026, 10, 19).Ticks && (w1.tasks.Find(t => t.id == "diligence")?.progress ?? -1) == 0,
            "clock: Monday 00:00 draws a new weekly round with diligence at 0");
        Check(R(0).startUtc == Utc(2026, 10, 12).Ticks && Stored(0).dayIndex == 0 && R(3).startUtc == mStart, "clock: Monday daily (dayIndex 0), monthly unchanged");

        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 31, 23, 59, 59);
        Check(R(3).startUtc == mStart && MetaProgression.ChallengePeriodEnd(ChallengePeriod.Monthly) == Utc(2026, 11, 1).Ticks, "clock: the monthly round lasts until the 1st");
        MetaProgression.ChallengeTestUtc = Utc(2026, 11, 1);
        var m1 = R(3);
        Check(m1.startUtc == Utc(2026, 11, 1).Ticks && m1.endUtc == Utc(2026, 12, 1).Ticks && P.challengeRounds.Count(r => r.period == 3) == 1, "clock: new monthly round on the 1st");
        string project1 = m1.tasks.Find(t => t.tier == (int)ChallengeTier.Project)?.id;
        Check(project1 != null && project1 != project0, "clock: the new month avoids last month's project (" + project0 + " -> " + project1 + ")");
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 20);
        Check(R(3).startUtc == m1.startUtc && R(0).startUtc >= Utc(2026, 11, 1).Ticks && R(1).startUtc >= Utc(2026, 10, 26).Ticks, "clock: going back to October reopens nothing");
        Check(P.challengePeriods.Count == 0, "clock: challengePeriods stays empty");
    }

    static void Bounds(ChallengePeriod period, DateTime now, TimeZoneInfo zone, DateTime start, DateTime end, int day, string label)
    {
        ChallengeEngine.Bounds(period, now, zone, out long s, out long e, out int d);
        Check(s == start.Ticks && e == end.Ticks && (day < 0 || d == day), "time zones: Bounds " + label + " (" + Show(s) + " - " + Show(e) + ", day " + d + ")");
    }
    static void TimeZones()
    {
        var cet = Cet(); var midnight = MidnightZone(); var utc = TimeZoneInfo.Utc;
        Bounds(ChallengePeriod.Daily, Utc(2026, 10, 6, 10), utc, Utc(2026, 10, 6), Utc(2026, 10, 7), 1, "UTC Tuesday");
        Bounds(ChallengePeriod.Weekly, Utc(2026, 10, 6, 10), utc, Utc(2026, 10, 5), Utc(2026, 10, 12), 0, "UTC week");
        Bounds(ChallengePeriod.Monthly, Utc(2026, 10, 6, 10), utc, Utc(2026, 10, 1), Utc(2026, 11, 1), -1, "UTC month");
        Bounds(ChallengePeriod.Monthly, Utc(2026, 10, 15, 12), cet, Utc(2026, 9, 30, 22), Utc(2026, 10, 31, 23), -1, "CET October = 31 d + 1 h");
        Bounds(ChallengePeriod.Monthly, Utc(2026, 3, 15, 12), cet, Utc(2026, 2, 28, 23), Utc(2026, 3, 31, 22), -1, "CET March = 31 d - 1 h");
        Bounds(ChallengePeriod.Daily, Utc(2026, 10, 25, 12), cet, Utc(2026, 10, 24, 22), Utc(2026, 10, 25, 23), 6, "CET 25-hour Sunday");
        Bounds(ChallengePeriod.Daily, Utc(2026, 3, 29, 12), cet, Utc(2026, 3, 28, 23), Utc(2026, 3, 29, 22), 6, "CET 23-hour Sunday");
        Bounds(ChallengePeriod.Weekly, Utc(2026, 10, 25, 12), cet, Utc(2026, 10, 18, 22), Utc(2026, 10, 25, 23), 0, "CET week across the DST change");
        Bounds(ChallengePeriod.Daily, Utc(2026, 3, 29, 12), midnight, Utc(2026, 3, 29, 4), Utc(2026, 3, 30, 3), 6, "invalid local midnight steps forward to 01:00");
        Bounds(ChallengePeriod.Daily, Utc(2026, 3, 28, 12), midnight, Utc(2026, 3, 28, 4), Utc(2026, 3, 29, 4), 5, "the day before ends at the first valid time");
        Bounds(ChallengePeriod.Daily, Utc(2026, 10, 25, 12), midnight, Utc(2026, 10, 25, 3), Utc(2026, 10, 26, 4), 6, "ambiguous local midnight uses the earliest instant");
        Bounds(ChallengePeriod.Daily, Utc(2026, 10, 24, 12), midnight, Utc(2026, 10, 24, 3), Utc(2026, 10, 25, 3), 5, "the day before ends at the earliest midnight instant");
        ChallengeEngine.Bounds(ChallengePeriod.Lifetime, T0, utc, out long ls, out long le, out int ld);
        Check(ls == 0 && le == long.MaxValue && ld == 0, "time zones: Lifetime bounds 0 / long.MaxValue / 0");

        Fresh("dst", Utc(2026, 10, 15, 12), S1(), cet);
        var month = R(3);
        Check(month.startUtc == Utc(2026, 9, 30, 22).Ticks && month.endUtc == Utc(2026, 10, 31, 23).Ticks && MetaProgression.ChallengePeriodEnd(ChallengePeriod.Monthly) == month.endUtc &&
            month.endUtc - month.startUtc == TimeSpan.FromDays(31).Ticks + TimeSpan.FromHours(1).Ticks, "time zones: the October round with the CET override lasts 31 d + 1 h");
        Check(R(0).startUtc == Utc(2026, 10, 14, 22).Ticks && Stored(0).endUtc == Utc(2026, 10, 15, 22).Ticks, "time zones: CEST daily round starts at 22:00Z");
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 25, 12);
        Check(R(0).startUtc == Utc(2026, 10, 24, 22).Ticks && Stored(0).endUtc == Utc(2026, 10, 25, 23).Ticks && Stored(0).dayIndex == 6, "time zones: the DST Sunday round lasts 25 h");
        Check(R(1).startUtc == Utc(2026, 10, 18, 22).Ticks && Stored(1).endUtc == Utc(2026, 10, 25, 23).Ticks, "time zones: the week across the DST change ends at local Monday midnight");
        Check(MetaProgression.ChallengeRemaining(ChallengePeriod.Daily) == TimeSpan.FromHours(11), "time zones: countdown to local midnight after the DST change (11 h)");
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 31, 22, 59, 59);
        Check(R(3).startUtc == month.startUtc, "time zones: monthly round unchanged at 23:59:59 local");
        MetaProgression.ChallengeTestUtc = Utc(2026, 10, 31, 23);
        var next = R(3);
        Check(next.startUtc == Utc(2026, 10, 31, 23).Ticks && next.endUtc == Utc(2026, 11, 30, 23).Ticks && P.challengeRounds.Count(r => r.period == 3) == 1,
            "time zones: the November round starts at local midnight (31.10. 23:00Z)");
    }

    // ---------- 6. lifetime milestones ----------
    static MetaChallengeProgress[] Lines() => MetaProgression.GetChallenges(ChallengePeriod.Lifetime).Where(v => v.kind == 2).ToArray();
    static void Milestones()
    {
        Fresh("milestones", T0, S1());
        MetaProgression.BeginRun("test-run-milestones");
        var sales = Lines().FirstOrDefault(v => v.id == "line-sales");
        Check(sales != null && sales.medal == 0 && sales.stage == 0 && sales.stages == 3 && sales.target == 50000 && sales.xp == 1000 && !sales.completed, "milestones: line-sales starts at stage 0");
        var ledger = Snap();
        MetaProgression.RecordSale(50000);
        Check(Counter("sales") == 50000, "milestones: sales lifetime counter 50000");
        var award = P.milestoneAwards.Find(a => a.id == "ms-sales-1");
        Check(award != null && award.xp == 1000 && P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1 && P.periodRewardXp - ledger.period >= 1000, "milestones: ms-sales-1 awarded once with 1000 XP into periodRewardXp");
        Check(MetaProgression.CurrentRun.resourceSources.Contains("cm:ms-sales-1"), "milestones: cm:ms-sales-1 marker in the run ledger");
        Consistent(ledger, "milestones: sales stage");
        sales = Lines().FirstOrDefault(v => v.id == "line-sales");
        Check(sales != null && sales.medal == 1 && sales.stage == 1 && sales.target == 1000000 && sales.xp == 2500 && sales.current == 50000, "milestones: line-sales view advances to stage 2 (Bronze)");
        ledger = Snap();
        MetaProgression.RecordSale(1);
        Check(P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1, "milestones: a reached stage is never awarded twice");
        Consistent(ledger, "milestones: after the stage");
        MetaProgression.RecordCraftedAmount(Item.BridgePart, 500);
        Check(P.milestoneAwards.Any(a => a.id == "ms-crafted-1" && a.xp == 1000) && MetaProgression.CurrentRun.resourceSources.Contains("cm:ms-crafted-1"), "milestones: crafted counter reaches ms-crafted-1");

        ledger = Snap(); long total = MetaProgression.TotalXp;
        MetaProgression.RecordResource(Item.Coal, 2000, "milestone-coal-a");
        Check(P.completedMilestones.Contains("lifetime-coal") && MetaProgression.CurrentRun.milestones.Contains("lifetime-coal"), "milestones: the legacy stage lifetime-coal still goes to completedMilestones");
        Check(!P.milestoneAwards.Any(a => LegacyMilestones.ContainsKey(a.id)) && !MetaProgression.CurrentRun.resourceSources.Contains("cm:lifetime-coal"), "milestones: legacy stages never create awards or cm markers");
        Consistent(ledger, "milestones: legacy stage XP stays outside periodRewardXp");
        Check(MetaProgression.TotalXp - total >= 1500, "milestones: TotalXp includes the legacy stage XP via completedMilestones");
        var coal = Lines().FirstOrDefault(v => v.id == "line-coal");
        Check(coal != null && coal.medal == 1 && coal.stage == 1 && coal.target == 10000 && coal.xp == 3000, "milestones: line-coal shows the legacy stage as reached");
        ledger = Snap();
        MetaProgression.RecordResource(Item.Coal, 8000, "milestone-coal-b");
        Check(P.milestoneAwards.Count(a => a.id == "ms-coal-2" && a.xp == 3000) == 1 && MetaProgression.CurrentRun.resourceSources.Contains("cm:ms-coal-2") && P.completedMilestones.Contains("lifetime-ores"),
            "milestones: ledger line total-coal reaches the new stage ms-coal-2; lifetime-ores stays legacy");
        Consistent(ledger, "milestones: ms-coal-2");
        MetaProgression.RecordResource(Item.Diamond, 100, "milestone-gems");
        Check(P.milestoneAwards.Any(a => a.id == "ms-gems-1" && a.xp == 1000), "milestones: total-gems reaches ms-gems-1");
        var lines = ChallengeCatalog.Milestones;
        int reached = lines.Sum(l => l.stages.Count(st => st.legacy ? P.completedMilestones.Contains(st.id) : P.milestoneAwards.Any(a => a.id == st.id)));
        var bonus = MetaProgression.ChallengeBonus(ChallengePeriod.Lifetime);
        Check(bonus.done == reached && bonus.required == lines.Sum(l => l.stages.Length) && bonus.xp == 0, "milestones: lifetime bonus " + bonus.done + "/" + bonus.required + " stages");

        string awards = JsonUtility.ToJson(new State { awards = P.milestoneAwards }); long period = P.periodRewardXp; total = MetaProgression.TotalXp;
        Check(MetaProgression.Save(), "milestones: save");
        MetaProgression.Reload();
        Check(JsonUtility.ToJson(new State { awards = P.milestoneAwards }) == awards && P.periodRewardXp == period && MetaProgression.TotalXp == total, "milestones: awards survive Save/Reload without new XP");
        MetaProgression.BeginRun("test-run-milestones");
        Check(P.periodRewardXp == period && JsonUtility.ToJson(new State { awards = P.milestoneAwards }) == awards, "milestones: BeginRun re-checks stages without paying twice");
        MetaProgression.BeginRun("test-run-milestones-2");
        Check(P.periodRewardXp == period, "milestones: a new run pays no stage again");
        DowngradeInvariants("milestones");
    }

    // ---------- 7. downgrade invariants of the saved JSON ----------
    static void DowngradeInvariants(string label)
    {
        label += " downgrade";
        Check(MetaProgression.Save() && !MetaProgression.IsSaving, label + ": profile saved");
        var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(MetaProgression.ProfilePath));
        Check(envelope != null && envelope.version == 1 && envelope.sha256 == Sha(envelope.payload), label + ": envelope version 1 with uppercase SHA-256");
        var p = JsonUtility.FromJson<MetaProfile>(envelope.payload);
        Check(p.version == 1 && p.challengePeriods.Count == 0, label + ": saved challengePeriods empty, profile version 1");
        Check(p.completedMilestones.Count <= 100 && p.completedMilestones.All(LegacyMilestones.ContainsKey), label + ": completedMilestones only hold legacy ids");
        var bad = new List<string>();
        foreach (var run in p.runs) {
            if (run.version != 1) bad.Add(run.runId + " version");
            if (run.objectiveIds.Count > 3 || !run.objectiveIds.All(RunObjectives.ContainsKey)) bad.Add(run.runId + " objectives");
            if (!run.completedObjectives.All(run.objectiveIds.Contains)) bad.Add(run.runId + " completed objectives");
            if (!run.milestones.All(id => LegacyMilestones.ContainsKey(id) && p.completedMilestones.Contains(id))) bad.Add(run.runId + " milestones");
            if (run.challengeXp != run.completedObjectives.Sum(id => RunObjectives.TryGetValue(id, out int xp) ? xp : 0) ||
                run.milestoneXp != run.milestones.Sum(id => LegacyMilestones.TryGetValue(id, out int xp) ? xp : 0)) bad.Add(run.runId + " objective/milestone XP");
            if (run.resourceSources.Count > 2000000 || run.resourceSources.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 160) ||
                new HashSet<string>(run.resourceSources).Count != run.resourceSources.Count) bad.Add(run.runId + " resourceSources");
        }
        Check(bad.Count == 0, label + ": runs satisfy the legacy ValidateRun rules (" + string.Join(", ", bad.Take(5)) + ")");
        long expected = p.periodRewardXp + p.runs.Sum(r => (long)Math.Floor(r.progressXp + r.efficiencyXp) + r.challengeXp) +
            p.completedMilestones.Sum(id => LegacyMilestones.TryGetValue(id, out int xp) ? xp : 0);
        Check(p.totalXp == expected, label + ": totalXp = periodRewardXp + run XP + legacy milestone XP (" + p.totalXp + " vs " + expected + ")");
        Check(p.challengeRounds.Sum(Paid) + AwardXp(p) <= p.periodRewardXp, label + ": round payouts and awards fit into periodRewardXp");
        var sources = new HashSet<string>(p.runs.SelectMany(r => r.resourceSources));
        Check(p.milestoneAwards.All(a => sources.Contains("cm:" + a.id)), label + ": every milestone award has a cm marker");
        var missing = new List<string>();
        foreach (var round in p.challengeRounds.Where(r => r.format == 2)) {
            for (int i = 0; i < round.tasks.Count; i++) {
                var t = round.tasks[i];
                if (t.tier == (int)ChallengeTier.Project) { if (StageMarkers(sources, round, i) != t.stagesPaid) missing.Add(TaskName(round, i) + " stages"); }
                else if (t.paid > 0 && !TaskMarker(sources, round, i)) missing.Add(TaskName(round, i));
            }
            if (round.bonusPaid && !BonusMarker(sources, round)) missing.Add((ChallengePeriod)round.period + " bonus");
        }
        Check(missing.Count == 0, label + ": every round payout has a cr marker older builds keep (" + string.Join(", ", missing.Take(5)) + ")");
    }

    // ---------- 8. downgrade -> upgrade cycle ----------
    static string Signature(MetaChallengeRound round) => round == null ? "none" : round.startUtc + ":" + string.Join("|", round.tasks.Select(t =>
        t.id + "/" + t.subject + "/" + t.target + "/" + t.xp + "/" + t.tier + "/" + string.Join(",", t.stageTargets ?? new int[0])));
    // What an older build does to the shared profile: it rewrites it without the challenge fields, but keeps the run ledgers.
    static string SimulateOlderBuild(string label)
    {
        Check(!MetaProgression.IsSaving, label + ": no save pending before the older build writes");
        var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(MetaProgression.ProfilePath));
        var old = JsonUtility.FromJson<MetaProfile>(envelope.payload);
        old.challengeRounds.Clear(); old.milestoneAwards.Clear(); old.lifetimeCounters.Clear();
        foreach (var run in old.runs) run.tallies.Clear();
        old.revision++;
        string payload = JsonUtility.ToJson(old).Replace("\"challengeRounds\":[],", "").Replace("\"milestoneAwards\":[],", "").Replace("\"lifetimeCounters\":[],", "").Replace(",\"tallies\":[]", "");
        Check(!payload.Contains("challengeRounds") && !payload.Contains("milestoneAwards") && !payload.Contains("lifetimeCounters") && !payload.Contains("tallies"),
            label + ": simulated older payload lacks the new fields but keeps the run ledgers");
        WriteEnvelope(MetaProgression.ProfilePath, payload);
        foreach (string extra in new[] { ".bak", ".tmp" }) if (File.Exists(MetaProgression.ProfilePath + extra)) File.Delete(MetaProgression.ProfilePath + extra);
        MetaProgression.Reload();
        return payload;
    }
    static string Descriptor(MetaChallengeRound round, string kind) => kind + ":" + round.period + ":" + round.startUtc + ":";
    static void DowngradeCycle()
    {
        // Default growth, and the June rounds are drawn in a run right after the May rounds (previous round, craft limits),
        // while the rounds after the downgrade are created in the menu: a redraw would differ, so they must be rebuilt.
        var s = S1();
        DateTime may = Utc(2026, 5, 31, 10), june = Utc(2026, 6, 1, 10); // Sunday, then Monday 1 June: day, week and month roll over together
        Fresh("cycle", may, s);
        MetaProgression.BeginRun("seed-cycle", new MetaRunState { runId = "seed-cycle", progressXp = MetaProgressionCatalog.XpForLevel(8) });
        MetaProgression.EndRun();
        var previous = new[] { 0, 1, 3 }.ToDictionary(p => p, p => R(p));
        MetaProgression.ChallengeTestUtc = june;
        MetaProgression.BeginRun("test-run-cycle");
        var drawn = new[] { 0, 1, 3 }.ToDictionary(p => p, p => R(p));
        Check(drawn.All(pair => pair.Value != null && previous[pair.Key] != null && previous[pair.Key].endUtc == pair.Value.startUtc && pair.Value.format == 2),
            "cycle: the June rounds directly follow the May rounds (drawn with their previous round)");
        var signatures = drawn.ToDictionary(pair => pair.Key, pair => Signature(pair.Value));
        int k = Cheapest(Stored(0));
        Reach(0, k, Stored(0).tasks[k].target);
        foreach (int p in new[] { 1, 3 }) { int c = Cheapest(Stored(p)); if (c >= 0) Reach(p, c, Stored(p).tasks[c].target); }
        int pi = Stored(3).tasks.FindIndex(t => t.tier == (int)ChallengeTier.Project);
        if (pi >= 0) Reach(3, pi, Stored(3).tasks[pi].stageTargets[0]);
        MetaProgression.RecordSale(50000);
        var done = new[] { 0, 1, 3 }.ToDictionary(p => p, p => Stored(p).tasks.Select(t => t.done).ToArray());
        int stages = pi >= 0 ? Stored(3).tasks[pi].stagesPaid : 0;
        Check(Stored(0).tasks[k].done && stages >= 1 && P.milestoneAwards.Any(a => a.id == "ms-sales-1") && done[1].Any(x => x) && done[3].Any(x => x),
            "cycle: setup paid daily, weekly and monthly tasks, a project stage and ms-sales-1");
        var described = new[] { 0, 1, 3 }.Where(p => !RunSources().Any(x => x.StartsWith(Descriptor(Stored(p), "cx"))) ||
            RunSources().Count(x => x.StartsWith(Descriptor(Stored(p), "ct"))) != Stored(p).tasks.Count).Select(p => (ChallengePeriod)p).ToList();
        Check(described.Count == 0, "cycle: every paid round is described by cx/ct markers in the ledger (" + string.Join(", ", described) + ")");
        // Without the descriptors, the round would be drawn again from the menu context and without its previous round.
        var redraws = new List<string>();
        foreach (int p in new[] { 0, 1, 3 }) {
            var round = Stored(p);
            var context = new ChallengeDrawContext { reachedLayer = Math.Max(1, P.reachedMiningLayer), level = MetaProgression.Level };
            if (Signature(ChallengeEngine.Draw((ChallengePeriod)p, round.startUtc, round.endUtc, round.dayIndex, context, MetaProgression.CurrentChallengeSettings, null)) != signatures[p])
                redraws.Add(((ChallengePeriod)p).ToString());
        }
        lines.Add("NOTE cycle: a plain redraw would change " + (redraws.Count == 0 ? "no round" : string.Join(", ", redraws)));
        long period = P.periodRewardXp, total = MetaProgression.TotalXp;
        MetaProgression.EndRun();

        string payload = SimulateOlderBuild("cycle");
        Check(payload.Contains("cm:ms-sales-1") && payload.Contains("\"cx:3:") && payload.Contains("\"tp:sales:"), "cycle: the older payload keeps the cm, cx and tp markers");
        Check(MetaProgression.LastError == null && P.periodRewardXp == period && MetaProgression.TotalXp == total, "cycle: the downgraded profile loads with unchanged XP");
        var award = P.milestoneAwards.Find(a => a.id == "ms-sales-1");
        Check(award != null && award.xp == 1000 && P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1, "cycle: milestone awards are rebuilt from cm markers without new XP");
        foreach (int p in new[] { 0, 1, 3 }) {
            var round = R(p);
            Check(Signature(round) == signatures[p] && round.format == 2 && round.endUtc == drawn[p].endUtc && round.dayIndex == drawn[p].dayIndex && round.bonusXp == drawn[p].bonusXp &&
                round.tasks.Select(t => t.name + "|" + t.unit + "|" + t.metric + "|" + t.mode + "|" + t.atLeast).SequenceEqual(drawn[p].tasks.Select(t => t.name + "|" + t.unit + "|" + t.metric + "|" + t.mode + "|" + t.atLeast)),
                "cycle: " + (ChallengePeriod)p + " round is rebuilt with identical tasks (" + Signature(round) + ")");
        }
        var restored = new List<string>();
        foreach (int p in new[] { 0, 1, 3 }) {
            var round = Stored(p);
            for (int i = 0; i < round.tasks.Count && i < done[p].Length; i++) {
                var t = round.tasks[i];
                if (t.tier == (int)ChallengeTier.Project) continue;
                if (done[p][i] ? !(t.done && t.paid == t.xp && t.progress == t.target) : t.done || t.paid != 0) restored.Add(TaskName(round, i));
            }
        }
        Check(restored.Count == 0, "cycle: exactly the tasks paid before the downgrade come back done and paid (" + string.Join(", ", restored) + ")");
        if (pi >= 0) {
            var project = Stored(3).tasks[pi];
            Check(project.stagesPaid == stages && project.paid == project.stageXp.Take(stages).Sum() && stages > 0 && project.progress == project.stageTargets[stages - 1],
                "cycle: the project's paid stages are restored (" + project.stagesPaid + " stages, progress " + project.progress + ")");
        }
        Check(P.periodRewardXp == period && MetaProgression.TotalXp == total, "cycle: restoring paid state pays nothing");
        MetaProgression.BeginRun("test-run-cycle");
        Check(P.periodRewardXp == period, "cycle: BeginRun after the downgrade pays nothing");
        var ledger = Snap(); long awardXp = AwardXp(P); long dayPaid = Stored(0).tasks[k].paid;
        MetaProgression.RecordSale(50000);
        Emit(0, k, 1, Stored(0).tasks[k].target);
        Check(P.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1 && AwardXp(P) == awardXp, "cycle: reaching the sales stage again (counter reset) awards nothing");
        Check(Stored(0).tasks[k].paid == dayPaid, "cycle: the restored daily task is not paid again");
        Consistent(ledger, "cycle: after the downgrade");
        DowngradeInvariants("cycle");
        static string PaidState(MetaChallengeRound round) => string.Join("|", round.tasks.Select(t => t.done + "/" + t.paid + "/" + t.stagesPaid)) + "/" + round.bonusPaid;
        var before = new[] { 0, 1, 3 }.ToDictionary(p => p, p => Signature(Stored(p)) + "#" + PaidState(Stored(p)));
        period = P.periodRewardXp;
        MetaProgression.EndRun();
        SimulateOlderBuild("cycle second");
        var changed = new[] { 0, 1, 3 }.Where(p => Signature(R(p)) + "#" + PaidState(Stored(p)) != before[p]).Select(p => (ChallengePeriod)p).ToList();
        Check(changed.Count == 0 && P.periodRewardXp == period, "cycle: a second downgrade rebuilds the same rounds with the same paid state (" + string.Join(", ", changed) + ")");

        // A converted legacy round (format 1) is never drawn again: paying it writes no round markers, so a later draw of
        // the same day after a downgrade cannot inherit its paid keys.
        var fx = Legacy(95, 52, Selections[0], false, 2, false, 0, 0, 0);
        fx.profile.challengePeriods.Remove(fx.weekly);
        long dayStart = fx.daily.startUtc;
        Fresh("cycle-legacy", new DateTime(dayStart, DateTimeKind.Utc).AddHours(10), s);
        MetaProgression.Reload();
        WriteEnvelope(MetaProgression.ProfilePath, JsonUtility.ToJson(fx.profile));
        foreach (string extra in new[] { ".bak", ".tmp" }) if (File.Exists(MetaProgression.ProfilePath + extra)) File.Delete(MetaProgression.ProfilePath + extra);
        MetaProgression.Reload();
        var converted = R(0);
        Check(converted != null && converted.format == 1 && converted.startUtc == dayStart, "cycle legacy: the running legacy day is a format-1 round");
        MetaProgression.BeginRun("test-run-cycle-legacy");
        int li = converted?.tasks.FindIndex(t => t.id == "ore" && t.subject == 0 && !t.done) ?? -1;
        long legacyPeriod = P.periodRewardXp;
        if (li >= 0) Reach(0, li, converted.tasks[li].target);
        Check(li >= 0 && Stored(0) == converted && converted.tasks[li].done && P.periodRewardXp > legacyPeriod, "cycle legacy: a task of the format-1 round is paid on this build");
        var day = new MetaChallengeRound { period = 0, startUtc = dayStart };
        var legacyMarkers = RunSources().Where(x => new[] { "cr", "cx", "ct", "cs" }.Any(kind => x.StartsWith(Descriptor(day, kind)))).ToList();
        Check(legacyMarkers.Count == 0, "cycle legacy: paying a format-1 round writes no round markers (" + string.Join(", ", legacyMarkers.Take(3)) + ")");
        legacyPeriod = P.periodRewardXp;
        MetaProgression.EndRun();
        SimulateOlderBuild("cycle legacy");
        var redrawn = R(0);
        Check(redrawn != null && redrawn.format == 2 && redrawn.startUtc == dayStart && Completed(redrawn) == 0 && Paid(redrawn) == 0 && P.periodRewardXp == legacyPeriod,
            "cycle legacy: the format-2 draw of the same day starts unpaid and pays nothing (" + Completed(redrawn) + " done)");
    }

    // ---------- 9. migration of legacy period states ----------
    static Expect E(string id, string metric, int mode = 0, int subject = -1, bool atLeast = false) => new() { id = id, metric = metric, mode = mode, subject = subject, atLeast = atLeast };
    static Expect Map(int i, MetaChallengePeriodState state, int variant)
    {
        switch (i) {
            case 0: case 14: return E("ores", "resources");
            case 1: return E("legacy-depth-gain", "depth-gain");
            case 2: case 15: return E("crafted", "crafted");
            case 3: return E("ore", "ore", 0, 0);
            case 4: case 19: return E("sales", "sales");
            case 5: return E("torches-crafted", "torches-crafted");
            case 6: return E("ore", "ore", 0, 3);
            case 7: return E("ore", "ore", 0, 1);
            case 8: return E("ore", "ore", 0, 4);
            case 9: return E("ore", "ore", 0, 2);
            case 10: return E("regions", "regions");
            case 11: return E("discoveries", "discoveries");
            case 12: return E("exotics", "exotics");
            case 13: return E("ladders-crafted", "ladders-crafted");
            case 16: return E("ore", "ore", 0, 8);
            case 17: return E("ore", "ore", 0, 28);
            case 18: return E("legacy-impossible", "unobtainable", 0, 31);
            case 20: return E("trees", "trees");
            case 21: return E("grass", "grass");
            case 22: return E("layer-blocks", "layer-blocks", 0, 1);
            case 23: case 37: return E("artifacts", "artifacts");
            case 24: case 38: return E("artifact", "artifact", 0, state.dailyArtifactType + 1);
            case 25: case 26: case 27: case 28: return E("layer-blocks", "layer-blocks", 0, i - 23);
            case 29: { int item = XxlOreItems[state.dailyOreType]; return item == 31 ? E("legacy-impossible", "unobtainable", 0, 31) : E("ore", "ore", 0, item); }
            case 30: case 35: return E("gems", "gems");
            case 31: return E("gem", "ore", 0, 32);
            case 32: return E("gem", "ore", 0, 40);
            case 33: return E("legacy-impossible", "unobtainable", 0, 36);
            case 34: return E("gem", "ore", 0, 33);
            case 36: { int item = GemItems[state.dailyGemType]; return item == 36 ? E("legacy-impossible", "unobtainable", 0, 36) : E("gem", "ore", 0, item); }
            case 39: return E("ultronium", "ore", 0, 24);
            case 40: return E("upgrades", "upgrades", 0, 0);
            case 41: return E("tools", "tools");
            case 42: return E("gear", "gear");
            case 43: return E("upgrades", "upgrades", 0, 1);
            case 44: return E("upgrades", "upgrades", 0, 2);
            case 45: return E("dynamite", "dynamite");
            case 46: return E("medkits", "medkits");
            case 47: return E("layers", "layers", (int)ChallengeMode.Distinct);
            case 48: return E("cells", "cells");
            case 49: return E("altars", "altars", (int)ChallengeMode.Max);
            case 50: return E("legacy-artifact-sighted", "artifact-sighted", (int)ChallengeMode.Distinct);
            default: return E("round-trips", "round-trips", 0, variant == 1 ? 5 : 3, true);
        }
    }
    static bool IsXxl(int i) => (i >= 27 && i <= 29) || (i >= 35 && i <= 39);
    static int LegacyTarget(int version, int[] variants, int i)
    {
        if (version == 1 && i == 18 && variants[i] == 0) return 25;
        if (version == 0) { int slot = Array.IndexOf(SpecificOreIndices, i); if (slot >= 0) return variants[i] == 1 ? PreviousOreLarge[slot] : PreviousOreSmall[slot]; }
        return variants[i] == 1 ? DailyLargeTargets[i] : DailyTargets[i];
    }
    static LegacyFixture Legacy(int f, int length, int[] selection, bool missingAward, int version, bool variants, int oreType, int gemType, int artifactType)
    {
        var fx = new LegacyFixture();
        var d = fx.daily = new MetaChallengePeriodState {
            period = 0, startUtc = Utc(2026, 9, 1).AddDays(f).Ticks, endUtc = Utc(2026, 9, 2).AddDays(f).Ticks, counters = new int[length], rewarded = new bool[length],
            awardedXp = missingAward ? null : new int[length], dailySelection = (int[])selection.Clone(), dailyVariants = null, oreTargetVersion = version,
            dailyOreType = oreType, dailyGemType = gemType, dailyArtifactType = artifactType };
        var v = new int[52];
        if (variants && length >= 20) {
            d.dailyVariants = new int[length];
            foreach (int k in new[] { 1, 3 }) if (selection[k] < length) d.dailyVariants[selection[k]] = 1;
            Array.Copy(d.dailyVariants, v, length);
        }
        long xp = 0;
        for (int k = 0; k < 8; k++) {
            int i = selection[k], target = LegacyTarget(version, v, i), reward = IsXxl(i) ? 3000 : v[i] == 1 ? 1500 : 500;
            int progress = 0, paid = 0; bool done = false;
            if (i < length && i != 20 && i != 21) {
                if (k == 0 || k == 2) { progress = target; done = true; paid = missingAward ? LegacyDailyRewards[i] : reward; }
                else if (k == 1) progress = Math.Max(0, target - 1);
            }
            if (i < length) { d.counters[i] = progress; d.rewarded[i] = done; if (!missingAward) d.awardedXp[i] = paid; }
            if (i == 47 && length == 52) d.visitedLayerMask = fx.layersMask = (1 << progress) - 1;
            if (i == 50 && length == 52) d.artifactVarietyMask = fx.sightedMask = (1 << progress) - 1;
            fx.dailyTasks.Add(new ExpTask { index = i, target = target, progress = progress, done = done, paid = paid, xp = done ? paid : reward,
                tier = IsXxl(i) ? 3 : v[i] == 1 ? 1 : 0, name = DailyNames[i], map = Map(i, d, v[i]) });
            xp += paid;
        }
        var w = fx.weekly = new MetaChallengePeriodState {
            period = 1, startUtc = Utc(2026, 8, 3).AddDays(7 * f).Ticks, endUtc = Utc(2026, 8, 10).AddDays(7 * f).Ticks, counters = new int[length], rewarded = new bool[length],
            awardedXp = missingAward ? null : new int[length] };
        for (int i = 0; i < 16; i++) {
            int progress = 0, paid = 0; bool done = false;
            if (i < length && (i == 0 || i == 3 || i == 14)) { progress = WeeklyTargets[i]; done = true; paid = WeeklyRewards[i]; }
            else if (i < length && i == 1) progress = 100;
            if (i < length) { w.counters[i] = progress; w.rewarded[i] = done; if (!missingAward) w.awardedXp[i] = paid; }
            fx.weeklyTasks.Add(new ExpTask { index = i, target = WeeklyTargets[i], progress = progress, done = done, paid = paid, xp = done ? paid : WeeklyRewards[i],
                tier = WeeklyRewards[i] < 1000 ? 0 : WeeklyRewards[i] < 1500 ? 1 : 2, name = WeeklyNames[i], map = Map(i, w, 0) });
            xp += paid;
        }
        fx.profile = new MetaProfile { periodRewardXp = xp + (f % 2) * 77 };
        fx.profile.totalXp = fx.profile.periodRewardXp;
        fx.profile.challengePeriods.Add(d); fx.profile.challengePeriods.Add(w);
        return fx;
    }
    static string Compare(List<MetaChallengeTask> tasks, List<ExpTask> expected)
    {
        if (tasks == null || tasks.Count != expected.Count) return "task count " + (tasks?.Count ?? -1) + " != " + expected.Count;
        for (int k = 0; k < expected.Count; k++) {
            var t = tasks[k]; var e = expected[k]; string at = "#" + k + " legacy " + e.index + ": ";
            if (t == null) return at + "null";
            if (t.id != e.map.id || t.metric != e.map.metric || t.mode != e.map.mode || t.subject != e.map.subject || t.atLeast != e.map.atLeast)
                return at + t.id + "/" + t.metric + "/" + t.mode + "/" + t.subject + "/" + t.atLeast + " != " + e.map.id + "/" + e.map.metric + "/" + e.map.mode + "/" + e.map.subject + "/" + e.map.atLeast;
            if (t.target != e.target || t.progress != e.progress || t.done != e.done || t.paid != e.paid || t.xp != e.xp || t.tier != e.tier)
                return at + "target/progress/done/paid/xp/tier " + t.target + "/" + t.progress + "/" + t.done + "/" + t.paid + "/" + t.xp + "/" + t.tier + " != " +
                    e.target + "/" + e.progress + "/" + e.done + "/" + e.paid + "/" + e.xp + "/" + e.tier;
            if (t.name != e.name) return at + "name " + t.name + " != " + e.name;
        }
        return null;
    }
    static void Migrate(LegacyFixture fx, string label)
    {
        label = "migration " + label;
        var p = fx.profile; long total = p.totalXp, period = p.periodRewardXp;
        bool ok = SafeValidate(p, out string error);
        Check(ok, label + ": legacy profile accepted" + error);
        if (!ok) return;
        Check(p.challengePeriods.Count == 0 && p.totalXp == total && p.periodRewardXp == period, label + ": challengePeriods emptied, XP unchanged");
        var day = p.challengeRounds.Find(r => r != null && r.period == 0); var week = p.challengeRounds.Find(r => r != null && r.period == 1);
        Check(day != null && day.format == 1 && day.startUtc == fx.daily.startUtc && day.endUtc == fx.daily.endUtc && day.bonusXp == 0 && !day.bonusPaid,
            label + ": daily converted to a format-1 round with the legacy bounds");
        string diff = day == null ? "missing" : Compare(day.tasks, fx.dailyTasks);
        Check(diff == null, label + ": 8 daily tasks follow the 7.6 map with legacy target/progress/done/paid (" + diff + ")");
        Check(week != null && week.format == 1 && week.startUtc == fx.weekly.startUtc && week.endUtc == fx.weekly.endUtc && week.bonusXp == 0, label + ": weekly converted to a format-1 round");
        diff = week == null ? "missing" : Compare(week.tasks, fx.weeklyTasks);
        Check(diff == null, label + ": 16 weekly tasks keep legacy values (" + diff + ")");
        if (fx.layersMask != 0) Check(Mask(day, "layers") == fx.layersMask, label + ": visitedLayerMask becomes the layers mask (" + Mask(day, "layers") + ")");
        if (fx.sightedMask != 0) Check(Mask(day, "artifact-sighted") == fx.sightedMask, label + ": artifactVarietyMask becomes the artifact-sighted mask");
        Check(day != null && week != null && ChallengeEngine.Sanitize(Clone(day)) && ChallengeEngine.Sanitize(Clone(week)), label + ": converted rounds pass Sanitize");
        string json = RoundsJson(p);
        Check(SafeValidate(p, out error) && RoundsJson(p) == json, label + ": validating again changes nothing" + error);
    }
    static MetaChallengeRound ValidRound(int period, long start, bool done, int days = 1)
    {
        var date = new DateTime(start, DateTimeKind.Utc);
        var round = new MetaChallengeRound { period = period, format = 2, startUtc = start, endUtc = start + days * TimeSpan.TicksPerDay, dayIndex = ((int)date.DayOfWeek + 6) % 7, bonusXp = 1000 };
        round.tasks.Add(new MetaChallengeTask { id = "blocks", name = "Schaufel und Hacke", unit = "Blöcke abgebaut", metric = "blocks", mode = 0, subject = -1, tier = 1,
            target = 250, xp = 600, progress = done ? 250 : 3, done = done, paid = done ? 600 : 0 });
        return round;
    }
    static void Migration()
    {
        int f = 0;
        for (int j = 0; j < Lengths.Length; j++, f++)
            Migrate(Legacy(f, Lengths[j], Selections[j % Selections.Length], j % 3 == 2, (j / 2) % 3, j % 2 == 0, j % 8, j % 4, j % 10),
                "length " + Lengths[j] + " sel " + (char)('A' + j % Selections.Length) + (j % 3 == 2 ? " no awardedXp" : "") + " v" + (j / 2) % 3);
        for (int j = 0; j < Selections.Length; j++, f++)
            Migrate(Legacy(f, 52, Selections[j], j % 4 == 3, j % 3, true, 7, 2, (j * 3) % 10), "52 sel " + (char)('A' + j) + (j % 4 == 3 ? " no awardedXp" : "") + " v" + j % 3);

        var file = Legacy(90, 52, Selections[2], false, 2, true, 2, 0, 4);
        string path = Path.Combine(root, "legacy-file", "meta-progression.json");
        WriteEnvelope(path, JsonUtility.ToJson(file.profile));
        var read = TryRead(path);
        var readDay = read?.challengeRounds.Find(r => r.period == 0);
        Check(read != null && read.challengePeriods.Count == 0 && readDay != null && Compare(readDay.tasks, file.dailyTasks) == null && Mask(readDay, "layers") == file.layersMask,
            "migration: a legacy profile file converts through TryRead (envelope, SHA, JSON arrays)");

        var periods = Legacy(91, 52, Selections[0], false, 2, false, 0, 0, 0);
        Check(ValidatePeriods(periods.profile) && periods.profile.challengePeriods.Count == 0 && periods.profile.challengeRounds.Count == 2, "migration: ValidatePeriods converts legacy states directly");

        var corrupt = Legacy(92, 52, Selections[0], false, 2, false, 0, 0, 0);
        corrupt.daily.counters[0] = DailyTargets[0] + 5;
        bool ok = SafeValidate(corrupt.profile, out string error);
        Check(ok && corrupt.profile.challengePeriods.Count == 0 && !corrupt.profile.challengeRounds.Any(r => r.period == 0) && corrupt.profile.challengeRounds.Any(r => r.period == 1 && r.format == 1),
            "migration: a corrupted legacy state is dropped, the other one converts" + error);
        var noSelection = new MetaProfile { periodRewardXp = 900, totalXp = 900 };
        noSelection.challengePeriods.Add(new MetaChallengePeriodState { period = 0, startUtc = Utc(2026, 10, 5).Ticks, endUtc = Utc(2026, 10, 6).Ticks,
            counters = new[] { 50, 100, 5, 20, 1000, 3 }, rewarded = Enumerable.Repeat(true, 6).ToArray() });
        ok = SafeValidate(noSelection, out error);
        Check(ok && noSelection.challengeRounds.Count == 0 && noSelection.challengePeriods.Count == 0 && noSelection.periodRewardXp == 900 && noSelection.totalXp == 900,
            "migration: a legacy daily without selection is dropped and its XP kept" + error);
        var unknown = new MetaProfile();
        unknown.challengePeriods.Add(new MetaChallengePeriodState { period = 5, startUtc = 1, endUtc = 2 });
        ok = SafeValidate(unknown, out error);
        Check(ok && unknown.challengeRounds.Count == 0 && unknown.challengePeriods.Count == 0, "migration: a state with an unknown period is dropped" + error);
        var newer = Legacy(93, 52, Selections[0], false, 2, false, 0, 0, 0);
        newer.profile.challengeRounds.Add(ValidRound(0, newer.daily.startUtc, false));
        ok = SafeValidate(newer.profile, out error);
        Check(ok && newer.profile.challengeRounds.Count(r => r.period == 0) == 1 && newer.profile.challengeRounds.First(r => r.period == 0).format == 2 &&
            newer.profile.challengeRounds.Any(r => r.period == 1 && r.format == 1), "migration: an existing round with startUtc >= legacy start wins over the legacy daily" + error);
        var over = new MetaProfile(); over.challengeRounds.Add(ValidRound(0, T0.Date.Ticks, true));
        Check(!SafeValidate(over, out _), "migration: rounds that paid more than periodRewardXp reject the profile (integrity)");
        var fits = new MetaProfile { periodRewardXp = 600, totalXp = 600 }; fits.challengeRounds.Add(ValidRound(0, T0.Date.Ticks, true));
        Check(SafeValidate(fits, out error) && fits.challengeRounds.Count == 1, "migration: the same round within periodRewardXp is accepted" + error);
        var awards = new MetaProfile { periodRewardXp = 500, totalXp = 500 }; awards.milestoneAwards.Add(new MetaMilestoneAward { id = "ms-sales-1", xp = 1000 });
        Check(!SafeValidate(awards, out _), "migration: awards above periodRewardXp reject the profile (integrity)");
    }

    // ---------- 10. robustness ----------
    static MetaProfile Garbage(long t)
    {
        long day = TimeSpan.TicksPerDay;
        var p = new MetaProfile { challengeClockUtc = long.MaxValue, periodRewardXp = 1005, totalXp = 1005 };
        var negative = ValidRound(1, t - 7 * day, false, 7); negative.tasks[0].target = -5;
        var mode = ValidRound(3, t - day, false); mode.tasks[0].mode = 7;
        var huge = ValidRound(0, t - 3 * day, false);
        for (int i = 0; i < 100; i++) { var extra = Clone(huge.tasks[0]); extra.id = "blocks-" + i; huge.tasks.Add(extra); }
        var lifetime = ValidRound(2, t - day, false);
        var inverted = ValidRound(3, t - 2 * day, false); inverted.endUtc = inverted.startUtc - 1;
        var masks = ValidRound(1, t - 14 * day, false, 7);
        for (int i = 0; i < 20; i++) masks.masks.Add(new MetaChallengeMask { metric = "m" + i, mask = 1 });
        var progress = ValidRound(0, t - 4 * day, false); progress.tasks[0].progress = 999;
        var paidOpen = ValidRound(0, t - 5 * day, false); paidOpen.tasks[0].paid = 600;
        var longId = ValidRound(1, t - 21 * day, false, 7); longId.tasks[0].id = new string('x', 65);
        var future = ValidRound(3, new DateTime(9500, 1, 1).Ticks, false);
        p.challengeRounds.AddRange(new[] { negative, mode, huge, lifetime, inverted, masks, progress, paidOpen, longId, future, null, ValidRound(0, t, false) });
        p.lifetimeCounters.AddRange(new[] { null, new MetaLifetimeCounter { id = "", value = 5 }, new MetaLifetimeCounter { id = "sales", value = 100 }, new MetaLifetimeCounter { id = "sales", value = 7 },
            new MetaLifetimeCounter { id = new string('c', 300), value = 1 }, new MetaLifetimeCounter { id = "crafted", value = -5 }, new MetaLifetimeCounter { id = "returns", value = long.MaxValue } });
        p.milestoneAwards.AddRange(new[] { null, new MetaMilestoneAward { id = "", xp = 3 }, new MetaMilestoneAward { id = "ms-sales-1", xp = 1000 }, new MetaMilestoneAward { id = "ms-sales-1", xp = 1000 },
            new MetaMilestoneAward { id = "future-stage", xp = 5 }, new MetaMilestoneAward { id = new string('a', 300), xp = 1 } });
        return p;
    }
    static void Robustness()
    {
        long t = Utc(2026, 10, 6).Ticks;
        var p = Garbage(t);
        bool ok = SafeValidate(p, out string error);
        Check(ok, "robustness: garbage challenge data never rejects the profile" + error);
        Check(p.challengeRounds != null && p.challengeRounds.Count == 1 && p.challengeRounds[0] != null && p.challengeRounds[0].period == 0 && p.challengeRounds[0].startUtc == t &&
            p.challengeRounds[0].tasks.Count == 1 && p.challengeRounds[0].tasks[0].progress == 3, "robustness: invalid rounds (negative target, unknown mode, 101 tasks, Lifetime, inverted, 20 masks, progress > target, paid but open, long id, year 9500, null) dropped, the valid round kept (" + (p.challengeRounds?.Count ?? -1) + ")");
        Check(p.lifetimeCounters != null && p.lifetimeCounters.All(c => c != null && !string.IsNullOrWhiteSpace(c.id) && c.id.Length <= 64 && c.value >= 0 && c.value <= 1000000000000000L) &&
            p.lifetimeCounters.Select(c => c.id).Distinct().Count() == p.lifetimeCounters.Count && p.lifetimeCounters.Find(c => c.id == "sales")?.value == 100, "robustness: lifetime counters repaired (first duplicate kept)");
        Check(p.milestoneAwards != null && p.milestoneAwards.All(a => a != null && !string.IsNullOrWhiteSpace(a.id) && a.id.Length <= 64 && a.xp >= 0 && a.xp <= 10000000) &&
            p.milestoneAwards.Count(a => a.id == "ms-sales-1") == 1 && p.milestoneAwards.Any(a => a.id == "future-stage"), "robustness: awards repaired, unknown stage ids kept");
        Check(p.challengeClockUtc >= 0 && p.challengeClockUtc <= new DateTime(9000, 1, 1).Ticks, "robustness: challengeClockUtc clamped to year 9000");
        string path = Path.Combine(root, "garbage", "meta-progression.json");
        WriteEnvelope(path, JsonUtility.ToJson(Garbage(t)));
        var read = TryRead(path);
        Check(read != null && read.challengeRounds.Count == 1, "robustness: the garbage profile file still loads through TryRead");
        var nulls = new MetaProfile { challengeRounds = null, milestoneAwards = null, lifetimeCounters = null };
        ok = SafeValidate(nulls, out error);
        Check(ok && nulls.challengeRounds != null && nulls.milestoneAwards != null && nulls.lifetimeCounters != null, "robustness: missing challenge lists are repaired" + error);
        var markers = new MetaProfile { periodRewardXp = 1000, totalXp = 1000 };
        markers.runs.Add(new MetaRunState { runId = "marker-run", resourceSources = { "cm:ms-sales-1", "cm:no-such-stage", "cr:0:123:blocks/-1/0:t" } });
        ok = SafeValidate(markers, out error);
        Check(ok && markers.milestoneAwards.Count == 1 && markers.milestoneAwards[0].id == "ms-sales-1" && markers.milestoneAwards[0].xp == 1000 && markers.periodRewardXp == 1000,
            "robustness: validation rebuilds awards from cm markers with catalog XP and skips unknown stages" + error);

        var run = new MetaRunState { runId = "tally-run" };
        run.tallies.AddRange(new[] { null, new MetaRunTally { id = "", value = 1 }, new MetaRunTally { id = "sales", value = 5, peak = 10 }, new MetaRunTally { id = "sales", value = 7, peak = 7 },
            new MetaRunTally { id = new string('t', 65), value = 1 }, new MetaRunTally { id = "crafted", value = -3, peak = 2000000000000000L } });
        for (int i = 0; i < 70; i++) run.tallies.Add(new MetaRunTally { id = "extra-" + i, value = i, peak = i });
        bool valid;
        try { valid = MetaProgression.ValidateRun(run); } catch (Exception e) { valid = false; error = " (threw " + Describe(e) + ")"; }
        Check(valid, "robustness: bad tallies never reject a run" + error);
        Check(run.tallies.Count <= 64 && run.tallies.All(x => x != null && !string.IsNullOrWhiteSpace(x.id) && x.id.Length <= 64 && x.value >= 0 && x.value <= 1000000000000L && x.peak >= 0 && x.peak <= 1000000000000L) &&
            run.tallies.Select(x => x.id).Distinct().Count() == run.tallies.Count && run.tallies.Find(x => x.id == "sales") is MetaRunTally sales && sales.value == 5 && sales.peak == 10 &&
            run.tallies.Find(x => x.id == "crafted")?.value == 0, "robustness: tallies repaired (" + run.tallies.Count + " kept)");
        Fresh("robust", T0, S1());
        var snapshot = new MetaRunState { runId = "robust-run" };
        snapshot.tallies.Add(null); snapshot.tallies.Add(new MetaRunTally { id = "", value = -1 });
        try { MetaProgression.BeginRun("robust-run", snapshot); Check(MetaProgression.CurrentRun?.runId == "robust-run", "robustness: a world snapshot with bad tallies still starts"); }
        catch (Exception e) { Check(false, "robustness: BeginRun rejected a snapshot with bad tallies: " + Describe(e)); }
        R(0);
        Check(MetaProgression.Save() && TryRead(MetaProgression.ProfilePath) != null, "robustness: the profile stays loadable afterwards");
    }
}
