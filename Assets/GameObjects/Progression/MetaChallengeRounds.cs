using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// Daily, weekly and monthly rounds drawn from ChallengeCatalog, gameplay counters and lifetime milestone stages.
// All challenge XP goes into periodRewardXp, so older builds still validate the total. Every payout also leaves a
// marker in the current run's ledger, which older builds keep: "cx:"/"ct:"/"cs:" describe the paid round so it is
// rebuilt exactly after an older build dropped it, "cr:" restores its paid tasks, "cm:" restores milestone awards on
// load (RebuildAwards) and "tp:" keeps each run tally's peak (MergeTallies). Neither pays a second time.
public static partial class MetaProgression
{
    // Older builds reject a run (and the whole profile) for a ledger string that is blank, longer or duplicated.
    const int MaxLedgerKey = 160;
    static readonly ChallengePeriod[] RoundPeriods = { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly };
    static readonly string[] LegacyMinedKeys = { "l2-block:", "l3-block:", "xxl-layer:4:", "xxl-layer:5:", "surface-block:" };
    // Rounds shown while the profile cannot be written; never persisted.
    static readonly Dictionary<(int, long), MetaChallengeRound> transientRounds = new();
    static HashSet<string> awardedStages;
    delegate ChallengeGain RoundRecorder(MetaChallengeRound round);

    // Test overrides are deliberately restricted to isolated profiles.
    public static DateTime? ChallengeTestUtc { get; set; }
    public static ChallengeSettings ChallengeSettingsOverride { get; set; }
    public static TimeZoneInfo ChallengeTimeZoneOverride { get; set; }
    static bool Isolated => !string.IsNullOrWhiteSpace(testDirectory);
    public static ChallengeSettings CurrentChallengeSettings =>
        ((Isolated ? ChallengeSettingsOverride : GpsSettings.Preferences.challenges) ?? new ChallengeSettings()).Sanitized();
    static TimeZoneInfo ChallengeZone => Isolated && ChallengeTimeZoneOverride != null ? ChallengeTimeZoneOverride : TimeZoneInfo.Local;
    // Cells newly credited in this session; the runtime compares it per trip. Never reset while playing.
    public static int CreditedBlocks { get; private set; }

    static DateTime ChallengeNow()
    {
        EnsureLoaded();
        DateTime now = Isolated && ChallengeTestUtc.HasValue ? DateTime.SpecifyKind(ChallengeTestUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        // Moving the OS clock backwards must not reopen an already rewarded period.
        return new DateTime(Math.Max(now.Ticks, profile.challengeClockUtc), DateTimeKind.Utc);
    }

    // The stored round of a period; a new one is drawn (and stored) only when the old one has ended.
    static MetaChallengeRound Round(ChallengePeriod period)
    {
        EnsureLoaded();
        profile.challengeRounds ??= new List<MetaChallengeRound>();
        DateTime now = ChallengeNow();
        MetaChallengeRound old = null;
        foreach (var round in profile.challengeRounds) if (round != null && round.period == (int)period) { old = round; break; }
        if (old != null && now.Ticks < old.endUtc) return old;
        var zone = ChallengeZone;
        ChallengeEngine.Bounds(period, now, zone, out long start, out long end, out int day);
        // After a time-zone change the next round starts where the old one ended, never overlapping it.
        if (old != null && start < old.endUtc) {
            start = old.endUtc;
            if (start >= end && end > 0 && end < DateTime.MaxValue.Ticks) {
                ChallengeEngine.Bounds(period, new DateTime(end, DateTimeKind.Utc), zone, out long next, out end, out day);
                start = Math.Max(next, old.endUtc);
            }
        }
        if (start >= end) end = start + TimeSpan.TicksPerDay;
        if (writeBlocked) {
            if (!transientRounds.TryGetValue(((int)period, start), out var cached))
                transientRounds[((int)period, start)] = cached = NewRound(period, start, end, day, now.Ticks, old);
            return cached;
        }
        var drawn = NewRound(period, start, end, day, now.Ticks, old);
        profile.challengeRounds.RemoveAll(round => round == null || round.period == (int)period);
        profile.challengeRounds.Add(drawn);
        profile.challengeClockUtc = Math.Max(profile.challengeClockUtc, now.Ticks);
        MarkDirty();
        // In a run the autosave stores it; a save here would snapshot a half-applied Record* call.
        if (run == null) RequestSave();
        return drawn;
    }
    // A round an older build dropped is rebuilt from its descriptor markers: a redraw would differ, because the draw
    // depends on the previous round and on the draw context. Otherwise it is drawn. Then paid markers are applied.
    static MetaChallengeRound NewRound(ChallengePeriod period, long start, long end, int day, long now, MetaChallengeRound old)
    {
        RoundLedger ledger = null; MetaChallengeRound round = null;
        try { ledger = ScanRound((int)period, start); round = RestoreRound(period, start, now, ledger); }
        catch (Exception error) { Debug.LogException(error); }
        round ??= DrawRound(period, start, end, day, old);
        if (ledger != null) ApplyPaidMarkers(round, old, ledger.paid);
        return round;
    }
    static MetaChallengeRound DrawRound(ChallengePeriod period, long start, long end, int day, MetaChallengeRound old)
    {
        MetaChallengeRound round = null;
        try { round = ChallengeEngine.Draw(period, start, end, day, DrawContext(), CurrentChallengeSettings, old != null && old.endUtc == start ? old : null); }
        catch (Exception error) { Debug.LogException(error); }
        round ??= new MetaChallengeRound { period = (int)period, startUtc = start, endUtc = end, dayIndex = day };
        round.tasks ??= new List<MetaChallengeTask>(); round.masks ??= new List<MetaChallengeMask>(); round.runValues ??= new List<MetaChallengeRunValue>();
        return round;
    }
    static ChallengeDrawContext DrawContext()
    {
        // Outside a run (main menu) the round is drawn for a fresh run: only free recipes (axe, scythe, dynamite) are
        // craftable there; blueprints for pickaxes, gear and medkits are bought again in every run.
        var context = new ChallengeDrawContext { level = Level, craftLimits = run != null ? CraftChallengeLimits() : (int[])FreshRunCraftLimits.Clone() };
        if (Isolated) { context.reachedLayer = Math.Max(1, profile.reachedMiningLayer); return context; }
        // A fresh world must not get tasks for the deepest layer ever reached: use what this run or a stored world reached.
        int layer = Math.Max(run != null ? HomeLandscape.LayerAtDepth(run.maxDepth) : 1, ImportReachedMiningLayers());
        context.reachedLayer = Math.Max(1, Math.Min(layer, profile.reachedMiningLayer));
        try { var inventory = InventoryManager.Instance; context.pickaxeHardness = inventory ? inventory.EquippedPickaxeMaximumHardness : 0f; }
        catch (Exception) { context.pickaxeHardness = 0f; }
        return context;
    }
    // Same layout as CraftChallengeLimits: pickaxe upgrades, tools, gear, energy upgrades, carrying upgrades, dynamite, medkits.
    static readonly int[] FreshRunCraftLimits = { 0, 2, 0, 0, 0, 20, 0 };
    // Highest home layer of the stored worlds; also raises the profile's reached layer (older profiles lack it).
    static int ImportReachedMiningLayers()
    {
        // Isolated profiles never inspect user save slots.
        if (Isolated) return 1;
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(GameSaveSystem.TestDirectory)) return 1;
#endif
        int reached = 1;
        try { foreach (int slot in GameSaveSystem.OccupiedSlots()) reached = Math.Max(reached, GameSaveSystem.GetHomeLayer(slot)); }
        catch (Exception) { }
        if (!writeBlocked && reached > profile.reachedMiningLayer) { profile.reachedMiningLayer = Math.Min(100, reached); MarkDirty(); }
        return reached;
    }

