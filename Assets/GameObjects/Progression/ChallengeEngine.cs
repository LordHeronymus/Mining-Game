using System;
using System.Collections.Generic;

[Serializable] public sealed class MetaChallengeTask
{
    public string id, name, metric, unit;
    public int mode;
    public int subject = -1;
    public bool atLeast;
    public int tier, target, xp, progress;
    public bool done;
    public int paid;
    public int[] stageTargets, stageXp;
    public int stagesPaid;
}

[Serializable] public sealed class MetaChallengeMask { public string metric; public int mask; }
[Serializable] public sealed class MetaChallengeRunValue { public string metric, runId; public long value; }

[Serializable] public sealed class MetaChallengeRound
{
    public int period;
    public int format = 2;
    public long startUtc, endUtc;
    public int dayIndex;
    public List<MetaChallengeTask> tasks = new();
    public int bonusXp;
    public bool bonusPaid;
    public List<MetaChallengeMask> masks = new();
    public List<MetaChallengeRunValue> runValues = new();
    public bool diligenceCredited;
}

public struct ChallengeGain { public int xp, completed; public bool changed; }

public sealed class ChallengeDrawContext
{
    public int reachedLayer = 1, level = 1;
    public int[] craftLimits;
    public float pickaxeHardness;
}

// Pure and static: rounds are drawn from a seeded System.Random, so the same period always yields the same tasks.
// Nothing here throws; corrupt input is ignored (recorders) or reported (Sanitize).
public static class ChallengeEngine
{
    const int MaxEntries = 16, MaxTasks = 16, MaxLegacyTasks = 24, Attempts = 60, AvoidAttempts = 45;
    const int XxlSalt = 0x58584C, ProjectSalt = 0x50524A, TaskSalt = 0x5441534B;
    static readonly long EndLimit = new DateTime(9000, 2, 1).Ticks;
    const long MaxLength = 33 * TimeSpan.TicksPerDay;
    // Family caps per period index (Mining, Exploration, Crafting, Trade, Artifacts, Special); Mining, Exploration and Crafting need one task each.
    static readonly int[][] FamilyCaps = { new[] { 3, 2, 2, 1, 1, 0 }, new[] { 3, 3, 2, 1, 2, 0 }, null, new[] { 2, 2, 2, 1, 1, 0 } };
    const int MinimumFamilies = 3;

