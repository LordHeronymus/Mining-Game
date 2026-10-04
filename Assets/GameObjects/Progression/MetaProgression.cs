using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static partial class MetaProgression
{
    const int FormatVersion = 1, MaxFileBytes = 64 * 1024 * 1024;
    const double WorkWindow = 300d;
    static MetaProfile profile;
    static MetaRunState run;
    static string loadedPath, testDirectory;
    static string loadedSelection;
    static int loadedSelectionMode = -1;
    static bool dirty, writeBlocked, recovered, milestonePending;
    static readonly MetaLoadout neutralLoadout = new();
    static double saveElapsed;
    static long mainLength = -1, mainWriteTicks = -1;
    static bool mainValid;
    static long changeGeneration, pendingGeneration, pendingRevision;
    static bool saveAfterPending;
    static Task<WriteResult> pendingSave;
    public static bool IsSaving => pendingSave != null;
    static readonly HashSet<string> sources = new(), regions = new(), discoveries = new();
    static readonly Dictionary<int, MetaResourceCount> resourceCounts = new();
    public static event Action Changed;
    public static string LastError { get; private set; }
    public static bool RecoveredFromBackup { get { EnsureLoaded(); return recovered; } }

    public static string TestDirectory
    {
        get => testDirectory;
        set { if (testDirectory == value) return; testDirectory = value; ClearSession(); }
    }
    public static string DirectoryPath
    {
        get {
            if (!string.IsNullOrWhiteSpace(testDirectory)) return testDirectory;
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) return Path.Combine(GameSaveSystem.TestDirectory, "MetaProfile");
#endif
            return Path.Combine(Application.persistentDataPath, "Tiefenhall", "Profile");
        }
    }
    public static string ProfilePath => Path.Combine(DirectoryPath, "meta-progression.json");
    public static MetaProfile Profile { get { EnsureLoaded(); return profile; } }
    public static MetaRunState CurrentRun { get { EnsureLoaded(); return run; } }
    public static MetaLoadout CurrentLoadout => CurrentRun?.loadout ?? neutralLoadout;
    public static int Level => MetaProgressionCatalog.LevelForXp(Profile.totalXp);
    public static long TotalXp => Profile.totalXp;
    public static long XpForNextLevel => MetaProgressionCatalog.LevelCost(Level);
    public static long XpIntoCurrentLevel => Level == MetaProgressionCatalog.MaxLevel ? 0 : TotalXp - MetaProgressionCatalog.XpForLevel(Level);
    public static int AvailablePowerPoints => Math.Max(0, MetaProgressionCatalog.EarnedPowerPoints(Level) - Spent(false));
    public static int AvailableComfortPoints => Math.Max(0, MetaProgressionCatalog.EarnedComfortPoints(Level) - Spent(true));
    public static bool CanEdit => CurrentRun == null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        ClearSession(); testDirectory = null; Changed = null; ChallengeTestUtc = null;
    }
    static void ClearSession()
    {
        CompletePending(true, false);
        if (profile != null && dirty && !writeBlocked) SaveCurrentProfile();
        profile = null; run = null; loadedPath = null; dirty = writeBlocked = recovered = milestonePending = false;
        loadedSelection = null; loadedSelectionMode = -1;
        LastError = null; saveElapsed = 0d; ClearCaches();
        mainLength = mainWriteTicks = -1; mainValid = false;
        changeGeneration = pendingGeneration = pendingRevision = 0; saveAfterPending = false;
    }
    public static void Reload() => ClearSession();
    static void ClearCaches() { sources.Clear(); regions.Clear(); discoveries.Clear(); resourceCounts.Clear(); }

    static void EnsureLoaded()
    {
        // Stat consumers read CurrentLoadout every frame. Resolve filesystem paths only
        // when the profile destination changes, not on each health/movement query.
        string selection = testDirectory;
        int selectionMode = !string.IsNullOrWhiteSpace(selection) ? 1 : 0;
#if UNITY_EDITOR
        if (selectionMode == 0 && !string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) { selection = GameSaveSystem.TestDirectory; selectionMode = 2; }
#endif
        if (profile != null && selectionMode == loadedSelectionMode && string.Equals(selection, loadedSelection, StringComparison.Ordinal)) return;
        string path = Path.GetFullPath(ProfilePath);
        ClearSession(); loadedPath = path; loadedSelection = selection; loadedSelectionMode = selectionMode;
        MetaProfile best = null; string bestPath = null; bool any = false, future = false;
        foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" }) {
            if (!File.Exists(candidate)) continue;
            any = true;
            bool valid = TryRead(candidate, out var result, out bool newer);
            if (candidate == path) { mainValid = valid; RememberMainStamp(); }
            if (valid && (best == null || result.revision > best.revision)) { best = result; bestPath = candidate; }
            future |= newer;
        }
        if (future) {
            // Never downgrade an unknown format, even if an older backup is readable.
            profile = best ?? new MetaProfile(); writeBlocked = true;
            LastError = "Das Fortschrittsprofil stammt aus einer neueren Spielversion.";
        } else if (best != null) {
            profile = best; recovered = bestPath != path;
        } else {
            profile = new MetaProfile(); writeBlocked = any;
            if (any) LastError = "Das Fortschrittsprofil konnte nicht gelesen werden. Die Dateien bleiben erhalten.";
        }
    }

    public static int GetRank(string id)
    {
        var definition = MetaProgressionCatalog.Upgrade(id);
        if (definition == null) return 0;
        var rank = Profile.upgrades.Find(value => value.id == id);
        return rank == null ? 0 : Mathf.Clamp(rank.rank, 0, definition.maxRank);
    }
    public static int GetRunRank(string id) => CurrentRun?.loadout?.Rank(id) ?? GetRank(id);
    static int Spent(bool comfort)
    {
        int sum = 0;
        foreach (var entry in Profile.upgrades) {
            var definition = MetaProgressionCatalog.Upgrade(entry.id);
            if (definition != null && definition.comfort == comfort) sum += definition.TotalCost(entry.rank);
        }
        return sum;
    }
    public static bool Spend(string upgradeId)
    {
        EnsureLoaded();
        if (run != null) { LastError = "Ausbau nur im Hauptmenü."; return false; }
        var definition = MetaProgressionCatalog.Upgrade(upgradeId);
        if (definition == null || writeBlocked) return false;
        int rank = GetRank(upgradeId), cost = definition.Cost(rank);
        if (rank >= definition.maxRank || cost <= 0 || cost > (definition.comfort ? AvailableComfortPoints : AvailablePowerPoints)) return false;
        var saved = profile.upgrades.Find(value => value.id == upgradeId);
        bool added = saved == null;
        if (added) { saved = new MetaUpgradeRank { id = upgradeId }; profile.upgrades.Add(saved); }
        saved.rank++;
        MarkDirty();
        if (!Save()) { saved.rank--; if (added) profile.upgrades.Remove(saved); MarkDirty(); return false; }
        Changed?.Invoke(); return true;
    }
    public static bool RefundAll()
    {
        EnsureLoaded();
        if (run != null) { LastError = "Ausbau nur im Hauptmenü."; return false; }
        if (writeBlocked) return false;
        var previous = profile.upgrades;
        profile.upgrades = new List<MetaUpgradeRank>(); MarkDirty();
        if (!Save()) { profile.upgrades = previous; MarkDirty(); return false; }
        Changed?.Invoke(); return true;
    }
    public static MetaLoadout CreateLoadout()
    {
        var result = new MetaLoadout();
        foreach (var definition in MetaProgressionCatalog.Upgrades) {
            int rank = GetRank(definition.id);
            if (rank > 0) result.ranks.Add(new MetaUpgradeRank { id = definition.id, rank = rank });
        }
        result.healthMultiplier = 1f + GetRank("health") * .025f;
        result.energyMultiplier = 1f + GetRank("energy") * .025f;
        result.startMoney = GetRank("money") * 10;
        result.movementMultiplier = 1f + GetRank("movement") * .01f;
        result.jumpMultiplier = 1f + GetRank("jump") * .008f;
        result.miningEnergyMultiplier = 1f - GetRank("mining") * .01f;
        result.buildReachBonus = GetRank("reach") * .015f;
        result.carryCapacityBonus = GetRank("capacity") * .4f;
        result.startingTorches = GetRank("torches"); result.startingLadders = GetRank("ladders");
        return result;
    }

    public static void BeginRun(string runId, MetaRunState saved = null)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 160) throw new ArgumentException("A stable run identity is required.", nameof(runId));
        if (saved != null && (!ValidateRun(saved) || saved.runId != runId)) throw new ArgumentException("Invalid progression run snapshot.", nameof(saved));
        if (run != null) Save();
        var existing = profile.runs.Find(value => value.runId == runId);
        if (existing == null) {
            existing = saved == null ? new MetaRunState { runId = runId, loadout = CreateLoadout(), settings = CurrentSettings() } : Clone(saved);
            profile.runs.Add(existing);
        } else if (saved != null) {
            MergeLedger(existing, saved);
            // The world save controls its build. A profile ledger must never restore later
            // inventory, learned recipes, starting supplies or a different loadout.
            existing.loadout = Clone(saved.loadout);
            existing.ended = saved.ended;
        }
        run = existing;
        if (run.objectiveIds.Count == 0) SelectObjectives(run);
        foreach (string milestone in run.milestones)
            if (MetaProgressionCatalog.Challenge(milestone)?.permanent == true && !profile.completedMilestones.Contains(milestone)) profile.completedMilestones.Add(milestone);
        RebuildCaches(); RecalculateTotal(); MarkDirty(); Save(); Changed?.Invoke();
    }
    static MetaProgressionSettings CurrentSettings()
        => (GpsSettings.Preferences.metaProgression ?? new MetaProgressionSettings()).Sanitized();
    public static MetaRunState CaptureRunState()
    {
        EnsureLoaded();
        if (run == null) return null;
        Save(); return Clone(run);
    }
    public static void EndRun()
    {
        EnsureLoaded();
        if (run != null) { Save(); run = null; ClearCaches(); Changed?.Invoke(); }
    }
    public static void RecordRunFinished(bool victory)
    {
        if (!HasRun()) return;
        run.ended = true; run.won |= victory; CheckChallenges(); MarkChanged(false); Save();
    }
    public static void RecordVictory() => RecordRunFinished(true);
    static bool HasRun() { EnsureLoaded(); return run != null && !writeBlocked; }

    public static void TickActive(float seconds)
    {
        CompletePending(false);
        if (saveAfterPending && pendingSave == null) RequestSave();
        if (!HasRun() || run.ended || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
        double elapsed = Math.Min(seconds, 60f);
        run.activeSeconds += elapsed;
        run.workWindowSeconds = Math.Min(WorkWindow, run.workWindowSeconds + elapsed);
        run.recentWorkXp *= Math.Exp(-elapsed / WorkWindow);
        saveElapsed += elapsed; MarkDirty();
        if (saveElapsed >= 5d) { saveElapsed = 0d; RequestSave(); }
    }
    public static void Flush() { if (profile != null && dirty) Save(); }

    public static void RecordDepth(int depth)
    {
        if (!HasRun() || run.ended) return;
        depth = Mathf.Clamp(depth, 0, 100000);
        if (depth <= run.maxDepth) return;
        int previous = run.maxDepth / run.settings.depthStep, next = depth / run.settings.depthStep;
        RecordPeriodProgress("depth", depth - run.maxDepth);
        run.maxDepth = depth;
        double reward = 0d;
        for (int step = previous; step < next; step++) reward += Math.Min(run.settings.depthMaxXp, run.settings.depthBaseXp + step * (double)run.settings.depthIncrementXp);
        AwardProgress(reward); CheckChallenges(); MarkChanged(false);
    }
    public static void RecordExploration(int regionX, int regionY, int depth)
    {
        if (!HasRun() || run.ended || depth < 0) return;
        string id = regionX + ":" + regionY;
        if (!regions.Add(id)) return;
        run.regions.Add(id);
        RecordPeriodProgress("regions", 1);
        double depthFactor = 1d + Math.Min(1d, Math.Max(0, depth) / 1000d) * .6d;
        AwardProgress(run.settings.explorationXp * depthFactor); CheckChallenges(); MarkChanged(false);
    }
    // Natural world events should ALWAYS supply a stable source ID (normally the tile cell).
    // The amount-only overload is for an authoritative aggregate producer, never inventory changes.
    public static void RecordResource(Item item, int count) => RecordResource(item, count, null);
    public static void RecordResource(Item item, int count, string sourceId)
    {
        if (!HasRun() || run.ended || count <= 0) return;
        double weight = MetaProgressionCatalog.ResourceWeight(item);
        if (weight <= 0d) return;
        if (sourceId != null) {
            if (sourceId.Length == 0 || sourceId.Length > 144) return;
            string key = (int)item + ":" + sourceId;
            if (!sources.Add(key)) return;
            run.resourceSources.Add(key);
        }
        if (!resourceCounts.TryGetValue((int)item, out var counter)) {
            counter = new MetaResourceCount { item = (int)item };
            resourceCounts.Add(counter.item, counter); run.resources.Add(counter);
        }
        int next = (int)Math.Min(10000000L, counter.count + (long)count);
        double reward = weight * (Math.Pow(next, .8d) - Math.Pow(counter.count, .8d)) * run.settings.resourceXpMultiplier;
        RecordPeriodProgress("resources", next - counter.count);
        if (item == Item.Copper) RecordPeriodProgress("copper", next - counter.count);
        if (item == Item.Coal) RecordPeriodProgress("coal", next - counter.count);
        if (item == Item.Iron) RecordPeriodProgress("iron", next - counter.count);
        if (item == Item.Silver) RecordPeriodProgress("silver", next - counter.count);
        if (item == Item.Gold) RecordPeriodProgress("gold", next - counter.count);
        counter.count = next; AwardProgress(reward); CheckChallenges(); MarkChanged(false);
    }
    public static void RecordDiscovery(string id)
    {
        if (!HasRun() || run.ended || string.IsNullOrWhiteSpace(id) || id.Length > 160 || !discoveries.Add(id)) return;
        run.discoveries.Add(id); RecordPeriodProgress("discoveries", 1);
        AwardProgress(run.settings.discoveryXp); CheckChallenges(); MarkChanged(true);
    }
    public static void RecordCraft(Item item)
    {
        if (!HasRun() || run.ended || run.crafts.Contains((int)item)) return;
        run.crafts.Add((int)item); CheckChallenges(); MarkChanged(false);
    }
    public static void RecordExoticCraft(Item item)
    {
        if (!HasRun() || run.ended || run.exoticCrafts.Contains((int)item)) return;
        run.exoticCrafts.Add((int)item); CheckChallenges(); MarkChanged(true);
    }
    static void AwardProgress(double amount)
    {
        if (amount <= 0d || double.IsNaN(amount) || double.IsInfinity(amount)) return;
        amount *= run.settings.xpMultiplier;
        double minuteRate = (run.recentWorkXp + amount) * 60d / Math.Max(60d, run.workWindowSeconds);
        double excess = Math.Max(0d, minuteRate / run.settings.efficiencyReferenceXpPerMinute - 1d);
        double bonus = run.settings.efficiencyMaxBonus * excess / (1d + excess);
        run.progressXp += amount;
        run.efficiencyXp += amount * bonus;
        run.recentWorkXp += amount;
    }
    static void MarkChanged(bool immediate)
    {
        int previousLevel = MetaProgressionCatalog.LevelForXp(profile.totalXp);
        RecalculateTotal(); MarkDirty();
        if (immediate || milestonePending || previousLevel != MetaProgressionCatalog.LevelForXp(profile.totalXp)) RequestSave();
        milestonePending = false;
        Changed?.Invoke();
    }

    static void SelectObjectives(MetaRunState state)
    {
        uint hash = 2166136261;
        foreach (char c in state.runId) hash = (hash ^ c) * 16777619;
        for (int group = 0; group < 3; group++) {
            var candidates = Array.FindAll(MetaProgressionCatalog.Challenges, value => !value.permanent && value.group == group && (value.id != "run-exotic" || Level >= 5));
            state.objectiveIds.Add(candidates[hash % (uint)candidates.Length].id);
            hash = hash * 1664525 + 1013904223;
        }
    }
    static void CheckChallenges()
    {
        bool milestone = false;
        foreach (var challenge in MetaProgressionCatalog.Challenges) {
            if (!challenge.permanent && !run.objectiveIds.Contains(challenge.id)) continue;
            var completed = challenge.permanent ? profile.completedMilestones : run.completedObjectives;
            if (completed.Contains(challenge.id) || (challenge.permanent ? LifetimeMetric(challenge.metric) : Metric(challenge.metric, run)) < challenge.target) continue;
            completed.Add(challenge.id);
            if (challenge.permanent) { run.milestones.Add(challenge.id); run.milestoneXp += challenge.xp; milestone = true; }
            else run.challengeXp += challenge.xp;
        }
        // Save is performed after RecalculateTotal in MarkChanged, so one transaction includes
        // both the permanent flag and its XP. A crash cannot leave one without the other.
        if (milestone) milestonePending = true;
    }
    static int Metric(string metric, MetaRunState state)
    {
        if (state == null) return 0;
        switch (metric) {
            case "depth": return state.maxDepth;
            case "regions": return state.regions.Count;
            case "discoveries": return state.discoveries.Count;
            case "crafts": return state.crafts.Count;
            case "exotics": return state.exoticCrafts.Count;
            case "victory": return state.won ? 1 : 0;
            case "variety": return state.resources.Count(value => value.count > 0);
            case "rare": return state.resources.Count(value => value.count > 0 && MetaProgressionCatalog.IsRare((Item)value.item));
            case "resources": return (int)Math.Min(int.MaxValue, state.resources.Sum(value => (long)value.count));
            default: return 0;
        }
    }
    static int LifetimeMetric(string metric)
    {
        if (!metric.StartsWith("total-", StringComparison.Ordinal))
            return profile.runs.Select(value => Metric(metric, value)).DefaultIfEmpty(0).Max();
        string unit = metric.Substring(6);
        // Recipe milestones count unique recipes across all runs; ores/regions/discoveries
        // accumulate from deduplicated run ledgers that survive deletion of save slots.
        if (unit == "exotics") return profile.runs.SelectMany(r => r.exoticCrafts).Distinct().Count();
        Item? ore = unit switch { "coal" => Item.Coal, "copper" => Item.Copper, "iron" => Item.Iron,
            "silver" => Item.Silver, "gold" => Item.Gold, _ => null };
        long total = ore.HasValue
            ? profile.runs.Sum(r => r.resources.Where(c => c.item == (int)ore.Value).Sum(c => (long)c.count))
            : profile.runs.Sum(r => (long)Metric(unit, r));
        return (int)Math.Min(int.MaxValue, total);
    }
    public static MetaChallengeProgress[] GetChallenges()
    {
        EnsureLoaded(); var result = new List<MetaChallengeProgress>();
        foreach (var definition in MetaProgressionCatalog.Challenges) {
            if (!definition.permanent && (run == null || !run.objectiveIds.Contains(definition.id))) continue;
            int current = definition.permanent ? LifetimeMetric(definition.metric) : Metric(definition.metric, run);
            bool completed = definition.permanent ? profile.completedMilestones.Contains(definition.id) : run.completedObjectives.Contains(definition.id);
            result.Add(new MetaChallengeProgress { id = definition.id, name = definition.name, metric = definition.metric, current = Math.Min(definition.target, completed ? definition.target : current), target = definition.target, xp = definition.xp, completed = completed, permanent = definition.permanent });
        }
        return result.ToArray();
    }

    static void RecalculateTotal()
    {
        long total = profile.periodRewardXp;
        foreach (var state in profile.runs) {
            state.earnedXp = (long)Math.Floor(state.progressXp + state.efficiencyXp) + state.challengeXp + state.milestoneXp;
            total += (long)Math.Floor(state.progressXp + state.efficiencyXp) + state.challengeXp;
        }
        foreach (string id in profile.completedMilestones) total += MetaProgressionCatalog.Challenge(id)?.xp ?? 0;
        profile.totalXp = total;
    }
    static void RebuildCaches()
    {
        ClearCaches(); if (run == null) return;
        foreach (var id in run.resourceSources) sources.Add(id);
        foreach (var id in run.regions) regions.Add(id);
        foreach (var id in run.discoveries) discoveries.Add(id);
        foreach (var counter in run.resources) resourceCounts[counter.item] = counter;
    }
    static void MergeLedger(MetaRunState target, MetaRunState saved)
    {
        target.maxDepth = Math.Max(target.maxDepth, saved.maxDepth);
        target.activeSeconds = Math.Max(target.activeSeconds, saved.activeSeconds);
        target.progressXp = Math.Max(target.progressXp, saved.progressXp);
        target.efficiencyXp = Math.Max(target.efficiencyXp, saved.efficiencyXp);
        target.challengeXp = Math.Max(target.challengeXp, saved.challengeXp);
        target.milestoneXp = Math.Max(target.milestoneXp, saved.milestoneXp);
        target.won |= saved.won;
        // A finished world can be loaded for play, but its already earned rewards remain final.
        target.ended |= saved.ended;
        Union(target.resourceSources, saved.resourceSources); Union(target.regions, saved.regions); Union(target.discoveries, saved.discoveries);
        Union(target.crafts, saved.crafts); Union(target.exoticCrafts, saved.exoticCrafts);
        Union(target.completedObjectives, saved.completedObjectives); Union(target.milestones, saved.milestones);
        target.challengeXp = target.completedObjectives.Sum(id => (long)MetaProgressionCatalog.Challenge(id).xp);
        target.milestoneXp = target.milestones.Sum(id => (long)MetaProgressionCatalog.Challenge(id).xp);
        foreach (var counter in saved.resources) {
            var current = target.resources.Find(value => value.item == counter.item);
            if (current == null) target.resources.Add(new MetaResourceCount { item = counter.item, count = counter.count });
            else current.count = Math.Max(current.count, counter.count);
        }
    }
    static void Union<T>(List<T> target, List<T> source)
    {
        var set = new HashSet<T>(target);
        foreach (T entry in source) if (set.Add(entry)) target.Add(entry);
    }
    static T Clone<T>(T value) => value == null ? default : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

    [Serializable] sealed class Envelope { public int version = FormatVersion; public string payload, sha256; }
    sealed class WriteResult
    {
        public bool success;
        public string error;
        public long length = -1, writeTicks = -1;
    }
    static void MarkDirty() { dirty = true; changeGeneration++; }

    // Copies references to immutable strings, not their contents. JsonUtility, hashing and
    // disk I/O then run on one worker, so a long-lived profile cannot stall every fifth frame-second.
    public static bool RequestSave()
    {
        EnsureLoaded(); CompletePending(false);
        if (writeBlocked) return false;
        if (pendingSave != null) { saveAfterPending |= changeGeneration != pendingGeneration; return true; }
        saveAfterPending = false;
        if (!dirty && File.Exists(loadedPath)) return true;
        RecalculateTotal();
        var snapshot = CopyProfile(profile);
        snapshot.revision = profile.revision + 1;
        pendingGeneration = changeGeneration; pendingRevision = snapshot.revision;
        string path = loadedPath;
        long length = mainLength, ticks = mainWriteTicks;
        bool valid = mainValid, recovery = recovered;
        saveElapsed = 0d;
        pendingSave = Task.Run(() => WriteProfile(snapshot, path, length, ticks, valid, recovery));
        return true;
    }

    static void CompletePending(bool wait, bool notify = true)
    {
        if (pendingSave == null || (!wait && !pendingSave.IsCompleted)) return;
        WriteResult result;
        try { result = pendingSave.GetAwaiter().GetResult(); }
        catch (Exception error) { result = new WriteResult { error = "Fortschritt konnte nicht gespeichert werden: " + error.Message }; }
        pendingSave = null;
        string previousError = LastError;
        if (result.success) {
            profile.revision = pendingRevision;
            mainLength = result.length; mainWriteTicks = result.writeTicks; mainValid = true; recovered = false;
            dirty = changeGeneration != pendingGeneration;
        } else dirty = true;
        LastError = result.error;
        if (notify && previousError != LastError) Changed?.Invoke();
    }

    // Purchases, world snapshots, leaving a run and application shutdown use this durable path.
    // Draining the single writer first prevents an older autosave replacing a newer transaction.
    public static bool Save()
    {
        EnsureLoaded(); CompletePending(true); saveAfterPending = false;
        return SaveCurrentProfile();
    }
    static bool SaveCurrentProfile()
    {
        if (writeBlocked) return false;
        if (!dirty && File.Exists(loadedPath)) return true;
        long previousRevision = profile.revision;
        RecalculateTotal(); profile.revision++;
        var result = WriteProfile(profile, loadedPath, mainLength, mainWriteTicks, mainValid, recovered);
        if (result.success) {
            mainLength = result.length; mainWriteTicks = result.writeTicks; mainValid = true; recovered = false;
            dirty = false; saveElapsed = 0d;
        } else { profile.revision = previousRevision; dirty = true; }
        LastError = result.error;
        return result.success;
    }

    static WriteResult WriteProfile(MetaProfile snapshot, string path, long knownLength, long knownWriteTicks, bool knownValid, bool recovery)
    {
        var result = new WriteResult();
        string temporary = path + ".tmp", backup = path + ".bak";
        try {
            string payload = JsonUtility.ToJson(snapshot);
            string encoded = JsonUtility.ToJson(new Envelope { payload = payload, sha256 = Hash(payload) });
            if (Encoding.UTF8.GetByteCount(encoded) > MaxFileBytes) throw new IOException("Das Fortschrittsprofil überschreitet die maximale Dateigröße.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] bytes = new UTF8Encoding(false).GetBytes(encoded);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) {
                var mainInfo = new FileInfo(path);
                bool validMain = knownValid;
                if (mainInfo.Length != knownLength || mainInfo.LastWriteTimeUtc.Ticks != knownWriteTicks) {
                    validMain = TryRead(path, out _, out bool future);
                    if (future) throw new IOException("Das Fortschrittsprofil wurde durch eine neuere Spielversion ersetzt.");
                }
                if (!validMain && !File.Exists(path + ".invalid")) File.Copy(path, path + ".invalid");
                File.Replace(temporary, path, validMain ? backup : null);
            } else File.Move(temporary, path);
            result.success = true;
            // A complete temp journal can recover a profile whose main and backup were
            // both damaged. Repair that backup too before declaring recovery complete.
            if (!File.Exists(backup) || (recovery && !TryRead(backup, out _, out _))) File.Copy(path, backup, true);
        } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException) {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            result.error = (result.success ? "Fortschritt gespeichert; Sicherung fehlgeschlagen: " : "Fortschritt konnte nicht gespeichert werden: ") + error.Message;
        }
        if (result.success) {
            try { var info = new FileInfo(path); result.length = info.Length; result.writeTicks = info.LastWriteTimeUtc.Ticks; }
            catch (IOException) { }
        }
        return result;
    }

    static MetaProfile CopyProfile(MetaProfile source)
    {
        var copy = new MetaProfile { version = source.version, revision = source.revision, totalXp = source.totalXp,
            upgrades = CopyRanks(source.upgrades), completedMilestones = new List<string>(source.completedMilestones),
            periodRewardXp = source.periodRewardXp, challengeClockUtc = source.challengeClockUtc,
            challengePeriods = Clone(source.challengePeriods),
            runs = new List<MetaRunState>(source.runs.Count) };
        foreach (var state in source.runs) {
            var copied = new MetaRunState { version = state.version, runId = state.runId, loadout = CopyLoadout(state.loadout),
                settings = state.settings.Sanitized(), maxDepth = state.maxDepth, activeSeconds = state.activeSeconds,
                recentWorkXp = state.recentWorkXp, workWindowSeconds = state.workWindowSeconds, progressXp = state.progressXp,
                efficiencyXp = state.efficiencyXp, earnedXp = state.earnedXp, challengeXp = state.challengeXp, milestoneXp = state.milestoneXp,
                ended = state.ended, won = state.won, resourceSources = new List<string>(state.resourceSources), regions = new List<string>(state.regions),
                discoveries = new List<string>(state.discoveries), crafts = new List<int>(state.crafts), exoticCrafts = new List<int>(state.exoticCrafts),
                objectiveIds = new List<string>(state.objectiveIds), completedObjectives = new List<string>(state.completedObjectives), milestones = new List<string>(state.milestones),
                resources = new List<MetaResourceCount>(state.resources.Count) };
            foreach (var resource in state.resources) copied.resources.Add(new MetaResourceCount { item = resource.item, count = resource.count });
            copy.runs.Add(copied);
        }
        return copy;
    }
    static List<MetaUpgradeRank> CopyRanks(List<MetaUpgradeRank> ranks)
    {
        var copy = new List<MetaUpgradeRank>(ranks.Count);
        foreach (var rank in ranks) copy.Add(new MetaUpgradeRank { id = rank.id, rank = rank.rank });
        return copy;
    }
    static MetaLoadout CopyLoadout(MetaLoadout loadout) => new MetaLoadout {
        ranks = CopyRanks(loadout.ranks), healthMultiplier = loadout.healthMultiplier, energyMultiplier = loadout.energyMultiplier,
        movementMultiplier = loadout.movementMultiplier, jumpMultiplier = loadout.jumpMultiplier, miningEnergyMultiplier = loadout.miningEnergyMultiplier,
        startMoney = loadout.startMoney, startingTorches = loadout.startingTorches, startingLadders = loadout.startingLadders,
        buildReachBonus = loadout.buildReachBonus, carryCapacityBonus = loadout.carryCapacityBonus
    };
    static bool TryRead(string path, out MetaProfile result, out bool newer)
    {
        result = null; newer = false;
        try {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes) return false;
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
            if (envelope == null) return false;
            if (envelope.version > FormatVersion) { newer = true; return false; }
            if (envelope.version != FormatVersion || string.IsNullOrEmpty(envelope.payload) || envelope.sha256 != Hash(envelope.payload)) return false;
            var candidate = JsonUtility.FromJson<MetaProfile>(envelope.payload);
            if (candidate != null && candidate.version > FormatVersion) { newer = true; return false; }
            if (!ValidateProfile(candidate)) return false;
            result = candidate; return true;
        } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException) { return false; }
    }
    static string Hash(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
    static void RememberMainStamp()
    {
        var info = new FileInfo(loadedPath);
        mainLength = info.Exists ? info.Length : -1;
        mainWriteTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : -1;
    }
    static bool ValidateProfile(MetaProfile value)
    {
        if (value == null || value.version != FormatVersion || value.revision < 0 || value.totalXp < 0 || value.totalXp > 1000000000000L || value.runs == null || value.runs.Count > 20000 || value.upgrades == null || value.upgrades.Count > MetaProgressionCatalog.Upgrades.Length || !ValidStrings(value.completedMilestones, 100)) return false;
        if (value.runs.Any(entry => !ValidateRun(entry)) || value.runs.Select(entry => entry.runId).Distinct().Count() != value.runs.Count) return false;
        if (value.completedMilestones.Any(id => MetaProgressionCatalog.Challenge(id)?.permanent != true)) return false;
        if (!ValidatePeriods(value)) return false;
        double expectedTotal = value.periodRewardXp + value.runs.Sum(entry => Math.Floor(entry.progressXp + entry.efficiencyXp) + entry.challengeXp) + value.completedMilestones.Sum(id => (long)MetaProgressionCatalog.Challenge(id).xp);
        if (expectedTotal != value.totalXp || expectedTotal > 1000000000000d || value.runs.Any(entry => entry.milestones.Any(id => !value.completedMilestones.Contains(id)))) return false;
        var seen = new HashSet<string>();
        int power = 0, comfort = 0;
        foreach (var upgrade in value.upgrades) {
            if (upgrade == null || !seen.Add(upgrade.id)) return false;
            var definition = MetaProgressionCatalog.Upgrade(upgrade.id);
            if (definition == null || upgrade.rank < 0 || upgrade.rank > definition.maxRank) return false;
            if (definition.comfort) comfort += definition.TotalCost(upgrade.rank); else power += definition.TotalCost(upgrade.rank);
        }
        int level = MetaProgressionCatalog.LevelForXp(value.totalXp);
        return power <= MetaProgressionCatalog.EarnedPowerPoints(level) && comfort <= MetaProgressionCatalog.EarnedComfortPoints(level);
    }
    public static bool ValidateRunState(MetaRunState value) => value == null || ValidateRun(value);
    public static bool ValidateRun(MetaRunState value)
    {
        if (value == null || value.version != FormatVersion || string.IsNullOrWhiteSpace(value.runId) || value.runId.Length > 160 || value.maxDepth < 0 || value.maxDepth > 100000 || value.loadout == null || value.settings == null) return false;
        if (!Finite(value.activeSeconds) || !Finite(value.recentWorkXp) || !Finite(value.workWindowSeconds) || !Finite(value.progressXp) || !Finite(value.efficiencyXp) || value.progressXp > 1000000000000d || value.efficiencyXp > value.progressXp * .200001d || value.earnedXp < 0 || value.earnedXp > 1200000010000L || value.challengeXp < 0 || value.milestoneXp < 0 || value.activeSeconds > 10000000000d || value.workWindowSeconds > WorkWindow || value.recentWorkXp > value.progressXp + .001d) return false;
        if (!ValidStrings(value.resourceSources, 2000000) || !ValidStrings(value.regions, 1000000) || !ValidStrings(value.discoveries, 100000) || !ValidStrings(value.objectiveIds, 3) || !ValidStrings(value.completedObjectives, 20) || !ValidStrings(value.milestones, 100)) return false;
        if (value.resources == null || value.resources.Count > 100 || value.resources.Any(entry => entry == null || entry.count < 0 || entry.count > 10000000 || MetaProgressionCatalog.ResourceWeight((Item)entry.item) <= 0f) || value.resources.Select(entry => entry.item).Distinct().Count() != value.resources.Count) return false;
        if (value.crafts == null || value.exoticCrafts == null || value.crafts.Count > 1000 || value.exoticCrafts.Count > 1000 || value.crafts.Any(entry => entry < 0) || value.exoticCrafts.Any(entry => entry < 0) || value.crafts.Distinct().Count() != value.crafts.Count || value.exoticCrafts.Distinct().Count() != value.exoticCrafts.Count) return false;
        if (value.objectiveIds.Any(id => MetaProgressionCatalog.Challenge(id)?.permanent != false) || value.completedObjectives.Any(id => !value.objectiveIds.Contains(id)) || value.milestones.Any(id => MetaProgressionCatalog.Challenge(id)?.permanent != true)) return false;
        if (value.challengeXp != value.completedObjectives.Sum(id => (long)MetaProgressionCatalog.Challenge(id).xp) || value.milestoneXp != value.milestones.Sum(id => (long)MetaProgressionCatalog.Challenge(id).xp)) return false;
        value.settings = value.settings.Sanitized();
        var loadout = value.loadout;
        return InRange(loadout.healthMultiplier, 1f, 1.51f) && InRange(loadout.energyMultiplier, 1f, 1.51f) && InRange(loadout.movementMultiplier, 1f, 1.11f) && InRange(loadout.jumpMultiplier, 1f, 1.09f) && InRange(loadout.miningEnergyMultiplier, .79f, 1f) && InRange(loadout.buildReachBonus, 0f, .31f) && InRange(loadout.carryCapacityBonus, 0f, 10.01f) && loadout.startMoney >= 0 && loadout.startMoney <= 200 && loadout.startingTorches >= 0 && loadout.startingTorches <= 10 && loadout.startingLadders >= 0 && loadout.startingLadders <= 10 && loadout.ranks != null && loadout.ranks.Count <= MetaProgressionCatalog.Upgrades.Length && loadout.ranks.All(entry => entry != null && MetaProgressionCatalog.Upgrade(entry.id) is MetaUpgradeDefinition definition && entry.rank >= 0 && entry.rank <= definition.maxRank) && loadout.ranks.Select(entry => entry.id).Distinct().Count() == loadout.ranks.Count;
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    static bool InRange(float value, float minimum, float maximum) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    static bool ValidStrings(List<string> values, int maximum) => values != null && values.Count <= maximum && values.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 160) && new HashSet<string>(values).Count == values.Count;
}
