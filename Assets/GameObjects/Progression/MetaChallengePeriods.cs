using System;
using System.Collections.Generic;
using System.Linq;

public enum ChallengePeriod { Daily, Weekly, Lifetime }

[Serializable] public sealed class MetaChallengePeriodState
{
    public int period;
    public long startUtc, endUtc;
    public int[] counters = new int[16];
    public bool[] rewarded = new bool[16];
}

public static partial class MetaProgression
{
    // Override is deliberately restricted to isolated profiles.
    public static DateTime? ChallengeTestUtc { get; set; }
    // Append only: persisted counter positions are stable. Distinct IDs also allow tiered goals.
    static readonly string[] PeriodIds = { "resources", "depth", "crafted", "copper", "sales", "torches",
        "coal", "iron", "silver", "gold", "regions", "discoveries", "exotics", "ladders", "large-haul", "production" };
    static readonly string[] PeriodMetrics = { "resources", "depth", "crafted", "copper", "sales", "torches",
        "coal", "iron", "silver", "gold", "regions", "discoveries", "exotics", "ladders", "resources", "crafted" };
    static readonly string[] DailyNames = { "Erzsammler", "Tiefer graben", "Handwerker", "Kupferfund", "Händler", "Licht im Dunkeln",
        "Kohleschicht", "Eiserner Vorrat", "Silberglanz", "Goldener Fund", "Neue Pfade", "Spurensuche", "Exotisches Handwerk", "Hoch hinaus", "Große Ausbeute", "Fleißige Werkstatt" };
    static readonly string[] WeeklyNames = { "Erzsammler", "Tiefer graben", "Handwerker", "Kupferfund", "Händler", "Licht im Dunkeln",
        "Kohlewoche", "Eisenlieferant", "Silbervorrat", "Goldrausch", "Kartograf", "Entdeckungsreise", "Exotik-Werkstatt", "Leiternetz", "Abbauwoche", "Produktionswoche" };
    static readonly int[] DailyTargets = { 50, 100, 5, 20, 1000, 3, 30, 20, 8, 5, 6, 1, 1, 5, 250, 15 };
    static readonly int[] WeeklyTargets = { 500, 750, 25, 150, 10000, 20, 300, 200, 80, 50, 50, 5, 3, 30, 2500, 100 };
    static readonly int[] DailyRewards = { 150, 200, 150, 100, 200, 100, 100, 150, 180, 200, 180, 200, 300, 120, 350, 250 };
    static readonly int[] WeeklyRewards = { 600, 1000, 800, 500, 1000, 500, 400, 600, 750, 900, 800, 1000, 1400, 500, 2000, 1600 };