    // Invariant digits: a negative subject must read the same under every culture (some write U+2212 as minus).
    static string MarkerPrefix(MetaChallengeRound round) => "cr:" + Inv(round.period) + ":" + Inv(round.startUtc) + ":";
    static string TaskKey(MetaChallengeTask task, int index) => task.id + "/" + Inv(task.subject) + "/" + Inv(index);
    static bool IsProject(MetaChallengeTask task) => task.tier == (int)ChallengeTier.Project;
    static void AddMarker(string key) { if (sources.Add(key)) run.resourceSources.Add(key); }
    static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
    static bool TryInv(string text, out long value) => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    // ":<period>:<start>:" after the two letters of a descriptor marker.
    static string DescriptorAt(int period, long start) => ":" + Inv(period) + ":" + Inv(start) + ":";
    // Idempotent: every paid task, stage and bonus of the round has its marker in the current run afterwards.
    static void WritePaidMarkers(MetaChallengeRound round)
    {
        // Converted legacy rounds are never drawn again, so their markers could only mark tasks of a later draw as paid.
        if (round.format != 2) return;
        string prefix = MarkerPrefix(round);
        for (int i = 0; i < round.tasks.Count; i++) {
            var task = round.tasks[i];
            if (task == null) continue;
            string key = prefix + TaskKey(task, i);
            if (IsProject(task)) for (int stage = 0; stage < task.stagesPaid; stage++) AddMarker(key + ":s" + stage);
            else if (task.done) AddMarker(key + ":t");
        }
        if (round.bonusPaid) AddMarker(prefix + "b");
        WriteDescriptors(round);
    }
    // Once per round and run: "cx:p:start:end:day:bonus:count", per task "ct:p:start:i:id/subject/tier/target/xp" and per
    // project "cs:p:start:i:targets/stageXp". All or nothing: a round that cannot be described exactly is drawn again.
    static void WriteDescriptors(MetaChallengeRound round)
    {
        if (round.tasks.Count == 0) return;
        string at = DescriptorAt(round.period, round.startUtc);
        string header = "cx" + at + Inv(round.endUtc) + ":" + Inv(round.dayIndex) + ":" + Inv(round.bonusXp) + ":" + Inv(round.tasks.Count);
        if (sources.Contains(header)) return;
        var keys = new List<string>(round.tasks.Count + 2) { header };
        for (int i = 0; i < round.tasks.Count; i++) {
            var task = round.tasks[i];
            if (task == null || ChallengeCatalog.Template(task.id) == null || task.id.IndexOf('/') >= 0) return;
            keys.Add("ct" + at + Inv(i) + ":" + task.id + "/" + Inv(task.subject) + "/" + Inv(task.tier) + "/" + Inv(task.target) + "/" + Inv(task.xp));
            if (!IsProject(task)) continue;
            if (task.stageTargets == null || task.stageXp == null) return;
            keys.Add("cs" + at + Inv(i) + ":" + string.Join(",", task.stageTargets.Select(value => Inv(value))) + "/" + string.Join(",", task.stageXp.Select(value => Inv(value))));
        }
        foreach (string key in keys) if (key.Length > MaxLedgerKey) return;
        foreach (string key in keys) AddMarker(key);
    }

