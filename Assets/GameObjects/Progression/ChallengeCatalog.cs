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
    // Amounts per period as [Easy, Medium, Hard, XXL] (monthly [Easy, Medium, Hard], projects: the three stages); 0 = not offered.
    internal int[] daily, weekly, monthly;
    internal double[] layerFactors;
    internal bool scales, atLeast;
    internal ChallengeSubject[] subjects;
}

sealed class ChallengeSubject
{
    public readonly int id, minLayer;
    public readonly double factor;
    public readonly string name, unit, icon;
    public ChallengeSubject(int id, string name, string unit, string icon, double factor = 1, int minLayer = 1)
    { this.id = id; this.name = name; this.unit = unit; this.icon = icon; this.factor = factor; this.minLayer = minLayer; }
}

struct ChallengeCandidate
{
    public ChallengeTemplate template;
    public int subject, target, weight;
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
    const ChallengeFamily Mining = ChallengeFamily.Mining, Exploration = ChallengeFamily.Exploration, Crafting = ChallengeFamily.Crafting,
        Trade = ChallengeFamily.Trade, Artifacts = ChallengeFamily.Artifacts, Special = ChallengeFamily.Special;
    const ChallengeMode Max = ChallengeMode.Max, Distinct = ChallengeMode.Distinct, Run = ChallengeMode.Run;
    static readonly double[] SalesFactors = { .3, .6, 1, 2.5, 5 }, SingleSaleFactors = { .1, .25, .5, 2.5, 5 };
    // Depth goals are explicit per reached layer (the deepest row is 1199, so they are never rounded).
    static readonly int[][] DepthDaily = { new[] { 15, 25, 40, 0 }, new[] { 40, 80, 140, 0 }, new[] { 100, 200, 350, 440 }, new[] { 250, 450, 700, 880 }, new[] { 400, 700, 950, 1150 } };
    static readonly int[][] DepthWeekly = { new[] { 30, 45, 80, 0 }, new[] { 80, 140, 200, 0 }, new[] { 200, 350, 440, 600 }, new[] { 450, 700, 880, 1050 }, new[] { 700, 950, 1100, 1190 } };
    static readonly int[][] DepthProject = { new[] { 40, 80, 150 }, new[] { 100, 200, 350 }, new[] { 200, 400, 600 }, new[] { 300, 600, 900 }, new[] { 300, 600, 1000 } };
    // Item ids in variety bit order: Coal, Copper, Iron, Silver, OrangeGarnet, Gold, Titanium, Platinum, Tungsten, Diamond, Ruby, Ultronium.
    static readonly int[] OreBits = { 3, 0, 1, 4, 32, 2, 28, 8, 29, 33, 40, 24 };
    static readonly int[] OresPerLayer = { 3, 4, 7, 11, 12 }, ArtifactsPerLayer = { 2, 3, 5, 9, 10 };
    static readonly string[] ArtifactNames = { "Sonnenscheibe", "Alter Kompass", "Jademaske", "Bernsteinskarabäus", "Astrolabium",
        "Antike Vase", "Siegelring", "Fossile Spirale", "Kupfertafel", "Kristall-Sanduhr" };
    static readonly int[] ArtifactLayers = { 1, 1, 3, 2, 4, 4, 3, 4, 4, 5 };
    static readonly double[] ArtifactFactors = { .2, .5, .7, .7, .5, .4, .5, .3, .25, .1 };
    static readonly int[] UpgradeLimits = { 0, 3, 4 }; // craft limit index per upgrades subject (pickaxe, energy, carrying)

    static readonly ChallengeTemplate[] templates = BuildTemplates();
    static readonly IReadOnlyList<ChallengeTemplate> templateView = Array.AsReadOnly(templates);
    static readonly Dictionary<string, ChallengeTemplate> byId = Index();
    static readonly ChallengeMilestoneLine[] lines = BuildLines();
    static readonly IReadOnlyList<ChallengeMilestoneLine> lineView = Array.AsReadOnly(lines);

    public static IReadOnlyList<ChallengeTemplate> Templates => templateView;
    public static ChallengeTemplate Template(string id) => id != null && byId.TryGetValue(id, out var template) ? template : null;
    public static IReadOnlyList<ChallengeMilestoneLine> Milestones => lineView;

    public static string Unit(string templateId, string metric, int subject)
    {
        var template = Template(templateId);
        if (template != null) return TemplateUnit(template, subject, 0);
        string unit = RunUnit(metric);
        if (unit != null) return unit;
        template = ByMetric(metric);
        if (template != null) return TemplateUnit(template, subject, 0);
        return Line(metric)?.unit ?? "";
    }

    public static string Icon(string templateId, string metric, int subject)
    {
        var template = Template(templateId);
        if (template != null) return TemplateIcon(template, subject);
        string icon = RunIcon(metric);
        if (icon != null) return icon;
        template = ByMetric(metric);
        if (template != null) return TemplateIcon(template, subject);
        return Line(metric)?.icon ?? "art:0";
    }