    public static void Bounds(ChallengePeriod period, DateTime utcNow, TimeZoneInfo zone, out long startUtc, out long endUtc, out int dayIndex)
    {
        startUtc = 0; endUtc = long.MaxValue; dayIndex = 0;
        if (period != ChallengePeriod.Daily && period != ChallengePeriod.Weekly && period != ChallengePeriod.Monthly) return;
        long now = utcNow.Ticks;
        try {
            if (utcNow.Kind == DateTimeKind.Local) now = utcNow.ToUniversalTime().Ticks;
            zone ??= TimeZoneInfo.Utc;
            var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(now, DateTimeKind.Utc), zone);
            Wall(period, local, out var start, out var end, out int day);
            long s = ToUtc(start, zone), e = ToUtc(end, zone);
            if (s >= 0 && s <= now && now < e) { startUtc = s; endUtc = e; dayIndex = day; return; }
        } catch (Exception) { }
        try {
            Wall(period, new DateTime(now, DateTimeKind.Unspecified), out var start, out var end, out dayIndex);
            startUtc = start.Ticks; endUtc = end.Ticks;
        } catch (Exception) {
            startUtc = now - now % TimeSpan.TicksPerDay; endUtc = startUtc + TimeSpan.TicksPerDay;
            dayIndex = (int)(startUtc / TimeSpan.TicksPerDay % 7); // 0001-01-01 was a Monday
        }
    }

    static void Wall(ChallengePeriod period, DateTime local, out DateTime start, out DateTime end, out int dayIndex)
    {
        var date = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        if (period == ChallengePeriod.Weekly) date = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        else if (period == ChallengePeriod.Monthly) date = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        start = date;
        end = period == ChallengePeriod.Daily ? date.AddDays(1) : period == ChallengePeriod.Weekly ? date.AddDays(7) : date.AddMonths(1);
        dayIndex = ((int)date.DayOfWeek + 6) % 7;
    }

    // Wall clock -> UTC: a skipped time moves forward in 30-minute steps, an ambiguous time takes its earliest instant.
    static long ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        for (int step = 0; step < 8 && zone.IsInvalidTime(wall); step++) wall = wall.AddMinutes(30);
        if (zone.IsInvalidTime(wall)) return (wall - zone.BaseUtcOffset).Ticks;
        if (zone.IsAmbiguousTime(wall)) {
            var offset = TimeSpan.MinValue;
            foreach (var candidate in zone.GetAmbiguousTimeOffsets(wall)) if (candidate > offset) offset = candidate;
            return (wall - offset).Ticks;
        }
        return (wall - zone.GetUtcOffset(wall)).Ticks;
    }

    static int Seed(long startUtc, ChallengePeriod period, int salt) => unchecked((int)(startUtc ^ (startUtc >> 32)) ^ ((int)period * 0x2F6B3A1) ^ salt);

    public static MetaChallengeRound Draw(ChallengePeriod period, long startUtc, long endUtc, int dayIndex,
        ChallengeDrawContext context, ChallengeSettings settings, MetaChallengeRound previous)
    {
        var round = new MetaChallengeRound { period = (int)period, startUtc = startUtc, endUtc = endUtc, dayIndex = dayIndex };
        try {
            settings = (settings ?? new ChallengeSettings()).Sanitized();
            var periodSettings = settings.Period(period);
            if (periodSettings == null) return round;
            context ??= new ChallengeDrawContext();
            var clean = new ChallengeDrawContext { reachedLayer = ChallengeCatalog.Layer(context.reachedLayer), level = Math.Max(1, context.level),
                craftLimits = context.craftLimits, pickaxeHardness = float.IsNaN(context.pickaxeHardness) ? 0 : context.pickaxeHardness };
            Fill(round, period, periodSettings, clean, settings, previous);
            round.bonusXp = round.tasks.Count > 0 ? periodSettings.completionBonusXp : 0;
        } catch (Exception) { round.tasks.Clear(); round.bonusXp = 0; }
        return round;
    }

    static void Fill(MetaChallengeRound round, ChallengePeriod period, ChallengePeriodSettings periodSettings,
        ChallengeDrawContext context, ChallengeSettings settings, MetaChallengeRound previous)
    {
        var previousActivities = new HashSet<string>();
        var previousSubjects = new Dictionary<string, HashSet<int>>();
        string previousProject = null;
        if (previous?.tasks != null) foreach (var task in previous.tasks) {
            if (task == null || string.IsNullOrEmpty(task.id)) continue;
            var template = ChallengeCatalog.Template(task.id);
            if (template != null) previousActivities.Add(template.activity);
            if (template != null && template.project) previousProject = task.id;
            if (!previousSubjects.TryGetValue(task.id, out var subjects)) previousSubjects[task.id] = subjects = new HashSet<int>();
            subjects.Add(task.subject);
        }
        var used = new HashSet<string>();
        var tasks = round.tasks;
        if (period == ChallengePeriod.Monthly) {
            var project = DrawProject(round.startUtc, context, settings, previousProject);
            if (project != null) {
                used.Add(project.activity);
                var stages = ChallengeCatalog.ProjectStages(project, context.reachedLayer, context.level, settings);
                tasks.Add(new MetaChallengeTask { id = project.id, name = project.name, unit = project.unit, metric = project.metric, mode = (int)project.mode,
                    tier = (int)ChallengeTier.Project, target = stages[stages.Length - 1], stageTargets = stages,
                    stageXp = new[] { settings.ProjectStageXp(0), settings.ProjectStageXp(1), settings.ProjectStageXp(2) } });
            }
        }
        int easy = periodSettings.countEasy, medium = periodSettings.countMedium, hard = periodSettings.countHard;
        int diligenceTasks = ChallengeCatalog.DiligenceTasks(settings);
        if (period == ChallengePeriod.Weekly && settings.diligenceDays > 0 && medium > 0 && diligenceTasks > 0) {
            var diligence = ChallengeCatalog.Template("diligence");
            medium--; used.Add(diligence.activity);
            tasks.Add(new MetaChallengeTask { id = diligence.id, name = diligence.name, unit = ChallengeCatalog.DiligenceUnit(diligenceTasks),
                metric = diligence.metric, mode = (int)diligence.mode, tier = (int)ChallengeTier.Medium, target = settings.diligenceDays,
                xp = periodSettings.Xp((int)ChallengeTier.Medium) });
        }
        var lists = new List<ChallengeCandidate>[4];
        for (int tier = 0; tier < 4; tier++) {
            lists[tier] = new List<ChallengeCandidate>();
            ChallengeCatalog.Candidates(period, tier, context, settings, lists[tier]);
            lists[tier].RemoveAll(candidate => used.Contains(candidate.template.activity));
        }
        double roll = new Random(Seed(round.startUtc, period, XxlSalt)).NextDouble() * 100;
        bool xxl = roll < periodSettings.xxlChancePercent && context.reachedLayer >= settings.xxlMinLayer && (hard > 0 || medium > 0) && lists[3].Count > 0;
        if (xxl) { if (hard > 0) hard--; else medium--; }
        var slots = new List<int>();
        if (xxl) slots.Add((int)ChallengeTier.Xxl);
        for (int i = 0; i < hard; i++) slots.Add((int)ChallengeTier.Hard);
        for (int i = 0; i < medium; i++) slots.Add((int)ChallengeTier.Medium);
        for (int i = 0; i < easy; i++) slots.Add((int)ChallengeTier.Easy);
        int room = Math.Max(0, MaxTasks - tasks.Count);
        if (slots.Count > room) slots.RemoveRange(room, slots.Count - room);
        var caps = FamilyCaps[(int)period];
        var random = new Random(Seed(round.startUtc, period, TaskSalt));
        List<ChallengeCandidate> picks = null;
        for (int attempt = 0; attempt < Attempts && picks == null; attempt++)
            picks = Attempt(slots, lists, caps, used, previousActivities, previousSubjects, random, attempt < AvoidAttempts, true);
        picks ??= Attempt(slots, lists, caps, used, previousActivities, previousSubjects, random, false, false);
        for (int i = 0; i < picks.Count; i++) {
            var pick = picks[i];
            if (pick.template == null) continue;
            int tier = slots[i];
            tasks.Add(new MetaChallengeTask { id = pick.template.id, name = ChallengeCatalog.Name(pick.template.id, pick.subject),
                unit = ChallengeCatalog.TemplateUnit(pick.template, pick.subject, diligenceTasks), metric = pick.template.metric,
                mode = (int)pick.template.mode, subject = pick.subject, atLeast = pick.template.atLeast, tier = tier, target = pick.target,
                xp = periodSettings.Xp(tier) });
        }
        tasks.Sort(Order);
    }

    static int Order(MetaChallengeTask a, MetaChallengeTask b)
    {
        bool projectA = a.tier == (int)ChallengeTier.Project, projectB = b.tier == (int)ChallengeTier.Project;
        if (projectA != projectB) return projectA ? -1 : 1;
        int result = a.tier.CompareTo(b.tier);
        if (result == 0) result = Family(a).CompareTo(Family(b));
        if (result == 0) result = string.CompareOrdinal(a.id, b.id);
        return result != 0 ? result : a.subject.CompareTo(b.subject);
    }
    static int Family(MetaChallengeTask task) => (int)(ChallengeCatalog.Template(task.id)?.family ?? ChallengeFamily.Special);

    static ChallengeTemplate DrawProject(long startUtc, ChallengeDrawContext context, ChallengeSettings settings, string previousProject)
    {
        var candidates = new List<ChallengeTemplate>();
        foreach (var template in ChallengeCatalog.Templates)
            if (template.project && !template.retired && template.weight > 0 && context.reachedLayer >= template.minLayer && context.level >= template.minLevel
                && ChallengeCatalog.ProjectStages(template, context.reachedLayer, context.level, settings) != null) candidates.Add(template);
        if (candidates.Count > 1) candidates.RemoveAll(template => template.id == previousProject);
        int total = 0;
        foreach (var template in candidates) total += template.weight;
        if (total <= 0) return null;
        int roll = new Random(Seed(startUtc, ChallengePeriod.Monthly, ProjectSalt)).Next(total);
        foreach (var template in candidates) if ((roll -= template.weight) < 0) return template;
        return null;
    }

    // One fill of all slots (XXL/Hard, Medium, Easy). Returns null when a slot stays empty or a minimum is missed,
    // except for the fallback (partial), which leaves unfillable slots empty (template == null).
    static List<ChallengeCandidate> Attempt(List<int> slots, List<ChallengeCandidate>[] lists, int[] caps, HashSet<string> taken,
        HashSet<string> previousActivities, Dictionary<string, HashSet<int>> previousSubjects, Random random, bool avoid, bool minimums)
    {
        var counts = new int[6];
        var used = new HashSet<string>(taken);
        var picks = new List<ChallengeCandidate>(slots.Count);
        var pool = new List<ChallengeCandidate>();
        for (int slot = 0; slot < slots.Count; slot++) {
            int missing = 0;
            if (minimums) for (int family = 0; family < MinimumFamilies; family++) if (counts[family] == 0) missing++;
            bool restrict = missing > 0 && slots.Count - slot <= missing;
            pool.Clear();
            foreach (var candidate in lists[slots[slot]]) {
                int family = (int)candidate.template.family;
                if (counts[family] >= caps[family] || used.Contains(candidate.template.activity)) continue;
                if (avoid && previousActivities.Contains(candidate.template.activity)) continue;
                if (restrict && (family >= MinimumFamilies || counts[family] > 0)) continue;
                pool.Add(candidate);
            }
            if (pool.Count == 0) {
                if (minimums) return null;
                picks.Add(default);
                continue;
            }
            var pick = Pick(pool, random, previousSubjects);
            counts[(int)pick.template.family]++;
            used.Add(pick.template.activity);
            picks.Add(pick);
        }
        if (minimums) for (int family = 0; family < MinimumFamilies; family++) if (counts[family] == 0) return null;
        return picks;
    }

    // Weighted template first (catalog weight), then a weighted subject that differs from the previous round when possible.
    static ChallengeCandidate Pick(List<ChallengeCandidate> pool, Random random, Dictionary<string, HashSet<int>> previousSubjects)
    {
        int total = 0;
        ChallengeTemplate last = null;
        foreach (var candidate in pool) if (candidate.template != last) { last = candidate.template; total += last.weight; }
        int roll = random.Next(total);
        ChallengeTemplate chosen = null;
        last = null;
        foreach (var candidate in pool) {
            if (candidate.template == last) continue;
            last = candidate.template;
            if ((roll -= last.weight) < 0) { chosen = last; break; }
        }
        chosen ??= pool[pool.Count - 1].template;
        previousSubjects.TryGetValue(chosen.id, out var avoided);
        bool fresh = false;
        foreach (var candidate in pool)
            if (candidate.template == chosen && (avoided == null || !avoided.Contains(candidate.subject))) { fresh = true; break; }
        int weights = 0;
        foreach (var candidate in pool) if (Allowed(candidate, chosen, avoided, fresh)) weights += Math.Max(1, candidate.weight);
        int subjectRoll = random.Next(weights);
        foreach (var candidate in pool)
            if (Allowed(candidate, chosen, avoided, fresh) && (subjectRoll -= Math.Max(1, candidate.weight)) < 0) return candidate;
        return pool[pool.Count - 1];
    }
    static bool Allowed(ChallengeCandidate candidate, ChallengeTemplate chosen, HashSet<int> avoided, bool fresh)
        => candidate.template == chosen && (!fresh || avoided == null || !avoided.Contains(candidate.subject));

    // ---- Recording

    static bool SubjectMatches(MetaChallengeTask task, int subject) => task.subject < 0 || (task.atLeast ? subject >= task.subject : subject == task.subject);
    static bool Matches(MetaChallengeTask task, string metric, ChallengeMode mode, int subject)
        => task != null && task.mode == (int)mode && task.metric == metric && SubjectMatches(task, subject);
    static bool HasTask(MetaChallengeRound round, string metric, ChallengeMode mode)
    {
        foreach (var task in round.tasks) if (task != null && task.mode == (int)mode && task.metric == metric) return true;
        return false;
    }

    public static ChallengeGain Record(MetaChallengeRound round, string metric, long amount, int subject = -1)
    {
        var gain = new ChallengeGain();
        if (round?.tasks == null || string.IsNullOrEmpty(metric) || amount <= 0) return gain;
        try {
            foreach (var task in round.tasks) {
                if (!Matches(task, metric, ChallengeMode.Add, subject) || task.done || task.progress >= task.target) continue;
                task.progress = amount >= (long)task.target - Math.Max(0, task.progress) ? task.target : Math.Max(0, task.progress) + (int)amount;
                gain.changed = true;
            }
            Pay(round, ref gain);
        } catch (Exception) { }
        return gain;
    }

    public static ChallengeGain RecordMax(MetaChallengeRound round, string metric, long value, int subject = -1)
    {
        var gain = new ChallengeGain();
        if (round?.tasks == null || string.IsNullOrEmpty(metric) || value <= 0) return gain;
        try {
            foreach (var task in round.tasks) {
                if (!Matches(task, metric, ChallengeMode.Max, subject)) continue;
                long next = Math.Min(task.target, value);
                if (next > task.progress) { task.progress = (int)next; gain.changed = true; }
            }
            Pay(round, ref gain);
        } catch (Exception) { }
        return gain;
    }

    public static ChallengeGain RecordDistinct(MetaChallengeRound round, string metric, int bit, int subject = -1)
    {
        var gain = new ChallengeGain();
        if (round?.tasks == null || string.IsNullOrEmpty(metric) || bit < 0 || bit > 30) return gain;
        try {
            if (!HasTask(round, metric, ChallengeMode.Distinct)) return gain;
            round.masks ??= new List<MetaChallengeMask>();
            MetaChallengeMask entry = null;
            foreach (var mask in round.masks) if (mask != null && mask.metric == metric) { entry = mask; break; }
            if (entry == null) {
                if (round.masks.Count >= MaxEntries || metric.Length > 64) return gain;
                round.masks.Add(entry = new MetaChallengeMask { metric = metric });
            }
            int flag = 1 << bit;
            if ((entry.mask & flag) != 0) return gain;
            entry.mask |= flag;
            gain.changed = true;
            foreach (var task in round.tasks)
                if (Matches(task, metric, ChallengeMode.Distinct, subject) && !task.done && task.progress < task.target) task.progress = Math.Max(0, task.progress) + 1;
            Pay(round, ref gain);
        } catch (Exception) { }
        return gain;
    }

    public static ChallengeGain RecordRun(MetaChallengeRound round, string metric, string runId, long amount, int subject = -1)
    {
        var gain = new ChallengeGain();
        if (round?.tasks == null || string.IsNullOrEmpty(metric) || amount <= 0) return gain;
        try {
            if (!HasTask(round, metric, ChallengeMode.Run) || metric.Length > 64) return gain;
            runId ??= "";
            if (runId.Length > 160) runId = runId.Substring(0, 160);
            round.runValues ??= new List<MetaChallengeRunValue>();
            var values = round.runValues;
            MetaChallengeRunValue entry = null;
            foreach (var value in values) if (value != null && value.metric == metric && value.runId == runId) { entry = value; break; }
            if (entry == null) {
                if (values.Count >= MaxEntries) { int own = Smallest(values, metric); values.RemoveAt(own >= 0 ? own : Smallest(values, null)); }
                values.Add(entry = new MetaChallengeRunValue { metric = metric, runId = runId });
            }
            entry.value = amount > long.MaxValue - Math.Max(0, entry.value) ? long.MaxValue : Math.Max(0, entry.value) + amount;
            gain.changed = true;
            long best = 0;
            foreach (var value in values) if (value != null && value.metric == metric && value.value > best) best = value.value;
            foreach (var task in round.tasks) {
                if (!Matches(task, metric, ChallengeMode.Run, subject)) continue;
                long next = Math.Min(task.target, best);
                if (next > task.progress) task.progress = (int)next;
            }
            Pay(round, ref gain);
        } catch (Exception) { }
        return gain;
    }

    // Index of the smallest run value of a metric (null = of any metric; null entries count as smallest), -1 if none.
    static int Smallest(List<MetaChallengeRunValue> values, string metric)
    {
        int result = -1;
        for (int i = 0; i < values.Count; i++) {
            if (metric != null && values[i]?.metric != metric) continue;
            if (result < 0 || (values[i]?.value ?? long.MinValue) < (values[result]?.value ?? long.MinValue)) result = i;
        }
        return result;
    }

    static bool IsProject(MetaChallengeTask task) => task.tier == (int)ChallengeTier.Project && task.stageTargets != null && task.stageTargets.Length > 0;

    // Pays every task that reached its target (projects stage by stage) and the bonus once all tasks of a non-empty round are done.
    static void Pay(MetaChallengeRound round, ref ChallengeGain gain)
    {
        bool all = round.tasks.Count > 0;
        foreach (var task in round.tasks) {
            if (task == null) { all = false; continue; }
            if (IsProject(task)) {
                while (!task.done && task.stagesPaid >= 0 && task.stagesPaid < task.stageTargets.Length && task.progress >= task.stageTargets[task.stagesPaid]) {
                    int xp = task.stageXp != null && task.stagesPaid < task.stageXp.Length ? Math.Max(0, task.stageXp[task.stagesPaid]) : 0;
                    task.paid += xp; gain.xp += xp; task.stagesPaid++;
                }
                if (!task.done && task.stagesPaid >= task.stageTargets.Length) { task.done = true; gain.completed++; }
            } else if (!task.done && task.progress >= task.target) {
                task.done = true; task.paid = Math.Max(0, task.xp); gain.xp += task.paid; gain.completed++;
            }
            if (!task.done) all = false;
        }
        if (all && round.bonusXp > 0 && !round.bonusPaid) { round.bonusPaid = true; gain.xp += round.bonusXp; }
    }

    public static int CompletedCount(MetaChallengeRound round)
    {
        int count = 0;
        if (round?.tasks != null) foreach (var task in round.tasks) if (task != null && task.done) count++;
        return count;
    }

    public static int PaidXp(MetaChallengeRound round)
    {
        if (round?.tasks == null) return 0;
        long total = round.bonusPaid ? Math.Max(0, round.bonusXp) : 0;
        foreach (var task in round.tasks) if (task != null) total += Math.Max(0, task.paid);
        return (int)Math.Min(int.MaxValue, total);
    }

    // Structural check of a stored round (false = caller drops it). Only missing lists are repaired.
    public static bool Sanitize(MetaChallengeRound round)
    {
        try {
            if (round == null) return false;
            if (round.period != (int)ChallengePeriod.Daily && round.period != (int)ChallengePeriod.Weekly && round.period != (int)ChallengePeriod.Monthly) return false;
            if (round.format != 1 && round.format != 2) return false;
            if (round.startUtc < 0 || round.startUtc >= round.endUtc || round.endUtc - round.startUtc > MaxLength || round.endUtc > EndLimit) return false;
            round.tasks ??= new List<MetaChallengeTask>();
            round.masks ??= new List<MetaChallengeMask>();
            round.runValues ??= new List<MetaChallengeRunValue>();
            if (round.tasks.Count > (round.format == 1 ? MaxLegacyTasks : MaxTasks)) return false;
            bool all = true;
            foreach (var task in round.tasks) {
                if (!ValidTask(task)) return false;
                all &= task.done;
            }
            if (round.masks.Count > MaxEntries || round.runValues.Count > MaxEntries) return false;
            foreach (var mask in round.masks) if (mask == null || Length(mask.metric) > 64) return false;
            foreach (var value in round.runValues) if (value == null || Length(value.metric) > 64 || Length(value.runId) > 160 || value.value < 0) return false;
            if (round.bonusXp < 0 || round.bonusXp > 10000000) return false;
            return !round.bonusPaid || round.tasks.Count > 0 && all;
        } catch (Exception) { return false; }
    }

    static int Length(string value) => value?.Length ?? 0;

    static bool ValidTask(MetaChallengeTask task)
    {
        if (task == null || string.IsNullOrEmpty(task.id) || task.id.Length > 64 || string.IsNullOrEmpty(task.metric) || task.metric.Length > 64) return false;
        if (Length(task.name) > 80 || Length(task.unit) > 80 || task.mode < 0 || task.mode > 3 || task.tier < 0 || task.tier > 4) return false;
        if (task.subject < -1 || task.subject > 10000 || task.target < 1 || task.target > 1000000000 || task.xp < 0 || task.xp > 10000000) return false;
        if (task.progress < 0 || task.progress > task.target) return false;
        if (task.tier != (int)ChallengeTier.Project) return task.done == task.progress >= task.target && task.paid == (task.done ? task.xp : 0);
        var targets = task.stageTargets; var xp = task.stageXp;
        if (targets == null || xp == null || targets.Length < 1 || targets.Length > 5 || xp.Length != targets.Length || task.xp != 0) return false;
        if (targets[targets.Length - 1] != task.target) return false;
        int reached = 0; long paid = 0;
        for (int i = 0; i < targets.Length; i++) {
            if (targets[i] <= 0 || i > 0 && targets[i] <= targets[i - 1] || xp[i] < 0 || xp[i] > 10000000) return false;
            if (targets[i] <= task.progress) { reached++; paid += xp[i]; }
        }
        return task.stagesPaid == reached && task.paid == paid && task.done == (reached == targets.Length);
    }
}