    // The markers of one round in all runs' ledgers, collected in one pass when the round is created.
    sealed class RoundLedger
    {
        public HashSet<string> paid;
        public string header;
        public Dictionary<int, string> tasks, stages;
        public bool conflict;
    }
    static RoundLedger ScanRound(int period, long start)
    {
        var result = new RoundLedger();
        string paidPrefix = "cr:" + period + ":" + start + ":", at = DescriptorAt(period, start);
        foreach (var state in profile.runs) {
            if (state?.resourceSources == null) continue;
            foreach (string key in state.resourceSources) {
                if (key == null || key.Length < 4 || key[0] != 'c' || key[2] != ':') continue;
                char kind = key[1];
                if (kind == 'r') {
                    if (key.Length > paidPrefix.Length && key.StartsWith(paidPrefix, StringComparison.Ordinal)) (result.paid ??= new HashSet<string>()).Add(key.Substring(paidPrefix.Length));
                    continue;
                }
                if ((kind != 'x' && kind != 't' && kind != 's') || key.Length <= 2 + at.Length || string.CompareOrdinal(key, 2, at, 0, at.Length) != 0) continue;
                string rest = key.Substring(2 + at.Length);
                if (kind == 'x') { result.conflict |= result.header != null && result.header != rest; result.header = rest; continue; }
                // "i:..." per task index; two different descriptions of one index (two different rounds) are not rebuilt.
                int split = rest.IndexOf(':');
                if (split <= 0 || !int.TryParse(rest.Substring(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out int index)) { result.conflict = true; continue; }
                var map = kind == 't' ? result.tasks ??= new Dictionary<int, string>() : result.stages ??= new Dictionary<int, string>();
                string value = rest.Substring(split + 1);
                if (map.TryGetValue(index, out string known) && known != value) result.conflict = true;
                map[index] = value;
            }
        }
        return result;
    }
    // Rebuilds the round from its descriptors, with fresh progress (ApplyPaidMarkers restores what was paid). Null when
    // they are missing, incomplete, contradictory or name an unknown template: the round is then drawn as before.
    static MetaChallengeRound RestoreRound(ChallengePeriod period, long start, long now, RoundLedger ledger)
    {
        if (ledger.header == null || ledger.tasks == null || ledger.conflict) return null;
        try {
            string[] head = ledger.header.Split(':');
            if (head.Length != 4 || !TryInv(head[0], out long end) || !TryInv(head[1], out long day) || !TryInv(head[2], out long bonus) || !TryInv(head[3], out long count)) return null;
            if (end <= now || day < 0 || day > 30 || bonus < 0 || bonus > int.MaxValue || count < 1 || count > 16 || ledger.tasks.Count != count) return null;
            int diligenceTasks = ChallengeCatalog.DiligenceTasks(CurrentChallengeSettings), projects = 0;
            var round = new MetaChallengeRound { period = (int)period, startUtc = start, endUtc = end, dayIndex = (int)day, bonusXp = (int)bonus };
            for (int i = 0; i < count; i++) {
                if (!ledger.tasks.TryGetValue(i, out string text)) return null;
                string[] part = text.Split('/');
                var template = part.Length == 5 ? ChallengeCatalog.Template(part[0]) : null;
                if (template == null || !TryInv(part[1], out long subject) || !TryInv(part[2], out long tier) || !TryInv(part[3], out long target) || !TryInv(part[4], out long xp)) return null;
                bool project = tier == (int)ChallengeTier.Project;
                if (project != template.project || subject < -1 || subject > 10000 || tier < 0 || tier > 4 || target < 1 || target > int.MaxValue || xp < 0 || xp > int.MaxValue) return null;
                // Name, unit and metric exactly as ChallengeEngine.Draw sets them.
                var task = new MetaChallengeTask { id = template.id, name = ChallengeCatalog.Name(template.id, (int)subject),
                    unit = project ? template.unit : ChallengeCatalog.TemplateUnit(template, (int)subject, diligenceTasks), metric = template.metric,
                    mode = (int)template.mode, subject = (int)subject, atLeast = template.atLeast, tier = (int)tier, target = (int)target, xp = (int)xp };
                if (project) {
                    projects++;
                    string[] lists = ledger.stages != null && ledger.stages.TryGetValue(i, out string stages) ? stages.Split('/') : null;
                    if (lists == null || lists.Length != 2 || !TryInts(lists[0], out var stageTargets) || !TryInts(lists[1], out var stageXp)) return null;
                    task.stageTargets = stageTargets; task.stageXp = stageXp;
                }
                round.tasks.Add(task);
            }
            return (ledger.stages?.Count ?? 0) == projects && ChallengeEngine.Sanitize(round) ? round : null;
        } catch (Exception error) { Debug.LogException(error); return null; }
    }
    static bool TryInts(string csv, out int[] values)
    {
        string[] parts = csv.Split(',');
        values = parts.Length >= 1 && parts.Length <= 5 ? new int[parts.Length] : null;
        if (values == null) return false;
        for (int i = 0; i < parts.Length; i++) {
            if (!TryInv(parts[i], out long value) || value < int.MinValue || value > int.MaxValue) return false;
            values[i] = (int)value;
        }
        return true;
    }
    // A round drawn again after an older build dropped it keeps what was already paid, without paying it again.
    // Only XP covered by periodRewardXp is restored, so the integrity check on load always holds.
    static void ApplyPaidMarkers(MetaChallengeRound round, MetaChallengeRound replaced, HashSet<string> paid)
    {
        if (round.tasks.Count == 0 || paid == null) return;
        long budget = profile.periodRewardXp;
        foreach (var other in profile.challengeRounds) if (other != null && other != replaced) budget -= ChallengeEngine.PaidXp(other);
        foreach (var award in profile.milestoneAwards) if (award != null) budget -= award.xp;
        for (int i = 0; i < round.tasks.Count; i++) {
            var task = round.tasks[i];
            if (task == null) continue;
            string key = TaskKey(task, i);
            if (!IsProject(task)) {
                if (task.done || !paid.Contains(key + ":t") || task.xp > budget) continue;
                task.progress = task.target; task.done = true; task.paid = task.xp; budget -= task.xp;
                continue;
            }
            if (task.stageTargets == null || task.stageXp == null || task.stageXp.Length != task.stageTargets.Length) continue;
            int stages = task.stagesPaid;
            long xp = 0;
            while (stages < task.stageTargets.Length && paid.Contains(key + ":s" + stages) && xp + task.stageXp[stages] <= budget) xp += task.stageXp[stages++];
            if (stages == task.stagesPaid) continue;
            task.progress = Math.Max(task.progress, task.stageTargets[stages - 1]);
            task.stagesPaid = stages; task.paid += (int)xp; task.done = stages == task.stageTargets.Length; budget -= xp;
        }
        if (!round.bonusPaid && paid.Contains("b") && round.tasks.All(task => task != null && task.done) && round.bonusXp <= budget) round.bonusPaid = true;
    }

    static void Merge(ref ChallengeGain total, ChallengeGain gain) { total.xp += gain.xp; total.completed += gain.completed; total.changed |= gain.changed; }
    static void Finish(ChallengeGain gain) { if (gain.changed || gain.xp > 0) MarkChanged(false); else MarkDirty(); }
    static ChallengeGain RecordChallenge(string metric, long amount, int subject = -1)
        => amount <= 0 ? default : RecordRounds(round => ChallengeEngine.Record(round, metric, amount, subject));
    static ChallengeGain RecordChallengeMax(string metric, long value, int subject = -1)
        => value <= 0 ? default : RecordRounds(round => ChallengeEngine.RecordMax(round, metric, value, subject));
    static ChallengeGain RecordChallengeDistinct(string metric, int bit, int subject = -1)
        => bit < 0 || bit > 30 ? default : RecordRounds(round => ChallengeEngine.RecordDistinct(round, metric, bit, subject));
    static ChallengeGain RecordChallengeRun(string metric, string runId, long amount, int subject = -1)
        => amount <= 0 ? default : RecordRounds(round => ChallengeEngine.RecordRun(round, metric, runId, amount, subject));
    // Feeds one event to the daily, weekly and monthly round and books the XP. The caller calls MarkChanged.
    static ChallengeGain RecordRounds(RoundRecorder record)
    {
        if (!HasRun() || run.ended) return default;
        var total = default(ChallengeGain);
        MetaChallengeRound daily = null, weekly = null;
        foreach (var period in RoundPeriods) {
            var round = Round(period);
            if (period == ChallengePeriod.Daily) daily = round; else if (period == ChallengePeriod.Weekly) weekly = round;
            Merge(ref total, Pay(round, record));
        }
        // Diligence: credited the moment a day reaches its task count, because rollover is lazy.
        if (daily != null && weekly != null && daily.format == 2 && !daily.diligenceCredited && daily.tasks.Count > 0) {
            int completed = ChallengeEngine.CompletedCount(daily);
            if (completed > 0 && completed >= Math.Min(CurrentChallengeSettings.diligenceDailyTasks, daily.tasks.Count)) {
                daily.diligenceCredited = true; total.changed = true;
                if (weekly.startUtc <= daily.startUtc && daily.startUtc < weekly.endUtc)
                    Merge(ref total, Pay(weekly, round => ChallengeEngine.RecordDistinct(round, "diligence", daily.dayIndex)));
            }
        }
        if (total.changed || total.xp > 0) { profile.challengeClockUtc = Math.Max(profile.challengeClockUtc, ChallengeNow().Ticks); MarkDirty(); }
        if (total.xp > 0) milestonePending = true;
        if (total.completed > 0 && AddLifetime("tasks-done", total.completed)) total.changed = true;
        return total;
    }
    static ChallengeGain Pay(MetaChallengeRound round, RoundRecorder record)
    {
        if (round == null) return default;
        ChallengeGain gain;
        try { gain = record(round); } catch (Exception error) { Debug.LogException(error); return default; }
        gain.xp = Math.Max(0, gain.xp);
        if (gain.xp > 0 || gain.completed > 0) WritePaidMarkers(round);
        profile.periodRewardXp += gain.xp;
        return gain;
    }

    static bool AddLifetime(string id, long amount)
    {
        if (amount <= 0 || run == null || writeBlocked) return false;
        var counters = profile.lifetimeCounters ??= new List<MetaLifetimeCounter>();
        var counter = counters.Find(value => value != null && value.id == id);
        if (counter == null) {
            if (counters.Count >= MaxLifetimeCounters) return false;
            counters.Add(counter = new MetaLifetimeCounter { id = id });
        }
        long next = Math.Min(MaxLifetimeValue, counter.value + amount);
        if (next == counter.value) return false;
        counter.value = next; MarkDirty();
        CheckMilestoneStages(id);
        return true;
    }
    static HashSet<string> AwardedStages() => awardedStages ??= new HashSet<string>(profile.milestoneAwards.Where(award => award != null).Select(award => award.id));
    static bool StageReached(ChallengeMilestoneStage stage) => stage.legacy ? profile.completedMilestones.Contains(stage.id) : AwardedStages().Contains(stage.id);
    static long LifetimeValue(ChallengeMilestoneLine line)
        => line.counter ? profile.lifetimeCounters?.Find(value => value != null && value.id == line.metric)?.value ?? 0 : LifetimeMetric(line.metric);
    // New milestone stages (legacy stages use CheckChallenges). The award needs a run to hold its "cm:" marker.
    static void CheckMilestoneStages(string counter = null)
    {
        if (run == null || writeBlocked) return;
        profile.milestoneAwards ??= new List<MetaMilestoneAward>();
        var awarded = AwardedStages();
        foreach (var line in ChallengeCatalog.Milestones) {
            if (line?.stages == null || (counter != null && (!line.counter || line.metric != counter))) continue;
            long value = -1;
            foreach (var stage in line.stages) {
                if (stage == null || stage.legacy || awarded.Contains(stage.id) || profile.milestoneAwards.Count >= MaxMilestoneAwards) continue;
                if (value < 0) value = LifetimeValue(line);
                if (value < stage.target) continue;
                int xp = Math.Min(10000000, Math.Max(0, stage.xp));
                profile.milestoneAwards.Add(new MetaMilestoneAward { id = stage.id, xp = xp });
                awarded.Add(stage.id);
                profile.periodRewardXp += xp;
                AddMarker("cm:" + stage.id);
                milestonePending = true; MarkDirty();
            }
        }
    }

    static MetaRunTally RunTally(string id)
    {
        run.tallies ??= new List<MetaRunTally>();
        var tally = run.tallies.Find(value => value != null && value.id == id);
        if (tally == null && run.tallies.Count < MaxRunTallies) run.tallies.Add(tally = new MetaRunTally { id = id });
        return tally;
    }
    // Replay guard: value follows the world save, peak never decreases. Only growth above the peak is credited.
    static long CreditTally(string id, long amount)
    {
        var tally = RunTally(id);
        if (tally == null || amount <= 0) return 0;
        tally.value = Math.Min(MaxTallyValue, tally.value + amount);
        long credit = Math.Max(0, tally.value - tally.peak);
        RaisePeak(tally, tally.value);
        return credit;
    }
    static long SetTally(string id, long value)
    {
        var tally = RunTally(id);
        if (tally == null) return 0;
        value = Math.Min(MaxTallyValue, Math.Max(0, value));
        long credit = Math.Max(0, value - tally.peak);
        if (tally.value != value || credit > 0) MarkDirty();
        tally.value = value; RaisePeak(tally, value);
        return credit;
    }
    // Older builds drop run tallies but keep the ledger, so each peak is mirrored in exactly one "tp:<id>:<peak>" marker
    // (MergeTallies restores it). The old marker was added recently, so it is searched from the end.
    static void RaisePeak(MetaRunTally tally, long peak)
    {
        if (peak <= tally.peak) return;
        string old = TallyMarker(tally.id, tally.peak);
        if (sources.Remove(old)) {
            int index = run.resourceSources.LastIndexOf(old);
            if (index >= 0) run.resourceSources.RemoveAt(index);
        }
        tally.peak = peak;
        string key = TallyMarker(tally.id, peak);
        if (key.Length <= MaxLedgerKey) AddMarker(key);
    }
    static string TallyMarker(string id, long peak) => "tp:" + id + ":" + Inv(peak);
    // Tally ids may contain ':' ("placed:torches-placed"), so the peak follows the last one.
    static bool TryTallyMarker(string key, out string id, out long peak)
    {
        id = null; peak = 0;
        if (key == null || key.Length < 6 || key[0] != 't' || key[1] != 'p' || key[2] != ':') return false;
        int split = key.LastIndexOf(':');
        if (split <= 3 || split == key.Length - 1 || !long.TryParse(key.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out peak)) return false;
        id = key.Substring(3, split - 3);
        peak = Math.Min(MaxTallyValue, peak);
        return true;
    }

    public static void RecordMinedBlock(Vector3Int cell, int layer, bool blasted)
    {
        if (!HasRun() || run.ended) return;
        string id = cell.x + ":" + cell.y, key = "mined:" + id;
        if (sources.Contains(key)) return;
        // Cells counted by the old layer-block challenges stay counted.
        foreach (string legacy in LegacyMinedKeys) if (sources.Contains(legacy + id)) return;
        sources.Add(key); run.resourceSources.Add(key);
        CreditedBlocks++;
        var gain = RecordChallenge("blocks", 1);
        if (layer > 0) Merge(ref gain, RecordChallenge("layer-blocks", 1, layer));
        if (blasted) { Merge(ref gain, RecordChallenge("blasted", 1)); gain.changed |= AddLifetime("blasted", 1); }
        Finish(gain);
    }
    public static void RecordVeinCleared(int item, Vector3Int representative, int size)
    {
        if (!HasRun() || run.ended || item < 0 || size < 2) return;
        string key = "vein:" + item + ":" + representative.x + ":" + representative.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        var gain = RecordChallenge("veins", 1, item);
        gain.changed |= AddLifetime("veins", 1);
        Finish(gain);
    }
    public static bool HasMinedOre(Item item, Vector3Int cell) => HasRun() && sources.Contains((int)item + ":" + cell.x + ":" + cell.y);
    static bool IsPlacedMetric(string metric) => metric == "torches-placed" || metric == "ladders-placed";
    public static void RecordPlacedCount(string metric, int count)
    {
        if (!HasRun() || run.ended || !IsPlacedMetric(metric) || count < 0) return;
        long credit = SetTally("placed:" + metric, count);
        if (credit <= 0) return;
        var gain = RecordChallenge(metric, credit);
        gain.changed |= AddLifetime(metric, credit);
        Finish(gain);
    }
    // Placements made while rewards are off (debug modes) or found at bind raise the peak without credit.
    public static void SyncPlacedCount(string metric, int count)
    {
        if (!HasRun() || !IsPlacedMetric(metric) || count < 0) return;
        SetTally("placed:" + metric, count);
    }
    public static void RecordReturn(int peakDepth, int peakLayer, bool fullBag, int newBlocks)
    {
        if (!HasRun() || run.ended) return;
        var settings = CurrentChallengeSettings;
        if (newBlocks < settings.tripMinNewBlocks) return;
        var gain = default(ChallengeGain);
        if (peakDepth >= settings.returnMinDepth) { Merge(ref gain, RecordChallenge("returns", 1)); gain.changed |= AddLifetime("returns", 1); }
        if (peakLayer >= 2) Merge(ref gain, RecordChallenge("round-trips", 1, Math.Min(peakLayer, 100)));
        if (fullBag) { Merge(ref gain, RecordChallenge("full-returns", 1)); gain.changed |= AddLifetime("full-returns", 1); }
        if (gain.changed || gain.xp > 0) MarkChanged(false);
    }
    // Position metrics: the runtime calls these only during a trip that started at the surface in this session.
    public static void RecordDepthReached(int depth)
    {
        if (!HasRun() || run.ended || depth <= 0) return;
        var gain = RecordChallengeMax("depth", Math.Min(depth, 100000));
        if (gain.changed || gain.xp > 0) MarkChanged(false);
    }
    public static void RecordLayerVisited(int layer)
    {
        if (!HasRun() || run.ended || layer < 1 || layer > 31) return;
        var gain = RecordChallengeDistinct("layers", layer - 1);
        if (gain.changed || gain.xp > 0) MarkChanged(false);
    }
    public static void RecordAltarReached()
    {
        if (!HasRun() || run.ended) return;
        var gain = RecordChallengeMax("altars", 1);
        if (gain.changed || gain.xp > 0) MarkChanged(false);
    }

    public static bool IsCraftTool(Item item) => item == Item.Axe || item == Item.Scythe || item == Item.PercussionHammer ||
        ((int)item >= (int)Item.CopperPickaxe && (int)item <= (int)Item.DiamondPickaxe);
    public static bool IsCraftGear(Item item) => item == Item.Backpack || item == Item.LoadBelt || item == Item.ReinforcedBackpack ||
        item == Item.HeavyDutyBoots || item == Item.SpringGreaves || item == Item.LoadFrame || item == Item.Exoskeleton ||
        item == Item.CrystalPendant || item == Item.CopperEnergyBracelet || item == Item.EnergyStorageVial || item == Item.RuneBelt ||
        item == Item.CrystalHeart || item == Item.CrystalHarness || item == Item.TravelMonolith;
    static bool IsCraftMaterial(Item item) => item == Item.Nails || item == Item.Rope || item == Item.Fabric || item == Item.Steel;
    static bool IsGem(Item item) => item == Item.OrangeGarnet || item == Item.Ruby || item == Item.Emerald || item == Item.Diamond;

    // Craftable amounts per category for the draw: pickaxe ranks, tools, gear, energy ranks, carrying ranks, dynamite, medkits.
    static int[] CraftChallengeLimits()
    {
        if (Isolated) return new[] { 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
        var catalog = Resources.Load<CraftingCatalog>("CraftingCatalog");
        var upgrades = Resources.Load<UpgradeSettings>("UpgradeSettings");
        var inventory = InventoryManager.Instance;
        var limits = new int[7];
        if (!catalog || !upgrades) return limits;
        int pickaxe = inventory ? inventory.EquippedPickaxeLevel : 1;
        int energy = inventory ? inventory.EnergyCapacityLevel : 1;
        int carrying = inventory ? inventory.CarryingCapacityLevel : 1;
        var pickaxeRanks = new HashSet<int>(); var energyRanks = new HashSet<int>(); var carryingRanks = new HashSet<int>();
        foreach (var recipe in catalog.recipes) {
            if (!recipe || !recipe.output || !RecipeUnlocks.IsUnlocked(recipe) || (inventory && inventory.IsPowerupUnlocked(recipe.output))) continue;
            var item = recipe.output;
            // Their recipes need items no world provides (IronPickaxe needs TungstenPickaxe; Obsidian, Mythril).
            if (item.item == Item.IronPickaxe || item.item == Item.ObsidianPickaxe || item.item == Item.TravelMonolith) continue;
            if (item.item == Item.Dynamite) limits[5] = 20;
            if (item.item == Item.Medkit) limits[6] = 10;
            if (IsCraftTool(item.item)) limits[1] += item.category == ItemCategory.Powerup ? 1 : 5;
            if (IsCraftGear(item.item)) limits[2]++;
            for (int i = 0; i < upgrades.PickaxeLevelCount; i++)
                if (upgrades.pickaxeLevels[i]?.pickaxe == item && i + 1 > pickaxe) pickaxeRanks.Add(i + 1);
            if (item.energyCapacityUpgradeLevel > energy && item.energyCapacityUpgradeLevel <= upgrades.EnergyCapacityLevelCount) energyRanks.Add(item.energyCapacityUpgradeLevel);
            if (item.carryingCapacityUpgradeLevel > carrying && item.carryingCapacityUpgradeLevel <= upgrades.CarryingCapacityLevelCount) carryingRanks.Add(item.carryingCapacityUpgradeLevel);
        }
        limits[0] = pickaxeRanks.Count; limits[3] = energyRanks.Count; limits[4] = carryingRanks.Count;
        return limits;
    }

    public static void RecordCraftUpgrade(string metric, int previousLevel, int newLevel)
    {
        int subject = metric switch { "pickaxe-upgrades" => 0, "energy-upgrades" => 1, "carrying-upgrades" => 2, _ => -1 };
        if (!HasRun() || run.ended || newLevel <= previousLevel || previousLevel < 0 || newLevel > 100 || subject < 0) return;
        string key = "craft-upgrade:" + metric + ":" + newLevel;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        Finish(RecordChallenge("upgrades", 1, subject));
    }
    // Crafted amounts carry no source ID, so each metric is guarded by a run tally (craft, reload, craft again).
    public static void RecordCraftedAmount(Item item, int amount, bool exotic = false, int crafts = 0)
    {
        if (!HasRun() || run.ended || amount <= 0) return;
        var gain = default(ChallengeGain);
        if (!IsCraftMaterial(item)) {
            long credit = CreditTally("crafted", amount);
            Merge(ref gain, RecordChallenge("crafted", credit));
            gain.changed |= AddLifetime("crafted", credit);
        }
        if (IsCraftTool(item)) Merge(ref gain, RecordChallenge("tools", CreditTally("tools", amount)));
        if (IsCraftGear(item) && sources.Add("gear-crafted:" + (int)item)) {
            run.resourceSources.Add("gear-crafted:" + (int)item);
            Merge(ref gain, RecordChallenge("gear", 1));
        }
        string metric = item switch { Item.Dynamite => "dynamite", Item.Medkit => "medkits", Item.Torche => "torches-crafted",
            Item.Ladder or Item.IronLadder => "ladders-crafted", _ => null };
        if (metric != null) Merge(ref gain, RecordChallenge(metric, CreditTally(metric, amount)));
        // Exotic batches, not output items: one IronLadder craft yields two ladders.
        if (exotic) Merge(ref gain, RecordChallenge("exotics", CreditTally("exotics", crafts > 0 ? crafts : amount)));
        Finish(gain);
    }
    public static void RecordSale(int proceeds)
    {
        if (!HasRun() || run.ended || proceeds <= 0) return;
        // Selling a restored inventory again after loading an older world save earns nothing.
        long credit = CreditTally("sales", proceeds);
        if (credit <= 0) { MarkDirty(); return; }
        var gain = RecordChallenge("sales", credit);
        Merge(ref gain, RecordChallengeRun("run-sales", run.runId, credit));
        Merge(ref gain, RecordChallengeMax("sale-single", credit));
        gain.changed |= AddLifetime("sales", credit);
        Finish(gain);
    }
    public static void RecordSurfaceHarvest(string metric, string sourceId)
    {
        if (!HasRun() || run.ended || (metric != "trees" && metric != "grass") ||
            string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128) return;
        string key = "harvest:" + metric + ":" + sourceId;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        var gain = RecordChallenge(metric, 1);
        if (metric == "trees") gain.changed |= AddLifetime("trees", 1);
        Finish(gain);
    }
    static int ArtifactType(string artifactId) => !string.IsNullOrEmpty(artifactId) && artifactId.StartsWith("Artifact_", StringComparison.Ordinal) &&
        int.TryParse(artifactId.Substring(9), out int type) && type >= 1 && type <= 10 ? type : 0;
    public static void RecordArtifact(Vector3Int cell, string artifactId = null)
    {
        if (!HasRun() || run.ended) return;
        string key = "artifact-mined:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        RecordArtifactSight(cell, artifactId);
        var gain = RecordChallenge("artifacts", 1);
        int type = ArtifactType(artifactId);
        if (type > 0) { Merge(ref gain, RecordChallenge("artifact", 1, type)); Merge(ref gain, RecordChallengeDistinct("artifact-variety", type - 1)); }
        gain.changed |= AddLifetime("artifacts", 1);
        Finish(gain);
    }
    public static void RecordArtifactSight(Vector3Int cell, string artifactId, bool award = true)
    {
        if (!HasRun() || run.ended || string.IsNullOrEmpty(artifactId)) return;
        string key = "artifact-sight:" + cell.x + ":" + cell.y;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        int type = ArtifactType(artifactId);
        Finish(award && type > 0 ? RecordChallengeDistinct("artifact-sighted", type - 1) : default);
    }
    public static void RecordRevealedCell(int x, int depth, bool award = true)
    {
        if (!HasRun() || run.ended || depth <= 0 || depth > 100000 || x < 0 || x > 100000) return;
        string key = "revealed:" + x + ":" + depth;
        if (!sources.Add(key)) return;
        run.resourceSources.Add(key);
        Finish(award ? RecordChallenge("cells", 1) : default);
    }
    // Layer visits and trips are recorded by RecordLayerVisited and RecordReturn; kept for existing callers.
    public static void RecordExplorationPosition(int layer, bool atSurface) { }
    public static void RecordReachedMiningLayer(int layer)
    {
        if (!HasRun() || run.ended || layer < 1 || layer > 100 || layer <= profile.reachedMiningLayer) return;
        profile.reachedMiningLayer = layer;
        MarkChanged(false);
    }

    static bool IsRoundPeriod(ChallengePeriod period) => period == ChallengePeriod.Daily || period == ChallengePeriod.Weekly || period == ChallengePeriod.Monthly;
    public static TimeSpan ChallengeRemaining(ChallengePeriod period)
        => !IsRoundPeriod(period) ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, Round(period).endUtc - ChallengeNow().Ticks));
    public static long ChallengePeriodEnd(ChallengePeriod period) => IsRoundPeriod(period) ? Round(period).endUtc : 0;

