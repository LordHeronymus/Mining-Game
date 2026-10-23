using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Edit-mode checks for the ore shares per vein thickness (VeinRichness, GPS → Erzverteilung → Erz-Anteile).
// 1. Synthetic veins: counts, Rich in the core and Medium around it, thin veins along their middle and never at the
//    tips, minimum size, factor per ore, off = previous logic, seed stability, UnityEngine.Random.
// 2. Complete worlds as MapGenerator builds them (GPS values, caves, altar chamber) for many seeds: shares and value
//    per thickness, ore and layer before (previous fixed thresholds) and after, timing.
// Sample veins are written as text grids (THIN/MID/THICK lines) for Design/OreVeins/VeinRichness/compose_previews.py.
// Changes nothing in the scene. Report: Temp/VeinRichnessChecks.txt.
public static class VeinRichnessChecks
{
    const string ReportPath = "Temp/VeinRichnessChecks.txt";
    const double WorldBudgetSeconds = 170;
    static readonly int[] Seeds = { -717317, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        20, 31337, -42, 99991, 123456, -5000000, 777, 2024, 4711, 65535 };
    static readonly string[] Classes = { "dünn", "mittel", "dick" }, Kinds = { "THIN", "MID", "THICK" };
    static readonly List<string> failures = new();
    static readonly StringBuilder report = new();

    static void Check(bool ok, string message) { if (!ok) failures.Add(message); }
    static void Line(string text) => report.AppendLine(text);

    public static string Main()
    {
        if (Application.isPlaying) return "FAIL: Edit Mode erforderlich.";
        failures.Clear(); report.Clear();
        Directory.CreateDirectory("Temp");
        var randomBefore = UnityEngine.Random.state;
        string summary;
        try
        {
            Synthetic();
            summary = Worlds();
        }
        catch (Exception error) { failures.Add("Exception: " + error); summary = "abgebrochen"; }
        Check(UnityEngine.Random.state.Equals(randomBefore), "UnityEngine.Random-Zustand wurde verändert.");
        foreach (var failure in failures) report.Insert(0, "FAIL " + failure + "\n");
        File.WriteAllText(ReportPath, report.ToString());
        return failures.Count == 0 ? "PASS " + summary : "FAIL " + failures.Count + " Fehler, erster: " + failures[0];
    }

    // ------------------------------------------------------------------ synthetic veins

    static Block CreateOre(BlockType id)
    {
        var block = ScriptableObject.CreateInstance<Block>();
        block.id = id;
        block.smallOre = new[] { ScriptableObject.CreateInstance<OreTile>() };
        block.mediumOre = new[] { ScriptableObject.CreateInstance<OreTile>() };
        block.richOre = new[] { ScriptableObject.CreateInstance<OreTile>() };
        return block;
    }

    static void Destroy(Block block)
    {
        foreach (var tile in block.smallOre.Concat(block.mediumOre).Concat(block.richOre)) UnityEngine.Object.DestroyImmediate(tile);
        UnityEngine.Object.DestroyImmediate(block);
    }

    static int Count(OreRichness[] richness, Block[] grid, Block ore, OreRichness level)
    {
        int count = 0;
        for (int i = 0; i < grid.Length; i++) if (grid[i] == ore && richness[i] == level) count++;
        return count;
    }

    static bool Near(int actual, float expected) => actual == Mathf.FloorToInt(expected) || actual == Mathf.CeilToInt(expected);