    public static string Name(string templateId, int subject)
    {
        var template = Template(templateId);
        if (template == null) return templateId ?? "";
        return Subject(template, subject)?.name ?? template.name;
    }

    public static string FamilyName(ChallengeFamily family) => family switch {
        ChallengeFamily.Mining => "Abbau", ChallengeFamily.Exploration => "Erkundung", ChallengeFamily.Crafting => "Handwerk",
        ChallengeFamily.Trade => "Handel", ChallengeFamily.Artifacts => "Artefakte", ChallengeFamily.Special => "Spezial", _ => "" };
    public static string TierName(int tier) => tier switch { 0 => "Leicht", 1 => "Mittel", 2 => "Schwer", 3 => "XXL", 4 => "Projekt", _ => "" };
    public static string MedalName(int medal) => medal switch { 1 => "Bronze", 2 => "Silber", 3 => "Gold", 4 => "Platin", _ => "" };
    public static int OreBit(int itemId) => Array.IndexOf(OreBits, itemId);
    public static string ArtifactName(int type) => type >= 1 && type <= ArtifactNames.Length ? ArtifactNames[type - 1] : "";
    public static int AvailableOres(int reachedLayer) => OresPerLayer[Layer(reachedLayer) - 1];
    public static int AvailableArtifacts(int reachedLayer) => ArtifactsPerLayer[Layer(reachedLayer) - 1];

    // ---- Draw support (used by ChallengeEngine.Draw)

    internal static int Layer(int layer) => Math.Min(5, Math.Max(1, layer));

    internal static double Growth(int layer, int level, ChallengeSettings settings)
        => 1 + Math.Min(settings.maxGrowthPercent, settings.growthPercentPerLayer * (double)(Layer(layer) - 1)
            + settings.growthPercentPer10Levels * (double)((Math.Max(1, level) - 1) / 10)) / 100;

    // <20 integer, <100 multiples of 5, <1000 of 10, <10000 of 50, else of 500; at least 1.
    internal static int Nice(double value)
    {
        if (double.IsNaN(value) || value < 1) return 1;
        if (value >= 1e9) return 1000000000;
        double step = value < 20 ? 1 : value < 100 ? 5 : value < 1000 ? 10 : value < 10000 ? 50 : 500;
        return Math.Max(1, (int)(Math.Round(value / step, MidpointRounding.AwayFromZero) * step));
    }

    static int Raw(ChallengeTemplate template, ChallengePeriod period, int tier, int subject, int layer, int level, ChallengeSettings settings)
    {
        layer = Layer(layer);
        int[] amounts = template.id switch {
            "depth" => period == ChallengePeriod.Daily ? DepthDaily[layer - 1] : period == ChallengePeriod.Weekly ? DepthWeekly[layer - 1] : null,
            "project-depth" => period == ChallengePeriod.Monthly ? DepthProject[layer - 1] : null,
            _ => period switch { ChallengePeriod.Daily => template.daily, ChallengePeriod.Weekly => template.weekly, ChallengePeriod.Monthly => template.monthly, _ => null }
        };
        if (amounts == null || tier < 0 || tier >= amounts.Length || amounts[tier] <= 0) return 0;
        double factor = 1;
        if (template.scales) factor *= Growth(layer, level, settings);
        if (template.layerFactors != null) factor *= template.layerFactors[layer - 1];
        var option = Subject(template, subject);
        if (option != null) factor *= option.factor;
        return factor == 1 ? amounts[tier] : Nice(amounts[tier] * factor);
    }

    static int RoundTripSubject(int tier, int layer) => tier switch {
        0 => Math.Max(2, layer - 2), 1 => Math.Max(2, layer - 1), 2 => layer, 3 => layer == 5 ? 5 : -1, _ => -1 };

    // Final amount of a tier, 0 when not offered: a tier must be harder than the next lower offered tier
    // (round trips compare the layer subject first, every other template has the same subject in all tiers).
    internal static int Target(ChallengeTemplate template, ChallengePeriod period, int tier, int subject, int layer, int level, ChallengeSettings settings)
    {
        int amount = Raw(template, period, tier, subject, layer, level, settings);
        if (amount <= 0) return 0;
        bool trips = template.id == "round-trips";
        for (int lower = tier - 1; lower >= 0; lower--) {
            int lowerSubject = trips ? RoundTripSubject(lower, Layer(layer)) : subject;
            if (trips && lowerSubject < 2) continue;
            int previous = Target(template, period, lower, lowerSubject, layer, level, settings);
            if (previous <= 0) continue;
            return subject > lowerSubject || subject == lowerSubject && amount > previous ? amount : 0;
        }
        return amount;
    }