    public static MetaChallengeProgress[] GetChallenges(ChallengePeriod period)
    {
        EnsureLoaded();
        if (period == ChallengePeriod.Lifetime) return LifetimeChallenges();
        if (!IsRoundPeriod(period)) return Array.Empty<MetaChallengeProgress>();
        var round = Round(period);
        var result = new List<MetaChallengeProgress>(); var ids = new HashSet<string>();
        for (int i = 0; i < round.tasks.Count; i++) {
            var task = round.tasks[i];
            if (task == null || IsProject(task)) continue;
            // Converted legacy rounds can hold the same template twice.
            string id = period + "-" + task.id + (task.subject >= 0 ? "-" + task.subject : "");
            if (!ids.Add(id)) ids.Add(id += "-" + i);
            result.Add(TaskView(task, id, 0));
        }
        return result.ToArray();
    }
    public static MetaChallengeProgress GetMonthlyProject()
    {
        EnsureLoaded();
        var task = Round(ChallengePeriod.Monthly).tasks.Find(value => value != null && IsProject(value));
        if (task?.stageTargets == null || task.stageXp == null || task.stageTargets.Length == 0 || task.stageXp.Length != task.stageTargets.Length) return null;
        var view = TaskView(task, "Monthly-" + task.id, 1);
        int last = task.stageTargets.Length - 1;
        view.target = task.stageTargets[last];
        view.xp = task.stageXp[Math.Min(Math.Max(0, task.stagesPaid), last)];
        view.stageTargets = (int[])task.stageTargets.Clone(); view.stageXp = (int[])task.stageXp.Clone();
        view.stagesDone = task.stagesPaid;
        return view;
    }
    static MetaChallengeProgress TaskView(MetaChallengeTask task, string id, int kind)
    {
        var template = ChallengeCatalog.Template(task.id);
        string unit = string.IsNullOrEmpty(task.unit) ? ChallengeCatalog.Unit(task.id, task.metric, task.subject) : task.unit;
        return new MetaChallengeProgress {
            id = id, name = string.IsNullOrEmpty(task.name) ? ChallengeCatalog.Name(task.id, task.subject) : task.name, metric = task.metric,
            subject = unit, unit = unit, icon = ChallengeCatalog.Icon(task.id, task.metric, task.subject),
            family = template != null ? ChallengeCatalog.FamilyName(template.family) : "", tier = task.tier, kind = kind,
            current = task.progress, target = task.target, xp = task.xp, completed = task.done
        };
    }
    static MetaChallengeProgress[] LifetimeChallenges()
    {
        var result = new List<MetaChallengeProgress>();
        foreach (var line in ChallengeCatalog.Milestones) {
            var stages = line?.stages?.Where(stage => stage != null).ToArray();
            if (stages == null || stages.Length == 0) continue;
            int reached = stages.Count(StageReached);
            var shown = stages.FirstOrDefault(stage => !StageReached(stage)) ?? stages[stages.Length - 1];
            result.Add(new MetaChallengeProgress {
                id = line.id, name = line.name, metric = line.metric, subject = line.unit, unit = line.unit, icon = line.icon, kind = 2, permanent = true,
                current = (int)Math.Min(int.MaxValue, Math.Max(0, LifetimeValue(line))), target = shown.target, xp = shown.xp, completed = reached == stages.Length,
                medal = reached, stage = reached, stages = stages.Length, stagesDone = reached,
                stageTargets = stages.Select(stage => stage.target).ToArray(), stageXp = stages.Select(stage => stage.xp).ToArray()
            });
        }
        if (run != null) foreach (var definition in MetaProgressionCatalog.Challenges) {
            if (definition.permanent || !run.objectiveIds.Contains(definition.id)) continue;
            bool completed = run.completedObjectives.Contains(definition.id);
            string unit = ChallengeCatalog.Unit(null, definition.metric, -1);
            result.Add(new MetaChallengeProgress {
                id = definition.id, name = definition.name, metric = definition.metric, subject = unit, unit = unit,
                icon = ChallengeCatalog.Icon(null, definition.metric, -1), kind = 3,
                current = Math.Min(definition.target, completed ? definition.target : Metric(definition.metric, run)),
                target = definition.target, xp = definition.xp, completed = completed
            });
        }
        return result.ToArray();
    }
    public static MetaChallengeBonus ChallengeBonus(ChallengePeriod period)
    {
        EnsureLoaded();
        if (!IsRoundPeriod(period)) {
            if (period != ChallengePeriod.Lifetime) return new MetaChallengeBonus();
            int done = 0, required = 0;
            foreach (var line in ChallengeCatalog.Milestones)
                if (line?.stages != null) foreach (var stage in line.stages) if (stage != null) { required++; if (StageReached(stage)) done++; }
            return new MetaChallengeBonus { done = done, required = required };
        }
        var round = Round(period);
        return new MetaChallengeBonus { done = ChallengeEngine.CompletedCount(round), required = round.tasks.Count, xp = round.bonusXp, awarded = round.bonusPaid };
    }
}
