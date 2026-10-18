using System;
using System.Collections.Generic;

// Pure data: no UnityEngine dependency, so the catalog and the draw can be simulated outside Unity.
public enum ChallengePeriod { Daily, Weekly, Lifetime, Monthly } // append-only: persisted rounds store 0, 1 and 3
public enum ChallengeFamily { Mining, Exploration, Crafting, Trade, Artifacts, Special }
public enum ChallengeTier { Easy, Medium, Hard, Xxl, Project }
public enum ChallengeMode { Add, Max, Distinct, Run }

public sealed class ChallengeTemplate
{
    public string id, name, activity, metric, unit, icon;
    public ChallengeFamily family;
    public ChallengeMode mode;
    public int minLayer = 1, minLevel = 1, weight = 10;
    public bool retired, project;
}

public sealed class ChallengeMilestoneStage
{
    public readonly string id;
    public readonly int target, xp;
    public readonly bool legacy;
    public ChallengeMilestoneStage(string id, int target, int xp, bool legacy = false) { this.id = id; this.target = target; this.xp = xp; this.legacy = legacy; }
}

public sealed class ChallengeMilestoneLine
{
    public readonly string id, name, metric, icon, unit;
    public readonly bool counter;
    public readonly ChallengeMilestoneStage[] stages;
    public ChallengeMilestoneLine(string id, string name, string metric, string icon, string unit, bool counter, params ChallengeMilestoneStage[] stages)
    { this.id = id; this.name = name; this.metric = metric; this.icon = icon; this.unit = unit; this.counter = counter; this.stages = stages; }
}

public static class ChallengeCatalog
{
    public static IReadOnlyList<ChallengeTemplate> Templates => Array.Empty<ChallengeTemplate>();
    public static ChallengeTemplate Template(string id) => null;
    public static IReadOnlyList<ChallengeMilestoneLine> Milestones => Array.Empty<ChallengeMilestoneLine>();
    public static string Unit(string templateId, string metric, int subject) => "";
    public static string Icon(string templateId, string metric, int subject) => "art:0";
    public static string Name(string templateId, int subject) => templateId ?? "";
    public static string FamilyName(ChallengeFamily family) => "";
    public static string TierName(int tier) => "";
    public static string MedalName(int medal) => "";
    public static int OreBit(int itemId) => -1;
    public static string ArtifactName(int type) => "";
    public static int AvailableOres(int reachedLayer) => 0;
    public static int AvailableArtifacts(int reachedLayer) => 0;
}
