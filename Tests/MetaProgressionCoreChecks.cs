using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

// Pipeline run_script, entry MetaProgressionCoreChecks.Main. All profile writes stay in Temp.
public static class MetaProgressionCoreChecks
{
    static int checks;
    static string root;
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++; File.AppendAllText("Temp/MetaProgressionCoreChecks.txt", "PASS " + label + "\n");
    }
    static void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < .001d, label + " (" + actual + ")");
    static void Fresh(string name)
    {
        MetaProgression.EndRun(); MetaProgression.TestDirectory = Path.Combine(root, name);
        Check(MetaProgression.TotalXp == 0 && MetaProgression.Level == 1, name + " starts isolated and empty");
    }
    static void SeedLevel(int level)
    {
        MetaProgression.BeginRun("seed-" + level, new MetaRunState { runId = "seed-" + level, progressXp = MetaProgressionCatalog.XpForLevel(level) });
        MetaProgression.EndRun();
    }
    public static object Main()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/MetaProgressionCoreChecks.txt", ""); checks = 0;
        root = Path.GetFullPath("Temp/MetaProgression-" + DateTime.UtcNow.Ticks);
        string previousDirectory = MetaProgression.TestDirectory;
        MetaRunState previousRun = MetaProgression.CaptureRunState();
        var previousSettings = GpsSettings.Preferences.metaProgression;
        try {
            GpsSettings.Preferences.metaProgression = new MetaProgressionSettings { efficiencyMaxBonus = 0f };
            Curve(); Awards(); Replay(); Upgrades(); Persistence(); AsyncPersistence(); Validation();
            File.AppendAllText("Temp/MetaProgressionCoreChecks.txt", "PASS " + checks + " checks\n");
            return new { passed = checks, profileDirectory = root };
        } catch (Exception error) {
            File.AppendAllText("Temp/MetaProgressionCoreChecks.txt", "FAIL " + error + "\n"); throw;
        } finally {
            MetaProgression.EndRun(); MetaProgression.TestDirectory = previousDirectory;
            GpsSettings.Preferences.metaProgression = previousSettings;
            if (previousRun != null) MetaProgression.BeginRun(previousRun.runId, previousRun);
        }
    }
    static void Curve()
    {
        Check(MetaProgressionCatalog.LevelCost(1) == 1000, "early level cost");
        Check(MetaProgressionCatalog.LevelCost(100) > 5880 && MetaProgressionCatalog.LevelCost(100) < 6000, "curve within two percent of plateau at level100");
        Check(MetaProgressionCatalog.LevelCost(999) == 6000, "late plateau");
        Check(MetaProgressionCatalog.LevelCost(1000) == 0, "level1000 is the cap");
        for (int level = 2; level <= 1000; level++) {
            long threshold = MetaProgressionCatalog.XpForLevel(level);
            if (MetaProgressionCatalog.LevelForXp(threshold) != level || MetaProgressionCatalog.LevelForXp(threshold - 1) != level - 1) throw new Exception("Level boundary " + level);
        }
        Check(true, "all999 level boundaries are exact");
        Check(MetaProgressionCatalog.LevelForXp(long.MaxValue) == 1000, "overflow-safe cap");
        Check(MetaProgressionCatalog.EarnedPowerPoints(200) == 199 && MetaProgressionCatalog.EarnedPowerPoints(1000) == 199, "combat growth stops after200");
        Check(MetaProgressionCatalog.EarnedComfortPoints(204) == 0 && MetaProgressionCatalog.EarnedComfortPoints(205) == 1 && MetaProgressionCatalog.EarnedComfortPoints(1000) == 160, "comfort point cadence");
        Check(MetaProgressionCatalog.Upgrades.Where(value => !value.comfort).Sum(value => value.TotalCost(value.maxRank)) > 199, "power builds require choices");
        Check(MetaProgressionCatalog.Upgrades.Where(value => value.comfort).Sum(value => value.TotalCost(value.maxRank)) > 160, "comfort spending lasts through1000");
        int[] levels = { 50, 100, 200, 1000 }; double[] low = { 10, 30, 80, 500 }, high = { 20, 50, 120, 800 };
        for (int i = 0; i < levels.Length; i++) {
            double hours = MetaProgressionCatalog.XpForLevel(levels[i]) / 10000d;
            Check(hours >= low[i] && hours <= high[i], "level" + levels[i] + " calibration target at10000XP/h: " + hours.ToString("0.0") + "h");
        }
    }
    static void Awards()
    {
        Fresh("awards"); MetaProgression.BeginRun("awards");
        Check(MetaProgression.CurrentRun.objectiveIds.Count == 3, "three stable objectives");
        for (int i = 0; i < 10; i++) MetaProgression.TickActive(60f);
        Check(MetaProgression.TotalXp == 0, "idle active time alone grants noXP");
        MetaProgression.RecordDepth(100);
        Near(MetaProgression.CurrentRun.progressXp, 150d, "first four depth bands");
        long before = MetaProgression.TotalXp;
        MetaProgression.RecordDepth(10); MetaProgression.RecordDepth(100);
        Check(MetaProgression.TotalXp == before, "repeat depth pays once");
        MetaProgression.RecordDepth(300);
        Near(MetaProgression.CurrentRun.progressXp, 690d, "depth300 exact base award");
        Check(MetaProgression.Profile.completedMilestones.Contains("depth-100") && MetaProgression.Profile.completedMilestones.Contains("depth-250"), "depth milestones complete");
        MetaProgression.RecordExploration(1, 2, 300); before = MetaProgression.TotalXp;
        MetaProgression.RecordExploration(1, 2, 600);
        Check(MetaProgression.TotalXp == before, "region identity cannot repay at another reported depth");
        MetaProgression.RecordResource(Item.Wood, 100, "surface-tree");
        Check(MetaProgression.CurrentRun.resources.Count == 0, "renewable surface resources give noXP");
        double prior = MetaProgression.CurrentRun.progressXp;
        MetaProgression.RecordResource(Item.Copper, 100, "ore-a");
        double first = MetaProgression.CurrentRun.progressXp - prior;
        MetaProgression.RecordResource(Item.Copper, 100, "ore-b");
        double both = MetaProgression.CurrentRun.progressXp - prior;
        Near(both / first, Math.Pow(2d, .8d), "double resource yield earns sublinearXP");
        before = MetaProgression.TotalXp;
        MetaProgression.RecordResource(Item.Copper, 100, "ore-a");
        Check(MetaProgression.TotalXp == before, "same mined cell cannot repay");
        MetaProgression.RecordDiscovery("altar:5:-300"); before = MetaProgression.TotalXp;
        MetaProgression.RecordDiscovery("altar:5:-300");
        Check(MetaProgression.TotalXp == before, "discovery cannot repay");
        GpsSettings.Preferences.metaProgression.xpMultiplier = 20f;
        Check(MetaProgression.CurrentRun.settings.xpMultiplier == 1f, "GPS award settings frozen per run");
        GpsSettings.Preferences.metaProgression.xpMultiplier = 1f;
        Fresh("batch"); MetaProgression.BeginRun("batch"); MetaProgression.RecordResource(Item.Gold, 100);
        double batch = MetaProgression.CurrentRun.progressXp;
        Fresh("split"); MetaProgression.BeginRun("split");
        for (int i = 0; i < 100; i++) MetaProgression.RecordResource(Item.Gold, 1);
        Near(MetaProgression.CurrentRun.progressXp, batch, "resourceXP independent of stack splitting");
        Fresh("efficiency");
        GpsSettings.Preferences.metaProgression.efficiencyMaxBonus = .2f;
        MetaProgression.BeginRun("efficiency"); MetaProgression.TickActive(60f); MetaProgression.RecordDepth(1200);
        Check(MetaProgression.CurrentRun.efficiencyXp > 0d && MetaProgression.CurrentRun.efficiencyXp <= MetaProgression.CurrentRun.progressXp * .2d, "efficiency bonus positive and capped20percent");
        before = MetaProgression.TotalXp;
        for (int i = 0; i < 20; i++) MetaProgression.TickActive(60f);
        Check(MetaProgression.TotalXp == before, "relaxing later never removes earned bonus");
        GpsSettings.Preferences.metaProgression.efficiencyMaxBonus = 0f;
    }
    static void Replay()
    {
        Fresh("replay"); MetaProgression.BeginRun("persistent-world");
        var oldSave = MetaProgression.CaptureRunState();
        MetaProgression.RecordDepth(300); MetaProgression.RecordResource(Item.Gold, 30, "12:-100");
        MetaProgression.RecordExploration(1, -10, 120); MetaProgression.RecordDiscovery("artifact:5:-150");
        MetaProgression.RecordExoticCraft(Item.IronLadder);
        MetaProgression.RecordCraft(Item.Ladder); MetaProgression.RecordCraft(Item.Torche); MetaProgression.RecordCraft(Item.Rope);
        long earned = MetaProgression.TotalXp;
        MetaProgression.Save(); MetaProgression.Reload(); MetaProgression.BeginRun(oldSave.runId, oldSave);
        MetaProgression.RecordDepth(300); MetaProgression.RecordResource(Item.Gold, 30, "12:-100");
        MetaProgression.RecordExploration(1, -10, 120); MetaProgression.RecordDiscovery("artifact:5:-150");
        MetaProgression.RecordExoticCraft(Item.IronLadder);
        MetaProgression.RecordCraft(Item.Ladder); MetaProgression.RecordCraft(Item.Torche); MetaProgression.RecordCraft(Item.Rope);
        Check(MetaProgression.TotalXp == earned, "rollback save cannot replay depth resources regions discoveries crafts or milestones");
        MetaProgression.RecordResource(Item.Gold, 1, "13:-100");
        Check(MetaProgression.TotalXp > earned, "unseen cells remain rewarding after rollback");
        earned = MetaProgression.TotalXp;
        MetaProgression.RecordRunFinished(false);
        Check(MetaProgression.TotalXp == earned, "death retains allXP");
        MetaProgression.BeginRun(oldSave.runId, oldSave); MetaProgression.RecordDepth(325);
        Check(MetaProgression.TotalXp > earned, "a restored unfinished world can earn new progress after death");
        int milestones = MetaProgression.Profile.completedMilestones.Count;
        MetaProgression.EndRun(); MetaProgression.BeginRun("second-world"); MetaProgression.RecordDepth(300);
        Check(MetaProgression.Profile.completedMilestones.Count == milestones, "permanent milestones do not repay in another run");
        Check(MetaProgression.CurrentRun.progressXp == 690d, "new worlds still earn repeatable depthXP");
        var snapshot = MetaProgression.CaptureRunState();
        snapshot.maxDepth = 999;
        Check(MetaProgression.CurrentRun.maxDepth == 300, "save snapshots are detached clones");
    }
    static void Upgrades()
    {
        Fresh("upgrades"); SeedLevel(205);
        Check(MetaProgression.Level == 205 && MetaProgression.AvailablePowerPoints == 199 && MetaProgression.AvailableComfortPoints == 1, "earned level currencies exact");
        Check(MetaProgression.Spend("health") && MetaProgression.AvailablePowerPoints == 197, "purchase debits correct currency");
        Check(MetaProgression.Spend("reach") && MetaProgression.AvailableComfortPoints == 0, "comfort purchase");
        Check(!MetaProgression.Spend("reach"), "insufficient currency rejected");
        MetaProgression.BeginRun("build");
        Near(MetaProgression.CurrentLoadout.healthMultiplier, 1.025d, "health starting snapshot");
        Near(MetaProgression.CurrentLoadout.buildReachBonus, .015d, "build reach starting snapshot");
        Check(!MetaProgression.Spend("health") && !MetaProgression.RefundAll(), "mid-run spending and refund disabled");
        var save = MetaProgression.CaptureRunState(); MetaProgression.EndRun();
        Check(MetaProgression.RefundAll() && MetaProgression.AvailablePowerPoints == 199 && MetaProgression.AvailableComfortPoints == 1, "refund returns both currencies for free");
        MetaProgression.BeginRun("build", save);
        Near(MetaProgression.CurrentLoadout.healthMultiplier, 1.025d, "saved build survives profile refund");
        Check(MetaProgression.GetRunRank("health") == 1 && MetaProgression.GetRank("health") == 0, "active rank distinct from next-run rank");
        MetaProgression.EndRun(); MetaProgression.BeginRun("next-build");
        Near(MetaProgression.CurrentLoadout.healthMultiplier, 1d, "next run gets refunded build");
        MetaProgression.EndRun(); MetaProgression.Reload();
        Check(MetaProgression.Level == 205 && MetaProgression.GetRank("health") == 0, "profile reload preserves XP and refund");
    }
    static void Persistence()
    {
        Fresh("recovery"); MetaProgression.BeginRun("recovery"); MetaProgression.RecordDepth(100); MetaProgression.Save();
        long earned = MetaProgression.TotalXp; var save = MetaProgression.CaptureRunState();
        MetaProgression.EndRun(); string path = MetaProgression.ProfilePath;
        File.Copy(path, path + ".bak", true); File.WriteAllText(path, "broken-profile");
        MetaProgression.Reload();
        Check(MetaProgression.TotalXp == earned && MetaProgression.RecoveredFromBackup, "corrupt main recovers checked backup");
        MetaProgression.BeginRun(save.runId, save); MetaProgression.RecordDepth(125); MetaProgression.Save(); MetaProgression.EndRun();
        Check(File.Exists(path + ".invalid"), "corrupt main preserved before recovery write");
        MetaProgression.Reload(); Check(MetaProgression.TotalXp >= earned, "recovered profile can persist again");
        Fresh("future"); MetaProgression.BeginRun("future"); MetaProgression.EndRun(); path = MetaProgression.ProfilePath;
        string future = "{\"version\":999,\"payload\":\"future\",\"sha256\":\"future\"}";
        File.WriteAllText(path, future); MetaProgression.Reload();
        Check(!string.IsNullOrEmpty(MetaProgression.ProfilePath) && !MetaProgression.Save() && !string.IsNullOrEmpty(MetaProgression.LastError), "future version blocks writes despite older backup");
        Check(File.ReadAllText(path) == future, "future file preserved verbatim");
        Fresh("unrecoverable"); Directory.CreateDirectory(MetaProgression.DirectoryPath); path = MetaProgression.ProfilePath;
        File.WriteAllText(path, "corrupt"); File.WriteAllText(path + ".bak", "also-corrupt"); MetaProgression.Reload();
        Check(MetaProgression.Level == 1 && !MetaProgression.Save(), "both corrupt files block destructive reset");
        Check(File.ReadAllText(path) == "corrupt" && File.ReadAllText(path + ".bak") == "also-corrupt", "unrecoverable files remain untouched");
        Fresh("tmp-recovery"); MetaProgression.BeginRun("temp-world"); MetaProgression.RecordDepth(100); MetaProgression.EndRun(); path = MetaProgression.ProfilePath;
        earned = MetaProgression.TotalXp; File.Copy(path, path + ".tmp"); File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", "broken"); MetaProgression.Reload();
        Check(MetaProgression.TotalXp == earned, "complete atomic-temp file recovers after interrupted replacement");
        MetaProgression.BeginRun("temp-world"); MetaProgression.EndRun();
        Check(File.ReadAllText(path + ".bak") != "broken", "temp-journal recovery repairs damaged backup");
        File.WriteAllText(path, "broken-again"); MetaProgression.Reload();
        Check(MetaProgression.TotalXp == earned, "repaired backup survives a subsequent main-file failure");
    }
    static void Validation()
    {
        Check(MetaProgression.ValidateRunState(null), "legacy saves allow missing progression");
        var state = new MetaRunState { runId = "valid" };
        Check(MetaProgression.ValidateRunState(state), "empty well-formed run valid");
        state.version = 2; Check(!MetaProgression.ValidateRunState(state), "unknown run version rejected"); state.version = 1;
        state.progressXp = double.NaN; Check(!MetaProgression.ValidateRunState(state), "NaN XP rejected"); state.progressXp = 0;
        state.loadout.healthMultiplier = float.PositiveInfinity; Check(!MetaProgression.ValidateRunState(state), "infinite saved bonus rejected"); state.loadout.healthMultiplier = 1;
        state.resourceSources.Add("same"); state.resourceSources.Add("same"); Check(!MetaProgression.ValidateRunState(state), "duplicate ledger IDs rejected"); state.resourceSources.Clear();
        state.settings.xpMultiplier = float.NaN; state.settings.depthStep = 0;
        Check(MetaProgression.ValidateRunState(state) && state.settings.xpMultiplier == 1f && state.settings.depthStep == 1, "invalid GPS values safely sanitized");
        var settings = new MetaProgressionSettings { efficiencyMaxBonus = 100f }.Sanitized();
        Check(settings.efficiencyMaxBonus == .2f, "GPS cannot remove efficiency cap");
    }
    [Serializable] sealed class TestEnvelope { public string payload; }
    static MetaProfile ReadProfile(string path) => JsonUtility.FromJson<MetaProfile>(JsonUtility.FromJson<TestEnvelope>(File.ReadAllText(path)).payload);
    static void WaitForSave()
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(20);
        while (MetaProgression.IsSaving) {
            MetaProgression.TickActive(0f);
            if (DateTime.UtcNow > timeout) throw new TimeoutException("Asynchronous profile writer did not finish.");
            Thread.Sleep(1);
        }
    }
    static void AsyncPersistence()
    {
        Fresh("async"); MetaProgression.BeginRun("async-world");
        MetaProgression.RecordResource(Item.Copper, 1, "a");
        Check(MetaProgression.RequestSave() && MetaProgression.IsSaving, "autosave queued on worker");
        MetaProgression.RecordResource(Item.Copper, 1, "b");
        WaitForSave();
        var snapshot = ReadProfile(MetaProgression.ProfilePath).runs.Single();
        Check(snapshot.resources.Single().count == 1 && snapshot.resourceSources.Count == 1, "worker owns detached snapshot while play continues");
        Check(MetaProgression.CurrentRun.resources.Single().count == 2 && MetaProgression.Save(), "older completion retains newer dirty changes");
        Check(ReadProfile(MetaProgression.ProfilePath).runs.Single().resources.Single().count == 2, "durable save includes changes after autosave snapshot");
        MetaProgression.RecordResource(Item.Copper, 1, "c"); MetaProgression.RequestSave();
        MetaProgression.RecordResource(Item.Copper, 1, "d"); MetaProgression.RequestSave();
        WaitForSave();
        Check(ReadProfile(MetaProgression.ProfilePath).runs.Single().resources.Single().count == 4, "repeated requests serialize through a single writer without losing new changes");
        string originalDirectory = MetaProgression.TestDirectory, originalPath = MetaProgression.ProfilePath;
        MetaProgression.RecordResource(Item.Copper, 1, "e"); MetaProgression.RequestSave();
        MetaProgression.RecordResource(Item.Copper, 1, "f");
        MetaProgression.TestDirectory = Path.Combine(root, "async-other");
        Check(!MetaProgression.IsSaving && MetaProgression.TotalXp == 0, "directory switch drains writer before changing profile");
        Check(ReadProfile(originalPath).runs.Single().resources.Single().count == 6, "directory switch also flushes newer unsnapshotted changes");
        MetaProgression.TestDirectory = originalDirectory;
        Check(MetaProgression.Profile.runs.Single().resources.Single().count == 6, "previous profile intact after scope switch");
        string obstruction = Path.Combine(root, "blocked-directory"); File.WriteAllText(obstruction, "preserve");
        MetaProgression.TestDirectory = obstruction; MetaProgression.RequestSave(); WaitForSave();
        Check(!string.IsNullOrEmpty(MetaProgression.LastError) && !MetaProgression.Save(), "worker I/O failure is surfaced and remains retryable");
        Check(File.ReadAllText(obstruction) == "preserve", "failed worker leaves obstructing file untouched");
    }
}
