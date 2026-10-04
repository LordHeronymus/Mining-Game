using System;
using System.Collections.Generic;
using UnityEngine;

// A run owns its starting build. Spending profile points never changes an existing run.
[Serializable] public sealed class MetaLoadout
{
    public List<MetaUpgradeRank> ranks = new();
    public float healthMultiplier = 1f, energyMultiplier = 1f, movementMultiplier = 1f;
    public float jumpMultiplier = 1f, miningEnergyMultiplier = 1f;
    public int startMoney, startingTorches, startingLadders;
    public float buildReachBonus, carryCapacityBonus;

    public int Rank(string id)
    {
        if (ranks != null) foreach (var rank in ranks) if (rank != null && rank.id == id) return rank.rank;
        return 0;
    }
}

[Serializable] public sealed class MetaUpgradeRank { public string id; public int rank; }
[Serializable] public sealed class MetaResourceCount { public int item, count; }

[Serializable] public sealed class MetaProgressionSettings
{
    [Min(0f)] public float xpMultiplier = 1f;
    [Min(1)] public int depthStep = 25;
    [Min(0f)] public float depthBaseXp = 30f, depthIncrementXp = 5f, depthMaxXp = 150f;
    [Min(1)] public int explorationRegionSize = 12, explorationCellThreshold = 24;
    [Min(0f)] public float explorationXp = 16f, resourceXpMultiplier = 1f, discoveryXp = 180f;
    [Range(0f, .2f)] public float efficiencyMaxBonus = .2f;
    [Min(1f)] public float efficiencyReferenceXpPerMinute = 100f;

    public MetaProgressionSettings Sanitized()
    {
        return new MetaProgressionSettings {
            xpMultiplier = Finite(xpMultiplier, 1f, 0f, 100f),
            depthStep = Mathf.Clamp(depthStep, 1, 1000),
            depthBaseXp = Finite(depthBaseXp, 30f, 0f, 10000f),
            depthIncrementXp = Finite(depthIncrementXp, 5f, 0f, 1000f),
            depthMaxXp = Finite(depthMaxXp, 150f, 0f, 100000f),
            explorationRegionSize = Mathf.Clamp(explorationRegionSize, 4, 64),
            explorationCellThreshold = Mathf.Clamp(explorationCellThreshold, 1, Mathf.Clamp(explorationRegionSize, 4, 64) * Mathf.Clamp(explorationRegionSize, 4, 64)),
            explorationXp = Finite(explorationXp, 16f, 0f, 10000f),
            resourceXpMultiplier = Finite(resourceXpMultiplier, 1f, 0f, 100f),
            discoveryXp = Finite(discoveryXp, 180f, 0f, 100000f),
            efficiencyMaxBonus = Finite(efficiencyMaxBonus, .2f, 0f, .2f),
            efficiencyReferenceXpPerMinute = Finite(efficiencyReferenceXpPerMinute, 100f, 1f, 100000f)
        };
    }
    static float Finite(float value, float fallback, float minimum, float maximum)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
}

[Serializable] public sealed class MetaRunState
{
    public int version = 1;
    public string runId;
    public MetaLoadout loadout = new();
    public MetaProgressionSettings settings = new();
    public int maxDepth;
    public double activeSeconds, recentWorkXp, workWindowSeconds;
    public double progressXp, efficiencyXp;
    public long earnedXp, challengeXp, milestoneXp;
    public bool ended, won;
    public List<MetaResourceCount> resources = new();
    public List<string> resourceSources = new(), regions = new(), discoveries = new();
    public List<int> crafts = new(), exoticCrafts = new();
    public List<string> objectiveIds = new(), completedObjectives = new(), milestones = new();
}

[Serializable] public sealed class MetaProfile
{
    public long periodRewardXp, challengeClockUtc;
    public List<MetaChallengePeriodState> challengePeriods = new();
    public int version = 1;
    public long revision, totalXp;
    public List<MetaUpgradeRank> upgrades = new();
    public List<string> completedMilestones = new();
    // These ledgers intentionally survive deletion of a save slot. Otherwise restoring an
    // old copy of that world could claim the same permanent rewards for a second time.
    public List<MetaRunState> runs = new();
}

public sealed class MetaChallengeProgress
{
    public string id, name, metric;
    public int current, target, xp;
    public bool completed, permanent;
}