    static DateTime ChallengeNow()
    {
        DateTime now = !string.IsNullOrWhiteSpace(TestDirectory) && ChallengeTestUtc.HasValue
            ? DateTime.SpecifyKind(ChallengeTestUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        // Moving the OS clock backwards must not reopen an already rewarded period.
        return new DateTime(Math.Max(now.Ticks, profile.challengeClockUtc), DateTimeKind.Utc);
    }

    static MetaChallengePeriodState PeriodState(ChallengePeriod period)
    {
        EnsureLoaded();
        profile.challengePeriods ??= new List<MetaChallengePeriodState>();
        DateTime now = ChallengeNow();
        var state = profile.challengePeriods.Find(p => p.period == (int)period);
        if (state != null && now.Ticks < state.endUtc) return state;
        var date = now.ToLocalTime().Date;
        if (period == ChallengePeriod.Weekly) date = date.AddDays(-((int)date.DayOfWeek + 6) % 7);
        var next = new MetaChallengePeriodState {
            period = (int)period, startUtc = date.ToUniversalTime().Ticks,
            endUtc = date.AddDays(period == ChallengePeriod.Weekly ? 7 : 1).ToUniversalTime().Ticks
        };
        if (!writeBlocked) {
            if (state != null) profile.challengePeriods.Remove(state);
            profile.challengePeriods.Add(next);
            profile.challengeClockUtc = now.Ticks;
            MarkDirty();
        }
        return next;
    }

    public static TimeSpan ChallengeRemaining(ChallengePeriod period)
    {
        if (period == ChallengePeriod.Lifetime) return TimeSpan.Zero;
        var state = PeriodState(period);
        return TimeSpan.FromTicks(Math.Max(0, state.endUtc - ChallengeNow().Ticks));
    }

    public static MetaChallengeProgress[] GetChallenges(ChallengePeriod period)
    {
        if (period == ChallengePeriod.Lifetime)
            return GetChallenges().Where(c => c.permanent).ToArray();
        var state = PeriodState(period);
        var targets = period == ChallengePeriod.Daily ? DailyTargets : WeeklyTargets;
        var rewards = period == ChallengePeriod.Daily ? DailyRewards : WeeklyRewards;
        var names = period == ChallengePeriod.Daily ? DailyNames : WeeklyNames;
        return Enumerable.Range(0, PeriodMetrics.Length).Select(i => new MetaChallengeProgress {
            id = period + "-" + PeriodIds[i], metric = PeriodMetrics[i], name = names[i],
            current = state.counters[i], target = targets[i], xp = rewards[i], completed = state.rewarded[i]
        }).ToArray();
    }

    static void RecordPeriodProgress(string metric, int amount)
    {
        if (amount <= 0 || writeBlocked) return;
        foreach (var period in new[] { ChallengePeriod.Daily, ChallengePeriod.Weekly }) {
            var state = PeriodState(period);
            for (int index = 0; index < PeriodMetrics.Length; index++) {
            if (PeriodMetrics[index] != metric) continue;
            int target = (period == ChallengePeriod.Daily ? DailyTargets : WeeklyTargets)[index];
            state.counters[index] = (int)Math.Min(target, state.counters[index] + (long)amount);
            if (state.counters[index] == target && !state.rewarded[index]) {
                state.rewarded[index] = true;
                profile.periodRewardXp += (period == ChallengePeriod.Daily ? DailyRewards : WeeklyRewards)[index];
                milestonePending = true;
            }
            }
        }
        profile.challengeClockUtc = ChallengeNow().Ticks;
    }

    public static void RecordCraftedAmount(Item item, int amount, bool exotic = false)
    {
        if (!HasRun() || run.ended || amount <= 0) return;
        RecordPeriodProgress("crafted", amount);
        if (item == Item.Torche) RecordPeriodProgress("torches", amount);
        if (item == Item.Ladder || item == Item.IronLadder) RecordPeriodProgress("ladders", amount);
        if (exotic) RecordPeriodProgress("exotics", amount);
        MarkChanged(false);
    }

    public static void RecordSale(int proceeds)
    {
        if (!HasRun() || run.ended || proceeds <= 0) return;
        RecordPeriodProgress("sales", proceeds); MarkChanged(false);
    }

    static bool ValidatePeriods(MetaProfile value)
    {
        if (value.periodRewardXp < 0 || value.periodRewardXp > 1000000000000L ||
            value.challengeClockUtc < 0 || value.challengeClockUtc > DateTime.MaxValue.Ticks) return false;
        value.challengePeriods ??= new List<MetaChallengePeriodState>();
        if (value.challengePeriods.Count > 2 || value.challengePeriods.Select(p => p?.period).Distinct().Count() != value.challengePeriods.Count) return false;
        long activeRewards = 0;
        foreach (var state in value.challengePeriods) {
            if (state == null || state.period < 0 || state.period > 1 || state.startUtc < 0 ||
                state.endUtc <= state.startUtc || state.endUtc > DateTime.MaxValue.Ticks ||
                state.counters == null || state.rewarded == null || state.counters.Length != state.rewarded.Length ||
                (state.counters.Length != 6 && state.counters.Length != PeriodMetrics.Length)) return false;
            // Older profiles keep their original six counters and rewards; new tasks start at zero.
            if (state.counters.Length == 6) {
                Array.Resize(ref state.counters, PeriodMetrics.Length);
                Array.Resize(ref state.rewarded, PeriodMetrics.Length);
            }
            var targets = state.period == 0 ? DailyTargets : WeeklyTargets;
            for (int i = 0; i < PeriodMetrics.Length; i++) {
                if (state.counters[i] < 0 || state.counters[i] > targets[i] || state.rewarded[i] != (state.counters[i] == targets[i])) return false;
                if (state.rewarded[i]) activeRewards += (state.period == 0 ? DailyRewards : WeeklyRewards)[i];
            }
        }
        return activeRewards <= value.periodRewardXp;
    }
}