    internal static int[] ProjectStages(ChallengeTemplate template, int layer, int level, ChallengeSettings settings)
    {
        var stages = new int[3];
        for (int i = 0; i < 3; i++) {
            stages[i] = Raw(template, ChallengePeriod.Monthly, i, -1, layer, level, settings);
            if (stages[i] <= 0 || i > 0 && stages[i] <= stages[i - 1]) return null;
        }
        return stages;
    }

    static List<(int subject, int weight)> Subjects(ChallengeTemplate template, ChallengePeriod period, int tier, int layer)
    {
        var result = new List<(int, int)>();
        switch (template.id) {
            case "layer-blocks":
                for (int i = 1; i <= layer; i++) if (i > 1 || layer <= 2) result.Add((i, i == layer ? 2 : 1));
                return result;
            case "round-trips":
                int trip = RoundTripSubject(tier, layer);
                if (trip >= 2) result.Add((trip, 1));
                return result;
            case "upgrades":
                if (period == ChallengePeriod.Weekly) result.Add((-1, 1));
                else for (int i = 0; i < 3; i++) result.Add((i, 1));
                return result;
        }
        if (template.subjects == null) { result.Add((-1, 1)); return result; }
        foreach (var option in template.subjects) if (option.minLayer <= layer) result.Add((option.id, 1));
        return result;
    }

    static long Limit(int[] limits, int index) => index >= 0 && index < limits.Length ? limits[index] : 0;

    static bool Gate(ChallengeTemplate template, ChallengePeriod period, int subject, int target, ChallengeDrawContext context)
    {
        int layer = Layer(context.reachedLayer);
        switch (template.id) {
            case "ore-variety": return target <= AvailableOres(layer);
            case "layers": return target <= layer;
            case "artifact-variety": return target <= AvailableArtifacts(layer);
            case "ultronium": return period != ChallengePeriod.Daily || context.pickaxeHardness >= 8;
        }
        if (period != ChallengePeriod.Daily && period != ChallengePeriod.Weekly) return true;
        var limits = context.craftLimits;
        return template.id switch {
            "tools" => limits != null && Limit(limits, 1) >= target,
            "gear" => limits != null && Limit(limits, 2) >= target,
            "upgrades" => limits != null && (subject < 0 ? Limit(limits, 0) + Limit(limits, 3) + Limit(limits, 4)
                : subject < UpgradeLimits.Length ? Limit(limits, UpgradeLimits[subject]) : 0) >= target,
            "dynamite" => limits == null || Limit(limits, 5) > 0,
            "medkits" => limits == null || Limit(limits, 6) > 0,
            _ => true
        };
    }

    // Every drawable (template, subject) of one period and tier, in catalog order. Diligence and projects are placed separately.
    internal static void Candidates(ChallengePeriod period, int tier, ChallengeDrawContext context, ChallengeSettings settings, List<ChallengeCandidate> result)
    {
        int layer = Layer(context.reachedLayer), level = Math.Max(1, context.level);
        if (tier == (int)ChallengeTier.Xxl && layer < settings.xxlMinLayer) return;
        foreach (var template in templates) {
            if (template.retired || template.project || template.weight <= 0 || template.family == Special) continue;
            if (layer < template.minLayer || level < template.minLevel) continue;
            foreach (var (subject, weight) in Subjects(template, period, tier, layer)) {
                int target = Target(template, period, tier, subject, layer, level, settings);
                if (target > 0 && Gate(template, period, subject, target, context))
                    result.Add(new ChallengeCandidate { template = template, subject = subject, target = target, weight = weight });
            }
        }
    }

    internal static int DiligenceTasks(ChallengeSettings settings)
    {
        var daily = settings.daily;
        return daily == null ? 0 : Math.Max(0, Math.Min(settings.diligenceDailyTasks, daily.countEasy + daily.countMedium + daily.countHard));
    }
    internal static string DiligenceUnit(int tasks) => tasks == 1 ? "Tage mit 1 Tagesaufgabe" : "Tage mit " + tasks + " Tagesaufgaben";

    // ---- Names, units and icons

    static ChallengeSubject Subject(ChallengeTemplate template, int subject)
    {
        if (template.subjects != null) foreach (var option in template.subjects) if (option.id == subject) return option;
        return null;
    }

    internal static string TemplateUnit(ChallengeTemplate template, int subject, int diligenceTasks)
    {
        switch (template.id) {
            case "layer-blocks": return subject >= 1 && subject <= 5 ? "Blöcke aus Schicht " + subject : template.unit;
            case "round-trips": return subject >= 1 && subject <= 5 ? "Rückkehr ab Schicht " + subject : template.unit;
            case "diligence": return DiligenceUnit(diligenceTasks > 0 ? diligenceTasks : DiligenceTasks(new ChallengeSettings()));
        }
        var option = Subject(template, subject);
        if (option?.unit != null) return option.unit;
        return template.metric == "ore" ? ItemUnit(subject) ?? template.unit : template.unit;
    }

