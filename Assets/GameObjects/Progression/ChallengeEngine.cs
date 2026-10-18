using System;
using System.Collections.Generic;

[Serializable] public sealed class MetaChallengeTask
{
    public string id, name, metric;
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
}

public static class ChallengeEngine
{
    public static void Bounds(ChallengePeriod period, DateTime utcNow, TimeZoneInfo zone, out long startUtc, out long endUtc, out int dayIndex)
    { startUtc = 0; endUtc = long.MaxValue; dayIndex = 0; }
    public static MetaChallengeRound Draw(ChallengePeriod period, long startUtc, long endUtc, int dayIndex,
        ChallengeDrawContext context, ChallengeSettings settings, MetaChallengeRound previous)
        => new MetaChallengeRound { period = (int)period, startUtc = startUtc, endUtc = endUtc, dayIndex = dayIndex };
    public static ChallengeGain Record(MetaChallengeRound round, string metric, long amount, int subject = -1) => default;
    public static ChallengeGain RecordMax(MetaChallengeRound round, string metric, long value, int subject = -1) => default;
    public static ChallengeGain RecordDistinct(MetaChallengeRound round, string metric, int bit, int subject = -1) => default;
    public static ChallengeGain RecordRun(MetaChallengeRound round, string metric, string runId, long amount, int subject = -1) => default;
    public static int CompletedCount(MetaChallengeRound round) => 0;
    public static int PaidXp(MetaChallengeRound round) => 0;
    public static bool Sanitize(MetaChallengeRound round) => round != null;
}
