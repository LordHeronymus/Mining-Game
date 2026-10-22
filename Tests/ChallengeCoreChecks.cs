using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

// Edit-mode check of the pure challenge core against spec v2 §1–§4, §7, §8: catalog content and milestone lines, every
// drawn task recomputed from an independent restatement of the spec tables (amounts, growth, rounding, tier rule, gates,
// names, units, XP), slots, XXL (seed of §7.4), diligence, monthly projects, caps, minimums, unique activities, avoidance,
// coverage, determinism, engine semantics, Sanitize (fixed cases plus a differential fuzz), Bounds in custom DST zones and
// settings clamping. Uses only the pure API and System.*, so it also runs under dotnet. Report: Temp/ChallengeCoreChecks.txt.
public static class ChallengeCoreChecks
{
    const string ReportFile = "Temp/ChallengeCoreChecks.txt";
    const int DetailLimit = 20, XxlSalt = 0x58584C;
    const long Day = TimeSpan.TicksPerDay, Hour = TimeSpan.TicksPerHour;
    static readonly long Day0 = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc).Ticks;
    static readonly long EndLimit = new DateTime(9000, 2, 1).Ticks;

    static readonly List<string> report = new(), details = new(), notes = new();
    static string section = "", firstFailure;
    static int sectionPass, sectionFail, passed, failed;

    public static object Main()
    {
        report.Clear(); details.Clear(); notes.Clear(); passed = failed = 0; firstFailure = null; cache.Clear();
        var watch = Stopwatch.StartNew();
        var summary = new List<string>();
        try { BuildSpecs(); } catch (Exception e) { return "FAIL spec model: " + e.Message; }
        Section("catalog", CatalogChecks, summary);
        Section("milestones", MilestoneChecks, summary);
        Section("text", TextChecks, summary);
        Section("settings", SettingsChecks, summary);
        Section("amounts", AmountChecks, summary);
        Section("draw", DrawSimulation, summary);
        Section("draw-variants", DrawVariants, summary);
        Section("engine", EngineChecks, summary);
        Section("sanitize", SanitizeChecks, summary);
        Section("fuzz", FuzzChecks, summary);
        Section("bounds", BoundsChecks, summary);
        watch.Stop();
        string time = watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
        report.Insert(0, (failed == 0 ? "PASS " : "FAIL ") + passed + " passed, " + failed + " failed, " + time);
        try { Directory.CreateDirectory("Temp"); File.WriteAllLines(ReportFile, report, new UTF8Encoding(false)); } catch (Exception) { }
        return failed == 0 ? "PASS " + passed + ": " + string.Join(", ", summary) + " (" + time + ")"
            : "FAIL " + failed + " of " + (passed + failed) + ": " + firstFailure;
    }

    // ---------- Reporting

    static void Section(string name, Action body, List<string> summary)
    {
        section = name; sectionPass = sectionFail = 0; details.Clear(); notes.Clear();
        var watch = Stopwatch.StartNew();
        try { body(); } catch (Exception e) { Fail("unexpected " + e.GetType().Name + ": " + e.Message + " | " + Frame(e)); }
        watch.Stop();
        report.Add((sectionFail == 0 ? "OK   " : "FAIL ") + name + ": " + sectionPass + " passed, " + sectionFail + " failed, " + watch.ElapsedMilliseconds + " ms");
        report.AddRange(details);
        if (sectionFail > DetailLimit) report.Add("FAIL [" + name + "] ... and " + (sectionFail - DetailLimit) + " more");
        foreach (var note in notes) report.Add("     " + note);
        summary.Add(name + " " + sectionPass + (sectionFail > 0 ? " (" + sectionFail + " failed)" : ""));
    }
    static string Frame(Exception e)
    {
        string trace = e.StackTrace ?? "";
        int cut = trace.IndexOf('\n');
        return (cut > 0 ? trace.Substring(0, cut) : trace).Trim();
    }
    static void Note(string text) => notes.Add(text);
    static void Pass() { sectionPass++; passed++; }
    static void Fail(string what)
    {
        sectionFail++; failed++;
        string line = "[" + section + "] " + what;
        firstFailure ??= line;
        if (sectionFail <= DetailLimit) details.Add("FAIL " + line);
    }
    static bool Check(bool ok, string what) { if (ok) Pass(); else Fail(what); return ok; }
    static bool Check(bool ok, Func<string> what) { if (ok) Pass(); else Fail(what()); return ok; }
    static bool Eq<T>(T actual, T expected, string what)
        => Check(EqualityComparer<T>.Default.Equals(actual, expected), () => what + ": expected '" + expected + "', got '" + actual + "'");

    // ---------- Spec model: an independent restatement of spec §7.1–§7.4 used to recompute every drawn task

    sealed class Sub
    {
        public readonly int id, minLayer; public readonly double factor; public readonly string name, unit, icon;
        public Sub(int id, string name, string unit, string icon, double factor = 1, int minLayer = 1)
        { this.id = id; this.name = name; this.unit = unit; this.icon = icon; this.factor = factor; this.minLayer = minLayer; }
    }
    sealed class Spec
    {
        public string id, name, unit, icon, activity, metric;
        public ChallengeFamily family; public ChallengeMode mode;
        public int minLayer = 1, minLevel = 1;
        public int[] daily, weekly, monthly; public double[] layerFactors;
        public bool scales, atLeast, project, retired;
        public Sub[] subs;
    }
    sealed class Ctx
    {
        public int layer = 1, level = 1; public int[] limits; public float hardness;
        public int L => Math.Min(5, Math.Max(1, layer));
        public int Lv => Math.Max(1, level);
        public ChallengeDrawContext Context() => new ChallengeDrawContext { reachedLayer = layer, level = level, craftLimits = limits, pickaxeHardness = hardness };
        public override string ToString() => "L" + layer + " lv" + level + " limits " + (limits == null ? "null" : "{" + string.Join(",", limits) + "}")
            + " hard " + hardness.ToString(CultureInfo.InvariantCulture);
    }
    struct Cand { public Spec spec; public int subject, target; }

    const ChallengeFamily Mining = ChallengeFamily.Mining, Exploration = ChallengeFamily.Exploration, Crafting = ChallengeFamily.Crafting,
        Trade = ChallengeFamily.Trade, Artifacts = ChallengeFamily.Artifacts, Special = ChallengeFamily.Special;
    const ChallengeMode Add = ChallengeMode.Add, Max = ChallengeMode.Max, Distinct = ChallengeMode.Distinct, RunMode = ChallengeMode.Run;
    const ChallengePeriod Daily = ChallengePeriod.Daily, Weekly = ChallengePeriod.Weekly, Monthly = ChallengePeriod.Monthly, Lifetime = ChallengePeriod.Lifetime;
    static readonly double[] SalesF = { .3, .6, 1, 2.5, 5 }, SingleF = { .1, .25, .5, 2.5, 5 };
    static readonly int[][] DepthD = { new[] { 15, 25, 40, 0 }, new[] { 40, 80, 140, 0 }, new[] { 100, 200, 350, 440 }, new[] { 250, 450, 700, 880 }, new[] { 400, 700, 950, 1150 } };
    static readonly int[][] DepthW = { new[] { 30, 45, 80, 0 }, new[] { 80, 140, 200, 0 }, new[] { 200, 350, 440, 600 }, new[] { 450, 700, 880, 1050 }, new[] { 700, 950, 1100, 1190 } };
    static readonly int[][] DepthP = { new[] { 40, 80, 150 }, new[] { 100, 200, 350 }, new[] { 200, 400, 600 }, new[] { 300, 600, 900 }, new[] { 300, 600, 1000 } };
    static readonly int[] OresAvail = { 3, 4, 7, 11, 12 }, ArtsAvail = { 2, 3, 5, 9, 10 };
    static readonly string[] ArtNames = { "Sonnenscheibe", "Alter Kompass", "Jademaske", "Bernsteinskarabäus", "Astrolabium", "Antike Vase", "Siegelring",
        "Fossile Spirale", "Kupfertafel", "Kristall-Sanduhr" };
    static readonly int[] ArtLayers = { 1, 1, 3, 2, 4, 4, 3, 4, 4, 5 };
    static readonly double[] ArtFactors = { .2, .5, .7, .7, .5, .4, .5, .3, .25, .1 };
    static readonly int[][] Caps = { new[] { 3, 2, 2, 1, 1 }, new[] { 3, 3, 2, 1, 2 }, null, new[] { 2, 2, 2, 1, 1 } };
    static readonly int[] Forbidden = { 31, 34, 36 }; // Obsidian, Mythril, Emerald are unobtainable
    static readonly HashSet<string> IconItems = new() { "CopperPickaxe", "IronPickaxe", "Coal", "Iron", "Silver", "Gold", "Titanium", "Platinum",
        "Tungsten", "OrangeGarnet", "Ruby", "Diamond", "Ultronium", "Axe", "Scythe", "Dynamite", "IronLadder", "Ladder", "Torche", "Backpack",
        "EnergyStorageVial", "Medkit", "PercussionHammer", "Obsidian", "Emerald", "Mythril" };
    static readonly HashSet<string> IconArt = new() { "0", "1", "2", "3", "4", "5", "8" };
    static List<Spec> specList;
    static Dictionary<string, Spec> specs;

    static int[] A(params int[] v) => v;
    static Spec S(string id, ChallengeFamily family, string name, string unit, string icon, int[] d, int[] w, int[] m, ChallengeMode mode = Add,
        string activity = null, string metric = null, int minLayer = 1, int minLevel = 1, bool scales = false)
        => new Spec { id = id, family = family, name = name, unit = unit, icon = icon, daily = d, weekly = w, monthly = m, mode = mode,
            activity = activity ?? id, metric = metric ?? id, minLayer = minLayer, minLevel = minLevel, scales = scales };

    static void BuildSpecs()
    {
        var list = new List<Spec>();
        Spec Put(Spec s) { list.Add(s); return s; }
        Spec Project(string id, string name, string unit, string metric, string activity, int[] stages, ChallengeMode mode = Add, int minLayer = 1,
            bool scales = false, double[] factors = null)
            => Put(new Spec { id = id, name = name, unit = unit, metric = metric, activity = activity, monthly = stages, mode = mode, minLayer = minLayer,
                scales = scales, layerFactors = factors, project = true, family = Special });
        // Mining
        Put(S("blocks", Mining, "Schaufel und Hacke", "Blöcke abgebaut", "art:1", A(100, 250, 500, 2000), A(600, 1500, 3000, 10000), A(2500, 6000, 12000), scales: true));
        Put(S("layer-blocks", Mining, null, null, "item:CopperPickaxe", A(80, 200, 400, 1500), A(500, 1200, 2500, 8000), A(2000, 5000, 10000), activity: "blocks")).subs = new[] {
            new Sub(1, "Unter der Grasnarbe", null, null), new Sub(2, "Steinernes Erwachen", null, null), new Sub(3, "In der Bergesmitte", null, null, .8),
            new Sub(4, "Herz des Berges", null, null, .6), new Sub(5, "Ruf des Abgrunds", null, null, .5) };
        Put(S("ores", Mining, "Schätze des Berges", "Erze", "art:0", A(40, 100, 200, 750), A(250, 600, 1200, 4000), A(1000, 2500, 5000),
            activity: "ores", metric: "resources", scales: true));
        Put(S("ore", Mining, null, null, null, A(25, 60, 120, 400), A(150, 350, 700, 2000), A(600, 1500, 3000), activity: "ores", scales: true)).subs = new[] {
            new Sub(3, "Schwarze Adern", "Kohle", "item:Coal"), new Sub(0, "Kupferglut", "Kupfer", "art:3", .8), new Sub(1, "Eisenherz", "Eisen", "item:Iron"),
            new Sub(4, "Silbermond", "Silber", "item:Silver", .8, 2), new Sub(2, "Goldfieber", "Gold", "item:Gold", .5, 3),
            new Sub(28, "Titankraft", "Titan", "item:Titanium", .8, 3), new Sub(8, "Platinglanz", "Platin", "item:Platinum", .5, 4),
            new Sub(29, "Wolframwille", "Wolfram", "item:Tungsten", .6, 4) };
        Put(S("gems", Mining, "Funkelnde Beute", "Edelsteine", "item:Diamond", A(5, 12, 25, 100), A(30, 75, 150, 500), A(120, 300, 600), minLayer: 3));
        Put(S("gem", Mining, null, null, null, A(3, 8, 15, 60), A(20, 50, 100, 300), A(80, 200, 400), activity: "gems", metric: "ore", minLayer: 3)).subs = new[] {
            new Sub(32, "Glut des Granats", "Granate", null, 1, 3), new Sub(40, "Rubinfeuer", "Rubine", null, 1, 4), new Sub(33, "Diamantenglanz", "Diamanten", null, 1, 4) };
        Put(S("ultronium", Mining, "Ultroniumrausch", "Ultronium", "item:Ultronium", A(0, 0, 3, 10), A(0, 5, 12, 25), A(0, 0, 30), metric: "ore", minLayer: 5)).subs = new[] {
            new Sub(24, null, "Ultronium", "item:Ultronium", 1, 5) };
        Put(S("trees", Mining, "Ruf der Wälder", "Bäume gefällt", "item:Axe", A(3, 6, 12, 30), A(15, 35, 70, 150), A(60, 150, 300)));
        Put(S("grass", Mining, "Grüne Ernte", "Grasbüschel geerntet", "item:Scythe", A(15, 40, 80, 200), A(100, 250, 500, 1200), A(400, 1000, 2000)));
        Put(S("veins", Mining, "Ader um Ader", "Erzadern geleert", "item:Silver", A(3, 6, 12, 40), A(20, 45, 90, 250), A(80, 200, 400)));
        Put(S("blasted", Mining, "Sprengmeister", "Blöcke gesprengt", "item:Dynamite", A(20, 50, 100, 400), A(120, 300, 600, 2000), A(500, 1200, 2500)));
        Put(S("ore-variety", Mining, "Bunte Ausbeute", "Erzarten", "art:0", A(2, 3, 5, 8), A(3, 5, 7, 10), A(4, 6, 9), Distinct));
        // Exploration
        Put(S("depth", Exploration, "Ruf der Tiefe", "Blöcke Tiefe", "item:IronLadder", null, null, null, Max));
        Put(S("layers", Exploration, "Grenzgänger", "Schichten besucht", "item:IronLadder", A(2, 3, 0, 0), A(3, 4, 0, 0), null, Distinct, activity: "depth"));
        Put(S("regions", Exploration, "Unbetretene Pfade", "Gebiete erkundet", "item:Torche", A(4, 8, 15, 40), A(25, 50, 90, 200), A(100, 200, 350)));
        Put(S("cells", Exploration, "Verborgene Weiten", "Kartenfelder aufgedeckt", "item:Torche", A(200, 500, 1000, 3000), A(1500, 3500, 7000, 15000), A(6000, 15000, 30000)));
        Put(S("round-trips", Exploration, "Hin und zurück", null, "item:Ladder", A(1, 1, 1, 2), A(3, 3, 4, 6), A(10, 12, 15), activity: "returns", minLayer: 2)).atLeast = true;
        Put(S("returns", Exploration, "Sicher zurück", "Rückkehren aus der Tiefe", "item:Ladder", A(2, 4, 7, 15), A(10, 20, 35, 80), A(40, 80, 150), minLayer: 2));
        Put(S("full-returns", Exploration, "Volle Taschen", "volle Rückkehren", "item:Backpack", A(1, 2, 4, 10), A(5, 10, 20, 45), A(20, 40, 80)));
        Put(S("torches-placed", Exploration, "Lichtbringer", "Fackeln platziert", "art:5", A(5, 12, 25, 60), A(30, 70, 140, 300), A(120, 300, 600)));
        Put(S("ladders-placed", Exploration, "Kletterer", "Leitern platziert", "item:Ladder", A(10, 25, 50, 120), A(60, 150, 300, 700), A(250, 600, 1200)));
        Put(S("altars", Exploration, "Ruf des Altars", "Altar erreicht", "item:Ultronium", null, A(0, 0, 1, 0), A(0, 0, 1), Max, minLayer: 5));
        Put(S("discoveries", Exploration, "Geheimnisse im Stein", "Entdeckungen", "item:Torche", A(2, 4, 8, 25), A(12, 25, 50, 120), A(50, 100, 200), activity: "artifacts"));
        // Crafting
        Put(S("crafted", Crafting, "Meister am Amboss", "Gegenstände hergestellt", "art:2", A(10, 25, 50, 150), A(60, 150, 300, 800), A(250, 600, 1200), scales: true));
        Put(S("tools", Crafting, "Werkzeugschmiede", "Werkzeuge hergestellt", "item:IronPickaxe", A(1, 2, 3, 0), A(2, 4, 0, 0), null));
        Put(S("gear", Crafting, "Für den Aufbruch", "Ausrüstungen hergestellt", "item:Backpack", A(1, 2, 3, 0), A(2, 4, 0, 0), null));
        Put(S("upgrades", Crafting, "Aufrüsten", "Upgrades", null, A(1, 1, 2, 0), A(0, 2, 4, 0), null)).subs = new[] {
            new Sub(0, "Die nächste Klinge", "Spitzhacken-Upgrades", "item:IronPickaxe"), new Sub(1, "Funken des Lebens", "Energie-Upgrades", "item:EnergyStorageVial"),
            new Sub(2, "Schultern aus Stahl", "Tragkraft-Upgrades", "item:Backpack") };
        Put(S("dynamite", Crafting, "Donner der Schmiede", "Dynamit hergestellt", "item:Dynamite", A(3, 8, 15, 40), A(20, 50, 100, 250), A(80, 200, 400)));
        Put(S("medkits", Crafting, "Heilende Vorräte", "Medkits hergestellt", "item:Medkit", A(2, 4, 8, 0), A(10, 20, 35, 0), A(40, 80, 150)));
        Put(S("torches-crafted", Crafting, "Funken im Dunkel", "Fackeln hergestellt", "art:5", A(8, 20, 40, 100), A(50, 120, 240, 600), A(200, 500, 1000)));
        Put(S("ladders-crafted", Crafting, "Himmelsleiter", "Leitern hergestellt", "item:Ladder", A(10, 25, 50, 150), A(60, 150, 300, 800), A(250, 600, 1200)));
        Put(S("exotics", Crafting, "Fremde Künste", "Exotics hergestellt", "item:PercussionHammer", A(1, 2, 4, 0), A(3, 6, 12, 0), A(10, 25, 50), minLevel: 5));
        // Trade
        Put(S("sales", Trade, "Goldene Geschäfte", "$ Verkaufserlös", "art:4", A(1500, 4000, 10000, 30000), A(8000, 20000, 50000, 150000), A(30000, 80000, 200000))).layerFactors = SalesF;
        Put(S("sale-single", Trade, "Grosser Handschlag", "$ auf einmal", "art:4", A(400, 1000, 2500, 8000), A(1500, 4000, 10000, 25000), A(4000, 10000, 25000), Max)).layerFactors = SingleF;
        Put(S("run-sales", Trade, "Goldener Lauf", "$ in einem Run", "art:4", A(1500, 4000, 10000, 30000), A(6000, 15000, 40000, 100000), A(20000, 50000, 120000), RunMode)).layerFactors = SalesF;
        // Artifacts
        Put(S("artifacts", Artifacts, "Relikte der Vorzeit", "Artefakte geborgen", "artifact", A(2, 4, 8, 25), A(10, 20, 40, 80), A(40, 80, 150), activity: "artifacts"));
        var artifact = Put(S("artifact", Artifacts, "Spuren der Vorzeit", null, "artifact", A(1, 2, 4, 10), A(3, 6, 12, 25), A(10, 20, 40)));
        artifact.subs = new Sub[10];
        for (int i = 0; i < 10; i++) artifact.subs[i] = new Sub(i + 1, null, ArtNames[i], null, ArtFactors[i], ArtLayers[i]);
        Put(S("artifact-variety", Artifacts, "Spuren der Alten", "Artefaktarten", "artifact", A(2, 3, 4, 6), A(3, 4, 6, 8), A(4, 6, 8), Distinct));
        // Special and retired
        Put(S("diligence", Special, "Fleissige Woche", null, "art:8", null, null, null, Distinct));
        Put(S("legacy-depth-gain", Exploration, null, "Blöcke Tiefe", "art:1", null, null, null, metric: "depth-gain")).retired = true;
        Put(S("legacy-artifact-sighted", Artifacts, null, "Artefakttypen", "artifact", null, null, null, Distinct, metric: "artifact-sighted")).retired = true;
        var impossible = Put(S("legacy-impossible", Mining, null, null, null, null, null, null, metric: "unobtainable"));
        impossible.retired = true;
        impossible.subs = new[] { new Sub(31, null, null, "item:Obsidian"), new Sub(36, null, null, "item:Emerald") };
        // Monthly projects (activity = metric where §7.3 names none: accepted deviation 3)
        Project("project-depth", "Tiefenexpedition", "Blöcke Tiefe", "depth", "depth", null, Max);
        Project("project-ores", "Erzmagnat", "Erze", "resources", "ores", A(1500, 4000, 10000), scales: true);
        Project("project-sales", "Handelsimperium", "$ Verkaufserlös", "sales", "sales", A(20000, 60000, 150000), factors: SalesF);
        Project("project-regions", "Kartograf", "Gebiete erkundet", "regions", "regions", A(100, 250, 500));
        Project("project-blasted", "Grosse Sprengung", "Blöcke gesprengt", "blasted", "blasted", A(300, 800, 2000));
        Project("project-artifacts", "Schatzkammer", "Artefakte geborgen", "artifacts", "artifacts", A(15, 40, 100));
        Project("project-crafted", "Meisterschmiede", "Gegenstände hergestellt", "crafted", "crafted", A(200, 500, 1200), scales: true);
        Project("project-veins", "Aderjäger", "Erzadern geleert", "veins", "veins", A(50, 120, 300));
        Project("project-returns", "Expeditionsleiter", "Rückkehren aus der Tiefe", "returns", "returns", A(30, 70, 150), minLayer: 2);
        specList = list;
        specs = new Dictionary<string, Spec>();
        foreach (var s in list) specs[s.id] = s;
    }

    static Sub FindSub(Spec s, int subject)
    {
        if (s.subs != null) foreach (var x in s.subs) if (x.id == subject) return x;
        return null;
    }
    static double Growth(int layer, int level, ChallengeSettings st)
        => 1 + Math.Min(st.maxGrowthPercent, st.growthPercentPerLayer * (double)(layer - 1) + st.growthPercentPer10Levels * (double)((level - 1) / 10)) / 100;
    static double Step(double v) => v < 20 ? 1 : v < 100 ? 5 : v < 1000 ? 10 : v < 10000 ? 50 : 500;
    static int Nice(double v)
    {
        if (double.IsNaN(v) || v < 1) return 1;
        if (v >= 1e9) return 1000000000;
        double step = Step(v);
        return Math.Max(1, (int)(Math.Round(v / step, MidpointRounding.AwayFromZero) * step));
    }
    static bool Midpoint(double v)
    {
        if (double.IsNaN(v) || v < 1 || v >= 1e9) return false;
        double q = v / Step(v);
        return Math.Abs(q - Math.Floor(q) - .5) < 1e-6;
    }
    static int[] Amounts(Spec s, ChallengePeriod p, int layer)
    {
        if (s.id == "depth") return p == Daily ? DepthD[layer - 1] : p == Weekly ? DepthW[layer - 1] : null;
        if (s.id == "project-depth") return p == Monthly ? DepthP[layer - 1] : null;
        return p == Daily ? s.daily : p == Weekly ? s.weekly : p == Monthly ? s.monthly : null;
    }
    // Listed amount × growth/layer factor × subject factor, rounded nicely only when a factor other than 1 applies (accepted deviation 6).
    static int Raw(Spec s, ChallengePeriod p, int tier, int subject, int layer, int level, ChallengeSettings st, ref bool mid)
    {
        var a = Amounts(s, p, layer);
        if (a == null || tier < 0 || tier >= a.Length || a[tier] <= 0) return 0;
        double f = 1;
        if (s.scales) f *= Growth(layer, level, st);
        if (s.layerFactors != null) f *= s.layerFactors[layer - 1];
        var sub = FindSub(s, subject);
        if (sub != null) f *= sub.factor;
        if (f == 1) return a[tier];
        double v = a[tier] * f;
        if (Midpoint(v)) mid = true;
        return Nice(v);
    }
    static int TripSubject(int tier, int layer) => tier switch { 0 => Math.Max(2, layer - 2), 1 => Math.Max(2, layer - 1), 2 => layer, 3 => layer == 5 ? 5 : -1, _ => -1 };
    // A tier is offered only when it beats the next lower offered tier (round trips compare (layer subject, amount): accepted deviation 5).
    static int Target(Spec s, ChallengePeriod p, int tier, int subject, int layer, int level, ChallengeSettings st, ref bool mid)
    {
        int amount = Raw(s, p, tier, subject, layer, level, st, ref mid);
        if (amount <= 0) return 0;
        bool trips = s.id == "round-trips";
        for (int lower = tier - 1; lower >= 0; lower--) {
            int lowerSubject = trips ? TripSubject(lower, layer) : subject;
            if (trips && lowerSubject < 2) continue;
            int previous = Target(s, p, lower, lowerSubject, layer, level, st, ref mid);
            if (previous <= 0) continue;
            return subject > lowerSubject || subject == lowerSubject && amount > previous ? amount : 0;
        }
        return amount;
    }
    static List<int> Subjects(Spec s, ChallengePeriod p, int tier, int layer)
    {
        var r = new List<int>();
        if (s.id == "layer-blocks") { for (int i = 1; i <= layer; i++) if (i > 1 || layer <= 2) r.Add(i); }
        else if (s.id == "round-trips") { int trip = TripSubject(tier, layer); if (trip >= 2) r.Add(trip); }
        else if (s.id == "upgrades") { if (p == Weekly) r.Add(-1); else { r.Add(0); r.Add(1); r.Add(2); } }
        else if (s.subs == null) r.Add(-1);
        else foreach (var x in s.subs) if (x.minLayer <= layer) r.Add(x.id);
        return r;
    }
    static long Lim(int[] l, int i) => l != null && i >= 0 && i < l.Length ? l[i] : 0;
    static bool Gate(Spec s, ChallengePeriod p, int subject, int target, Ctx c)
    {
        switch (s.id) {
            case "ore-variety": return target <= OresAvail[c.L - 1];
            case "layers": return target <= c.L;
            case "artifact-variety": return target <= ArtsAvail[c.L - 1];
            case "ultronium": return p != Daily || c.hardness >= 8;
        }
        if (p != Daily && p != Weekly) return true;
        var l = c.limits;
        switch (s.id) {
            case "tools": return l != null && Lim(l, 1) >= target;
            case "gear": return l != null && Lim(l, 2) >= target;
            case "upgrades": return l != null && (subject < 0 ? Lim(l, 0) + Lim(l, 3) + Lim(l, 4) : Lim(l, subject == 0 ? 0 : subject == 1 ? 3 : 4)) >= target;
            case "dynamite": return l == null || Lim(l, 5) > 0;
            case "medkits": return l == null || Lim(l, 6) > 0;
        }
        return true;
    }
    static List<Cand> Candidates(ChallengePeriod p, int tier, Ctx c, ChallengeSettings st)
    {
        var r = new List<Cand>();
        if (tier == 3 && c.L < st.xxlMinLayer) return r;
        foreach (var s in specList) {
            if (s.retired || s.project || s.family == Special || c.L < s.minLayer || c.Lv < s.minLevel) continue;
            foreach (int subject in Subjects(s, p, tier, c.L)) {
                bool mid = false;
                int target = Target(s, p, tier, subject, c.L, c.Lv, st, ref mid);
                if (target > 0 && Gate(s, p, subject, target, c)) r.Add(new Cand { spec = s, subject = subject, target = target });
            }
        }
        return r;
    }
    static int[] Stages(Spec s, Ctx c, ChallengeSettings st)
    {
        var r = new int[3];
        bool mid = false;
        for (int i = 0; i < 3; i++) {
            r[i] = Raw(s, Monthly, i, -1, c.L, c.Lv, st, ref mid);
            if (r[i] <= 0 || i > 0 && r[i] <= r[i - 1]) return null;
        }
        return r;
    }
    static List<Spec> Projects(Ctx c, ChallengeSettings st)
    {
        var r = new List<Spec>();
        foreach (var s in specList) if (s.project && c.L >= s.minLayer && c.Lv >= s.minLevel && Stages(s, c, st) != null) r.Add(s);
        return r;
    }
    static readonly Dictionary<string, object> cache = new();
    static string SettingsKey(ChallengeSettings st) => string.Join("|", st.growthPercentPerLayer, st.growthPercentPer10Levels, st.maxGrowthPercent, st.xxlMinLayer);
    static List<Cand>[] CandidateSet(ChallengePeriod p, Ctx c, ChallengeSettings st)
    {
        string key = "c|" + p + "|" + c + "|" + SettingsKey(st);
        if (cache.TryGetValue(key, out var hit)) return (List<Cand>[])hit;
        var set = new List<Cand>[4];
        for (int tier = 0; tier < 4; tier++) set[tier] = Candidates(p, tier, c, st);
        cache[key] = set;
        return set;
    }
    static List<Spec> ProjectSet(Ctx c, ChallengeSettings st)
    {
        string key = "p|" + c + "|" + SettingsKey(st);
        if (cache.TryGetValue(key, out var hit)) return (List<Spec>)hit;
        var set = Projects(c, st);
        cache[key] = set;
        return set;
    }
    static int DilTasks(ChallengeSettings st) => Math.Max(0, Math.Min(st.diligenceDailyTasks, st.daily.countEasy + st.daily.countMedium + st.daily.countHard));
    static string ExpName(Spec s, int subject) => FindSub(s, subject)?.name ?? s.name;
    static string ExpUnit(Spec s, int subject, ChallengeSettings st)
    {
        if (s.id == "layer-blocks") return "Blöcke aus Schicht " + subject;
        if (s.id == "round-trips") return "Rückkehr ab Schicht " + subject;
        if (s.id == "diligence") { int n = DilTasks(st); return n == 1 ? "Tage mit 1 Tagesaufgabe" : "Tage mit " + n + " Tagesaufgaben"; } // singular: accepted deviation 11
        return FindSub(s, subject)?.unit ?? s.unit;
    }
    static string ExpIcon(Spec s, int subject) => FindSub(s, subject)?.icon ?? s.icon;
    static bool ValidIcon(string icon) => icon == "artifact" || icon != null && (icon.StartsWith("art:", StringComparison.Ordinal) && IconArt.Contains(icon.Substring(4))
        || icon.StartsWith("item:", StringComparison.Ordinal) && IconItems.Contains(icon.Substring(5)));
    static int Seed(long start, ChallengePeriod p, int salt) => unchecked((int)(start ^ (start >> 32)) ^ ((int)p * 0x2F6B3A1) ^ salt);
    static int FamilyOf(MetaChallengeTask t) => t.id != null && specs.TryGetValue(t.id, out var s) && !s.project ? (int)s.family : (int)Special;
    static int Compare(MetaChallengeTask a, MetaChallengeTask b)
    {
        int r = a.tier.CompareTo(b.tier);
        if (r == 0) r = FamilyOf(a).CompareTo(FamilyOf(b));
        if (r == 0) r = string.CompareOrdinal(a.id, b.id);
        return r != 0 ? r : a.subject.CompareTo(b.subject);
    }

    // ---------- Round helpers

    static string Join(int[] a) => a == null ? "-" : string.Join(",", a);
    static bool Same(int[] a, int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    static string Dump(MetaChallengeRound r, bool withXp = true)
    {
        if (r == null) return "null";
        var b = new StringBuilder();
        b.Append(r.period).Append('|').Append(r.format).Append('|').Append(r.startUtc).Append('|').Append(r.endUtc).Append('|').Append(r.dayIndex).Append('|')
            .Append(withXp ? r.bonusXp : 0).Append('|').Append(r.bonusPaid).Append('|').Append(r.diligenceCredited).Append('\n');
        if (r.tasks != null) foreach (var t in r.tasks) {
            if (t == null) { b.Append("null\n"); continue; }
            b.Append(t.id).Append('|').Append(t.name).Append('|').Append(t.metric).Append('|').Append(t.unit).Append('|').Append(t.mode).Append('|')
                .Append(t.subject).Append('|').Append(t.atLeast).Append('|').Append(t.tier).Append('|').Append(t.target).Append('|').Append(withXp ? t.xp : 0).Append('|')
                .Append(t.progress).Append('|').Append(t.done).Append('|').Append(withXp ? t.paid : 0).Append('|').Append(Join(t.stageTargets)).Append('|')
                .Append(withXp ? Join(t.stageXp) : "").Append('|').Append(t.stagesPaid).Append('\n');
        }
        if (r.masks != null) foreach (var m in r.masks) b.Append(m == null ? "null" : m.metric + "=" + m.mask).Append(';');
        b.Append('\n');
        if (r.runValues != null) foreach (var v in r.runValues) b.Append(v == null ? "null" : v.metric + "/" + v.runId + "=" + v.value).Append(';');
        return b.ToString();
    }
    static MetaChallengeTask CloneTask(MetaChallengeTask t) => t == null ? null : new MetaChallengeTask {
        id = t.id, name = t.name, metric = t.metric, unit = t.unit, mode = t.mode, subject = t.subject, atLeast = t.atLeast, tier = t.tier, target = t.target,
        xp = t.xp, progress = t.progress, done = t.done, paid = t.paid, stageTargets = (int[])t.stageTargets?.Clone(), stageXp = (int[])t.stageXp?.Clone(),
        stagesPaid = t.stagesPaid };
    static MetaChallengeRound Clone(MetaChallengeRound r)
    {
        if (r == null) return null;
        var c = new MetaChallengeRound { period = r.period, format = r.format, startUtc = r.startUtc, endUtc = r.endUtc, dayIndex = r.dayIndex, bonusXp = r.bonusXp,
            bonusPaid = r.bonusPaid, diligenceCredited = r.diligenceCredited, tasks = r.tasks == null ? null : new List<MetaChallengeTask>(),
            masks = r.masks == null ? null : new List<MetaChallengeMask>(), runValues = r.runValues == null ? null : new List<MetaChallengeRunValue>() };
        if (r.tasks != null) foreach (var t in r.tasks) c.tasks.Add(CloneTask(t));
        if (r.masks != null) foreach (var m in r.masks) c.masks.Add(m == null ? null : new MetaChallengeMask { metric = m.metric, mask = m.mask });
        if (r.runValues != null) foreach (var v in r.runValues) c.runValues.Add(v == null ? null : new MetaChallengeRunValue { metric = v.metric, runId = v.runId, value = v.value });
        return c;
    }
    static void AddGain(ref ChallengeGain total, ChallengeGain g) { total.xp += g.xp; total.completed += g.completed; total.changed |= g.changed; }

    // Records exactly what each open task still needs (Add/Run: the rest, Max: the target, Distinct: new bits) and sums the gains.
    static ChallengeGain CompleteAll(MetaChallengeRound round)
    {
        var total = new ChallengeGain();
        foreach (var t in round.tasks) {
            if (t == null || t.done) continue;
            switch ((ChallengeMode)t.mode) {
                case Add: AddGain(ref total, ChallengeEngine.Record(round, t.metric, (long)t.target - t.progress, t.subject)); break;
                case Max: AddGain(ref total, ChallengeEngine.RecordMax(round, t.metric, t.target, t.subject)); break;
                case Distinct: for (int bit = 0; bit <= 30 && !t.done; bit++) AddGain(ref total, ChallengeEngine.RecordDistinct(round, t.metric, bit, t.subject)); break;
                case RunMode: AddGain(ref total, ChallengeEngine.RecordRun(round, t.metric, "core-run", (long)t.target - t.progress, t.subject)); break;
            }
        }
        return total;
    }
    static long ExpectedPayout(MetaChallengeRound round)
    {
        long sum = 0;
        foreach (var t in round.tasks) if (t.tier == (int)ChallengeTier.Project) { foreach (int x in t.stageXp) sum += x; } else sum += t.xp;
        if (round.tasks.Count > 0 && round.bonusXp > 0) sum += round.bonusXp;
        return sum;
    }
    static void CheckCompletion(string tag, MetaChallengeRound round)
    {
        long expected = ExpectedPayout(round);
        var total = CompleteAll(round);
        int open = 0;
        foreach (var t in round.tasks) if (!t.done) open++;
        Check(open == 0, () => tag + ": " + open + " task(s) still open after recording their targets");
        Check(total.completed == round.tasks.Count && ChallengeEngine.CompletedCount(round) == round.tasks.Count,
            () => tag + ": completed " + total.completed + " / CompletedCount " + ChallengeEngine.CompletedCount(round) + " of " + round.tasks.Count);
        Check(total.xp == expected && ChallengeEngine.PaidXp(round) == expected, () => tag + ": paid " + total.xp + " / PaidXp " + ChallengeEngine.PaidXp(round) + ", expected " + expected);
        Check(round.bonusPaid == (round.tasks.Count > 0 && round.bonusXp > 0), () => tag + ": bonusPaid " + round.bonusPaid);
        Check(ChallengeEngine.Sanitize(Clone(round)), () => tag + ": Sanitize rejects the completed round");
        var again = new ChallengeGain();
        foreach (var t in round.tasks) {
            AddGain(ref again, ChallengeEngine.Record(round, t.metric, 5, t.subject));
            AddGain(ref again, ChallengeEngine.RecordMax(round, t.metric, t.target + 5L, t.subject));
        }
        Check(again.xp == 0 && again.completed == 0, () => tag + ": completed round paid again (" + again.xp + " XP)");
    }

    // ---------- Draw validation (every drawn round goes through this)

    sealed class Outcome { public bool xxl, eligible; public int repeats; }

    static Outcome ValidateRound(string tag, MetaChallengeRound round, ChallengePeriod p, long start, long end, int day, Ctx ctx, ChallengeSettings settings,
        MetaChallengeRound previous, bool exact, Dictionary<string, int> drawn)
    {
        var o = new Outcome();
        var st = (settings ?? new ChallengeSettings()).Sanitized();
        var ps = st.Period(p);
        o.eligible = ctx.L >= st.xxlMinLayer;
        if (!Check(round != null && round.tasks != null && round.masks != null && round.runValues != null, tag + ": round or one of its lists is null")) return o;
        Check(round.period == (int)p && round.format == 2 && round.startUtc == start && round.endUtc == end && round.dayIndex == day,
            () => tag + ": header " + round.period + "/" + round.format + "/" + round.startUtc + "/" + round.endUtc + "/" + round.dayIndex);
        Check(!round.bonusPaid && !round.diligenceCredited && round.masks.Count == 0 && round.runValues.Count == 0, () => tag + ": fresh round carries state");
        var tasks = round.tasks;
        var cands = CandidateSet(p, ctx, st);
        bool diligence = p == Weekly && st.diligenceDays > 0 && ps.countMedium > 0 && DilTasks(st) > 0;
        var used = new HashSet<string>();
        if (diligence) used.Add("diligence");
        MetaChallengeTask project = null;
        foreach (var t in tasks) if (t != null && t.tier == (int)ChallengeTier.Project) { project = t; if (t.id != null && specs.TryGetValue(t.id, out var s)) used.Add(s.activity); }
        int easy = ps.countEasy, medium = ps.countMedium - (diligence ? 1 : 0), hard = ps.countHard;
        bool rolled = new Random(Seed(start, p, XxlSalt)).NextDouble() * 100 < ps.xxlChancePercent;
        bool xxlCandidate = false;
        foreach (var c in cands[3]) if (!used.Contains(c.spec.activity)) { xxlCandidate = true; break; }
        bool xxlExpected = rolled && ctx.L >= st.xxlMinLayer && (hard > 0 || medium > 0) && xxlCandidate;
        if (xxlExpected) { if (hard > 0) hard--; else medium--; }
        var count = new int[5];
        int dilCount = 0;
        foreach (var t in tasks) if (t != null) { if (t.id == "diligence") dilCount++; else if (t.tier >= 0 && t.tier <= 4) count[t.tier]++; }
        o.xxl = count[3] > 0;
        Check(o.xxl == xxlExpected, () => tag + ": XXL " + o.xxl + ", expected " + xxlExpected + " (seed roll " + rolled + ")");
        Check(dilCount == (diligence ? 1 : 0), () => tag + ": " + dilCount + " diligence task(s), expected " + (diligence ? 1 : 0));
        int projects = p == Monthly && ProjectSet(ctx, st).Count > 0 ? 1 : 0;
        Check(count[4] == projects, () => tag + ": " + count[4] + " project(s), expected " + projects);
        if (exact) Check(count[0] == easy && count[1] == medium && count[2] == hard && count[3] == (xxlExpected ? 1 : 0),
            () => tag + ": tiers E/M/H/X " + count[0] + "/" + count[1] + "/" + count[2] + "/" + count[3] + ", expected " + easy + "/" + medium + "/" + hard + "/" + (xxlExpected ? 1 : 0));
        else Check(count[0] <= Math.Max(0, easy) && count[1] <= Math.Max(0, medium) && count[2] <= Math.Max(0, hard) && count[3] <= 1,
            () => tag + ": tiers E/M/H/X " + count[0] + "/" + count[1] + "/" + count[2] + "/" + count[3] + " exceed the slots");
        Check(tasks.Count <= 16, () => tag + ": " + tasks.Count + " tasks");
        Check(round.bonusXp == (tasks.Count > 0 ? ps.completionBonusXp : 0), () => tag + ": bonusXp " + round.bonusXp);
        if (project != null) Check(tasks[0] == project, tag + ": project is not the first task");
        for (int i = 1; i < tasks.Count; i++) {
            var a = tasks[i - 1]; var b = tasks[i];
            if (a == null || b == null || a.tier == (int)ChallengeTier.Project) continue;
            Check(Compare(a, b) <= 0, () => tag + ": order " + a.id + "/T" + a.tier + " before " + b.id + "/T" + b.tier);
        }
        var activities = new HashSet<string>();
        var families = new int[6];
        Dictionary<string, HashSet<int>> previousSubjects = null;
        HashSet<string> previousActivities = null;
        string previousProject = null;
        if (previous?.tasks != null) {
            previousSubjects = new Dictionary<string, HashSet<int>>(); previousActivities = new HashSet<string>();
            foreach (var t in previous.tasks) {
                if (t == null || string.IsNullOrEmpty(t.id)) continue;
                if (!previousSubjects.TryGetValue(t.id, out var set)) previousSubjects[t.id] = set = new HashSet<int>();
                set.Add(t.subject);
                if (specs.TryGetValue(t.id, out var s)) { previousActivities.Add(s.activity); if (s.project) previousProject = t.id; }
            }
        }
        foreach (var t in tasks) {
            if (!Check(t != null, tag + ": null task")) continue;
            string tt = tag + " " + t.id + "/" + t.subject + "/T" + t.tier;
            if (t.id == null || !specs.TryGetValue(t.id, out var s)) { Fail(tt + ": template unknown to the spec"); continue; }
            Check(!s.retired, () => tt + ": retired template drawn");
            Check(Array.IndexOf(Forbidden, t.subject) < 0 && t.metric != "unobtainable", () => tt + ": unobtainable subject offered");
            Check(activities.Add(s.activity), () => tt + ": activity '" + s.activity + "' twice in one round");
            Check(t.progress == 0 && !t.done && t.paid == 0 && t.stagesPaid == 0, () => tt + ": fresh task carries progress");
            Check(t.metric == s.metric && t.mode == (int)s.mode && t.atLeast == s.atLeast, () => tt + ": metric/mode/atLeast " + t.metric + "/" + t.mode + "/" + t.atLeast);
            Text(tt, t.name, true); Text(tt, t.unit, false);
            if (s.project) {
                Check(p == Monthly && t.tier == (int)ChallengeTier.Project && t.subject == -1 && t.xp == 0, () => tt + ": project fields");
                var stages = Stages(s, ctx, st);
                Check(stages != null && Same(t.stageTargets, stages) && t.target == stages[2], () => tt + ": stages " + Join(t.stageTargets) + ", spec " + Join(stages));
                Check(Same(t.stageXp, new[] { st.projectStage1Xp, st.projectStage2Xp, st.projectStage3Xp }), () => tt + ": stage XP " + Join(t.stageXp));
                Check(t.name == s.name && t.unit == s.unit, () => tt + ": name/unit '" + t.name + "' / '" + t.unit + "'");
                if (previousProject != null) Check(t.id != previousProject, () => tt + ": same project as the previous month");
            } else if (s.id == "diligence") {
                Check(p == Weekly && t.tier == (int)ChallengeTier.Medium && t.subject == -1 && t.target == st.diligenceDays && t.xp == ps.xpMedium,
                    () => tt + ": diligence target/xp " + t.target + "/" + t.xp);
                Check(t.name == s.name && t.unit == ExpUnit(s, -1, st), () => tt + ": diligence name/unit '" + t.name + "' / '" + t.unit + "'");
            } else {
                families[(int)s.family]++;
                Check(t.xp == ps.Xp(t.tier) && t.tier >= 0 && t.tier <= 3, () => tt + ": xp " + t.xp + ", settings " + ps.Xp(t.tier));
                bool found = false; Cand match = default;
                if (t.tier >= 0 && t.tier <= 3) foreach (var c in cands[t.tier]) if (c.spec == s && c.subject == t.subject) { match = c; found = true; break; }
                if (Check(found, () => tt + ": not eligible for " + ctx + " (layer/level/subject/gate/tier rule)")) {
                    if (t.target == match.target) Pass();
                    else {
                        bool mid = false;
                        Target(s, p, t.tier, t.subject, ctx.L, ctx.Lv, st, ref mid);
                        Check(mid && Math.Abs(t.target - match.target) <= Step(match.target), () => tt + ": target " + t.target + ", spec " + match.target + " (" + ctx + ")");
                    }
                }
                Check(t.name == ExpName(s, t.subject), () => tt + ": name '" + t.name + "', spec '" + ExpName(s, t.subject) + "'");
                Check(t.unit == ExpUnit(s, t.subject, st), () => tt + ": unit '" + t.unit + "', spec '" + ExpUnit(s, t.subject, st) + "'");
                string icon = ExpIcon(s, t.subject);
                if (icon != null) Check(ChallengeCatalog.Icon(t.id, t.metric, t.subject) == icon, () => tt + ": icon " + ChallengeCatalog.Icon(t.id, t.metric, t.subject));
                Check(ChallengeCatalog.Name(t.id, t.subject) == t.name && ChallengeCatalog.Unit(t.id, t.metric, t.subject) == t.unit, () => tt + ": catalog Name/Unit differ from the stored snapshot");
                if (previousSubjects != null && previousSubjects.TryGetValue(t.id, out var avoided) && avoided.Contains(t.subject) && t.tier >= 0 && t.tier <= 3) {
                    bool fresh = false;
                    foreach (var c in cands[t.tier]) if (c.spec == s && !avoided.Contains(c.subject)) { fresh = true; break; }
                    Check(!fresh, () => tt + ": repeats the previous round's subject although another subject was available");
                }
                if (previousActivities != null && previousActivities.Contains(s.activity)) o.repeats++;
            }
            if (drawn != null) { string key = p + ":" + t.id; drawn.TryGetValue(key, out int n); drawn[key] = n + 1; }
        }
        var caps = Caps[(int)p];
        if (caps != null) for (int f = 0; f < 5; f++) { int fam = f; Check(families[fam] <= caps[fam], () => tag + ": family " + (ChallengeFamily)fam + " " + families[fam] + " > cap " + caps[fam]); }
        if (exact && easy + medium + hard + (xxlExpected ? 1 : 0) >= 3) for (int f = 0; f < 3; f++) { int fam = f; Check(families[fam] >= 1, () => tag + ": no " + (ChallengeFamily)fam + " task (minimum 1)"); }
        return o;
    }

    // ---------- Catalog

    static void CatalogChecks()
    {
        Check((int)Daily == 0 && (int)Weekly == 1 && (int)Lifetime == 2 && (int)Monthly == 3, "ChallengePeriod values Daily 0, Weekly 1, Lifetime 2, Monthly 3");
        Check((int)Mining == 0 && (int)Exploration == 1 && (int)Crafting == 2 && (int)Trade == 3 && (int)Artifacts == 4 && (int)Special == 5, "ChallengeFamily order");
        Check((int)ChallengeTier.Easy == 0 && (int)ChallengeTier.Medium == 1 && (int)ChallengeTier.Hard == 2 && (int)ChallengeTier.Xxl == 3 && (int)ChallengeTier.Project == 4, "ChallengeTier order");
        Check((int)Add == 0 && (int)Max == 1 && (int)Distinct == 2 && (int)RunMode == 3, "ChallengeMode order");
        var task = new MetaChallengeTask();
        Check(task.subject == -1 && !task.atLeast && task.progress == 0 && !task.done && task.paid == 0 && task.stagesPaid == 0, "MetaChallengeTask defaults (subject -1)");
        var fresh = new MetaChallengeRound();
        Check(fresh.format == 2 && fresh.tasks != null && fresh.masks != null && fresh.runValues != null && !fresh.bonusPaid && !fresh.diligenceCredited, "MetaChallengeRound defaults (format 2, lists)");
        var context = new ChallengeDrawContext();
        Check(context.reachedLayer == 1 && context.level == 1 && context.craftLimits == null && context.pickaxeHardness == 0, "ChallengeDrawContext defaults");
        var gain = default(ChallengeGain);
        Check(gain.xp == 0 && gain.completed == 0 && !gain.changed, "ChallengeGain default");
        var ids = new HashSet<string>();
        foreach (var t in ChallengeCatalog.Templates) {
            if (!Check(t != null && !string.IsNullOrEmpty(t.id), "null template or empty id")) continue;
            Check(ids.Add(t.id), () => "duplicate template id " + t.id);
            Check(ChallengeCatalog.Template(t.id) == t, () => "Template(" + t.id + ") lookup");
            Check(!string.IsNullOrEmpty(t.metric) && !string.IsNullOrEmpty(t.activity) && !string.IsNullOrEmpty(t.name) && !string.IsNullOrEmpty(t.unit), () => t.id + ": empty metric/activity/name/unit");
            Check(ValidIcon(t.icon), () => t.id + ": icon " + t.icon);
        }
        Check(ChallengeCatalog.Template(null) == null && ChallengeCatalog.Template("no-such-template") == null, "Template(null / unknown) returns null");
        foreach (var s in specList) Check(ids.Contains(s.id), () => "template " + s.id + " missing");
        foreach (var id in ids) Check(specs.ContainsKey(id), () => "template " + id + " is not in the spec");
        foreach (var s in specList) {
            var t = ChallengeCatalog.Template(s.id);
            if (t == null) continue;
            Eq(t.metric, s.metric, s.id + " metric");
            Eq(t.mode, s.mode, s.id + " mode");
            Eq(t.activity, s.activity, s.id + " activity");
            Eq(t.project, s.project, s.id + " project");
            Eq(t.retired, s.retired, s.id + " retired");
            Check(s.retired ? t.weight == 0 : t.weight > 0, () => s.id + ": weight " + t.weight);
            if (!s.project && !s.retired) Eq(t.family, s.family, s.id + " family");
            Eq(t.minLayer, s.minLayer, s.id + " minLayer");
            Eq(t.minLevel, s.minLevel, s.id + " minLevel");
            if (s.name != null) Eq(t.name, s.name, s.id + " name");
            if (s.subs == null && s.unit != null) Eq(ChallengeCatalog.Unit(s.id, s.metric, -1), s.unit, s.id + " unit");
            if (s.subs == null && s.icon != null) Eq(ChallengeCatalog.Icon(s.id, s.metric, -1), s.icon, s.id + " icon");
            if (s.subs != null) foreach (var x in s.subs) {
                string expectedName = ExpName(s, x.id), expectedUnit = ExpUnit(s, x.id, new ChallengeSettings()), expectedIcon = ExpIcon(s, x.id);
                if (expectedName != null) Eq(ChallengeCatalog.Name(s.id, x.id), expectedName, s.id + "/" + x.id + " name");
                if (expectedUnit != null) Eq(ChallengeCatalog.Unit(s.id, s.metric, x.id), expectedUnit, s.id + "/" + x.id + " unit");
                if (expectedIcon != null) Eq(ChallengeCatalog.Icon(s.id, s.metric, x.id), expectedIcon, s.id + "/" + x.id + " icon");
                Check(ValidIcon(ChallengeCatalog.Icon(s.id, s.metric, x.id)), () => s.id + "/" + x.id + ": icon " + ChallengeCatalog.Icon(s.id, s.metric, x.id));
            }
        }
        for (int n = 2; n <= 5; n++) Eq(ChallengeCatalog.Unit("round-trips", "round-trips", n), "Rückkehr ab Schicht " + n, "round-trips unit " + n);
        Eq(ChallengeCatalog.Name("upgrades", -1), "Aufrüsten", "weekly upgrades name");
        Eq(ChallengeCatalog.Unit("upgrades", "upgrades", -1), "Upgrades", "weekly upgrades unit");
        Eq(ChallengeCatalog.Unit("diligence", "diligence", -1), "Tage mit 3 Tagesaufgaben", "diligence unit with default settings");
        Eq(ChallengeCatalog.Unit("legacy-impossible", "unobtainable", 31) != "" && ChallengeCatalog.Unit("legacy-impossible", "unobtainable", 36) != "", true, "legacy-impossible units");
        // Helpers
        string[] tiers = { "Leicht", "Mittel", "Schwer", "XXL", "Projekt" };
        for (int i = 0; i < 5; i++) Eq(ChallengeCatalog.TierName(i), tiers[i], "TierName(" + i + ")");
        Eq(ChallengeCatalog.TierName(5), "", "TierName(5)");
        string[] medals = { "", "Bronze", "Silber", "Gold", "Platin", "" };
        for (int i = 0; i < 6; i++) Eq(ChallengeCatalog.MedalName(i), medals[i], "MedalName(" + i + ")");
        string[] familyNames = { "Abbau", "Erkundung", "Handwerk", "Handel", "Artefakte" };
        for (int i = 0; i < 5; i++) Eq(ChallengeCatalog.FamilyName((ChallengeFamily)i), familyNames[i], "FamilyName(" + (ChallengeFamily)i + ")");
        Check(!string.IsNullOrEmpty(ChallengeCatalog.FamilyName(Special)), "FamilyName(Special) not empty");
        for (int i = 1; i <= 10; i++) Eq(ChallengeCatalog.ArtifactName(i), ArtNames[i - 1], "ArtifactName(" + i + ")");
        Check(ChallengeCatalog.ArtifactName(0) == "" && ChallengeCatalog.ArtifactName(11) == "", "ArtifactName out of range is empty");
        int[] oreBits = { 3, 0, 1, 4, 32, 2, 28, 8, 29, 33, 40, 24 };
        for (int i = 0; i < oreBits.Length; i++) Eq(ChallengeCatalog.OreBit(oreBits[i]), i, "OreBit(item " + oreBits[i] + ")");
        foreach (int item in Forbidden) Check(ChallengeCatalog.OreBit(item) < 0, () => "OreBit(" + item + ") must not give an unobtainable item a variety bit");
        for (int layer = 1; layer <= 5; layer++) {
            Eq(ChallengeCatalog.AvailableOres(layer), OresAvail[layer - 1], "AvailableOres(" + layer + ")");
            Eq(ChallengeCatalog.AvailableArtifacts(layer), ArtsAvail[layer - 1], "AvailableArtifacts(" + layer + ")");
        }
        // Run-objective metrics without template (§7) and every §4 metric
        string[,] runUnits = { { "variety", "Erzarten" }, { "resources", "Erze" }, { "crafts", "Herstellungen" }, { "regions", "Gebiete" }, { "depth", "Blöcke Tiefe" },
            { "discoveries", "Entdeckungen" }, { "exotics", "Exotics hergestellt" }, { "rare", "seltene Erzarten" }, { "victory", "Siege" } };
        for (int i = 0; i < runUnits.GetLength(0); i++) {
            Eq(ChallengeCatalog.Unit(null, runUnits[i, 0], -1), runUnits[i, 1], "Unit(null, " + runUnits[i, 0] + ")");
            string icon = ChallengeCatalog.Icon(null, runUnits[i, 0], -1);
            Check(ValidIcon(icon), () => "Icon(null, " + runUnits[i, 0] + ") = " + icon);
        }
        string[] metrics = { "blocks", "layer-blocks", "blasted", "resources", "ore", "gems", "ore-variety", "veins", "trees", "grass", "depth", "depth-gain", "regions",
            "cells", "layers", "discoveries", "altars", "round-trips", "returns", "full-returns", "torches-placed", "ladders-placed", "crafted", "tools", "dynamite",
            "medkits", "torches-crafted", "ladders-crafted", "exotics", "gear", "upgrades", "sales", "run-sales", "sale-single", "artifacts", "artifact",
            "artifact-variety", "artifact-sighted", "diligence", "unobtainable", "tasks-done" };
        int[] probe = { -1, 0, 1, 2, 3, 24, 31, 33 };
        foreach (string metric in metrics) foreach (int subject in probe) {
            string unit = ChallengeCatalog.Unit(null, metric, subject), icon = ChallengeCatalog.Icon(null, metric, subject);
            Check(!string.IsNullOrEmpty(unit) && ValidIcon(icon), () => "metric " + metric + "/" + subject + ": unit '" + unit + "', icon '" + icon + "'");
        }
        Eq(ChallengeCatalog.Unit(null, "ore", 33), "Diamanten", "Unit(null, ore, Diamond)");
        Eq(ChallengeCatalog.Unit(null, "layer-blocks", 3), "Blöcke aus Schicht 3", "Unit(null, layer-blocks, 3)");
        Eq(ChallengeCatalog.Unit(null, "artifact", 3), "Jademaske", "Unit(null, artifact, 3)");
        Eq(ChallengeCatalog.Unit(null, "sales", -1), "$ Verkaufserlös", "Unit(null, sales)");
        Eq(ChallengeCatalog.Unit(null, "tasks-done", -1), "Aufgaben erledigt", "Unit(null, tasks-done)");
        Eq(ChallengeCatalog.Icon(null, "tasks-done", -1), "art:8", "Icon(null, tasks-done)");
        Check(ChallengeCatalog.Unit(null, null, -1) != null && ChallengeCatalog.Icon(null, null, -1) != null && ChallengeCatalog.Name(null, -1) != null, "null metric/template handled");
    }

    // ---------- Milestones (§7.5)

    static readonly string[] LineTable = {
        "line-depth;Abstieg in die Tiefe;depth;0;Blöcke Tiefe;item:IronLadder;depth-100 100 250 L,depth-250 250 600 L,depth-500 500 1200 L,depth-1000 1000 2400 L",
        "line-exotics;Meister fremder Künste;total-exotics;0;Exotics;item:PercussionHammer;exotic-first 1 400 L,lifetime-exotics 3 1600 L",
        "line-victories;Tiefenhalls Champion;total-victory;0;Siege;item:Ultronium;first-victory 1 3000 L,lifetime-victories 5 8000 L,ms-victories-3 15 12000",
        "line-gem-variety;Edelsteinjäger;rare;0;seltene Funde in einem Run;item:Diamond;gem-variety 3 800 L,ms-gem-variety-2 4 1600",
        "line-ores;Bergbaulegende;total-resources;0;Erze;art:0;lifetime-ores 5000 4000 L,ms-ores-2 25000 8000,ms-ores-3 100000 16000",
        "line-coal;Schwarzes Gold;total-coal;0;Kohle;item:Coal;lifetime-coal 2000 1500 L,ms-coal-2 10000 3000,ms-coal-3 40000 6000",
        "line-copper;Herr der Kupferadern;total-copper;0;Kupfer;art:3;lifetime-copper 1000 1800 L,ms-copper-2 5000 3600,ms-copper-3 20000 7200",
        "line-iron;Eisenbaron;total-iron;0;Eisen;item:Iron;lifetime-iron 1000 2000 L,ms-iron-2 10000 4000,ms-iron-3 40000 8000",
        "line-silver;Silberkönig;total-silver;0;Silber;item:Silver;lifetime-silver 500 2500 L,ms-silver-2 5000 5000,ms-silver-3 20000 10000",
        "line-gold;Goldene Ära;total-gold;0;Gold;item:Gold;lifetime-gold 250 3000 L,ms-gold-2 2500 6000,ms-gold-3 10000 12000",
        "line-regions;Weltenwanderer;total-regions;0;Gebiete;item:Torche;lifetime-regions 250 2000 L,ms-regions-2 1500 4000,ms-regions-3 5000 8000",
        "line-discoveries;Geheimnisse des Berges;total-discoveries;0;Entdeckungen;item:Torche;lifetime-discoveries 25 3000 L,ms-discoveries-2 150 6000,ms-discoveries-3 500 12000",
        "line-gems;Juwelensammler;total-gems;0;Edelsteine;item:Diamond;ms-gems-1 100 1000,ms-gems-2 1000 2500,ms-gems-3 5000 5000",
        "line-ultronium;Ultroniumfürst;total-ultronium;0;Ultronium;item:Ultronium;ms-ultronium-1 50 1000,ms-ultronium-2 250 2500,ms-ultronium-3 1000 5000",
        "line-blasted;Meister der Sprengung;blasted;1;Blöcke gesprengt;item:Dynamite;ms-blasted-1 500 1000,ms-blasted-2 5000 2500,ms-blasted-3 25000 5000",
        "line-torches;Lichtbringer;torches-placed;1;Fackeln platziert;art:5;ms-torches-1 100 1000,ms-torches-2 1000 2500,ms-torches-3 5000 5000",
        "line-ladders;Himmelsstürmer;ladders-placed;1;Leitern platziert;item:Ladder;ms-ladders-1 200 1000,ms-ladders-2 2000 2500,ms-ladders-3 10000 5000",
        "line-full-returns;Schwer beladen;full-returns;1;volle Rückkehren;item:Backpack;ms-full-returns-1 10 1000,ms-full-returns-2 100 2500,ms-full-returns-3 500 5000",
        "line-returns;Heimkehrer;returns;1;Rückkehren;item:Ladder;ms-returns-1 25 1000,ms-returns-2 250 2500,ms-returns-3 1000 5000",
        "line-veins;Aderleerer;veins;1;Erzadern geleert;item:Silver;ms-veins-1 50 1000,ms-veins-2 500 2500,ms-veins-3 2500 5000",
        "line-sales;Handelsfürst;sales;1;$ Verkaufserlös;art:4;ms-sales-1 50000 1000,ms-sales-2 1000000 2500,ms-sales-3 10000000 5000",
        "line-crafted;Meisterhandwerk;crafted;1;Gegenstände hergestellt;art:2;ms-crafted-1 500 1000,ms-crafted-2 5000 2500,ms-crafted-3 25000 5000",
        "line-artifacts;Hüter der Relikte;artifacts;1;Artefakte geborgen;artifact;ms-artifacts-1 25 1000,ms-artifacts-2 250 2500,ms-artifacts-3 1000 5000",
        "line-trees;Holzfäller;trees;1;Bäume gefällt;item:Axe;ms-trees-1 100 1000,ms-trees-2 1000 2500,ms-trees-3 5000 5000",
        "line-tasks;Herausforderer;tasks-done;1;Aufgaben erledigt;art:8;ms-tasks-1 50 1000,ms-tasks-2 500 2500,ms-tasks-3 2000 5000" };
    // The 17 lifetime ids older builds validate (MetaProgressionCatalog) with their XP, and the 9 run objective ids.
    static readonly string[] LegacyStages = { "depth-100 250", "depth-250 600", "depth-500 1200", "depth-1000 2400", "exotic-first 400", "gem-variety 800",
        "first-victory 3000", "lifetime-ores 4000", "lifetime-coal 1500", "lifetime-copper 1800", "lifetime-iron 2000", "lifetime-silver 2500", "lifetime-gold 3000",
        "lifetime-regions 2000", "lifetime-discoveries 3000", "lifetime-victories 8000", "lifetime-exotics 1600" };
    static readonly string[] RunObjectives = { "run-ores", "run-miner", "run-crafter", "run-map", "run-depth", "run-discovery", "run-deep", "run-rare", "run-exotic" };

    static void MilestoneChecks()
    {
        var lines = ChallengeCatalog.Milestones;
        if (!Eq(lines?.Count ?? -1, LineTable.Length, "milestone line count")) return;
        var stageIds = new HashSet<string>();
        var legacy = new Dictionary<string, int>();
        for (int i = 0; i < LineTable.Length; i++) {
            var f = LineTable[i].Split(';');
            var line = lines[i];
            if (!Check(line != null, () => "milestone line " + i + " is null")) continue;
            string tag = f[0];
            Eq(line.id, f[0], "line " + i + " id (spec order)");
            Eq(line.name, f[1], tag + " name");
            Eq(line.metric, f[2], tag + " metric");
            Eq(line.counter, f[3] == "1", tag + " counter");
            Eq(line.unit, f[4], tag + " unit");
            Eq(line.icon, f[5], tag + " icon");
            var stages = f[6].Split(',');
            if (!Eq(line.stages?.Length ?? -1, stages.Length, tag + " stage count")) continue;
            Check(stages.Length >= 1 && stages.Length <= 4 && ChallengeCatalog.MedalName(stages.Length) != "", () => tag + ": stage count has a medal");
            for (int k = 0; k < stages.Length; k++) {
                var e = stages[k].Split(' ');
                var st = line.stages[k];
                if (!Check(st != null, () => tag + " stage " + k + " is null")) continue;
                Eq(st.id, e[0], tag + " stage " + k + " id");
                Eq(st.target, int.Parse(e[1], CultureInfo.InvariantCulture), e[0] + " target");
                Eq(st.xp, int.Parse(e[2], CultureInfo.InvariantCulture), e[0] + " xp");
                Eq(st.legacy, e.Length > 3, e[0] + " legacy flag");
                Check(stageIds.Add(st.id), () => "stage id " + st.id + " used twice");
                Check(k == 0 || st.target > line.stages[k - 1].target, () => e[0] + ": targets not ascending");
                Check(st.xp > 0, () => e[0] + ": xp " + st.xp);
                if (st.legacy) legacy[st.id] = st.xp;
            }
        }
        Eq(legacy.Count, LegacyStages.Length, "legacy stage count");
        foreach (var entry in LegacyStages) {
            var e = entry.Split(' ');
            Check(legacy.TryGetValue(e[0], out int xp) && xp == int.Parse(e[1], CultureInfo.InvariantCulture), () => "legacy stage " + e[0] + " must keep id and XP " + e[1]);
        }
        foreach (var id in RunObjectives) Check(!stageIds.Contains(id), () => "stage id collides with run objective " + id);
    }

    // ---------- Text limits (names <= 26, units <= 34, Swiss spelling)

    static void Text(string tag, string text, bool name)
    {
        int limit = name ? 26 : 34;
        if (text == null) { Fail(tag + ": " + (name ? "name" : "unit") + " is null"); return; }
        Check(text.Length > 0 && text.Length <= limit && text.IndexOf('ß') < 0, () => tag + ": " + (name ? "name" : "unit") + " '" + text + "' (" + text.Length + " > " + limit + " or ß)");
    }

    static void TextChecks()
    {
        foreach (var t in ChallengeCatalog.Templates) {
            if (t == null) continue;
            Text("template " + t.id, t.name, true); Text("template " + t.id, t.unit, false);
            for (int subject = -1; subject <= 41; subject++) {
                Text(t.id + "/" + subject, ChallengeCatalog.Name(t.id, subject), true);
                Text(t.id + "/" + subject, ChallengeCatalog.Unit(t.id, t.metric, subject), false);
            }
        }
        foreach (var line in ChallengeCatalog.Milestones) { Text(line.id, line.name, true); Text(line.id, line.unit, false); }
        for (int i = 0; i < 6; i++) Text("family " + i, ChallengeCatalog.FamilyName((ChallengeFamily)i), true);
        for (int i = 0; i < 5; i++) Text("tier " + i, ChallengeCatalog.TierName(i), true);
        for (int i = 1; i <= 4; i++) Text("medal " + i, ChallengeCatalog.MedalName(i), true);
        for (int i = 1; i <= 10; i++) Text("artifact " + i, ChallengeCatalog.ArtifactName(i), false);
        foreach (string metric in new[] { "variety", "resources", "crafts", "regions", "depth", "discoveries", "exotics", "rare", "victory", "tasks-done" })
            Text("run metric " + metric, ChallengeCatalog.Unit(null, metric, -1), false);
        var st = new ChallengeSettings { diligenceDailyTasks = 10 };
        st.daily.countEasy = st.daily.countMedium = st.daily.countHard = 10;
        Text("diligence unit (10 tasks)", ExpUnit(specs["diligence"], -1, st), false);
    }

    // ---------- Settings (§8)

    static string Fields(ChallengePeriodSettings p) => p == null ? "null" : string.Join(",", p.xpEasy, p.xpMedium, p.xpHard, p.xpXxl, p.countEasy, p.countMedium, p.countHard,
        p.xxlChancePercent.ToString("R", CultureInfo.InvariantCulture), p.completionBonusXp);
    static string Fields(ChallengeSettings s) => string.Join(";", Fields(s.daily), Fields(s.weekly), Fields(s.monthly), s.projectStage1Xp, s.projectStage2Xp, s.projectStage3Xp,
        s.diligenceDays, s.diligenceDailyTasks, s.xxlMinLayer, F(s.growthPercentPerLayer), F(s.growthPercentPer10Levels), F(s.maxGrowthPercent), s.returnMinDepth,
        s.fullBagMinDepth, F(s.fullBagPercent), F(s.fullBagGainPercent), s.fullBagCapKg, s.tripMinNewBlocks);
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static ChallengePeriodSettings PeriodAll(int xp, int count, float chance, int bonus)
        => new ChallengePeriodSettings { xpEasy = xp, xpMedium = xp, xpHard = xp, xpXxl = xp, countEasy = count, countMedium = count, countHard = count, xxlChancePercent = chance, completionBonusXp = bonus };
    static ChallengeSettings All(int xp, int count, float chance, int bonus, int project, int days, int dailyTasks, int layer, float growth, int depth, float percent, int cap, int trip)
        => new ChallengeSettings { daily = PeriodAll(xp, count, chance, bonus), weekly = PeriodAll(xp, count, chance, bonus), monthly = PeriodAll(xp, count, chance, bonus),
            projectStage1Xp = project, projectStage2Xp = project, projectStage3Xp = project, diligenceDays = days, diligenceDailyTasks = dailyTasks, xxlMinLayer = layer,
            growthPercentPerLayer = growth, growthPercentPer10Levels = growth, maxGrowthPercent = growth, returnMinDepth = depth, fullBagMinDepth = depth,
            fullBagPercent = percent, fullBagGainPercent = percent, fullBagCapKg = cap, tripMinNewBlocks = trip };

    static void SettingsChecks()
    {
        var d = new ChallengeSettings();
        Eq(Fields(d.daily), "300,600,1200,3000,3,3,1,5,1000", "daily defaults (concept §5/§6)");
        Eq(Fields(d.weekly), "1500,3000,5000,10000,3,3,2,5,5000", "weekly defaults (concept §5/§7)");
        Eq(Fields(d.monthly), "5000,10000,20000,0,2,2,2,0,15000", "monthly defaults (concept §5/§8)");
        Check(0 < d.projectStage1Xp && d.projectStage1Xp < d.projectStage2Xp && d.projectStage2Xp < d.projectStage3Xp, "project stage XP ascending (last stage pays most)");
        Check(d.diligenceDays == 5 && d.diligenceDailyTasks == 3, "diligence defaults 5 days with 3 daily tasks");
        Check(d.tripMinNewBlocks == 10 && d.fullBagCapKg == 150, "tripMinNewBlocks 10 and fullBagCapKg 150 (§8)");
        Check(d.xxlMinLayer >= 1 && d.xxlMinLayer <= 5, "xxlMinLayer default in range");
        Check(d.Period(Daily) == d.daily && d.Period(Weekly) == d.weekly && d.Period(Monthly) == d.monthly && d.Period(Lifetime) == null, "Period() mapping");
        Check(d.ProjectStageXp(0) == d.projectStage1Xp && d.ProjectStageXp(1) == d.projectStage2Xp && d.ProjectStageXp(2) == d.projectStage3Xp
            && d.ProjectStageXp(3) == 0 && d.ProjectStageXp(-1) == 0, "ProjectStageXp mapping");
        Check(d.daily.Xp(0) == 300 && d.daily.Xp(1) == 600 && d.daily.Xp(2) == 1200 && d.daily.Xp(3) == 3000 && d.daily.Xp(4) == 0 && d.daily.Xp(-1) == 0, "Xp(tier) mapping");
        Check(d.weekly.Count(0) == 3 && d.weekly.Count(1) == 3 && d.weekly.Count(2) == 2 && d.weekly.Count(3) == 0, "Count(tier) mapping");
        var s = d.Sanitized();
        Check(!ReferenceEquals(s, d) && !ReferenceEquals(s.daily, d.daily) && !ReferenceEquals(s.monthly, d.monthly), "Sanitized returns a copy");
        Eq(Fields(s), Fields(d), "Sanitized keeps the defaults");
        var low = All(-1, -1, -1f, -1, -1, -1, 0, 0, -1f, 0, 9f, 9, -1);
        low.daily.xpHard = int.MinValue; low.daily.countHard = int.MinValue; low.fullBagGainPercent = -1f;
        string lowBefore = Fields(low);
        Eq(Fields(low.Sanitized()), "0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0;0,0,0,0,0,0,0,0,0;0;0;0;0;1;1;0;0;0;1;1;10;0;10;0", "Sanitized clamps low values");
        Eq(Fields(low), lowBefore, "Sanitized does not modify its source");
        var high = All(1000001, 11, 101f, 2000000, 1000001, 8, 11, 6, 501f, 1201, 301f, 2001, 201);
        high.weekly.xpEasy = int.MaxValue; high.monthly.countEasy = int.MaxValue;
        Eq(Fields(high.Sanitized()), "1000000,1000000,1000000,1000000,10,10,10,100,1000000;1000000,1000000,1000000,1000000,10,10,10,100,1000000;"
            + "1000000,1000000,1000000,1000000,10,10,10,100,1000000;1000000;1000000;1000000;7;10;5;500;500;500;1200;1200;300;300;2000;200", "Sanitized clamps high values");
        var mid = All(123, 4, 12.5f, 777, 4444, 3, 4, 2, 33.25f, 77, 120.5f, 600, 25);
        Eq(Fields(mid.Sanitized()), Fields(mid), "Sanitized keeps in-range values");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }) {
            var odd = new ChallengeSettings { growthPercentPerLayer = bad, growthPercentPer10Levels = bad, maxGrowthPercent = bad, fullBagPercent = bad, fullBagGainPercent = bad };
            odd.daily.xxlChancePercent = bad; odd.weekly.xxlChancePercent = bad; odd.monthly.xxlChancePercent = bad;
            var c = odd.Sanitized();
            string label = "Sanitized " + F(bad);
            Check(Finite(c.growthPercentPerLayer, 0, 500) && Finite(c.growthPercentPer10Levels, 0, 500) && Finite(c.maxGrowthPercent, 0, 500), label + ": growth finite and in range");
            Check(Finite(c.fullBagPercent, 10, 300) && Finite(c.fullBagGainPercent, 0, 300), label + ": full bag percentages finite and in range");
            Check(Finite(c.daily.xxlChancePercent, 0, 100) && Finite(c.weekly.xxlChancePercent, 0, 100) && Finite(c.monthly.xxlChancePercent, 0, 100), label + ": XXL chance finite and in range");
        }
        var nulls = new ChallengeSettings { daily = null, weekly = null, monthly = null }.Sanitized();
        Check(nulls.daily != null && nulls.weekly != null && nulls.monthly != null, "Sanitized replaces null period settings");
        Eq(Fields(nulls), Fields(d), "null period settings fall back to the defaults");
        var lifetime = ChallengeSettings.Defaults(Lifetime);
        Check(lifetime != null && lifetime.countEasy + lifetime.countMedium + lifetime.countHard == 0, "Defaults(Lifetime) has no slots");
    }
    static bool Finite(float v, float min, float max) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= min && v <= max;

    // ---------- Amounts worked out by hand from §7.1–§7.3 (anchor the spec model that every drawn task is compared with)

    static void Lit(string id, ChallengePeriod p, Ctx c, int subject, params int[] expected)
    {
        var cands = CandidateSet(p, c, new ChallengeSettings().Sanitized());
        for (int tier = 0; tier < expected.Length; tier++) {
            int subj = id == "round-trips" ? TripSubject(tier, c.L) : subject, actual = 0, t = tier;
            foreach (var cand in cands[tier]) if (cand.spec.id == id && cand.subject == subj) actual = cand.target;
            Check(actual == expected[tier], () => "spec model " + id + "/" + subj + " " + p + " T" + t + " (" + c + "): " + actual + ", hand-computed " + expected[t]);
        }
    }
    static void LitStages(string id, Ctx c, params int[] expected)
    {
        var st = new ChallengeSettings().Sanitized();
        var stages = ProjectSet(c, st).Contains(specs[id]) ? Stages(specs[id], c, st) : null;
        Check(expected.Length == 0 ? stages == null : Same(stages, expected), () => "spec model " + id + " (" + c + "): stages " + Join(stages) + ", hand-computed " + Join(expected));
    }

    static void AmountChecks()
    {
        int[] full = FullLimits;
        Lit("blocks", Daily, C(1, 1), -1, 100, 250, 500, 0);
        Lit("blocks", Daily, C(3, 1), -1, 120, 300, 600, 2400);
        Lit("blocks", Weekly, C(5, 200), -1, 960, 2400, 4800, 16000);
        Lit("ores", Monthly, C(2, 25), -1, 1150, 2850, 5700);
        Lit("ore", Weekly, C(2, 1), 0, 130, 310, 620, 0);
        Lit("ore", Daily, C(4, 1), 8, 16, 40, 80, 260);
        Lit("ore", Daily, C(3, 1), 8, 0, 0, 0, 0);
        Lit("ore", Daily, C(1, 1), 4, 0, 0, 0, 0);
        Lit("gem", Daily, C(4, 1), 40, 3, 8, 15, 60);
        Lit("gem", Daily, C(3, 1), 40, 0, 0, 0, 0);
        Lit("gem", Weekly, C(3, 1), 32, 20, 50, 100, 300);
        Lit("gems", Daily, C(2, 1), -1, 0, 0, 0, 0);
        Lit("ultronium", Daily, C(5, 1, null, 8), 24, 0, 0, 3, 10);
        Lit("ultronium", Daily, C(5, 1, null, 7.5f), 24, 0, 0, 0, 0);
        Lit("ultronium", Weekly, C(5, 1), 24, 0, 5, 12, 25);
        Lit("ultronium", Monthly, C(5, 1), 24, 0, 0, 30);
        Lit("ultronium", Weekly, C(4, 1, null, 8), 24, 0, 0, 0, 0);
        Lit("layer-blocks", Daily, C(5, 1), 5, 40, 100, 200, 750);
        Lit("layer-blocks", Daily, C(5, 1), 1, 0, 0, 0, 0);
        Lit("layer-blocks", Daily, C(2, 1), 1, 80, 200, 400, 0);
        Lit("layer-blocks", Weekly, C(4, 1), 4, 300, 720, 1500, 4800);
        Lit("layer-blocks", Weekly, C(3, 1), 4, 0, 0, 0, 0);
        Lit("sales", Daily, C(1, 1), -1, 450, 1200, 3000, 0);
        Lit("sales", Daily, C(5, 1), -1, 7500, 20000, 50000, 150000);
        Lit("sale-single", Weekly, C(4, 1), -1, 3750, 10000, 25000, 62500);
        Lit("run-sales", Monthly, C(2, 1), -1, 12000, 30000, 72000);
        Lit("artifact", Daily, C(1, 1), 1, 1, 0, 0, 0);
        Lit("artifact", Daily, C(3, 1), 3, 1, 0, 3, 7);
        Lit("artifact", Weekly, C(4, 1), 6, 1, 2, 5, 10);
        Lit("artifact", Daily, C(2, 1), 3, 0, 0, 0, 0);
        Lit("artifact", Daily, C(4, 1), 10, 0, 0, 0, 0);
        Lit("round-trips", Weekly, C(5, 1), 0, 3, 3, 4, 6);
        Lit("round-trips", Daily, C(2, 1), 0, 1, 0, 0, 0);
        Lit("round-trips", Daily, C(3, 1), 0, 1, 0, 1, 0);
        Lit("round-trips", Monthly, C(4, 1), 0, 10, 12, 15);
        Lit("round-trips", Daily, C(1, 1), 0, 0, 0, 0, 0);
        Lit("depth", Weekly, C(4, 1), -1, 450, 700, 880, 1050);
        Lit("depth", Daily, C(1, 1), -1, 15, 25, 40, 0);
        Lit("depth", Monthly, C(3, 1), -1, 0, 0, 0);
        Lit("layers", Daily, C(2, 1), -1, 2, 0, 0, 0);
        Lit("layers", Daily, C(3, 1), -1, 2, 3, 0, 0);
        Lit("layers", Weekly, C(3, 1), -1, 3, 0, 0, 0);
        Lit("layers", Weekly, C(4, 1), -1, 3, 4, 0, 0);
        Lit("ore-variety", Daily, C(1, 1), -1, 2, 3, 0, 0);
        Lit("ore-variety", Weekly, C(3, 1), -1, 3, 5, 7, 0);
        Lit("ore-variety", Daily, C(4, 1), -1, 2, 3, 5, 8);
        Lit("artifact-variety", Daily, C(1, 1), -1, 2, 0, 0, 0);
        Lit("artifact-variety", Weekly, C(4, 1), -1, 3, 4, 6, 8);
        Lit("upgrades", Daily, C(1, 1, full), 1, 1, 0, 2, 0);
        Lit("upgrades", Daily, C(1, 1, new[] { 1, 0, 0, 0, 0, 0, 0 }), 0, 1, 0, 0, 0);
        Lit("upgrades", Weekly, C(1, 1, full), -1, 0, 2, 4, 0);
        Lit("upgrades", Daily, C(1, 1), 0, 0, 0, 0, 0);
        Lit("tools", Daily, C(1, 1, full), -1, 1, 2, 3, 0);
        Lit("tools", Daily, C(1, 1, new[] { 1, 1, 1, 1, 1, 1, 1 }), -1, 1, 0, 0, 0);
        Lit("tools", Daily, C(1, 1), -1, 0, 0, 0, 0);
        Lit("tools", Weekly, C(1, 1, full), -1, 2, 4, 0, 0);
        Lit("gear", Daily, C(1, 1, full), -1, 1, 2, 3, 0);
        Lit("dynamite", Daily, C(1, 1), -1, 3, 8, 15, 0);
        Lit("dynamite", Daily, C(1, 1, new int[7]), -1, 0, 0, 0, 0);
        Lit("dynamite", Monthly, C(1, 1, new int[7]), -1, 80, 200, 400);
        Lit("medkits", Weekly, C(3, 1, full), -1, 10, 20, 35, 0);
        Lit("medkits", Weekly, C(3, 1, new int[7]), -1, 0, 0, 0, 0);
        Lit("exotics", Daily, C(1, 4), -1, 0, 0, 0, 0);
        Lit("exotics", Daily, C(1, 5), -1, 1, 2, 4, 0);
        Lit("crafted", Daily, C(4, 33), -1, 14, 35, 70, 200);
        Lit("altars", Weekly, C(5, 1), -1, 0, 0, 1, 0);
        Lit("altars", Weekly, C(4, 1), -1, 0, 0, 0, 0);
        Lit("altars", Daily, C(5, 1), -1, 0, 0, 0, 0);
        Lit("returns", Daily, C(1, 1), -1, 0, 0, 0, 0);
        Lit("returns", Daily, C(2, 1), -1, 2, 4, 7, 0);
        Lit("trees", Daily, C(3, 1), -1, 3, 6, 12, 30);
        Lit("cells", Monthly, C(5, 200), -1, 6000, 15000, 30000);
        LitStages("project-depth", C(1, 1), 40, 80, 150);
        LitStages("project-depth", C(5, 1), 300, 600, 1000);
        LitStages("project-sales", C(2, 1), 12000, 36000, 90000);
        LitStages("project-ores", C(5, 200), 2400, 6400, 16000);
        LitStages("project-crafted", C(3, 1), 240, 600, 1450);
        LitStages("project-veins", C(4, 33), 50, 120, 300);
        LitStages("project-returns", C(1, 1));
        LitStages("project-returns", C(2, 1), 30, 70, 150);
        Lit("diligence", Weekly, C(3, 1), -1, 0, 0, 0, 0);
        foreach (var s in specList) if (s.retired) Lit(s.id, Daily, C(5, 99, full, 8), s.subs == null ? -1 : s.subs[0].id, 0, 0, 0, 0);
    }

    // ---------- Draw simulation (~3000 daily, 600 weekly, 200 monthly rounds, all recomputed from the spec model)

    static readonly int[] LayerCycle = { 1, 2, 3, 4, 5, 3, 4, 5 };
    static readonly int[] LevelCycle = { 1, 4, 5, 33, 200 };
    static readonly int[][] LimitCycle = { null, new[] { 2, 5, 3, 2, 2, 20, 10 }, new int[7], new[] { 1, 1, 0, 0, 2, 0, 3 }, new[] { 0, 3 } };
    static Ctx SeriesContext(int s) => new Ctx { layer = LayerCycle[s % 8], level = LevelCycle[s / 8 % 5], limits = LimitCycle[s % 5], hardness = s / 2 % 2 == 0 ? 8f : 0f };
    static DateTime Instant(ChallengePeriod p, int index) => p == Daily ? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(index)
        : p == Weekly ? new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc).AddDays(7.0 * index) : new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc).AddMonths(index);
    static void PeriodBounds(ChallengePeriod p, int index, out long start, out long end, out int day)
        => ChallengeEngine.Bounds(p, Instant(p, index), TimeZoneInfo.Utc, out start, out end, out day);

    sealed class Stats { public int draws, eligible, xxl, xxlLow, repeats, completed; }
    static readonly int[,] layerBlocks = new int[6, 2]; // per reached layer: picks of the deepest layer, all layer-blocks picks

    static Stats RunSeries(ChallengePeriod p, int series, int length, Dictionary<string, int> drawn, HashSet<string> eligible)
    {
        var stats = new Stats();
        var settings = new ChallengeSettings();
        var st = settings.Sanitized();
        for (int s = 0; s < series; s++) {
            var ctx = SeriesContext(s);
            var cands = CandidateSet(p, ctx, st);
            for (int tier = 0; tier < 4; tier++) if (st.Period(p).Count(tier) > 0 || tier == 3) foreach (var c in cands[tier]) eligible.Add(p + ":" + c.spec.id);
            if (p == Weekly) eligible.Add(p + ":diligence");
            if (p == Monthly) foreach (var project in ProjectSet(ctx, st)) eligible.Add(p + ":" + project.id);
            MetaChallengeRound previous = null;
            for (int k = 0; k < length; k++) {
                int index = s * length + k;
                PeriodBounds(p, index, out long start, out long end, out int day);
                string tag = p + " #" + index + " (" + ctx + ")";
                var round = ChallengeEngine.Draw(p, start, end, day, ctx.Context(), settings, previous);
                var o = ValidateRound(tag, round, p, start, end, day, ctx, settings, previous, true, drawn);
                stats.draws++;
                if (o.eligible) { stats.eligible++; if (o.xxl) stats.xxl++; } else if (o.xxl) stats.xxlLow++;
                stats.repeats += o.repeats;
                if (round?.tasks != null) foreach (var t in round.tasks) if (t != null && t.id == "layer-blocks") { layerBlocks[ctx.L, 1]++; if (t.subject == ctx.L) layerBlocks[ctx.L, 0]++; }
                if (previous != null) Check(o.repeats == 0, () => tag + ": " + o.repeats + " activity repeat(s) from the previous round");
                if (round == null) continue;
                if (index % 7 == 0) Check(Dump(ChallengeEngine.Draw(p, start, end, day, ctx.Context(), settings, Clone(previous))) == Dump(round), () => tag + ": redraw differs (determinism)");
                Check(ChallengeEngine.Sanitize(Clone(round)), () => tag + ": Sanitize rejects a drawn round");
                if (index % 4 == 1) { CheckCompletion(tag, Clone(round)); stats.completed++; }
                previous = round;
            }
        }
        return stats;
    }

    static void DrawSimulation()
    {
        var drawn = new Dictionary<string, int>();
        var eligible = new HashSet<string>();
        Array.Clear(layerBlocks, 0, layerBlocks.Length);
        var daily = RunSeries(Daily, 60, 50, drawn, eligible);
        var weekly = RunSeries(Weekly, 60, 10, drawn, eligible);
        var monthly = RunSeries(Monthly, 40, 5, drawn, eligible);
        foreach (var key in eligible) Check(drawn.ContainsKey(key), () => key + " is eligible in some simulated context but was never drawn");
        var seen = new HashSet<string>();
        foreach (var key in drawn.Keys) seen.Add(key.Substring(key.IndexOf(':') + 1));
        foreach (var s in specList) if (!s.retired) Check(seen.Contains(s.id), () => "template " + s.id + " never drawn in any period or context");
        Check(daily.eligible >= 2000, () => "only " + daily.eligible + " daily rounds at layer >= xxlMinLayer");
        double dailyRate = 100.0 * daily.xxl / Math.Max(1, daily.eligible), weeklyRate = 100.0 * weekly.xxl / Math.Max(1, weekly.eligible);
        Check(dailyRate >= 2 && dailyRate <= 9, () => "daily XXL rate " + dailyRate.ToString("F2", CultureInfo.InvariantCulture) + " % outside 2..9 %");
        Check(weeklyRate >= 1 && weeklyRate <= 12, () => "weekly XXL rate " + weeklyRate.ToString("F2", CultureInfo.InvariantCulture) + " % outside 1..12 %");
        Check(daily.xxlLow == 0 && weekly.xxlLow == 0, "XXL below xxlMinLayer");
        Check(monthly.xxl == 0 && monthly.xxlLow == 0, "monthly rounds never contain XXL");
        // Subject weights of layer-blocks: the deepest reached layer has weight 2, every other offered layer 1 (§7.2).
        double observed = 0, expected = 0, variance = 0, uniform = 0; int picks = 0;
        for (int layer = 2; layer <= 5; layer++) {
            int n = layer <= 2 ? layer : layer - 1, total = layerBlocks[layer, 1];
            double share = 2.0 / (n + 1);
            observed += layerBlocks[layer, 0]; expected += total * share; variance += total * share * (1 - share); uniform += (double)total / n; picks += total;
        }
        Check(picks >= 100 && Math.Abs(observed - expected) <= 3.5 * Math.Sqrt(variance),
            () => "layer-blocks deepest-layer picks " + observed + " of " + picks + ", expected " + expected.ToString("F0", CultureInfo.InvariantCulture) + " for weight 2 (uniform would give "
                + uniform.ToString("F0", CultureInfo.InvariantCulture) + ")");
        Note("layer-blocks: deepest reached layer picked " + observed + " of " + picks + " times (weight 2 expects " + expected.ToString("F0", CultureInfo.InvariantCulture)
            + ", uniform " + uniform.ToString("F0", CultureInfo.InvariantCulture) + ")");
        Note("draws: daily " + daily.draws + ", weekly " + weekly.draws + ", monthly " + monthly.draws + "; fully completed and paid: " + (daily.completed + weekly.completed + monthly.completed));
        Note("XXL: daily " + daily.xxl + "/" + daily.eligible + " = " + dailyRate.ToString("F2", CultureInfo.InvariantCulture) + " %, weekly " + weekly.xxl + "/" + weekly.eligible
            + " = " + weeklyRate.ToString("F2", CultureInfo.InvariantCulture) + " % (layer >= 3); activity repeats vs previous round: " + (daily.repeats + weekly.repeats + monthly.repeats));
        Note("coverage: " + drawn.Count + " period/template pairs drawn, " + eligible.Count + " eligible by the spec model");
    }

    // ---------- Draw variants (settings, contexts, avoidance, inputs)

    static int variantIndex = 4000;
    static MetaChallengeRound DrawValidated(string label, ChallengePeriod p, ChallengeSettings settings, Ctx ctx, MetaChallengeRound previous, bool exact,
        out Outcome outcome, int index = -1)
    {
        if (index < 0) index = variantIndex++;
        PeriodBounds(p, index, out long start, out long end, out int day);
        MetaChallengeRound round = null;
        outcome = new Outcome();
        try { round = ChallengeEngine.Draw(p, start, end, day, ctx.Context(), settings, previous); }
        catch (Exception e) { Fail(label + ": Draw threw " + e.GetType().Name + ": " + e.Message); return null; }
        outcome = ValidateRound(label + " " + p + " #" + index + " (" + ctx + ")", round, p, start, end, day, ctx, settings, previous, exact, null);
        if (round != null) Check(ChallengeEngine.Sanitize(Clone(round)), () => label + ": Sanitize rejects the drawn round");
        return round;
    }
    static Ctx C(int layer, int level = 5, int[] limits = null, float hardness = 0) => new Ctx { layer = layer, level = level, limits = limits, hardness = hardness };
    static int Count(MetaChallengeRound r, int tier, bool includeDiligence = false)
    {
        int n = 0;
        if (r?.tasks != null) foreach (var t in r.tasks) if (t != null && t.tier == tier && (includeDiligence || t.id != "diligence")) n++;
        return n;
    }
    static MetaChallengeTask Find(MetaChallengeRound r, string id)
    {
        if (r?.tasks != null) foreach (var t in r.tasks) if (t != null && t.id == id) return t;
        return null;
    }
    static readonly int[] FullLimits = { 2, 5, 3, 2, 2, 20, 10 };

    static void DrawVariants()
    {
        // Slot counts come from the settings
        var slots = new ChallengeSettings();
        slots.daily.countEasy = 2; slots.daily.countMedium = 2; slots.daily.countHard = 2;
        slots.weekly.countEasy = 1; slots.weekly.countMedium = 2; slots.weekly.countHard = 1;
        slots.monthly.countEasy = 1; slots.monthly.countMedium = 1; slots.monthly.countHard = 3;
        for (int layer = 1; layer <= 5; layer++) for (int i = 0; i < 4; i++) {
            var r = DrawValidated("slots", Daily, slots, C(layer, 5, FullLimits), null, true, out _);
            Check(r?.tasks.Count == 6, () => "slots: daily task count " + r?.tasks.Count + ", expected 6");
            r = DrawValidated("slots", Weekly, slots, C(layer, 5, FullLimits), null, true, out _);
            Check(r?.tasks.Count == 4, () => "slots: weekly task count " + r?.tasks.Count + ", expected 4 (diligence + 3)");
            r = DrawValidated("slots", Monthly, slots, C(layer, 5, FullLimits), null, true, out _);
            Check(r?.tasks.Count == 6, () => "slots: monthly task count " + r?.tasks.Count + ", expected 6 (project + 5)");
        }
        // XXL forced on and its minimum layer
        var xxl = new ChallengeSettings { xxlMinLayer = 4 };
        xxl.daily.xxlChancePercent = 100; xxl.weekly.xxlChancePercent = 100; xxl.monthly.xxlChancePercent = 100; xxl.monthly.xpXxl = 5000;
        for (int i = 0; i < 8; i++) {
            DrawValidated("xxl 100% L3", Daily, xxl, C(3), null, true, out var o3);
            Check(!o3.xxl, "xxl 100%: XXL drawn below xxlMinLayer 4");
            var r4 = DrawValidated("xxl 100% L4", Daily, xxl, C(4 + i % 2), null, true, out var o4);
            Check(o4.xxl && Count(r4, 2) == 0 && Count(r4, 1) == 3, () => "xxl 100%: daily XXL must replace the only Hard slot (H " + Count(r4, 2) + ", M " + Count(r4, 1) + ")");
            var w4 = DrawValidated("xxl 100% L4", Weekly, xxl, C(4 + i % 2), null, true, out var ow);
            Check(ow.xxl && Count(w4, 2) == 1, () => "xxl 100%: weekly XXL must replace the last Hard slot (H " + Count(w4, 2) + ")");
            var m5 = DrawValidated("xxl 100% monthly", Monthly, xxl, C(5), null, true, out _);
            Check(Count(m5, 3) == 0, "monthly rounds never offer XXL (no monthly XXL amounts)");
        }
        var noHard = new ChallengeSettings();
        noHard.daily.countHard = 0; noHard.daily.xxlChancePercent = 100;
        for (int i = 0; i < 6; i++) {
            var r = DrawValidated("xxl without Hard", Daily, noHard, C(5), null, true, out var o);
            Check(o.xxl && Count(r, 1) == 2 && Count(r, 2) == 0, () => "xxl without Hard slots must replace the last Medium (M " + Count(r, 1) + ")");
        }
        var never = new ChallengeSettings();
        never.daily.xxlChancePercent = 0; never.weekly.xxlChancePercent = 0;
        for (int i = 0; i < 20; i++) {
            DrawValidated("xxl 0%", i % 4 == 0 ? Weekly : Daily, never, C(5, 40, FullLimits, 8), null, true, out var o);
            Check(!o.xxl, "xxl 0%: XXL drawn");
        }
        // Diligence: off, unit text, target, XP
        var off = new ChallengeSettings { diligenceDays = 0 };
        for (int layer = 1; layer <= 5; layer++) {
            var r = DrawValidated("diligence off", Weekly, off, C(layer), null, true, out _);
            Check(Find(r, "diligence") == null && Count(r, 1) == 3, () => "diligence off: diligence present or Medium count " + Count(r, 1));
        }
        var units = new (string label, Action<ChallengeSettings> set, string unit)[] {
            ("5 daily tasks", st => st.diligenceDailyTasks = 5, "Tage mit 5 Tagesaufgaben"),
            ("2 daily slots", st => { st.daily.countEasy = 1; st.daily.countMedium = 1; st.daily.countHard = 0; }, "Tage mit 2 Tagesaufgaben"),
            ("1 daily slot", st => { st.daily.countEasy = 1; st.daily.countMedium = 0; st.daily.countHard = 0; }, "Tage mit 1 Tagesaufgabe"),
            ("7 days, xp 777", st => { st.diligenceDays = 7; st.weekly.xpMedium = 777; }, "Tage mit 3 Tagesaufgaben"),
            ("no daily slots", st => { st.daily.countEasy = 0; st.daily.countMedium = 0; st.daily.countHard = 0; }, null) };
        foreach (var u in units) {
            var st = new ChallengeSettings();
            u.set(st);
            var r = DrawValidated("diligence " + u.label, Weekly, st, C(3), null, true, out _);
            var dil = Find(r, "diligence");
            if (u.unit == null) Check(dil == null, "diligence without daily tasks must not be offered (accepted deviation 1)");
            else Check(dil != null && dil.unit == u.unit && dil.target == st.diligenceDays && dil.xp == st.weekly.xpMedium && dil.tier == 1,
                () => "diligence " + u.label + ": " + (dil == null ? "missing" : dil.unit + " / " + dil.target + " / " + dil.xp));
        }
        foreach (var p in new[] { Daily, Monthly }) {
            var r = DrawValidated("no diligence outside weekly", p, new ChallengeSettings(), C(3), null, true, out _);
            Check(Find(r, "diligence") == null, () => "diligence drawn in " + p);
        }
        // XP-only settings changes keep the selection; changed settings only reach new draws
        var xp = new ChallengeSettings { projectStage1Xp = 166, projectStage2Xp = 177, projectStage3Xp = 188 };
        xp.daily.xpEasy = 11; xp.daily.xpMedium = 22; xp.daily.xpHard = 33; xp.daily.xpXxl = 44; xp.daily.completionBonusXp = 55;
        xp.weekly.xpEasy = 66; xp.weekly.xpMedium = 77; xp.weekly.xpHard = 88; xp.weekly.xpXxl = 99; xp.weekly.completionBonusXp = 111;
        xp.monthly.xpEasy = 122; xp.monthly.xpMedium = 133; xp.monthly.xpHard = 144; xp.monthly.completionBonusXp = 155;
        foreach (var p in new[] { Daily, Weekly, Monthly }) for (int i = 0; i < 6; i++) {
            int index = variantIndex++;
            var ctx = SeriesContext(i * 7 + 3);
            var a = DrawValidated("xp default", p, new ChallengeSettings(), ctx, null, true, out _, index);
            var b = DrawValidated("xp custom", p, xp, ctx, null, true, out _, index);
            Check(a != null && b != null && Dump(a, false) == Dump(b, false), () => "xp-only change altered the " + p + " selection");
        }
        {
            int index = variantIndex++;
            var ctx = C(3, 5, FullLimits);
            var first = DrawValidated("settings change (old)", Daily, xp, ctx, null, true, out _, index);
            string snapshot = Dump(first);
            var second = DrawValidated("settings change (new)", Daily, new ChallengeSettings(), ctx, first, true, out _, index + 1);
            Check(Dump(first) == snapshot, "Draw modified the previous round");
            long expected = 0;
            foreach (var t in first.tasks) expected += xp.daily.Xp(t.tier);
            expected += xp.daily.completionBonusXp;
            var copy = Clone(first);
            var gain = CompleteAll(copy);
            Check(gain.xp == expected && ChallengeEngine.PaidXp(copy) == expected, () => "old round must pay its stored XP " + expected + " after a settings change, paid " + gain.xp);
            foreach (var t in second.tasks) Check(t.xp == new ChallengeSettings().daily.Xp(t.tier), () => "new round must use the new settings XP (" + t.id + " " + t.xp + ")");
        }
        // Empty slot settings
        var empty = new ChallengeSettings();
        foreach (var ps in new[] { empty.daily, empty.weekly, empty.monthly }) { ps.countEasy = 0; ps.countMedium = 0; ps.countHard = 0; }
        foreach (var p in new[] { Daily, Weekly }) {
            var r = DrawValidated("no slots", p, empty, C(3), null, true, out _);
            Check(r != null && r.tasks.Count == 0 && r.bonusXp == 0, () => "no slots: " + p + " must be empty without bonus (" + r?.tasks.Count + " tasks, bonus " + r?.bonusXp + ")");
        }
        var onlyProject = DrawValidated("no slots", Monthly, empty, C(3), null, true, out _);
        Check(onlyProject != null && onlyProject.tasks.Count == 1 && onlyProject.tasks[0].tier == 4 && onlyProject.bonusXp == empty.monthly.completionBonusXp,
            "no slots: monthly keeps the project and its bonus");
        // Unsanitized settings, null inputs, odd contexts, growth variants
        var wild = new ChallengeSettings { diligenceDays = 99, xxlMinLayer = -4, growthPercentPerLayer = float.NaN, maxGrowthPercent = 9999 };
        wild.daily.countEasy = 99; wild.daily.countMedium = -5; wild.daily.xpEasy = -100; wild.daily.xxlChancePercent = 1000;
        wild.weekly.countHard = 50; wild.monthly.countEasy = 40;
        string wildBefore = Fields(wild);
        foreach (var p in new[] { Daily, Weekly, Monthly }) for (int layer = 1; layer <= 5; layer += 2) DrawValidated("unsanitized settings", p, wild, C(layer, 50, FullLimits, 8), null, false, out _);
        Eq(Fields(wild), wildBefore, "Draw must not modify the caller's settings");
        foreach (var p in new[] { Daily, Weekly, Monthly }) {
            int index = variantIndex++;
            PeriodBounds(p, index, out long start, out long end, out int day);
            string a = null, b = null;
            try { a = Dump(ChallengeEngine.Draw(p, start, end, day, null, null, null)); b = Dump(ChallengeEngine.Draw(p, start, end, day, new ChallengeDrawContext(), new ChallengeSettings(), null)); }
            catch (Exception e) { Fail("Draw with null context/settings threw " + e.GetType().Name); }
            Check(a != null && a == b, () => "Draw(" + p + ", null context, null settings) must equal the defaults");
        }
        var odd = new[] { C(-3), C(0, 0), C(99, int.MaxValue, FullLimits, float.PositiveInfinity), C(int.MinValue, -7, new int[0]), C(4, 1, new[] { 5 }, float.NaN),
            C(5, 20, new[] { -1, -1, -1, -1, -1, -1, -1 }, -1), C(3, 12, new int[20], 8) };
        foreach (var ctx in odd) foreach (var p in new[] { Daily, Weekly, Monthly }) DrawValidated("odd context", p, new ChallengeSettings(), ctx, null, true, out _);
        var growth = new[] { new ChallengeSettings { growthPercentPerLayer = 0, growthPercentPer10Levels = 0 },
            new ChallengeSettings { growthPercentPerLayer = 50, growthPercentPer10Levels = 50, maxGrowthPercent = 500 }, new ChallengeSettings { maxGrowthPercent = 0 } };
        foreach (var st in growth) foreach (var p in new[] { Daily, Weekly, Monthly }) for (int i = 0; i < 5; i++) DrawValidated("growth", p, st, SeriesContext(i * 9 + 4), null, true, out _);
        // Subject avoidance: a previous round that covers every activity forces the no-avoidance attempts, where subjects must still differ when possible
        var cover = new MetaChallengeRound { period = 0, startUtc = Day0, endUtc = Day0 + Day };
        foreach (var s in specList) if (!s.retired && !s.project && s.family != Special) cover.tasks.Add(new MetaChallengeTask { id = s.id, name = "x", metric = s.metric, mode = (int)s.mode, target = 1 });
        Find(cover, "ore").subject = 3;
        cover.tasks.Add(new MetaChallengeTask { id = "ore", name = "x", metric = "ore", target = 1, subject = 0 });
        Find(cover, "artifact").subject = 1;
        Find(cover, "layer-blocks").subject = 2;
        Find(cover, "upgrades").subject = 0;
        int ore = 0, artifactSeen = 0, layerBlocks = 0;
        for (int i = 0; i < 80; i++) {
            var ctx = C(1 + i % 2, 5, FullLimits);
            var r = DrawValidated("subject avoidance", Daily, new ChallengeSettings(), ctx, cover, true, out _);
            if (r == null) continue;
            foreach (var t in r.tasks) {
                if (t.id == "ore") { ore++; Check(t.subject != 3 && t.subject != 0, () => "subject avoidance: ore subject " + t.subject + " repeats the previous round"); }
                if (t.id == "artifact") artifactSeen++;
                if (t.id == "layer-blocks" && ctx.L == 2) { layerBlocks++; Check(t.subject == 1, () => "subject avoidance: layer-blocks subject " + t.subject + " at layer 2"); }
            }
        }
        Check(ore > 0 && artifactSeen > 0 && layerBlocks > 0, () => "subject avoidance test is vacuous (ore " + ore + ", artifact " + artifactSeen + ", layer-blocks " + layerBlocks + ")");
        Note("subject avoidance: ore " + ore + ", artifact " + artifactSeen + ", layer-blocks " + layerBlocks + " forced picks checked");
        // Previous project is avoided; legacy and odd previous rounds are harmless
        foreach (var s in specList) {
            if (!s.project) continue;
            var previous = new MetaChallengeRound { period = 3, startUtc = Day0, endUtc = Day0 + 31 * Day };
            previous.tasks.Add(new MetaChallengeTask { id = s.id, name = s.name, metric = s.metric, mode = (int)s.mode, tier = 4, target = 3, stageTargets = new[] { 1, 2, 3 }, stageXp = new[] { 1, 2, 3 } });
            for (int i = 0; i < 6; i++) {
                var r = DrawValidated("project avoidance", Monthly, new ChallengeSettings(), C(3, 5), previous, true, out _);
                Check(r != null && r.tasks.Count > 0 && r.tasks[0].id != s.id, () => "project avoidance: " + s.id + " repeated");
            }
        }
        var legacy = new MetaChallengeRound { period = 0, format = 1, startUtc = Day0, endUtc = Day0 + Day };
        legacy.tasks.Add(null);
        legacy.tasks.Add(new MetaChallengeTask { id = null, metric = "x" });
        legacy.tasks.Add(new MetaChallengeTask { id = "future-template", metric = "x" });
        legacy.tasks.Add(new MetaChallengeTask { id = "legacy-impossible", metric = "unobtainable", subject = 31 });
        legacy.tasks.Add(new MetaChallengeTask { id = "legacy-depth-gain", metric = "depth-gain" });
        legacy.tasks.Add(new MetaChallengeTask { id = "ores", metric = "resources" });
        legacy.tasks.Add(new MetaChallengeTask { id = "ore", metric = "ore", subject = 3 });
        string legacyBefore = Dump(legacy);
        for (int i = 0; i < 6; i++) {
            DrawValidated("legacy previous", Daily, new ChallengeSettings(), C(3, 5, FullLimits), legacy, true, out var o);
            Check(o.repeats == 0, () => "legacy previous: " + o.repeats + " activity repeat(s)");
        }
        Eq(Dump(legacy), legacyBefore, "Draw must not modify a legacy previous round");
        // Lifetime and unknown periods draw nothing
        foreach (var p in new[] { Lifetime, (ChallengePeriod)7 }) {
            try {
                var r = ChallengeEngine.Draw(p, 0, long.MaxValue, 0, new ChallengeDrawContext(), new ChallengeSettings(), null);
                Check(r != null && (r.tasks == null || r.tasks.Count == 0) && r.bonusXp == 0, () => "Draw(" + p + ") must not create tasks");
            } catch (Exception e) { Fail("Draw(" + p + ") threw " + e.GetType().Name); }
        }
    }

    // ---------- Engine semantics (§3)

    static MetaChallengeTask NT(string id, string metric, ChallengeMode mode, int target, int xp, int subject = -1, bool atLeast = false, int tier = 0)
        => new MetaChallengeTask { id = id, name = id, metric = metric, mode = (int)mode, subject = subject, atLeast = atLeast, tier = tier, target = target, xp = xp };
    static MetaChallengeTask PT(string metric, ChallengeMode mode, int[] stages, int[] xp)
        => new MetaChallengeTask { id = "project-test", name = "Projekt", metric = metric, mode = (int)mode, tier = 4, target = stages[stages.Length - 1], stageTargets = stages, stageXp = xp };
    static MetaChallengeRound R(int bonus, params MetaChallengeTask[] tasks)
        => new MetaChallengeRound { period = 0, format = 2, startUtc = Day0, endUtc = Day0 + Day, tasks = new List<MetaChallengeTask>(tasks), bonusXp = bonus };
    static void Gain(string label, ChallengeGain g, int xp, int completed, bool changed)
        => Check(g.xp == xp && g.completed == completed && g.changed == changed,
            () => label + ": xp " + g.xp + ", completed " + g.completed + ", changed " + g.changed + "; expected " + xp + ", " + completed + ", " + changed);
    static MetaChallengeRunValue RunEntry(MetaChallengeRound r, string metric, string runId)
    {
        foreach (var v in r.runValues) if (v != null && v.metric == metric && v.runId == runId) return v;
        return null;
    }

    static void EngineChecks()
    {
        // Null and degenerate input
        Gain("Record(null round)", ChallengeEngine.Record(null, "blocks", 5), 0, 0, false);
        Gain("RecordMax(null round)", ChallengeEngine.RecordMax(null, "depth", 5), 0, 0, false);
        Gain("RecordDistinct(null round)", ChallengeEngine.RecordDistinct(null, "layers", 1), 0, 0, false);
        Gain("RecordRun(null round)", ChallengeEngine.RecordRun(null, "run-sales", "a", 5), 0, 0, false);
        Check(ChallengeEngine.CompletedCount(null) == 0 && ChallengeEngine.PaidXp(null) == 0, "CompletedCount/PaidXp(null) are 0");
        var noTasks = new MetaChallengeRound { tasks = null, bonusXp = 100 };
        Gain("Record(tasks null)", ChallengeEngine.Record(noTasks, "blocks", 5), 0, 0, false);
        Gain("RecordDistinct(tasks null)", ChallengeEngine.RecordDistinct(noTasks, "layers", 1), 0, 0, false);
        Gain("RecordRun(tasks null)", ChallengeEngine.RecordRun(noTasks, "run-sales", "a", 5), 0, 0, false);
        Check(ChallengeEngine.CompletedCount(noTasks) == 0 && ChallengeEngine.PaidXp(noTasks) == 0, "CompletedCount/PaidXp with null tasks");
        var r = R(0, NT("blocks", "blocks", Add, 100, 300));
        var blocks = r.tasks[0];
        Gain("Record(null metric)", ChallengeEngine.Record(r, null, 5), 0, 0, false);
        Gain("Record(empty metric)", ChallengeEngine.Record(r, "", 5), 0, 0, false);
        Gain("RecordMax(null metric)", ChallengeEngine.RecordMax(r, null, 5), 0, 0, false);
        Gain("RecordDistinct(empty metric)", ChallengeEngine.RecordDistinct(r, "", 1), 0, 0, false);
        Gain("RecordRun(null metric)", ChallengeEngine.RecordRun(r, null, "a", 5), 0, 0, false);
        Eq(blocks.progress, 0, "degenerate input changed progress");
        // Add
        Gain("Add 30", ChallengeEngine.Record(r, "blocks", 30), 0, 0, true);
        Eq(blocks.progress, 30, "Add 30 progress");
        Gain("Add 0 ignored", ChallengeEngine.Record(r, "blocks", 0), 0, 0, false);
        Gain("Add -5 ignored", ChallengeEngine.Record(r, "blocks", -5), 0, 0, false);
        Gain("Add other metric", ChallengeEngine.Record(r, "trees", 50), 0, 0, false);
        Eq(blocks.progress, 30, "ignored amounts keep progress");
        Gain("Add 80 completes", ChallengeEngine.Record(r, "blocks", 80), 300, 1, true);
        Check(blocks.progress == 100 && blocks.done && blocks.paid == 300, "Add caps progress at target, done, paid = xp");
        Gain("Add after done", ChallengeEngine.Record(r, "blocks", 10), 0, 0, false);
        Check(ChallengeEngine.PaidXp(r) == 300 && ChallengeEngine.CompletedCount(r) == 1, "PaidXp/CompletedCount after one task");
        var big = R(0, NT("blocks", "blocks", Add, 1000000000, 10), NT("trees", "trees", Add, 1000, 20));
        big.tasks[1].progress = 999;
        Gain("Add long.MaxValue", ChallengeEngine.Record(big, "blocks", long.MaxValue), 10, 1, true);
        Gain("Add beyond int range", ChallengeEngine.Record(big, "trees", int.MaxValue + 10L), 20, 1, true);
        Check(big.tasks[0].progress == 1000000000 && big.tasks[1].progress == 1000, "long amounts cap at target without overflow");
        // Modes are separate
        var modes = R(0, NT("depth", "depth", Max, 300, 600), NT("legacy-depth-gain", "depth-gain", Add, 300, 600), NT("layers", "layers", Distinct, 3, 600),
            NT("run-sales", "run-sales", RunMode, 1000, 600));
        Gain("Add on a Max metric", ChallengeEngine.Record(modes, "depth", 50), 0, 0, false);
        Gain("Max on an Add metric", ChallengeEngine.RecordMax(modes, "depth-gain", 50), 0, 0, false);
        Gain("Add on a Distinct metric", ChallengeEngine.Record(modes, "layers", 2), 0, 0, false);
        Gain("Add on a Run metric", ChallengeEngine.Record(modes, "run-sales", 500), 0, 0, false);
        Gain("Max on a Run metric", ChallengeEngine.RecordMax(modes, "run-sales", 500), 0, 0, false);
        Gain("Distinct on a Max metric", ChallengeEngine.RecordDistinct(modes, "depth", 1), 0, 0, false);
        Check(modes.tasks.TrueForAll(t => t.progress == 0), "modes only advance tasks of their own mode");
        // Subjects and atLeast
        var subjects = R(0, NT("ore", "ore", Add, 100, 300, 3), NT("any", "ore", Add, 100, 300));
        ChallengeEngine.Record(subjects, "ore", 5, 3); ChallengeEngine.Record(subjects, "ore", 5, 0); ChallengeEngine.Record(subjects, "ore", 5);
        Eq(subjects.tasks[0].progress, 5, "subject task counts only its subject");
        Eq(subjects.tasks[1].progress, 15, "subject -1 task counts every subject");
        var trips = R(0, NT("round-trips", "round-trips", Add, 10, 300, 3, true), NT("exact", "round-trips", Add, 10, 300, 3));
        foreach (int s in new[] { 2, 3, 5, -1 }) ChallengeEngine.Record(trips, "round-trips", 1, s);
        Eq(trips.tasks[0].progress, 2, "atLeast task counts subjects >= its subject");
        Eq(trips.tasks[1].progress, 1, "exact subject task counts only its subject");
        var maxSubject = R(0, NT("x", "depth", Max, 300, 600, 2));
        ChallengeEngine.RecordMax(maxSubject, "depth", 150, 1);
        ChallengeEngine.RecordMax(maxSubject, "depth", 120, 2);
        Eq(maxSubject.tasks[0].progress, 120, "RecordMax honours the subject");
        // Max
        var depth = R(0, NT("depth", "depth", Max, 300, 600));
        Gain("Max 120", ChallengeEngine.RecordMax(depth, "depth", 120), 0, 0, true);
        Gain("Max lower value", ChallengeEngine.RecordMax(depth, "depth", 100), 0, 0, false);
        Gain("Max 0 ignored", ChallengeEngine.RecordMax(depth, "depth", 0), 0, 0, false);
        Gain("Max -5 ignored", ChallengeEngine.RecordMax(depth, "depth", -5), 0, 0, false);
        Eq(depth.tasks[0].progress, 120, "Max keeps the highest value");
        Gain("Max 1000 completes", ChallengeEngine.RecordMax(depth, "depth", 1000), 600, 1, true);
        Eq(depth.tasks[0].progress, 300, "Max caps at target");
        Gain("Max after done", ChallengeEngine.RecordMax(depth, "depth", 2000), 0, 0, false);
        // Distinct
        var variety = R(0, NT("ore-variety", "ore-variety", Distinct, 3, 600));
        Gain("bit 0", ChallengeEngine.RecordDistinct(variety, "ore-variety", 0), 0, 0, true);
        Gain("bit 0 again", ChallengeEngine.RecordDistinct(variety, "ore-variety", 0), 0, 0, false);
        Gain("bit 31 ignored", ChallengeEngine.RecordDistinct(variety, "ore-variety", 31), 0, 0, false);
        Gain("bit -1 ignored", ChallengeEngine.RecordDistinct(variety, "ore-variety", -1), 0, 0, false);
        Gain("bit 30", ChallengeEngine.RecordDistinct(variety, "ore-variety", 30), 0, 0, true);
        Eq(variety.tasks[0].progress, 2, "Distinct counts new bits");
        Gain("bit 5 completes", ChallengeEngine.RecordDistinct(variety, "ore-variety", 5), 600, 1, true);
        Gain("bit 7 after done changes the mask only", ChallengeEngine.RecordDistinct(variety, "ore-variety", 7), 0, 0, true);
        Check(variety.tasks[0].progress == 3 && variety.masks.Count == 1 && variety.masks[0].metric == "ore-variety" && variety.masks[0].mask == (1 | 1 << 30 | 1 << 5 | 1 << 7),
            "Distinct mask per metric and progress capped at target");
        var many = R(0);
        for (int i = 0; i < 17; i++) many.tasks.Add(NT("d" + i, "d" + i, Distinct, 5, 10));
        for (int i = 0; i < 17; i++) ChallengeEngine.RecordDistinct(many, "d" + i, 0);
        Check(many.masks.Count == 16 && many.tasks[16].progress == 0 && many.tasks[15].progress == 1, () => "mask entries capped at 16 (" + many.masks.Count + ")");
        // Run values per runId
        var run = R(0, NT("run-sales", "run-sales", RunMode, 1000, 600));
        Gain("run A 600", ChallengeEngine.RecordRun(run, "run-sales", "A", 600), 0, 0, true);
        Gain("run B 500", ChallengeEngine.RecordRun(run, "run-sales", "B", 500), 0, 0, true);
        Check(run.tasks[0].progress == 600 && run.runValues.Count == 2, "Run progress is the best run, not the sum");
        Gain("run 0 ignored", ChallengeEngine.RecordRun(run, "run-sales", "A", 0), 0, 0, false);
        Gain("run -1 ignored", ChallengeEngine.RecordRun(run, "run-sales", "A", -1), 0, 0, false);
        Gain("run A +300", ChallengeEngine.RecordRun(run, "run-sales", "A", 300), 0, 0, true);
        Eq(run.tasks[0].progress, 900, "Run accumulates per runId");
        Gain("run B +600 completes", ChallengeEngine.RecordRun(run, "run-sales", "B", 600), 600, 1, true);
        Check(run.tasks[0].progress == 1000 && RunEntry(run, "run-sales", "A")?.value == 900 && RunEntry(run, "run-sales", "B")?.value == 1100, "run values kept per runId");
        var evict = R(0, NT("run-sales", "run-sales", RunMode, 1000000000, 1), NT("run-x", "run-x", RunMode, 1000000000, 1));
        for (int i = 0; i < 8; i++) ChallengeEngine.RecordRun(evict, "run-x", "x" + i, i + 1);
        for (int i = 0; i < 8; i++) ChallengeEngine.RecordRun(evict, "run-sales", "s" + i, (i + 1) * 100);
        Eq(evict.runValues.Count, 16, "16 run entries");
        ChallengeEngine.RecordRun(evict, "run-sales", "s8", 50);
        Check(evict.runValues.Count == 16 && RunEntry(evict, "run-sales", "s0") == null && RunEntry(evict, "run-sales", "s8")?.value == 50 && RunEntry(evict, "run-x", "x0") != null,
            "a full run list evicts the smallest value of the same metric");
        Eq(evict.tasks[0].progress, 800, "progress after eviction is the best remaining run");
        ChallengeEngine.RecordRun(evict, "run-x", "x8", 1000);
        Check(RunEntry(evict, "run-x", "x0") == null && RunEntry(evict, "run-x", "x8") != null && evict.tasks[1].progress == 1000, "eviction for the second metric");
        var overflow = R(0, NT("run-sales", "run-sales", RunMode, 1000, 5));
        ChallengeEngine.RecordRun(overflow, "run-sales", "A", long.MaxValue);
        ChallengeEngine.RecordRun(overflow, "run-sales", "A", long.MaxValue);
        Check(overflow.runValues[0].value > 0 && overflow.tasks[0].progress == 1000 && overflow.tasks[0].done, "run values never overflow");
        // Bonus
        var bonus = R(1000, NT("a", "blocks", Add, 10, 300), NT("b", "trees", Add, 5, 600, tier: 1));
        Gain("first task", ChallengeEngine.Record(bonus, "blocks", 10), 300, 1, true);
        Check(!bonus.bonusPaid, "no bonus while a task is open");
        Gain("last task pays the bonus", ChallengeEngine.Record(bonus, "trees", 5), 1600, 1, true);
        Check(bonus.bonusPaid, "bonusPaid after the last task");
        Gain("bonus only once", ChallengeEngine.Record(bonus, "blocks", 10), 0, 0, false);
        Check(ChallengeEngine.PaidXp(bonus) == 1900 && ChallengeEngine.CompletedCount(bonus) == 2, "PaidXp includes the bonus once");
        var empty = R(500);
        Gain("empty round Add", ChallengeEngine.Record(empty, "blocks", 5), 0, 0, false);
        Gain("empty round Max", ChallengeEngine.RecordMax(empty, "depth", 5), 0, 0, false);
        Gain("empty round Distinct", ChallengeEngine.RecordDistinct(empty, "layers", 1), 0, 0, false);
        Gain("empty round Run", ChallengeEngine.RecordRun(empty, "run-sales", "a", 5), 0, 0, false);
        Check(!empty.bonusPaid && ChallengeEngine.PaidXp(empty) == 0, "an empty round never pays its bonus");
        var nulls = R(500, (MetaChallengeTask)null);
        Gain("null task round", ChallengeEngine.Record(nulls, "blocks", 5), 0, 0, false);
        Check(!nulls.bonusPaid, "a round of null tasks never pays its bonus");
        var zero = R(0, NT("a", "blocks", Add, 10, 300));
        Gain("bonusXp 0", ChallengeEngine.Record(zero, "blocks", 10), 300, 1, true);
        Check(!zero.bonusPaid, "bonusXp 0 is never marked paid");
        // Projects
        var project = R(50, PT("resources", Add, new[] { 10, 20, 30 }, new[] { 100, 200, 300 }));
        var p = project.tasks[0];
        Gain("project 5", ChallengeEngine.Record(project, "resources", 5), 0, 0, true);
        Gain("project stage 1", ChallengeEngine.Record(project, "resources", 10), 100, 0, true);
        Check(p.stagesPaid == 1 && p.paid == 100 && !p.done, "project pays stage 1 only");
        Gain("project stages 2+3 and bonus", ChallengeEngine.Record(project, "resources", 100), 550, 1, true);
        Check(p.progress == 30 && p.stagesPaid == 3 && p.paid == 600 && p.done && project.bonusPaid, "project pays the remaining stages in one call");
        Gain("project after done", ChallengeEngine.Record(project, "resources", 5), 0, 0, false);
        Check(ChallengeEngine.PaidXp(project) == 650 && ChallengeEngine.CompletedCount(project) == 1, "project PaidXp and CompletedCount");
        var projectMax = R(0, PT("depth", Max, new[] { 200, 400, 600 }, new[] { 1000, 2000, 4000 }));
        Gain("depth project 450", ChallengeEngine.RecordMax(projectMax, "depth", 450), 3000, 0, true);
        Gain("depth project lower", ChallengeEngine.RecordMax(projectMax, "depth", 300), 0, 0, false);
        Gain("depth project 9999", ChallengeEngine.RecordMax(projectMax, "depth", 9999), 4000, 1, true);
        Eq(projectMax.tasks[0].progress, 600, "Max project caps at its last stage");
        var mixed = R(100, PT("sales", Add, new[] { 10, 20, 30 }, new[] { 1, 2, 3 }), NT("t", "trees", Add, 1, 5));
        Gain("mixed: task", ChallengeEngine.Record(mixed, "trees", 1), 5, 1, true);
        Gain("mixed: two stages, no bonus yet", ChallengeEngine.Record(mixed, "sales", 20), 3, 0, true);
        Gain("mixed: last stage and bonus", ChallengeEngine.Record(mixed, "sales", 10), 103, 1, true);
        Check(ChallengeEngine.Sanitize(Clone(mixed)) && ChallengeEngine.CompletedCount(mixed) == 2, "mixed round stays valid");
        RandomSequence();
    }

    // Random recorder calls: the sum of all gains equals PaidXp, progress and paid never decrease, the round stays valid.
    static void RandomSequence()
    {
        var round = R(1000, NT("blocks", "blocks", Add, 500, 300), NT("ore", "ore", Add, 80, 300, 3), NT("round-trips", "round-trips", Add, 6, 600, 3, true, 1),
            NT("depth", "depth", Max, 300, 600, tier: 1), NT("ore-variety", "ore-variety", Distinct, 5, 600, tier: 1), NT("layers", "layers", Distinct, 3, 1200, tier: 2),
            NT("run-sales", "run-sales", RunMode, 4000, 1200, tier: 2), PT("resources", Add, new[] { 100, 250, 500 }, new[] { 5000, 10000, 20000 }));
        var rng = new Random(424242);
        var total = new ChallengeGain();
        var progress = new int[round.tasks.Count]; var paid = new int[round.tasks.Count];
        string[] addMetrics = { "blocks", "ore", "round-trips", "resources", "trees" };
        bool monotonic = true, valid = true;
        for (int step = 0; step < 2000; step++) {
            ChallengeGain g;
            switch (rng.Next(4)) {
                case 0: g = ChallengeEngine.Record(round, addMetrics[rng.Next(addMetrics.Length)], rng.Next(-5, 40), rng.Next(-1, 6)); break;
                case 1: g = ChallengeEngine.RecordMax(round, "depth", rng.Next(-10, 320)); break;
                case 2: g = ChallengeEngine.RecordDistinct(round, rng.Next(2) == 0 ? "ore-variety" : "layers", rng.Next(-2, 34)); break;
                default: g = ChallengeEngine.RecordRun(round, "run-sales", "r" + rng.Next(5), rng.Next(-10, 200)); break;
            }
            AddGain(ref total, g);
            for (int i = 0; i < round.tasks.Count; i++) {
                if (round.tasks[i].progress < progress[i] || round.tasks[i].paid < paid[i]) monotonic = false;
                progress[i] = round.tasks[i].progress; paid[i] = round.tasks[i].paid;
            }
            if (step % 100 == 0 && !ChallengeEngine.Sanitize(Clone(round))) valid = false;
        }
        Check(monotonic, "random sequence: progress or paid decreased");
        Check(valid && ChallengeEngine.Sanitize(Clone(round)), "random sequence: round became invalid");
        Check(total.xp == ChallengeEngine.PaidXp(round), () => "random sequence: sum of gains " + total.xp + " != PaidXp " + ChallengeEngine.PaidXp(round));
        Check(total.completed == ChallengeEngine.CompletedCount(round), () => "random sequence: completed " + total.completed + " != CompletedCount " + ChallengeEngine.CompletedCount(round));
        bool all = round.tasks.TrueForAll(t => t.done);
        Check(round.bonusPaid == all, "random sequence: bonus paid exactly when all tasks are done");
        Note("random sequence: " + ChallengeEngine.CompletedCount(round) + "/" + round.tasks.Count + " done, " + total.xp + " XP");
    }

    // ---------- Sanitize (§3): fixed accept/reject cases

    static MetaChallengeRound SanitizeBase()
    {
        var r = R(1000, NT("blocks", "blocks", Add, 100, 300), NT("trees", "trees", Add, 5, 600, tier: 1), PT("resources", Add, new[] { 10, 20, 30 }, new[] { 100, 200, 300 }));
        r.tasks[0].progress = 40;
        r.tasks[1].progress = 5; r.tasks[1].done = true; r.tasks[1].paid = 600;
        r.tasks[2].progress = 15; r.tasks[2].stagesPaid = 1; r.tasks[2].paid = 100;
        r.masks.Add(new MetaChallengeMask { metric = "ore-variety", mask = 5 });
        r.runValues.Add(new MetaChallengeRunValue { metric = "run-sales", runId = "run-1", value = 500 });
        return r;
    }
    static string Text(int n) => new string('x', n);
    static void Grow(MetaChallengeRound r, int count)
    {
        r.tasks ??= new List<MetaChallengeTask>();
        int i = 0;
        while (r.tasks.Count < count) r.tasks.Add(NT("filler-" + i++, "blocks", Add, 10, 5));
        while (r.tasks.Count > count) r.tasks.RemoveAt(r.tasks.Count - 1);
    }
    static void ProjectStages(MetaChallengeTask t, int[] stages, int[] xp)
    {
        t.stageTargets = stages; t.stageXp = xp; t.target = stages[stages.Length - 1];
        Consistent(t, t.progress);
    }
    static void Consistent(MetaChallengeTask t, int progress)
    {
        if (t == null) return;
        t.progress = progress;
        if (t.tier != 4) { t.done = t.progress >= t.target; t.paid = t.done ? t.xp : 0; return; }
        if (t.stageTargets == null || t.stageXp == null) return;
        int reached = 0; long paid = 0;
        for (int i = 0; i < t.stageTargets.Length && i < t.stageXp.Length; i++) if (t.stageTargets[i] <= t.progress) { reached++; paid += t.stageXp[i]; }
        t.stagesPaid = reached; t.paid = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, paid)); t.done = reached == t.stageTargets.Length;
    }
    static void CompleteConsistently(MetaChallengeRound r)
    {
        if (r.tasks == null) return;
        foreach (var t in r.tasks) if (t != null && t.target >= 1) Consistent(t, t.target);
    }

    static void Expect(bool accept, string label, Action<MetaChallengeRound> mutate)
    {
        var r = SanitizeBase();
        mutate(r);
        bool model = Valid(Clone(r)), result;
        string before = Dump(r);
        try { result = ChallengeEngine.Sanitize(r); } catch (Exception e) { Fail(label + ": Sanitize threw " + e.GetType().Name); return; }
        Check(result == accept, () => label + ": Sanitize " + result + ", expected " + accept);
        Check(model == accept, () => label + ": test validator disagrees (" + model + ")");
        if (result && r.tasks != null && r.masks != null && r.runValues != null && before.IndexOf("null", StringComparison.Ordinal) < 0)
            Check(Dump(r) == before, () => label + ": Sanitize changed an accepted round");
    }

    static void SanitizeChecks()
    {
        Expect(true, "base round", r => { });
        Expect(true, "unknown template id", r => r.tasks[0].id = "some-future-template");
        Expect(true, "start 0", r => { r.startUtc = 0; r.endUtc = Day; });
        Expect(true, "33 days long", r => r.endUtc = r.startUtc + 33 * Day);
        Expect(true, "end at 9000-02-01", r => { r.startUtc = EndLimit - Day; r.endUtc = EndLimit; });
        Expect(true, "weekly / monthly period", r => r.period = 1);
        Expect(true, "monthly period", r => r.period = 3);
        Expect(true, "format 2 with 16 tasks", r => Grow(r, 16));
        Expect(true, "format 1 with 24 tasks", r => { r.format = 1; Grow(r, 24); });
        Expect(true, "name/unit 80 chars", r => { r.tasks[0].name = Text(80); r.tasks[0].unit = Text(80); });
        Expect(true, "name/unit null", r => { r.tasks[0].name = null; r.tasks[0].unit = null; });
        Expect(true, "subject -1 and 10000", r => r.tasks[1].subject = 10000);
        Expect(true, "mode 3 tier 3", r => { r.tasks[0].mode = 3; r.tasks[0].tier = 3; });
        Expect(true, "target 1e9, xp 1e7", r => { r.tasks[0].target = 1000000000; r.tasks[0].xp = 10000000; });
        Expect(true, "target 1 done", r => { r.tasks[0].target = 1; Consistent(r.tasks[0], 1); });
        Expect(true, "bonusXp 0", r => r.bonusXp = 0);
        Expect(true, "bonusXp 1e7", r => r.bonusXp = 10000000);
        Expect(true, "16 masks and 16 run values", r => {
            for (int i = 1; i < 16; i++) { r.masks.Add(new MetaChallengeMask { metric = "m" + i }); r.runValues.Add(new MetaChallengeRunValue { metric = "run-sales", runId = "r" + i, value = i }); } });
        Expect(true, "runId 160 / metric 64 chars", r => { r.runValues[0].runId = Text(160); r.runValues[0].metric = Text(64); r.masks[0].metric = Text(64); });
        Expect(true, "id/metric 64 chars", r => { r.tasks[0].id = Text(64); r.tasks[0].metric = Text(64); });
        Expect(true, "project with 1 stage", r => ProjectStages(r.tasks[2], new[] { 30 }, new[] { 5 }));
        Expect(true, "project with 5 stages", r => ProjectStages(r.tasks[2], new[] { 10, 20, 30, 40, 50 }, new[] { 1, 2, 3, 4, 5 }));
        Expect(true, "project complete", r => Consistent(r.tasks[2], 30));
        Expect(true, "all done and bonus paid", r => { CompleteConsistently(r); r.bonusPaid = true; });
        Expect(true, "all done, bonus not yet paid", r => CompleteConsistently(r));
        Expect(true, "no tasks", r => r.tasks.Clear());
        Expect(true, "stage arrays on a normal task are ignored", r => { r.tasks[0].stageTargets = new[] { 1 }; r.tasks[0].stageXp = new[] { 1, 2 }; });
        var repaired = SanitizeBase();
        repaired.tasks = null; repaired.masks = null; repaired.runValues = null;
        Check(ChallengeEngine.Sanitize(repaired) && repaired.tasks != null && repaired.masks != null && repaired.runValues != null, "Sanitize accepts and repairs null lists");

        Expect(false, "period 2 (Lifetime)", r => r.period = 2);
        Expect(false, "period -1", r => r.period = -1);
        Expect(false, "period 4", r => r.period = 4);
        Expect(false, "format 0", r => r.format = 0);
        Expect(false, "format 3", r => r.format = 3);
        Expect(false, "start < 0", r => r.startUtc = -1);
        Expect(false, "start == end", r => r.endUtc = r.startUtc);
        Expect(false, "start > end", r => r.endUtc = r.startUtc - Hour);
        Expect(false, "longer than 33 days", r => r.endUtc = r.startUtc + 33 * Day + 1);
        Expect(false, "end after 9000-02-01", r => { r.startUtc = EndLimit - Day; r.endUtc = EndLimit + 1; });
        Expect(false, "17 tasks in format 2", r => Grow(r, 17));
        Expect(false, "25 tasks in format 1", r => { r.format = 1; Grow(r, 25); });
        Expect(false, "null task", r => r.tasks.Add(null));
        Expect(false, "id null", r => r.tasks[0].id = null);
        Expect(false, "id empty", r => r.tasks[0].id = "");
        Expect(false, "id 65 chars", r => r.tasks[0].id = Text(65));
        Expect(false, "metric null", r => r.tasks[0].metric = null);
        Expect(false, "metric empty", r => r.tasks[0].metric = "");
        Expect(false, "metric 65 chars", r => r.tasks[0].metric = Text(65));
        Expect(false, "name 81 chars", r => r.tasks[0].name = Text(81));
        Expect(false, "unit 81 chars", r => r.tasks[0].unit = Text(81));
        Expect(false, "mode -1", r => r.tasks[0].mode = -1);
        Expect(false, "mode 4", r => r.tasks[0].mode = 4);
        Expect(false, "tier -1", r => r.tasks[0].tier = -1);
        Expect(false, "tier 5", r => r.tasks[0].tier = 5);
        Expect(false, "subject -2", r => r.tasks[0].subject = -2);
        Expect(false, "subject 10001", r => r.tasks[0].subject = 10001);
        Expect(false, "target 0", r => { r.tasks[0].target = 0; r.tasks[0].progress = 0; });
        Expect(false, "target 1e9+1", r => r.tasks[0].target = 1000000001);
        Expect(false, "xp -1", r => r.tasks[0].xp = -1);
        Expect(false, "xp 1e7+1", r => r.tasks[0].xp = 10000001);
        Expect(false, "progress -1", r => r.tasks[0].progress = -1);
        Expect(false, "progress above target", r => Consistent(r.tasks[0], 101));
        Expect(false, "done below target", r => r.tasks[0].done = true);
        Expect(false, "not done at target", r => r.tasks[0].progress = 100);
        Expect(false, "paid differs from xp", r => r.tasks[1].paid = 599);
        Expect(false, "paid on an open task", r => r.tasks[0].paid = 5);
        Expect(false, "project stageTargets null", r => r.tasks[2].stageTargets = null);
        Expect(false, "project stageXp null", r => r.tasks[2].stageXp = null);
        Expect(false, "project stage lengths differ", r => r.tasks[2].stageXp = new[] { 100, 200 });
        Expect(false, "project without stages", r => { r.tasks[2].stageTargets = new int[0]; r.tasks[2].stageXp = new int[0]; });
        Expect(false, "project with 6 stages", r => ProjectStages(r.tasks[2], new[] { 5, 10, 15, 20, 25, 30 }, new[] { 1, 2, 3, 4, 5, 6 }));
        Expect(false, "project stages not ascending", r => ProjectStages(r.tasks[2], new[] { 10, 10, 30 }, new[] { 100, 200, 300 }));
        Expect(false, "project stage 0", r => ProjectStages(r.tasks[2], new[] { 0, 20, 30 }, new[] { 100, 200, 300 }));
        Expect(false, "project last stage != target", r => r.tasks[2].target = 40);
        Expect(false, "project stagesPaid wrong", r => r.tasks[2].stagesPaid = 2);
        Expect(false, "project paid wrong", r => r.tasks[2].paid = 150);
        Expect(false, "project done too early", r => r.tasks[2].done = true);
        Expect(false, "project xp not 0", r => r.tasks[2].xp = 5);
        Expect(false, "project negative stage XP", r => ProjectStages(r.tasks[2], new[] { 10, 20, 30 }, new[] { -1, 200, 300 }));
        Expect(false, "project stage XP above 1e7", r => ProjectStages(r.tasks[2], new[] { 10, 20, 30 }, new[] { 100, 200, 10000001 }));
        Expect(false, "normal task turned project without stages", r => r.tasks[0].tier = 4);
        Expect(false, "17 masks", r => { for (int i = 1; i < 17; i++) r.masks.Add(new MetaChallengeMask { metric = "m" + i }); });
        Expect(false, "null mask", r => r.masks.Add(null));
        Expect(false, "mask metric 65 chars", r => r.masks[0].metric = Text(65));
        Expect(false, "17 run values", r => { for (int i = 1; i < 17; i++) r.runValues.Add(new MetaChallengeRunValue { metric = "run-sales", runId = "r" + i, value = i }); });
        Expect(false, "null run value", r => r.runValues.Add(null));
        Expect(false, "run metric 65 chars", r => r.runValues[0].metric = Text(65));
        Expect(false, "runId 161 chars", r => r.runValues[0].runId = Text(161));
        Expect(false, "negative run value", r => r.runValues[0].value = -1);
        Expect(false, "bonusXp -1", r => r.bonusXp = -1);
        Expect(false, "bonusXp 1e7+1", r => r.bonusXp = 10000001);
        Expect(false, "bonusPaid with open tasks", r => r.bonusPaid = true);
        Expect(false, "bonusPaid without tasks", r => { r.tasks.Clear(); r.bonusPaid = true; });
        Check(!ChallengeEngine.Sanitize(null), "Sanitize(null) is false");
    }

    // Spec §3 Sanitize rules (plus accepted deviation 7: format 1/2, no null mask/run entries, run values >= 0, stage XP 0..1e7).
    static bool Valid(MetaChallengeRound r)
    {
        if (r == null || r.period != 0 && r.period != 1 && r.period != 3 || r.format != 1 && r.format != 2) return false;
        if (r.startUtc < 0 || r.startUtc >= r.endUtc || r.endUtc - r.startUtc > 33 * Day || r.endUtc > EndLimit) return false;
        var tasks = r.tasks ?? new List<MetaChallengeTask>();
        if (tasks.Count > (r.format == 1 ? 24 : 16)) return false;
        bool all = true;
        foreach (var t in tasks) { if (!ValidTask(t)) return false; all &= t.done; }
        var masks = r.masks ?? new List<MetaChallengeMask>();
        var runs = r.runValues ?? new List<MetaChallengeRunValue>();
        if (masks.Count > 16 || runs.Count > 16) return false;
        foreach (var m in masks) if (m == null || (m.metric?.Length ?? 0) > 64) return false;
        foreach (var v in runs) if (v == null || (v.metric?.Length ?? 0) > 64 || (v.runId?.Length ?? 0) > 160 || v.value < 0) return false;
        if (r.bonusXp < 0 || r.bonusXp > 10000000) return false;
        return !r.bonusPaid || tasks.Count > 0 && all;
    }
    static bool ValidTask(MetaChallengeTask t)
    {
        if (t == null || string.IsNullOrEmpty(t.id) || t.id.Length > 64 || string.IsNullOrEmpty(t.metric) || t.metric.Length > 64) return false;
        if ((t.name?.Length ?? 0) > 80 || (t.unit?.Length ?? 0) > 80 || t.mode < 0 || t.mode > 3 || t.tier < 0 || t.tier > 4) return false;
        if (t.subject < -1 || t.subject > 10000 || t.target < 1 || t.target > 1000000000 || t.xp < 0 || t.xp > 10000000 || t.progress < 0 || t.progress > t.target) return false;
        if (t.tier != 4) return t.done == (t.progress >= t.target) && t.paid == (t.done ? t.xp : 0);
        var s = t.stageTargets; var x = t.stageXp;
        if (s == null || x == null || s.Length < 1 || s.Length > 5 || x.Length != s.Length || t.xp != 0 || s[s.Length - 1] != t.target) return false;
        int reached = 0; long paid = 0;
        for (int i = 0; i < s.Length; i++) {
            if (s[i] <= 0 || i > 0 && s[i] <= s[i - 1] || x[i] < 0 || x[i] > 10000000) return false;
            if (s[i] <= t.progress) { reached++; paid += x[i]; }
        }
        return t.stagesPaid == reached && t.paid == paid && t.done == (reached == s.Length);
    }

    // ---------- Fuzz: mutated rounds against the spec validator; the engine must never throw and must keep valid rounds valid

    static int Pick(Random g, params int[] v) => v[g.Next(v.Length)];
    static long PickL(Random g, params long[] v) => v[g.Next(v.Length)];
    static string PickS(Random g, params string[] v) => v[g.Next(v.Length)];

    static string Mutate(MetaChallengeRound r, Random g)
    {
        int kind = g.Next(42);
        var t = r.tasks != null && r.tasks.Count > 0 ? r.tasks[g.Next(r.tasks.Count)] : null;
        switch (kind) {
            case 0: r.period = Pick(g, -1, 0, 1, 2, 3, 4, int.MinValue); return "period";
            case 1: r.format = Pick(g, -1, 0, 1, 2, 3); return "format";
            case 2: r.startUtc = PickL(g, -1, 0, r.endUtc, r.endUtc - 1, r.endUtc + 1, r.startUtc - Day, long.MinValue); return "start";
            case 3: r.endUtc = PickL(g, r.startUtc, r.startUtc - 1, r.startUtc + 33 * Day, r.startUtc + 33 * Day + 1, EndLimit, EndLimit + 1, long.MaxValue, r.startUtc + Day); return "end";
            case 4: r.tasks = null; return "tasks null";
            case 5: r.tasks?.Add(null); return "null task";
            case 6: if (r.tasks != null && r.tasks.Count > 0) r.tasks.RemoveAt(g.Next(r.tasks.Count)); return "remove task";
            case 7: Grow(r, Pick(g, 15, 16, 17)); return "grow";
            case 8: r.format = 1; Grow(r, Pick(g, 20, 24, 25)); return "format 1 grow";
            case 9: if (t != null) t.id = PickS(g, null, "", Text(64), Text(65), "future-template"); return "id";
            case 10: if (t != null) t.metric = PickS(g, null, "", Text(64), Text(65), "blocks"); return "metric";
            case 11: if (t != null) t.name = PickS(g, null, "", Text(80), Text(81)); return "name";
            case 12: if (t != null) t.unit = PickS(g, null, Text(80), Text(81)); return "unit";
            case 13: if (t != null) t.mode = Pick(g, -1, 0, 3, 4); return "mode";
            case 14: if (t != null) t.tier = Pick(g, -1, 0, 3, 4, 5); return "tier";
            case 15: if (t != null) t.subject = Pick(g, -2, -1, 0, 10000, 10001); return "subject";
            case 16: if (t != null) t.target = Pick(g, 0, 1, -1, 1000000000, 1000000001, t.progress, t.target + 1); return "target";
            case 17: if (t != null) t.xp = Pick(g, -1, 0, 10000000, 10000001, 5); return "xp";
            case 18: if (t != null) t.progress = Pick(g, -1, 0, t.target, t.target + 1, t.target - 1); return "progress";
            case 19: if (t != null) t.done = !t.done; return "done";
            case 20: if (t != null) t.paid = Pick(g, 0, t.xp, t.xp + 1, -1); return "paid";
            case 21: if (t != null && t.target >= 0) Consistent(t, g.Next(t.target + 1)); return "consistent progress";
            case 22:
                if (t == null) return "stages";
                t.stageTargets = g.Next(7) switch { 0 => null, 1 => new int[0], 2 => new[] { t.target }, 3 => new[] { 1, 2, 3, 4, 5, Math.Max(6, t.target) },
                    4 => new[] { t.target, 2, 1 }, 5 => new[] { 0, 1, t.target }, _ => new[] { 1, 2, t.target + 1 } };
                return "stage targets";
            case 23: if (t != null) t.stageXp = g.Next(4) switch { 0 => null, 1 => new[] { 1 }, 2 => new[] { -1, 2, 3 }, _ => new[] { 1, 2, 10000001 } }; return "stage xp";
            case 24: if (t != null) t.stagesPaid = Pick(g, -1, t.stagesPaid + 1, 0); return "stagesPaid";
            case 25: if (t != null) t.tier = 4; return "tier 4";
            case 26: r.masks = null; return "masks null";
            case 27: r.masks?.Add(null); return "null mask";
            case 28: { r.masks ??= new List<MetaChallengeMask>(); int n = Pick(g, 16, 17); while (r.masks.Count < n) r.masks.Add(new MetaChallengeMask { metric = "m" + r.masks.Count }); return "masks " + n; }
            case 29: if (r.masks != null && r.masks.Count > 0 && r.masks[0] != null) r.masks[0].metric = PickS(g, null, Text(64), Text(65)); return "mask metric";
            case 30: r.runValues = null; return "runs null";
            case 31: r.runValues?.Add(null); return "null run";
            case 32: { r.runValues ??= new List<MetaChallengeRunValue>(); int n = Pick(g, 16, 17); while (r.runValues.Count < n) r.runValues.Add(new MetaChallengeRunValue { metric = "run-sales", runId = "r" + r.runValues.Count, value = 1 }); return "runs " + n; }
            case 33:
                if (r.runValues != null && r.runValues.Count > 0 && r.runValues[0] != null) {
                    var v = r.runValues[0];
                    switch (g.Next(4)) { case 0: v.runId = PickS(g, null, Text(160), Text(161)); break; case 1: v.value = PickL(g, -1, 0, long.MaxValue); break; default: v.metric = PickS(g, null, Text(64), Text(65)); break; }
                }
                return "run entry";
            case 34: r.bonusXp = Pick(g, -1, 0, 10000000, 10000001); return "bonusXp";
            case 35: r.bonusPaid = !r.bonusPaid; return "bonusPaid";
            case 36: CompleteConsistently(r); r.bonusPaid = true; return "complete all";
            case 37: r.tasks?.Clear(); return "clear tasks";
            case 38: if (t != null && t.tier != 4) { t.stageTargets = new[] { 1 }; t.stageXp = null; } return "stages on normal";
            case 39:
                if (t != null && t.target >= 3) { t.tier = 4; t.xp = 0; ProjectStages(t, new[] { t.target / 3, 2 * t.target / 3 + 1, t.target }, new[] { 1, 2, 3 }); }
                return "valid project";
            case 40: if (t != null) t.xp = 5; return "project xp";
            default: CompleteConsistently(r); return "complete tasks";
        }
    }

    static void Poke(MetaChallengeRound r, Random g)
    {
        var metrics = new List<string> { "blocks", "depth", null, "", Text(70) };
        if (r.tasks != null) foreach (var t in r.tasks) if (t?.metric != null) metrics.Add(t.metric);
        for (int i = 0; i < 6; i++) {
            string m = metrics[g.Next(metrics.Count)];
            int subject = g.Next(-2, 12);
            long amount = PickL(g, -5, 0, 1, 7, 1000, long.MaxValue);
            switch (g.Next(4)) {
                case 0: ChallengeEngine.Record(r, m, amount, subject); break;
                case 1: ChallengeEngine.RecordMax(r, m, amount, subject); break;
                case 2: ChallengeEngine.RecordDistinct(r, m, g.Next(-3, 34), subject); break;
                default: ChallengeEngine.RecordRun(r, m, PickS(g, null, "", "run-a", "run-b", Text(200)), amount, subject); break;
            }
        }
        ChallengeEngine.CompletedCount(r);
        ChallengeEngine.PaidXp(r);
    }

    static MetaChallengeRound Garbage(Random g)
    {
        var r = new MetaChallengeRound { period = g.Next(-1, 5), format = g.Next(0, 4), startUtc = PickL(g, -5, 0, Day0, long.MaxValue / 2), bonusXp = g.Next(-10, 2000), bonusPaid = g.Next(4) == 0 };
        r.endUtc = r.startUtc + PickL(g, -Day, 0, Day, 7 * Day, 31 * Day, 40 * Day);
        if (g.Next(5) == 0) r.tasks = null;
        else for (int i = g.Next(0, 20); i > 0; i--) {
            if (g.Next(15) == 0) { r.tasks.Add(null); continue; }
            var t = new MetaChallengeTask { id = PickS(g, "blocks", "x", null, ""), metric = PickS(g, "blocks", "depth", null), mode = g.Next(-1, 5), subject = g.Next(-2, 50),
                tier = g.Next(-1, 6), target = g.Next(-2, 50), xp = g.Next(-2, 500), progress = g.Next(-2, 60), done = g.Next(2) == 0, paid = g.Next(-2, 500),
                stagesPaid = g.Next(-1, 4), atLeast = g.Next(2) == 0 };
            if (g.Next(3) == 0) { t.stageTargets = new[] { g.Next(-1, 10), g.Next(5, 20), g.Next(10, 50) }; t.stageXp = new[] { g.Next(-1, 9), 2, 3 }; }
            if (g.Next(2) == 0) Consistent(t, Math.Max(0, Math.Min(t.target, t.progress)));
            r.tasks.Add(t);
        }
        for (int i = g.Next(0, 3); i > 0; i--) r.masks.Add(g.Next(6) == 0 ? null : new MetaChallengeMask { metric = PickS(g, "layers", null, Text(70)), mask = g.Next() });
        for (int i = g.Next(0, 3); i > 0; i--) r.runValues.Add(g.Next(6) == 0 ? null : new MetaChallengeRunValue { metric = "run-sales", runId = PickS(g, "a", null, Text(170)), value = g.Next(-3, 100) });
        return r;
    }

    static void FuzzChecks()
    {
        var bases = new List<MetaChallengeRound>();
        var rng = new Random(20261018);
        for (int i = 0; i < 12; i++) {
            var p = i % 3 == 0 ? Daily : i % 3 == 1 ? Weekly : Monthly;
            var ctx = SeriesContext(i * 5 + 3);
            PeriodBounds(p, 9000 + i, out long start, out long end, out int day);
            var round = ChallengeEngine.Draw(p, start, end, day, ctx.Context(), new ChallengeSettings(), null);
            foreach (var t in round.tasks) if (rng.Next(3) == 0) Consistent(t, rng.Next(t.target + 1));
            bases.Add(round);
        }
        bases.Add(SanitizeBase());
        var legacy = SanitizeBase(); legacy.format = 1; Grow(legacy, 20); bases.Add(legacy);
        var done = SanitizeBase(); CompleteConsistently(done); done.bonusPaid = true; bases.Add(done);
        foreach (var b in bases) Check(ChallengeEngine.Sanitize(Clone(b)) && Valid(Clone(b)), "fuzz base round is valid");
        int accepted = 0, rejected = 0, preserved = 0;
        for (int n = 0; n < 4000; n++) {
            var r = Clone(bases[rng.Next(bases.Count)]);
            var applied = new List<string>();
            for (int k = 1 + rng.Next(3); k > 0; k--) applied.Add(Mutate(r, rng));
            bool expected = Valid(Clone(r)), actual;
            int number = n;
            try { actual = ChallengeEngine.Sanitize(r); } catch (Exception e) { Fail("mutation #" + n + " (" + string.Join(", ", applied) + "): Sanitize threw " + e.GetType().Name); continue; }
            if (!Check(actual == expected, () => "mutation #" + number + " (" + string.Join(", ", applied) + "): Sanitize " + actual + ", spec " + expected)) continue;
            if (actual) accepted++; else rejected++;
            if (actual) Check(r.tasks != null && r.masks != null && r.runValues != null, "accepted round has non-null lists");
            int paidBefore = actual ? ChallengeEngine.PaidXp(r) : 0;
            try { Poke(r, rng); Pass(); } catch (Exception e) { Fail("mutation #" + n + ": engine threw " + e.GetType().Name + ": " + e.Message); continue; }
            if (actual) {
                bool still = ChallengeEngine.Sanitize(Clone(r));
                Check(still, () => "mutation #" + number + " (" + string.Join(", ", applied) + "): recorders turned a valid round invalid");
                Check(ChallengeEngine.PaidXp(r) >= paidBefore, () => "mutation #" + number + ": PaidXp decreased");
                if (still) preserved++;
            }
        }
        Check(accepted >= 400 && rejected >= 400, () => "fuzz mix too one-sided: " + accepted + " accepted, " + rejected + " rejected");
        for (int n = 0; n < 1500; n++) {
            var r = Garbage(rng);
            bool expected = Valid(Clone(r)), actual;
            int number = n;
            try { actual = ChallengeEngine.Sanitize(r); } catch (Exception e) { Fail("garbage #" + n + ": Sanitize threw " + e.GetType().Name); continue; }
            Check(actual == expected, () => "garbage #" + number + ": Sanitize " + actual + ", spec " + expected);
            try { Poke(r, rng); Pass(); } catch (Exception e) { Fail("garbage #" + n + ": engine threw " + e.GetType().Name); }
        }
        Note("fuzz: 4000 mutated rounds (" + accepted + " accepted, " + rejected + " rejected, " + preserved + " stayed valid under random recorder calls), 1500 garbage rounds");
    }

    // ---------- Bounds (§3): custom DST zones and UTC

    static TimeZoneInfo Zone(string id, double baseHours, TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
        => TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(baseHours), id, id + " standard", id + " daylight", new[] {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end) });
    static TimeZoneInfo.TransitionTime Floating(int hour, int month, int week, DayOfWeek day)
        => TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, hour, 0, 0), month, week, day);
    static TimeZoneInfo.TransitionTime FixedDate(int hour, int month, int day) => TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, hour, 0, 0), month, day);
    static DateTime Z(int y, int mo, int d, int h = 0, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);
    static int Mon(int y, int m, int d) => ((int)new DateTime(y, m, d).DayOfWeek + 6) % 7;
    static string Fmt(long ticks) => ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ? ticks.ToString(CultureInfo.InvariantCulture)
        : new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z";

    static void ExpectBounds(string label, ChallengePeriod p, DateTime now, TimeZoneInfo zone, DateTime start, DateTime end, int day)
    {
        long s, e; int d;
        try { ChallengeEngine.Bounds(p, now, zone, out s, out e, out d); } catch (Exception ex) { Fail(label + ": Bounds threw " + ex.GetType().Name); return; }
        Check(s == start.Ticks && e == end.Ticks && d == day, () => label + ": [" + Fmt(s) + ", " + Fmt(e) + ") day " + d + ", expected [" + Fmt(start.Ticks) + ", " + Fmt(end.Ticks) + ") day " + day);
    }

    // Walks consecutive periods: each starts where the previous ended, has a plausible length, starts at local midnight (or the first valid
    // time after a skipped midnight) on the right weekday/day of month, and the last tick of a period maps back to the same bounds.
    static void Contiguous(string label, TimeZoneInfo zone, ChallengePeriod p, DateTime from, int count)
    {
        long minLength = p == Daily ? 23 * Hour : p == Weekly ? 7 * Day - Hour : 28 * Day - Hour;
        long maxLength = p == Daily ? 25 * Hour : p == Weekly ? 7 * Day + Hour : 31 * Day + Hour;
        ChallengeEngine.Bounds(p, from, zone, out long s0, out long previousEnd, out int d0);
        DateTime previousLocal = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(s0, DateTimeKind.Utc), zone).Date;
        int bad = 0;
        for (int i = 0; i < count; i++) {
            ChallengeEngine.Bounds(p, new DateTime(previousEnd, DateTimeKind.Utc), zone, out long s, out long e, out int d);
            ChallengeEngine.Bounds(p, new DateTime(e - 1, DateTimeKind.Utc), zone, out long s2, out long e2, out int d2);
            var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(s, DateTimeKind.Utc), zone);
            var date = local.Date;
            bool midnight = local.TimeOfDay == TimeSpan.Zero || zone.IsInvalidTime(DateTime.SpecifyKind(date, DateTimeKind.Unspecified)) && local.TimeOfDay <= TimeSpan.FromHours(4);
            bool calendar = p == Daily ? date == previousLocal.AddDays(1) && d == ((int)date.DayOfWeek + 6) % 7
                : p == Weekly ? date == previousLocal.AddDays(7) && date.DayOfWeek == DayOfWeek.Monday && d == 0
                : date == previousLocal.AddMonths(1) && date.Day == 1 && d == ((int)date.DayOfWeek + 6) % 7;
            bool ok = s == previousEnd && e - s >= minLength && e - s <= maxLength && midnight && calendar && s2 == s && e2 == e && d2 == d;
            if (!ok && bad++ < 3) Fail(label + " " + p + " step " + i + ": [" + Fmt(s) + ", " + Fmt(e) + ") local start " + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " day " + d + " (previous end " + Fmt(previousEnd) + ")");
            else if (ok) Pass();
            previousEnd = e; previousLocal = date;
        }
    }

    static void BoundsChecks()
    {
        TimeZoneInfo cet, midnight, havana, south;
        try {
            cet = Zone("Test/CET", 1, Floating(2, 3, 5, DayOfWeek.Sunday), Floating(3, 10, 5, DayOfWeek.Sunday));
            midnight = Zone("Test/Midnight", -3, FixedDate(0, 4, 1), FixedDate(1, 10, 1));
            havana = Zone("Test/Havana", -5, Floating(0, 3, 2, DayOfWeek.Sunday), Floating(1, 11, 1, DayOfWeek.Sunday));
            south = Zone("Test/South", -3, Floating(0, 10, 1, DayOfWeek.Sunday), Floating(0, 2, 3, DayOfWeek.Sunday));
        } catch (Exception e) { Fail("custom time zones could not be created: " + e.Message); return; }
        // CET/CEST (Zurich rules)
        ExpectBounds("CET day of DST end (25 h)", Daily, Z(2026, 10, 25, 12), cet, Z(2026, 10, 24, 22), Z(2026, 10, 25, 23), 6);
        ExpectBounds("CET day of DST start (23 h)", Daily, Z(2026, 3, 29, 12), cet, Z(2026, 3, 28, 23), Z(2026, 3, 29, 22), 6);
        ExpectBounds("CET Oct 2026 = 31 d + 1 h", Monthly, Z(2026, 10, 10), cet, Z(2026, 9, 30, 22), Z(2026, 10, 31, 23), Mon(2026, 10, 1));
        ExpectBounds("CET Mar 2026 = 31 d - 1 h", Monthly, Z(2026, 3, 15), cet, Z(2026, 2, 28, 23), Z(2026, 3, 31, 22), Mon(2026, 3, 1));
        ExpectBounds("CET week starts Monday", Weekly, Z(2026, 10, 18, 12), cet, Z(2026, 10, 11, 22), Z(2026, 10, 18, 22), 0);
        ExpectBounds("CET week with DST end (7 d + 1 h)", Weekly, Z(2026, 10, 21, 12), cet, Z(2026, 10, 18, 22), Z(2026, 10, 25, 23), 0);
        ExpectBounds("CET week with DST start (7 d - 1 h)", Weekly, Z(2026, 3, 25, 12), cet, Z(2026, 3, 22, 23), Z(2026, 3, 29, 22), 0);
        ExpectBounds("CET ambiguous hour, first pass", Daily, Z(2026, 10, 25, 0, 30), cet, Z(2026, 10, 24, 22), Z(2026, 10, 25, 23), 6);
        ExpectBounds("CET ambiguous hour, second pass", Daily, Z(2026, 10, 25, 1, 30), cet, Z(2026, 10, 24, 22), Z(2026, 10, 25, 23), 6);
        ExpectBounds("CET just after the skipped hour", Daily, Z(2026, 3, 29, 1, 30), cet, Z(2026, 3, 28, 23), Z(2026, 3, 29, 22), 6);
        ExpectBounds("CET new year's day", Daily, Z(2027, 1, 1, 10), cet, Z(2026, 12, 31, 23), Z(2027, 1, 1, 23), Mon(2027, 1, 1));
        ExpectBounds("CET week across the year boundary", Weekly, Z(2027, 1, 1, 10), cet, Z(2026, 12, 27, 23), Z(2027, 1, 3, 23), 0);
        ExpectBounds("CET January 2027", Monthly, Z(2027, 1, 1, 10), cet, Z(2026, 12, 31, 23), Z(2027, 1, 31, 23), Mon(2027, 1, 1));
        ExpectBounds("CET last minute of 2026 (day)", Daily, Z(2026, 12, 31, 22, 59), cet, Z(2026, 12, 30, 23), Z(2026, 12, 31, 23), Mon(2026, 12, 31));
        ExpectBounds("CET last minute of 2026 (month)", Monthly, Z(2026, 12, 31, 22, 59), cet, Z(2026, 11, 30, 23), Z(2026, 12, 31, 23), Mon(2026, 12, 1));
        ExpectBounds("CET exactly at local midnight", Daily, Z(2026, 12, 31, 23), cet, Z(2026, 12, 31, 23), Z(2027, 1, 1, 23), Mon(2027, 1, 1));
        ExpectBounds("CET Feb 2028 = 29 d", Monthly, Z(2028, 2, 15), cet, Z(2028, 1, 31, 23), Z(2028, 2, 29, 23), Mon(2028, 2, 1));
        ExpectBounds("CET Feb 2027 = 28 d", Monthly, Z(2027, 2, 15), cet, Z(2027, 1, 31, 23), Z(2027, 2, 28, 23), Mon(2027, 2, 1));
        ExpectBounds("CET leap day", Daily, Z(2028, 2, 29, 12), cet, Z(2028, 2, 28, 23), Z(2028, 2, 29, 23), Mon(2028, 2, 29));
        Contiguous("CET", cet, Daily, Z(2026, 1, 1, 12), 730);
        Contiguous("CET", cet, Weekly, Z(2026, 1, 1, 12), 105);
        Contiguous("CET", cet, Monthly, Z(2026, 1, 1, 12), 26);
        // DST starts at local midnight (fixed date: 1 April 00:00 -> 01:00), ends 1 October 01:00 -> 00:00 (midnight ambiguous)
        ExpectBounds("midnight zone: day before the skipped midnight", Daily, Z(2026, 3, 31, 12), midnight, Z(2026, 3, 31, 3), Z(2026, 4, 1, 3), Mon(2026, 3, 31));
        ExpectBounds("midnight zone: day starts at 01:00", Daily, Z(2026, 4, 1, 12), midnight, Z(2026, 4, 1, 3), Z(2026, 4, 2, 2), Mon(2026, 4, 1));
        ExpectBounds("midnight zone: month starts at 01:00", Monthly, Z(2026, 4, 15), midnight, Z(2026, 4, 1, 3), Z(2026, 5, 1, 2), Mon(2026, 4, 1));
        ExpectBounds("midnight zone: week over the skipped midnight", Weekly, Z(2026, 4, 1, 12), midnight, Z(2026, 3, 30, 3), Z(2026, 4, 6, 2), 0);
        ExpectBounds("midnight zone: March", Monthly, Z(2026, 3, 15), midnight, Z(2026, 3, 1, 3), Z(2026, 4, 1, 3), Mon(2026, 3, 1));
        ExpectBounds("midnight zone: ambiguous midnight, first pass", Daily, Z(2026, 10, 1, 2, 30), midnight, Z(2026, 10, 1, 2), Z(2026, 10, 2, 3), Mon(2026, 10, 1));
        ExpectBounds("midnight zone: ambiguous midnight, second pass", Daily, Z(2026, 10, 1, 3, 30), midnight, Z(2026, 10, 1, 2), Z(2026, 10, 2, 3), Mon(2026, 10, 1));
        ExpectBounds("midnight zone: day before the ambiguous midnight", Daily, Z(2026, 9, 30, 12), midnight, Z(2026, 9, 30, 2), Z(2026, 10, 1, 2), Mon(2026, 9, 30));
        ExpectBounds("midnight zone: October", Monthly, Z(2026, 10, 15), midnight, Z(2026, 10, 1, 2), Z(2026, 11, 1, 3), Mon(2026, 10, 1));
        ExpectBounds("midnight zone: September", Monthly, Z(2026, 9, 15), midnight, Z(2026, 9, 1, 2), Z(2026, 10, 1, 2), Mon(2026, 9, 1));
        Contiguous("midnight zone", midnight, Daily, Z(2026, 1, 1, 12), 730);
        Contiguous("midnight zone", midnight, Weekly, Z(2026, 1, 1, 12), 105);
        Contiguous("midnight zone", midnight, Monthly, Z(2026, 1, 1, 12), 26);
        // Floating rules at midnight (Havana-like): second Sunday of March 00:00, first Sunday of November 01:00
        ExpectBounds("havana: skipped midnight", Daily, Z(2026, 3, 8, 12), havana, Z(2026, 3, 8, 5), Z(2026, 3, 9, 4), 6);
        ExpectBounds("havana: ambiguous midnight", Daily, Z(2026, 11, 1, 12), havana, Z(2026, 11, 1, 4), Z(2026, 11, 2, 5), 6);
        ExpectBounds("havana: week of DST start", Weekly, Z(2026, 3, 8, 12), havana, Z(2026, 3, 2, 5), Z(2026, 3, 9, 4), 0);
        ExpectBounds("havana: March", Monthly, Z(2026, 3, 8, 12), havana, Z(2026, 3, 1, 5), Z(2026, 4, 1, 4), Mon(2026, 3, 1));
        ExpectBounds("havana: November", Monthly, Z(2026, 11, 10), havana, Z(2026, 11, 1, 4), Z(2026, 12, 1, 5), Mon(2026, 11, 1));
        Contiguous("havana", havana, Daily, Z(2026, 1, 1, 12), 730);
        // Southern hemisphere rule (DST over new year): invariants only
        int southOk = 0;
        for (int i = 0; i < 400; i++) {
            var now = Z(2026, 1, 1, 7).AddHours(23.5 * i);
            foreach (var p in new[] { Daily, Weekly, Monthly }) {
                try {
                    ChallengeEngine.Bounds(p, now, south, out long s, out long e, out int d);
                    long max = p == Daily ? 25 * Hour : p == Weekly ? 7 * Day + Hour : 31 * Day + Hour;
                    if (s <= now.Ticks && now.Ticks < e && e - s <= max && d >= 0 && d <= 6) southOk++;
                    else Fail("south " + p + " at " + Fmt(now.Ticks) + ": [" + Fmt(s) + ", " + Fmt(e) + ") day " + d);
                } catch (Exception ex) { Fail("south " + p + ": Bounds threw " + ex.GetType().Name); }
            }
        }
        Check(southOk == 1200, () => "southern zone invariants held for " + southOk + " of 1200 instants");
        // UTC and null zone
        ExpectBounds("UTC day", Daily, Z(2026, 10, 18, 15), TimeZoneInfo.Utc, Z(2026, 10, 18), Z(2026, 10, 19), 6);
        ExpectBounds("UTC week", Weekly, Z(2026, 10, 18, 15), TimeZoneInfo.Utc, Z(2026, 10, 12), Z(2026, 10, 19), 0);
        ExpectBounds("UTC Feb 2028", Monthly, Z(2028, 2, 10), TimeZoneInfo.Utc, Z(2028, 2, 1), Z(2028, 3, 1), Mon(2028, 2, 1));
        ExpectBounds("UTC December", Monthly, Z(2026, 12, 31, 23, 59), TimeZoneInfo.Utc, Z(2026, 12, 1), Z(2027, 1, 1), Mon(2026, 12, 1));
        ExpectBounds("UTC exactly midnight", Daily, Z(2027, 1, 1), TimeZoneInfo.Utc, Z(2027, 1, 1), Z(2027, 1, 2), Mon(2027, 1, 1));
        ExpectBounds("null zone behaves like UTC", Daily, Z(2026, 10, 18, 15), null, Z(2026, 10, 18), Z(2026, 10, 19), 6);
        Contiguous("UTC", TimeZoneInfo.Utc, Daily, Z(2027, 6, 1, 12), 400);
        Contiguous("UTC", TimeZoneInfo.Utc, Monthly, Z(2027, 6, 1, 12), 30);
        foreach (var zone in new[] { TimeZoneInfo.Utc, cet, null }) {
            ChallengeEngine.Bounds(Lifetime, Z(2026, 10, 18), zone, out long s, out long e, out int d);
            Check(s == 0 && e == long.MaxValue && d == 0, () => "Lifetime bounds must be 0 / long.MaxValue / 0, got " + s + " / " + e + " / " + d);
        }
        // Extreme instants never throw and still bracket "now"
        foreach (var zone in new[] { TimeZoneInfo.Utc, cet, midnight, null })
            foreach (var now in new[] { new DateTime(DateTime.MinValue.Ticks, DateTimeKind.Utc), new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Utc), Z(1, 1, 1, 0, 30), Z(9999, 12, 31, 12) })
                foreach (var p in new[] { Daily, Weekly, Monthly }) {
                    try {
                        ChallengeEngine.Bounds(p, now, zone, out long s, out long e, out int d);
                        Check(s >= 0 && s <= now.Ticks && now.Ticks < e && d >= 0 && d <= 6, () => "extreme " + p + " at " + now.Ticks + " (" + (zone?.Id ?? "null") + "): " + s + " / " + e + " / " + d);
                    } catch (Exception ex) { Fail("extreme " + p + " at " + now.Ticks + ": Bounds threw " + ex.GetType().Name); }
                }
    }
}