    static string TemplateIcon(ChallengeTemplate template, int subject)
    {
        var option = Subject(template, subject);
        if (option?.icon != null) return option.icon;
        return template.metric == "ore" ? ItemIcon(subject) ?? template.icon : template.icon;
    }

    static ChallengeTemplate ByMetric(string metric)
    {
        if (string.IsNullOrEmpty(metric)) return null;
        foreach (var template in templates) if (template.metric == metric && !template.project) return template;
        foreach (var template in templates) if (template.metric == metric) return template;
        return null;
    }

    static ChallengeMilestoneLine Line(string metric)
    {
        if (string.IsNullOrEmpty(metric)) return null;
        foreach (var line in lines) if (line.metric == metric) return line;
        return null;
    }

    // Run objectives (MetaProgressionCatalog) and other metrics without a template.
    static string RunUnit(string metric) => metric switch {
        "variety" => "Erzarten", "resources" => "Erze", "crafts" => "Herstellungen", "regions" => "Gebiete", "depth" => "Blöcke Tiefe",
        "discoveries" => "Entdeckungen", "exotics" => "Exotics hergestellt", "rare" => "seltene Erzarten", "victory" => "Siege",
        "tasks-done" => "Aufgaben erledigt", _ => null };
    static string RunIcon(string metric) => metric switch {
        "variety" or "resources" => "art:0", "crafts" => "art:2", "regions" or "discoveries" => "item:Torche", "depth" => "item:IronLadder",
        "exotics" => "item:PercussionHammer", "rare" => "item:Diamond", "victory" => "item:Ultronium", "tasks-done" => "art:8", _ => null };

    static string ItemUnit(int item) => item switch {
        3 => "Kohle", 0 => "Kupfer", 1 => "Eisen", 4 => "Silber", 2 => "Gold", 28 => "Titan", 8 => "Platin", 29 => "Wolfram",
        32 => "Granate", 40 => "Rubine", 33 => "Diamanten", 24 => "Ultronium", 31 => "Obsidian", 36 => "Smaragde", 34 => "Mythril", _ => null };
    static string ItemIcon(int item) => item switch {
        3 => "item:Coal", 0 => "art:3", 1 => "item:Iron", 4 => "item:Silver", 2 => "item:Gold", 28 => "item:Titanium", 8 => "item:Platinum",
        29 => "item:Tungsten", 32 => "item:OrangeGarnet", 40 => "item:Ruby", 33 => "item:Diamond", 24 => "item:Ultronium",
        31 => "item:Obsidian", 36 => "item:Emerald", 34 => "item:Mythril", _ => null };

    // ---- Content

    static int[] A(params int[] amounts) => amounts;

    static ChallengeTemplate T(string id, ChallengeFamily family, string name, string unit, string icon, int[] daily, int[] weekly, int[] monthly,
        ChallengeMode mode = ChallengeMode.Add, string activity = null, string metric = null, int minLayer = 1, int minLevel = 1, bool scales = false)
        => new ChallengeTemplate { id = id, family = family, name = name, unit = unit, icon = icon, daily = daily, weekly = weekly, monthly = monthly,
            mode = mode, activity = activity ?? id, metric = metric ?? id, minLayer = minLayer, minLevel = minLevel, scales = scales };

    static ChallengeTemplate Project(string id, ChallengeFamily family, string name, string unit, string icon, string metric, int[] stages,
        ChallengeMode mode = ChallengeMode.Add, string activity = null, int minLayer = 1, bool scales = false)
    {
        var template = T(id, family, name, unit, icon, null, null, stages, mode, activity ?? metric, metric, minLayer, 1, scales);
        template.project = true;
        return template;
    }

    static ChallengeTemplate Retired(ChallengeTemplate template) { template.retired = true; template.weight = 0; return template; }