    static void Synthetic()
    {
        var gold = CreateOre(BlockType.GoldOre);
        var iron = CreateOre(BlockType.IronOre);
        var stone = ScriptableObject.CreateInstance<Block>();
        stone.id = BlockType.Stone;
        try
        {
            const int w = 40, h = 24, seed = 4242;
            var exact = new VeinRichnessSettings { variationPercent = 0 };
            Block[] Grid() { var grid = new Block[w * h]; for (int i = 0; i < grid.Length; i++) grid[i] = stone; return grid; }

            // Thin band 2 x 12: 24 × 33 % Medium, 24 × 4 % Rich, never in the outermost columns.
            var band = Grid();
            for (int y = 2; y < 4; y++) for (int x = 4; x < 16; x++) band[y * w + x] = gold;
            var after = OreVeins.Build(band, w, h, seed, exact);
            int bandMedium = Count(after, band, gold, OreRichness.Medium), bandRich = Count(after, band, gold, OreRichness.Rich);
            Check(Near(bandMedium, 24 * .33f) && Near(bandRich, 24 * .04f), "Band 2x12: " + bandMedium + " Medium / " + bandRich + " Rich.");
            for (int i = 0; i < band.Length; i++)
                if (band[i] == gold && after[i] != OreRichness.Small) Check(i % w != 4 && i % w != 15, "Band: Medium/Rich in der äussersten Spalte.");

            // Line 1 x 20: the tips stay Small.
            var line = Grid();
            for (int x = 10; x < 30; x++) line[10 * w + x] = gold;
            after = OreVeins.Build(line, w, h, seed, exact);
            Check(after[10 * w + 10] == OreRichness.Small && after[10 * w + 29] == OreRichness.Small, "Linie: Spitze nicht Small.");
            Check(Near(Count(after, line, gold, OreRichness.Medium), 20 * .33f), "Linie: Medium-Anzahl falsch.");

            // Below the minimum size: only Small.
            var tiny = Grid();
            for (int x = 3; x < 6; x++) tiny[5 * w + x] = gold;
            var tinyRichness = OreVeins.Build(tiny, w, h, seed, exact);
            Check(Count(tinyRichness, tiny, gold, OreRichness.Small) == 3, "Ader unter Mindestgrösse nicht nur Small.");

            // Thick disc: shares of the thick row, Rich only in the deepest cells and Medium around them.
            var disc = Grid();
            int discCells = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if ((x - 20) * (x - 20) + (y - 12) * (y - 12) < 40) { disc[y * w + x] = iron; discCells++; }
            after = OreVeins.Build(disc, w, h, seed, exact);
            int discRich = Count(after, disc, iron, OreRichness.Rich), discMedium = Count(after, disc, iron, OreRichness.Medium);
            Check(Near(discRich, discCells * .12f) && Near(discMedium, discCells * .36f), "Scheibe: " + discMedium + " Medium / " + discRich + " Rich von " + discCells);
            var depth = Depth(disc, w, h);
            int Min(OreRichness level) { int m = int.MaxValue; for (int i = 0; i < disc.Length; i++) if (disc[i] == iron && after[i] == level) m = Math.Min(m, depth[i]); return m; }
            int Max(OreRichness level) { int m = 0; for (int i = 0; i < disc.Length; i++) if (disc[i] == iron && after[i] == level) m = Math.Max(m, depth[i]); return m; }
            Check(Min(OreRichness.Rich) >= Max(OreRichness.Medium) && Min(OreRichness.Medium) >= Max(OreRichness.Small), "Scheibe: Rich/Medium nicht im Kern.");

            // Three blocks thick (medium row).
            var spine = Grid();
            for (int y = 6; y < 9; y++) for (int x = 5; x < 19; x++) spine[y * w + x] = iron;
            after = OreVeins.Build(spine, w, h, seed, exact);
            Check(Near(Count(after, spine, iron, OreRichness.Medium), 42 * .34f) && Near(Count(after, spine, iron, OreRichness.Rich), 42 * .08f), "Mittlere Ader: Anteile falsch.");

            // Factor per ore, off, variation within ±25 %.
            var goldZero = new VeinRichnessSettings { variationPercent = 0, ores = new[] { new VeinRichnessOre { ore = BlockType.GoldOre, factor = 0 } } };
            Check(Count(OreVeins.Build(band, w, h, seed, goldZero), band, gold, OreRichness.Small) == 24, "Faktor 0 wirkt nicht.");
            var goldDouble = new VeinRichnessSettings { variationPercent = 0, ores = new[] { new VeinRichnessOre { ore = BlockType.GoldOre, factor = 2 } } };
            Check(Near(Count(OreVeins.Build(band, w, h, seed, goldDouble), band, gold, OreRichness.Medium), 24 * .66f), "Faktor 2 wirkt nicht.");
            var ironOnly = new VeinRichnessSettings { variationPercent = 0, ores = new[] { new VeinRichnessOre { ore = BlockType.IronOre, factor = 0 } } };
            Check(Count(OreVeins.Build(band, w, h, seed, ironOnly), band, gold, OreRichness.Medium) == bandMedium, "Faktor eines anderen Erzes wirkt auf Gold.");
            Check(OreVeins.Build(disc, w, h, seed, new VeinRichnessSettings { enabled = false }).SequenceEqual(OreVeins.Build(disc, w, h, seed)), "Ausgeschaltet nicht wie vorher.");
            int lowest = int.MaxValue, highest = 0;
            for (int s = 0; s < 200; s++)
            {
                int medium = Count(OreVeins.Build(disc, w, h, seed + s, new VeinRichnessSettings()), disc, iron, OreRichness.Medium);
                lowest = Math.Min(lowest, medium); highest = Math.Max(highest, medium);
            }
            Check(lowest >= Mathf.FloorToInt(discCells * .36f * .75f) && highest <= Mathf.CeilToInt(discCells * .36f * 1.25f) && highest > lowest,
                "Schwankung ±25 % verletzt: " + lowest + "–" + highest);

            // Seed stability and independence from UnityEngine.Random (state neither read nor changed).
            var saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(1);
            var first = OreVeins.Build(band, w, h, seed, new VeinRichnessSettings());
            var state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(987654);
            var second = OreVeins.Build(band, w, h, seed, new VeinRichnessSettings());
            UnityEngine.Random.InitState(1);
            Check(state.Equals(UnityEngine.Random.state), "Build hat UnityEngine.Random verändert.");
            UnityEngine.Random.state = saved;
            Check(first.SequenceEqual(second), "Ergebnis hängt von UnityEngine.Random ab.");

            // GPS: one field, defaults, profile still valid.
            Check(GpsSchema.Fields(typeof(GpsPreferences)).Count(field => field.name == "veinRichness") == 1, "GPS-Feld veinRichness fehlt.");
            Check(GpsSchema.Sections.Any(section => section.tab == "Erzverteilung" && section.fields.Any(field => field.name == "veinRichness")), "GPS-Abschnitt fehlt.");
            var shipped = new GpsPreferences().veinRichness;
            Check(shipped != null && shipped.enabled && shipped.minimumSize == 4 && shipped.variationPercent == 25 && shipped.thin.mediumPercent == 33 &&
                shipped.thin.richPercent == 4 && shipped.spreadPercent == 50 && Mathf.Approximately(shipped.thicknessPreference, .65f), "GPS-Standardwerte falsch.");
            Check(GpsSettings.ValidateDocument(GpsSettings.Document, out string gpsError), "GPS-Profil ungültig: " + gpsError);
            Line("Synthetisch: Band 2x12 → " + bandMedium + " Medium / " + bandRich + " Rich, Scheibe " + discCells + " → " + discMedium + "/" + discRich +
                 ", Schwankung " + lowest + "–" + highest + ". GPS aktuell = Standard: " +
                 (JsonUtility.ToJson(VeinRichnessSettings.Current) == JsonUtility.ToJson(new VeinRichnessSettings())));
        }
        finally { Destroy(gold); Destroy(iron); UnityEngine.Object.DestroyImmediate(stone); }
    }

