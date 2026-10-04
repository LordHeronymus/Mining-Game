using System;
using System.Globalization;
using UnityEngine;

public sealed class MetaUpgradeDefinition
{
    public readonly string id, name;
    public readonly bool comfort;
    public readonly int maxRank, baseCost, costInterval;
    public readonly float step;
    readonly string suffix;

    public MetaUpgradeDefinition(string id, string name, bool comfort, int ranks, float step, int baseCost, int interval, string suffix)
    { this.id = id; this.name = name; this.comfort = comfort; maxRank = ranks; this.step = step; this.baseCost = baseCost; costInterval = interval; this.suffix = suffix; }

    public int Cost(int currentRank) => currentRank < 0 || currentRank >= maxRank ? 0 : baseCost + currentRank / costInterval;
    public int TotalCost(int rank)
    {
        int result = 0;
        for (int i = 0; i < Mathf.Clamp(rank, 0, maxRank); i++) result += Cost(i);
        return result;
    }
    public string ValueLabel(int rank)
    {
        float value = Mathf.Clamp(rank, 0, maxRank) * step;
        if (suffix == "%") value *= 100f;
        string sign = id == "mining" ? "−" : "+";
        return sign + value.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")) + suffix;
    }
}

public sealed class MetaChallengeDefinition
{
    public readonly string id, name, metric;
    public readonly int target, xp, group;
    public readonly bool permanent;
    public MetaChallengeDefinition(string id, string name, string metric, int target, int xp, bool permanent, int group = 0)
    { this.id = id; this.name = name; this.metric = metric; this.target = target; this.xp = xp; this.permanent = permanent; this.group = group; }
}

public static class MetaProgressionCatalog
{
    public const int MaxLevel = 1000, PowerLevelCap = 200;
    public const int ComfortLevelInterval = 5;
    // Costs remain stable when GPS reward tuning is changed. This preserves earned levels
    // and already purchased ranks across balancing updates.
    public const int InitialLevelCost = 1000, PlateauLevelCost = 6000;
    public const double CurveWidth = 25d;
    public static readonly MetaUpgradeDefinition[] Upgrades = {
        new("health", "Gesundheit", false, 20, .025f, 2, 5, "%"),
        new("energy", "Energie", false, 20, .025f, 2, 5, "%"),
        new("money", "Startgeld", false, 20, 10f, 1, 5, " Gold"),
        new("movement", "Bewegung", false, 10, .01f, 2, 3, "%"),
        new("jump", "Sprunghöhe", false, 10, .008f, 2, 3, "%"),
        new("mining", "Abbauverbrauch", false, 20, .01f, 2, 5, "%"),
        new("reach", "Baureichweite", true, 20, .015f, 1, 5, " Blöcke"),
        new("capacity", "Tragkraft", true, 25, .4f, 1, 5, " kg"),
        new("torches", "Startfackeln", true, 10, 1f, 2, 3, ""),
        new("ladders", "Startleitern", true, 10, 1f, 2, 3, "")
    };

    public static readonly MetaChallengeDefinition[] Challenges = {
        new("depth-100", "Unter der Oberfläche", "depth", 100, 250, true),
        new("depth-250", "Tiefer Vorstoß", "depth", 250, 600, true),
        new("depth-500", "Im Herzen des Berges", "depth", 500, 1200, true),
        new("depth-1000", "Abgrundforscher", "depth", 1000, 2400, true),
        new("exotic-first", "Exotischer Handwerker", "exotics", 1, 400, true),
        new("gem-variety", "Edelsteinjäger", "rare", 3, 800, true),
        new("first-victory", "Tiefenhalls Vermächtnis", "victory", 1, 3000, true),
        new("run-ores", "Vielseitiger Abbau", "variety", 3, 180, false, 0),
        new("run-miner", "Reiche Ausbeute", "resources", 100, 220, false, 0),
        new("run-crafter", "Gut ausgerüstet", "crafts", 3, 180, false, 0),
        new("run-map", "Neue Wege", "regions", 12, 240, false, 1),
        new("run-depth", "Abwärts", "depth", 150, 280, false, 1),
        new("run-discovery", "Spurensucher", "discoveries", 2, 240, false, 1),
        new("run-deep", "Mut zum Abgrund", "depth", 400, 500, false, 2),
        new("run-rare", "Seltene Schätze", "rare", 2, 400, false, 2),
        new("run-exotic", "Ungewöhnliches Werkzeug", "exotics", 1, 400, false, 2)
    };

    static readonly long[] thresholds = BuildThresholds();
    static long[] BuildThresholds()
    {
        var result = new long[MaxLevel + 1];
        for (int level = 2; level <= MaxLevel; level++) result[level] = result[level - 1] + LevelCost(level - 1);
        return result;
    }
    public static long LevelCost(int level) => level >= MaxLevel ? 0 : (long)Math.Round(InitialLevelCost + (PlateauLevelCost - InitialLevelCost) * (1d - Math.Exp(-(Math.Max(1, level) - 1d) / CurveWidth)));
    public static long XpForLevel(int level) => thresholds[Mathf.Clamp(level, 1, MaxLevel)];
    public static int LevelForXp(long xp)
    {
        int low = 1, high = MaxLevel;
        while (low < high) { int middle = (low + high + 1) / 2; if (xp >= thresholds[middle]) low = middle; else high = middle - 1; }
        return low;
    }
    public static int EarnedPowerPoints(int level) => Mathf.Clamp(level - 1, 0, PowerLevelCap - 1);
    public static int EarnedComfortPoints(int level) => Mathf.Max(0, Mathf.Min(level, MaxLevel) - PowerLevelCap) / ComfortLevelInterval;
    public static MetaUpgradeDefinition Upgrade(string id) => Array.Find(Upgrades, value => value.id == id);
    public static MetaChallengeDefinition Challenge(string id) => Array.Find(Challenges, value => value.id == id);

    public static float ResourceWeight(Item item)
    {
        switch (item) {
            case Item.Coal: return 4f;
            case Item.Copper: return 6f;
            case Item.Iron: return 9f;
            case Item.Silver: return 14f;
            case Item.Gold: return 20f;
            case Item.Platinum: return 28f;
            case Item.Titanium: return 32f;
            case Item.Tungsten: return 40f;
            case Item.Obsidian: return 45f;
            case Item.OrangeGarnet: return 50f;
            case Item.Ruby: return 65f;
            case Item.Emerald: return 80f;
            case Item.Mythril: return 95f;
            case Item.Diamond: return 120f;
            case Item.Ultronium: return 160f;
            default: return 0f;
        }
    }
    public static bool IsRare(Item item) => item == Item.Ruby || item == Item.Emerald || item == Item.Diamond || item == Item.Mythril || item == Item.OrangeGarnet || item == Item.Ultronium;
}