    static ChallengeTemplate[] BuildTemplates()
    {
        var layerBlocks = T("layer-blocks", Mining, "Steinernes Erwachen", "Blöcke", "item:CopperPickaxe",
            A(80, 200, 400, 1500), A(500, 1200, 2500, 8000), A(2000, 5000, 10000), activity: "blocks");
        layerBlocks.subjects = new[] {
            new ChallengeSubject(1, "Unter der Grasnarbe", null, null, 1), new ChallengeSubject(2, "Steinernes Erwachen", null, null, 1),
            new ChallengeSubject(3, "In der Bergesmitte", null, null, .8), new ChallengeSubject(4, "Herz des Berges", null, null, .6),
            new ChallengeSubject(5, "Ruf des Abgrunds", null, null, .5) };
        var ore = T("ore", Mining, "Schätze des Berges", "Erze", "art:0",
            A(25, 60, 120, 400), A(150, 350, 700, 2000), A(600, 1500, 3000), activity: "ores", scales: true);
        ore.subjects = new[] {
            new ChallengeSubject(3, "Schwarze Adern", "Kohle", "item:Coal", 1), new ChallengeSubject(0, "Kupferglut", "Kupfer", "art:3", .8),
            new ChallengeSubject(1, "Eisenherz", "Eisen", "item:Iron", 1), new ChallengeSubject(4, "Silbermond", "Silber", "item:Silver", .8, 2),
            new ChallengeSubject(2, "Goldfieber", "Gold", "item:Gold", .5, 3), new ChallengeSubject(28, "Titankraft", "Titan", "item:Titanium", .8, 3),
            new ChallengeSubject(8, "Platinglanz", "Platin", "item:Platinum", .5, 4), new ChallengeSubject(29, "Wolframwille", "Wolfram", "item:Tungsten", .6, 4) };
        var gem = T("gem", Mining, "Funkelnde Beute", "Edelsteine", "item:Diamond",
            A(3, 8, 15, 60), A(20, 50, 100, 300), A(80, 200, 400), activity: "gems", metric: "ore", minLayer: 3);
        gem.subjects = new[] {
            new ChallengeSubject(32, "Glut des Granats", "Granate", "item:OrangeGarnet", 1, 3), new ChallengeSubject(40, "Rubinfeuer", "Rubine", "item:Ruby", 1, 4),
            new ChallengeSubject(33, "Diamantenglanz", "Diamanten", "item:Diamond", 1, 4) };
        var ultronium = T("ultronium", Mining, "Ultroniumrausch", "Ultronium", "item:Ultronium",
            A(0, 0, 3, 10), A(0, 5, 12, 25), A(0, 0, 30), metric: "ore", minLayer: 5);
        ultronium.subjects = new[] { new ChallengeSubject(24, null, null, null, 1, 5) };
        var trips = T("round-trips", Exploration, "Hin und zurück", "Rückkehren", "item:Ladder",
            A(1, 1, 1, 2), A(3, 3, 4, 6), A(10, 12, 15), activity: "returns", minLayer: 2);
        trips.atLeast = true;
        var upgrades = T("upgrades", Crafting, "Aufrüsten", "Upgrades", "item:IronPickaxe", A(1, 1, 2, 0), A(0, 2, 4, 0), null);
        upgrades.subjects = new[] {
            new ChallengeSubject(0, "Die nächste Klinge", "Spitzhacken-Upgrades", "item:IronPickaxe"),
            new ChallengeSubject(1, "Funken des Lebens", "Energie-Upgrades", "item:EnergyStorageVial"),
            new ChallengeSubject(2, "Schultern aus Stahl", "Tragkraft-Upgrades", "item:Backpack") };
        var artifact = T("artifact", Artifacts, "Spuren der Vorzeit", "Artefakte", "artifact", A(1, 2, 4, 10), A(3, 6, 12, 25), A(10, 20, 40));
        artifact.subjects = new ChallengeSubject[ArtifactNames.Length];
        for (int i = 0; i < ArtifactNames.Length; i++)
            artifact.subjects[i] = new ChallengeSubject(i + 1, null, ArtifactNames[i], null, ArtifactFactors[i], ArtifactLayers[i]);
        var sales = T("sales", Trade, "Goldene Geschäfte", "$ Verkaufserlös", "art:4",
            A(1500, 4000, 10000, 30000), A(8000, 20000, 50000, 150000), A(30000, 80000, 200000));
        var single = T("sale-single", Trade, "Grosser Handschlag", "$ auf einmal", "art:4",
            A(400, 1000, 2500, 8000), A(1500, 4000, 10000, 25000), A(4000, 10000, 25000), Max);
        var runSales = T("run-sales", Trade, "Goldener Lauf", "$ in einem Run", "art:4",
            A(1500, 4000, 10000, 30000), A(6000, 15000, 40000, 100000), A(20000, 50000, 120000), Run);
        sales.layerFactors = runSales.layerFactors = SalesFactors;
        single.layerFactors = SingleSaleFactors;
        var impossible = Retired(T("legacy-impossible", Mining, "Obsidianfluch", "Erze", "item:Obsidian", null, null, null, metric: "unobtainable"));
        impossible.subjects = new[] {
            new ChallengeSubject(31, "Obsidianfluch", "Obsidian", "item:Obsidian"), new ChallengeSubject(36, "Smaragdtraum", "Smaragde", "item:Emerald") };
        var projectSales = Project("project-sales", Trade, "Handelsimperium", "$ Verkaufserlös", "art:4", "sales", A(20000, 60000, 150000));
        projectSales.layerFactors = SalesFactors;

        return new[] {
            // Mining
            T("blocks", Mining, "Schaufel und Hacke", "Blöcke abgebaut", "art:1",
                A(100, 250, 500, 2000), A(600, 1500, 3000, 10000), A(2500, 6000, 12000), scales: true),
            layerBlocks,
            T("ores", Mining, "Schätze des Berges", "Erze", "art:0",
                A(40, 100, 200, 750), A(250, 600, 1200, 4000), A(1000, 2500, 5000), metric: "resources", scales: true),
            ore,
            T("gems", Mining, "Funkelnde Beute", "Edelsteine", "item:Diamond", A(5, 12, 25, 100), A(30, 75, 150, 500), A(120, 300, 600), minLayer: 3),
            gem,
            ultronium,
            T("trees", Mining, "Ruf der Wälder", "Bäume gefällt", "item:Axe", A(3, 6, 12, 30), A(15, 35, 70, 150), A(60, 150, 300)),
            T("grass", Mining, "Grüne Ernte", "Grasbüschel geerntet", "item:Scythe", A(15, 40, 80, 200), A(100, 250, 500, 1200), A(400, 1000, 2000)),
            T("veins", Mining, "Ader um Ader", "Erzadern geleert", "item:Silver", A(3, 6, 12, 40), A(20, 45, 90, 250), A(80, 200, 400)),
            T("blasted", Mining, "Sprengmeister", "Blöcke gesprengt", "item:Dynamite", A(20, 50, 100, 400), A(120, 300, 600, 2000), A(500, 1200, 2500)),
            T("ore-variety", Mining, "Bunte Ausbeute", "Erzarten", "art:0", A(2, 3, 5, 8), A(3, 5, 7, 10), A(4, 6, 9), Distinct),
            // Exploration
            T("depth", Exploration, "Ruf der Tiefe", "Blöcke Tiefe", "item:IronLadder", null, null, null, Max),
            T("layers", Exploration, "Grenzgänger", "Schichten besucht", "item:IronLadder", A(2, 3, 0, 0), A(3, 4, 0, 0), null, Distinct, "depth"),
            T("regions", Exploration, "Unbetretene Pfade", "Gebiete erkundet", "item:Torche", A(4, 8, 15, 40), A(25, 50, 90, 200), A(100, 200, 350)),
            T("cells", Exploration, "Verborgene Weiten", "Kartenfelder aufgedeckt", "item:Torche",
                A(200, 500, 1000, 3000), A(1500, 3500, 7000, 15000), A(6000, 15000, 30000)),
            trips,
            T("returns", Exploration, "Sicher zurück", "Rückkehren aus der Tiefe", "item:Ladder", A(2, 4, 7, 15), A(10, 20, 35, 80), A(40, 80, 150), minLayer: 2),
            T("full-returns", Exploration, "Volle Taschen", "volle Rückkehren", "item:Backpack", A(1, 2, 4, 10), A(5, 10, 20, 45), A(20, 40, 80)),
            T("torches-placed", Exploration, "Lichtbringer", "Fackeln platziert", "art:5", A(5, 12, 25, 60), A(30, 70, 140, 300), A(120, 300, 600)),
            T("ladders-placed", Exploration, "Kletterer", "Leitern platziert", "item:Ladder", A(10, 25, 50, 120), A(60, 150, 300, 700), A(250, 600, 1200)),
            T("altars", Exploration, "Ruf des Altars", "Altar erreicht", "item:Ultronium", null, A(0, 0, 1, 0), A(0, 0, 1), Max, minLayer: 5),
            T("discoveries", Exploration, "Geheimnisse im Stein", "Entdeckungen", "item:Torche", A(2, 4, 8, 25), A(12, 25, 50, 120), A(50, 100, 200), activity: "artifacts"),
            // Crafting
            T("crafted", Crafting, "Meister am Amboss", "Gegenstände hergestellt", "art:2",
                A(10, 25, 50, 150), A(60, 150, 300, 800), A(250, 600, 1200), scales: true),
            T("tools", Crafting, "Werkzeugschmiede", "Werkzeuge hergestellt", "item:IronPickaxe", A(1, 2, 3, 0), A(2, 4, 0, 0), null),
            T("gear", Crafting, "Für den Aufbruch", "Ausrüstungen hergestellt", "item:Backpack", A(1, 2, 3, 0), A(2, 4, 0, 0), null),
            upgrades,
            T("dynamite", Crafting, "Donner der Schmiede", "Dynamit hergestellt", "item:Dynamite", A(3, 8, 15, 40), A(20, 50, 100, 250), A(80, 200, 400)),
            T("medkits", Crafting, "Heilende Vorräte", "Medkits hergestellt", "item:Medkit", A(2, 4, 8, 0), A(10, 20, 35, 0), A(40, 80, 150)),
            T("torches-crafted", Crafting, "Funken im Dunkel", "Fackeln hergestellt", "art:5", A(8, 20, 40, 100), A(50, 120, 240, 600), A(200, 500, 1000)),
            T("ladders-crafted", Crafting, "Himmelsleiter", "Leitern hergestellt", "item:Ladder", A(10, 25, 50, 150), A(60, 150, 300, 800), A(250, 600, 1200)),
            T("exotics", Crafting, "Fremde Künste", "Exotics hergestellt", "item:PercussionHammer", A(1, 2, 4, 0), A(3, 6, 12, 0), A(10, 25, 50), minLevel: 5),
            // Trade
            sales, single, runSales,
            // Artifacts
            T("artifacts", Artifacts, "Relikte der Vorzeit", "Artefakte geborgen", "artifact", A(2, 4, 8, 25), A(10, 20, 40, 80), A(40, 80, 150)),
            artifact,
            T("artifact-variety", Artifacts, "Spuren der Alten", "Artefaktarten", "artifact", A(2, 3, 4, 6), A(3, 4, 6, 8), A(4, 6, 8), Distinct),
            // Special: weekly only, placed in one Medium slot by the draw; target = settings.diligenceDays.
            T("diligence", Special, "Fleissige Woche", "Tage mit 3 Tagesaufgaben", "art:8", null, null, null, Distinct),
            // Retired: only reached through converted legacy rounds, never drawn.
            Retired(T("legacy-depth-gain", Exploration, "Ruf der Tiefe", "Blöcke Tiefe", "art:1", null, null, null, metric: "depth-gain")),
            Retired(T("legacy-artifact-sighted", Artifacts, "Spuren der Alten", "Artefakttypen", "artifact", null, null, null, Distinct, metric: "artifact-sighted")),
            impossible,
            // Monthly projects (three stages)
            Project("project-depth", Exploration, "Tiefenexpedition", "Blöcke Tiefe", "item:IronLadder", "depth", null, Max),
            Project("project-ores", Mining, "Erzmagnat", "Erze", "art:0", "resources", A(1500, 4000, 10000), activity: "ores", scales: true),
            projectSales,
            Project("project-regions", Exploration, "Kartograf", "Gebiete erkundet", "item:Torche", "regions", A(100, 250, 500)),
            Project("project-blasted", Mining, "Grosse Sprengung", "Blöcke gesprengt", "item:Dynamite", "blasted", A(300, 800, 2000)),
            Project("project-artifacts", Artifacts, "Schatzkammer", "Artefakte geborgen", "artifact", "artifacts", A(15, 40, 100)),
            Project("project-crafted", Crafting, "Meisterschmiede", "Gegenstände hergestellt", "art:2", "crafted", A(200, 500, 1200), scales: true),
            Project("project-veins", Mining, "Aderjäger", "Erzadern geleert", "item:Silver", "veins", A(50, 120, 300)),
            Project("project-returns", Exploration, "Expeditionsleiter", "Rückkehren aus der Tiefe", "item:Ladder", "returns", A(30, 70, 150), minLayer: 2)
        };
    }