    // ------------------------------------------------------------------ complete worlds

    sealed class Setup
    {
        public MapGenerator map;
        public int width, height, transition, legacyMinimum;
        public MapLayer[] layers;
        public int[] layerStarts;
        public AnimationCurve density;
        public float densityPercent;
        public bool useOreSettings;
        public OreDistributionSetting[] oreSettings;
        public CaveGenerationSettings caves;

        public int Minimum(Block ore)
        {
            var setting = oreSettings == null || !ore ? null : Array.Find(oreSettings, entry => entry != null && entry.ore == ore.id);
            if (setting != null && setting.minimumVeinSize > 0) return Mathf.Max(1, setting.minimumVeinSize);
            return ore && ore.id == BlockType.UltroniumOre ? 1 : Mathf.Max(1, legacyMinimum);
        }

        public int Layer(int depth)
        {
            int layer = 0;
            for (int i = 0; i < layerStarts.Length; i++) if (depth >= layerStarts[i]) layer = i;
            return layer;
        }
    }

    // The game applies the GPS profile to the map before it generates; the scene component may hold older values.
    static Setup ReadSetup()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) throw new Exception("MapGenerator mit Registry fehlt in der Szene.");
        var record = GpsSettings.Document.records.Find(entry => entry.key == GpsSchema.ComponentKey(map));
        T Value<T>(string name, T fallback)
        {
            var node = record?.fields.Find(field => field.name == name);
            return node == null ? fallback : (T)GpsCodec.Write(node, GpsSettings.Profile.Resolve);
        }
        var setup = new Setup
        {
            map = map, width = Value("mapWidth", map.mapWidth), height = Value("mapHeight", map.mapHeight),
            transition = map.transitionThickness, legacyMinimum = map.minimumOreVeinSize, layers = Value("layers", map.layers),
            density = Value("oreDensityCurve", map.oreDensityCurve), densityPercent = Value("oreDensityMultiplierPercent", map.oreDensityMultiplierPercent),
            useOreSettings = Value("useOreSettings", map.useOreSettings), oreSettings = Value("oreSettings", map.oreSettings),
            caves = Value("caveGeneration", map.caveGeneration)
        };
        setup.layerStarts = setup.layers.Select(layer => layer.startDepth).OrderBy(start => start).ToArray();
        return setup;
    }

    static Block[] Generate(Setup setup, int seed, out long veinMilliseconds)
    {
        int width = setup.width, height = setup.height, offsetX = -width / 2;
        var sampler = new MapGenerationSampler(setup.map.registry, seed, height, setup.layers, setup.density, setup.densityPercent,
            setup.transition, setup.useOreSettings ? setup.oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
        var altar = setup.map.AltarChamber;
        var chamber = altar ? altar.ChooseLayout(seed, width, height) : default;
        var caves = CaveGenerator.Generate(setup.caves, seed, width, height,
            reserved: (x, y) => chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
        var blocks = new Block[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector3Int(x + offsetX, -y, 0);
                int index = y * width + x;
                blocks[index] = chamber.IsOpen(cell) || caves[index] ? null :
                    chamber.IsShell(cell) ? sampler.GetBaseBlock(x, y) : sampler.GetBlock(x, y);
            }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ConnectedOreVeins.Generate(blocks, width, height, seed, sampler, setup.Minimum,
            (x, y) => caves[y * width + x] || chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
        veinMilliseconds = watch.ElapsedMilliseconds;
        return blocks;
    }

    // Distance to the vein boundary as in OreVeins.BuildSteps (4-neighbourhood, other blocks and map edge are boundary).
    static int[] Depth(Block[] blocks, int width, int height)
    {
        var distance = new int[blocks.Length];
        var queue = new int[blocks.Length];
        int head = 0, tail = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            if (!block || !block.HasOreOverlays) continue;
            int x = i % width, y = i / width;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1 || blocks[i - 1] != block || blocks[i + 1] != block ||
                blocks[i - width] != block || blocks[i + width] != block) { distance[i] = 1; queue[tail++] = i; }
        }
        while (head < tail)
        {
            int i = queue[head++], x = i % width, y = i / width;
            if (x > 0) Visit(i - 1);
            if (x + 1 < width) Visit(i + 1);
            if (y > 0) Visit(i - width);
            if (y + 1 < height) Visit(i + width);
            void Visit(int to)
            {
                if (distance[to] != 0 || blocks[to] != blocks[i]) return;
                distance[to] = distance[i] + 1;
                queue[tail++] = to;
            }
        }
        return distance;
    }

    // 4-connected veins of one ore as OreVeins.BuildSteps sees them; cells ascending (first = lowest index).
    static List<int[]> Veins(Block[] blocks, int width, int height)
    {
        var result = new List<int[]>();
        var visited = new bool[blocks.Length];
        var queue = new int[blocks.Length];
        for (int start = 0; start < blocks.Length; start++)
        {
            var ore = blocks[start];
            if (visited[start] || !ore || !ore.HasOreOverlays) continue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                int i = queue[head++], x = i % width, y = i / width;
                if (x > 0) Visit(i - 1);
                if (x + 1 < width) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y + 1 < height) Visit(i + width);
            }
            var cells = new int[tail];
            Array.Copy(queue, cells, tail);
            Array.Sort(cells);
            result.Add(cells);

            void Visit(int index)
            {
                if (visited[index] || blocks[index] != ore) return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }
        return result;
    }

    sealed class Tally { public long veins, cells, mediumBefore, richBefore, mediumAfter, richAfter; }

    static double Value(long cells, long medium, long rich) => (cells + .5 * medium + rich) / Math.Max(1.0, cells);

    static string Worlds()
    {
        var setup = ReadSetup();
        int width = setup.width, height = setup.height;
        var defaults = new VeinRichnessSettings();
        var tallies = new Dictionary<(int ore, int layer, int cls), Tally>();
        Tally Get((int, int, int) key) => tallies.TryGetValue(key, out var tally) ? tally : tallies[key] = new Tally();
        var names = new Dictionary<int, string>();
        int seedsDone = 0, tips = 0, thinPlaced = 0;
        long legacyMs = 0, newMs = 0, veinMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var samples = new StringBuilder();
        var sampled = new int[3];
        foreach (int seed in Seeds)
        {
            if (seedsDone >= 4 && clock.Elapsed.TotalSeconds > WorldBudgetSeconds) break;
            var randomBefore = UnityEngine.Random.state;
            var blocks = Generate(setup, seed, out long veins);
            veinMs += veins;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var before = OreVeins.Build(blocks, width, height, seed);
            legacyMs += watch.ElapsedMilliseconds;
            watch.Restart();
            var after = OreVeins.Build(blocks, width, height, seed, defaults);
            newMs += watch.ElapsedMilliseconds;
            Check(UnityEngine.Random.state.Equals(randomBefore), "Seed " + seed + ": Generierung hat UnityEngine.Random verändert.");
            Check(after.SequenceEqual(OreVeins.Build(blocks, width, height, seed, defaults)), "Seed " + seed + ": zweiter Build weicht ab.");
            if (seedsDone < 2)
            {
                var again = Generate(setup, seed, out _);
                Check(blocks.SequenceEqual(again), "Seed " + seed + ": zweite Generierung ergibt andere Erzbelegung.");
                Check(after.SequenceEqual(OreVeins.Build(again, width, height, seed, defaults)), "Seed " + seed + ": zweite Generierung ergibt andere Erzgrössen.");
            }
            var depth = Depth(blocks, width, height);
            foreach (var cells in Veins(blocks, width, height))
            {
                var ore = blocks[cells[0]];
                names[(int)ore.id] = ore.displayName;
                int cls = Mathf.Min(2, cells.Max(i => depth[i]) - 1), layer = setup.Layer(cells[cells.Length / 2] / width);
                long mb = 0, rb = 0, ma = 0, ra = 0;
                foreach (int i in cells)
                {
                    if (before[i] == OreRichness.Medium) mb++; else if (before[i] == OreRichness.Rich) rb++;
                    if (after[i] == OreRichness.Medium) ma++; else if (after[i] == OreRichness.Rich) ra++;
                }
                if (cells.Length < defaults.minimumSize) Check(ma + ra == 0, "Seed " + seed + ": Ader unter Mindestgrösse mit Medium/Rich.");
                if (cls == 0)
                {
                    var set = new HashSet<int>(cells);
                    foreach (int i in cells)
                    {
                        if (after[i] == OreRichness.Small) continue;
                        int x = i % width, y = i / width, sides = 0;
                        if (x > 0 && set.Contains(i - 1)) sides++;
                        if (x + 1 < width && set.Contains(i + 1)) sides++;
                        if (y > 0 && set.Contains(i - width)) sides++;
                        if (y + 1 < height && set.Contains(i + width)) sides++;
                        if (sides <= 1) tips++;
                        thinPlaced++;
                    }
                }
                if (seedsDone == 0 && sampled[cls] < 3 && cells.Length >= 10 && cells.Length <= 40 && ma + ra > 0)
                {
                    sampled[cls]++;
                    samples.AppendLine(Sample(Kinds[cls], seed, ore, layer, cells, before, after, width));
                }
                foreach (var key in new[] { ((int)ore.id, -1, -1), (-1, layer, -1), (-1, -1, cls), ((int)ore.id, -1, cls), (-1, -1, -1) })
                {
                    var t = Get(key);
                    t.veins++; t.cells += cells.Length;
                    t.mediumBefore += mb; t.richBefore += rb; t.mediumAfter += ma; t.richAfter += ra;
                }
            }
            seedsDone++;
        }

        var total = Get((-1, -1, -1));
        double change = 100.0 * (Value(total.cells, total.mediumAfter, total.richAfter) / Value(total.cells, total.mediumBefore, total.richBefore) - 1);
        var shares = new[] { defaults.thin, defaults.medium, defaults.thick };
        double previousValue = 0;
        for (int c = 0; c < 3; c++)
        {
            var t = Get((-1, -1, c));
            double medium = 100.0 * t.mediumAfter / Math.Max(1, t.cells), rich = 100.0 * t.richAfter / Math.Max(1, t.cells);
            Check(Math.Abs(medium - shares[c].mediumPercent) < 3 && Math.Abs(rich - shares[c].richPercent) < 2,
                Classes[c] + ": Anteile " + medium.ToString("0.0") + "/" + rich.ToString("0.0") + " % statt " + shares[c].mediumPercent + "/" + shares[c].richPercent + " %");
            double value = Value(t.cells, t.mediumAfter, t.richAfter);
            Check(value > previousValue, Classes[c] + ": nicht wertvoller als die dünnere Stufe.");
            previousValue = value;
        }
        Check(tips * 100 <= thinPlaced, "Zu viele Medium/Rich an Spitzen dünner Adern: " + tips + " von " + thinPlaced);

        Line("Welten: " + seedsDone + " Seeds, " + width + "x" + height + ", GPS-Werte, Höhlen und Altarkammer wie im Spiel. Standard: dünn " +
             defaults.thin.mediumPercent + "/" + defaults.thin.richPercent + ", mittel " + defaults.medium.mediumPercent + "/" + defaults.medium.richPercent +
             ", dick " + defaults.thick.mediumPercent + "/" + defaults.thick.richPercent + " % Medium/Rich, Mindestgrösse " + defaults.minimumSize +
             ", Schwankung ±" + defaults.variationPercent + " %.");
        Line("Spalten: Adern | Zellen (Anteil) | Medium % vorher → nachher | Rich % vorher → nachher | Wert je Zelle vorher → nachher (Δ)");
        void Row(string label, Tally t)
        {
            double vb = Value(t.cells, t.mediumBefore, t.richBefore), va = Value(t.cells, t.mediumAfter, t.richAfter);
            Line(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0,-16} {1,6} {2,8} ({3,4:0.0}%) | M {4,4:0.0} → {5,4:0.0} | R {6,4:0.0} → {7,4:0.0} | W {8:0.000} → {9:0.000} ({10:+0.0;-0.0}%)",
                label, t.veins, t.cells, 100.0 * t.cells / Math.Max(1, total.cells), 100.0 * t.mediumBefore / Math.Max(1, t.cells), 100.0 * t.mediumAfter / Math.Max(1, t.cells),
                100.0 * t.richBefore / Math.Max(1, t.cells), 100.0 * t.richAfter / Math.Max(1, t.cells), vb, va, 100 * (va / vb - 1)));
        }
        for (int c = 0; c < 3; c++) Row(Classes[c], Get((-1, -1, c)));
        foreach (var key in tallies.Keys.Where(key => key.layer == -1 && key.cls == -1 && key.ore >= 0).OrderBy(key => key.ore))
            Row(names.TryGetValue(key.ore, out var name) ? name : ((BlockType)key.ore).ToString(), tallies[key]);
        foreach (var key in tallies.Keys.Where(key => key.ore == -1 && key.cls == -1 && key.layer >= 0).OrderBy(key => key.layer))
            Row("Ebene " + (key.layer + 1), tallies[key]);
        Row("Gesamt", total);
        Line("An Spitzen dünner Adern: " + tips + " von " + thinPlaced + " Medium/Rich.");
        Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Zeit je Welt: Adern wachsen {0:0} ms, Reichhaltigkeit vorher {1:0} ms, nachher {2:0} ms.",
            veinMs / (double)seedsDone, legacyMs / (double)seedsDone, newMs / (double)seedsDone));
        report.Append(samples);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} Seeds: Wert je Welt {1:+0.0;-0.0} %, Anteile M/R dünn {2:0.0}/{3:0.0}, mittel {4:0.0}/{5:0.0}, dick {6:0.0}/{7:0.0} %; Reichhaltigkeit {8:0} → {9:0} ms je Welt.",
            seedsDone, change, Pct(0, true), Pct(0, false), Pct(1, true), Pct(1, false), Pct(2, true), Pct(2, false), legacyMs / (double)seedsDone, newMs / (double)seedsDone);

        double Pct(int c, bool medium) { var t = Get((-1, -1, c)); return 100.0 * (medium ? t.mediumAfter : t.richAfter) / Math.Max(1, t.cells); }
    }

    // One line per vein: bounding box in generation cells and row-major grids (s/m/r, '.' outside the vein).
    static string Sample(string kind, int seed, Block ore, int layer, int[] cells, OreRichness[] before, OreRichness[] after, int width)
    {
        int minX = cells.Min(i => i % width), maxX = cells.Max(i => i % width), minY = cells.Min(i => i / width), maxY = cells.Max(i => i / width);
        var set = new HashSet<int>(cells);
        string Grid(OreRichness[] richness)
        {
            var text = new StringBuilder();
            for (int y = minY; y <= maxY; y++)
            {
                if (y > minY) text.Append('/');
                for (int x = minX; x <= maxX; x++)
                {
                    int i = y * width + x;
                    text.Append(!set.Contains(i) ? '.' : richness[i] == OreRichness.Small ? 's' : richness[i] == OreRichness.Medium ? 'm' : 'r');
                }
            }
            return text.ToString();
        }
        return kind + " " + seed + " " + ore.id + " E" + (layer + 1) + " " + minX + "," + minY + " " + Grid(before) + " " + Grid(after);
    }
}