    static Dictionary<string, ChallengeTemplate> Index()
    {
        var result = new Dictionary<string, ChallengeTemplate>();
        foreach (var template in templates) result[template.id] = template;
        return result;
    }

    static ChallengeMilestoneStage S(string id, int target, int xp, bool legacy = false) => new(id, target, xp, legacy);

    // Stage ids are permanent (stored in milestoneAwards and "cm:" markers); legacy stages keep their MetaProgressionCatalog ids and XP.
    static ChallengeMilestoneLine[] BuildLines() => new ChallengeMilestoneLine[] {
        new("line-depth", "Abstieg in die Tiefe", "depth", "item:IronLadder", "Blöcke Tiefe", false,
            S("depth-100", 100, 250, true), S("depth-250", 250, 600, true), S("depth-500", 500, 1200, true), S("depth-1000", 1000, 2400, true)),
        new("line-exotics", "Meister fremder Künste", "total-exotics", "item:PercussionHammer", "Exotics", false,
            S("exotic-first", 1, 400, true), S("lifetime-exotics", 3, 1600, true)),
        new("line-victories", "Tiefenhalls Champion", "total-victory", "item:Ultronium", "Siege", false,
            S("first-victory", 1, 3000, true), S("lifetime-victories", 5, 8000, true), S("ms-victories-3", 15, 12000)),
        new("line-gem-variety", "Edelsteinjäger", "rare", "item:Diamond", "seltene Funde in einem Run", false,
            S("gem-variety", 3, 800, true), S("ms-gem-variety-2", 4, 1600)),
        new("line-ores", "Bergbaulegende", "total-resources", "art:0", "Erze", false,
            S("lifetime-ores", 5000, 4000, true), S("ms-ores-2", 25000, 8000), S("ms-ores-3", 100000, 16000)),
        new("line-coal", "Schwarzes Gold", "total-coal", "item:Coal", "Kohle", false,
            S("lifetime-coal", 2000, 1500, true), S("ms-coal-2", 10000, 3000), S("ms-coal-3", 40000, 6000)),
        new("line-copper", "Herr der Kupferadern", "total-copper", "art:3", "Kupfer", false,
            S("lifetime-copper", 1000, 1800, true), S("ms-copper-2", 5000, 3600), S("ms-copper-3", 20000, 7200)),
        new("line-iron", "Eisenbaron", "total-iron", "item:Iron", "Eisen", false,
            S("lifetime-iron", 1000, 2000, true), S("ms-iron-2", 10000, 4000), S("ms-iron-3", 40000, 8000)),
        new("line-silver", "Silberkönig", "total-silver", "item:Silver", "Silber", false,
            S("lifetime-silver", 500, 2500, true), S("ms-silver-2", 5000, 5000), S("ms-silver-3", 20000, 10000)),
        new("line-gold", "Goldene Ära", "total-gold", "item:Gold", "Gold", false,
            S("lifetime-gold", 250, 3000, true), S("ms-gold-2", 2500, 6000), S("ms-gold-3", 10000, 12000)),
        new("line-regions", "Weltenwanderer", "total-regions", "item:Torche", "Gebiete", false,
            S("lifetime-regions", 250, 2000, true), S("ms-regions-2", 1500, 4000), S("ms-regions-3", 5000, 8000)),
        new("line-discoveries", "Geheimnisse des Berges", "total-discoveries", "item:Torche", "Entdeckungen", false,
            S("lifetime-discoveries", 25, 3000, true), S("ms-discoveries-2", 150, 6000), S("ms-discoveries-3", 500, 12000)),
        new("line-gems", "Juwelensammler", "total-gems", "item:Diamond", "Edelsteine", false,
            S("ms-gems-1", 100, 1000), S("ms-gems-2", 1000, 2500), S("ms-gems-3", 5000, 5000)),
        new("line-ultronium", "Ultroniumfürst", "total-ultronium", "item:Ultronium", "Ultronium", false,
            S("ms-ultronium-1", 50, 1000), S("ms-ultronium-2", 250, 2500), S("ms-ultronium-3", 1000, 5000)),
        new("line-blasted", "Meister der Sprengung", "blasted", "item:Dynamite", "Blöcke gesprengt", true,
            S("ms-blasted-1", 500, 1000), S("ms-blasted-2", 5000, 2500), S("ms-blasted-3", 25000, 5000)),
        new("line-torches", "Lichtbringer", "torches-placed", "art:5", "Fackeln platziert", true,
            S("ms-torches-1", 100, 1000), S("ms-torches-2", 1000, 2500), S("ms-torches-3", 5000, 5000)),
        new("line-ladders", "Himmelsstürmer", "ladders-placed", "item:Ladder", "Leitern platziert", true,
            S("ms-ladders-1", 200, 1000), S("ms-ladders-2", 2000, 2500), S("ms-ladders-3", 10000, 5000)),
        new("line-full-returns", "Schwer beladen", "full-returns", "item:Backpack", "volle Rückkehren", true,
            S("ms-full-returns-1", 10, 1000), S("ms-full-returns-2", 100, 2500), S("ms-full-returns-3", 500, 5000)),
        new("line-returns", "Heimkehrer", "returns", "item:Ladder", "Rückkehren", true,
            S("ms-returns-1", 25, 1000), S("ms-returns-2", 250, 2500), S("ms-returns-3", 1000, 5000)),
        new("line-veins", "Aderleerer", "veins", "item:Silver", "Erzadern geleert", true,
            S("ms-veins-1", 50, 1000), S("ms-veins-2", 500, 2500), S("ms-veins-3", 2500, 5000)),
        new("line-sales", "Handelsfürst", "sales", "art:4", "$ Verkaufserlös", true,
            S("ms-sales-1", 50000, 1000), S("ms-sales-2", 1000000, 2500), S("ms-sales-3", 10000000, 5000)),
        new("line-crafted", "Meisterhandwerk", "crafted", "art:2", "Gegenstände hergestellt", true,
            S("ms-crafted-1", 500, 1000), S("ms-crafted-2", 5000, 2500), S("ms-crafted-3", 25000, 5000)),
        new("line-artifacts", "Hüter der Relikte", "artifacts", "artifact", "Artefakte geborgen", true,
            S("ms-artifacts-1", 25, 1000), S("ms-artifacts-2", 250, 2500), S("ms-artifacts-3", 1000, 5000)),
        new("line-trees", "Holzfäller", "trees", "item:Axe", "Bäume gefällt", true,
            S("ms-trees-1", 100, 1000), S("ms-trees-2", 1000, 2500), S("ms-trees-3", 5000, 5000)),
        new("line-tasks", "Herausforderer", "tasks-done", "art:8", "Aufgaben erledigt", true,
            S("ms-tasks-1", 50, 1000), S("ms-tasks-2", 500, 2500), S("ms-tasks-3", 2000, 5000))
    };
}
