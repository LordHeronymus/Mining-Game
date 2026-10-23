using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Edit-mode checks for Medium ores in thin veins (ThinVeinMedium, GPS → Erzverteilung → Dünne Adern).
// 1. Synthetic veins: counts, tips, positions, Rich cells, overrides, seed stability, UnityEngine.Random.
// 2. Complete worlds as MapGenerator builds them (GPS values, caves, altar chamber, connected veins) for many seeds:
//    richness before (feature off = previous logic) and after, per ore and layer, value balance, timing.
// Sample veins are written as text grids (VEIN lines) for the previews in Design/OreVeins/MediumInThinVeins/.
// Changes nothing in the scene. Report: Temp/ThinVeinMediumChecks.txt.
public static class ThinVeinMediumChecks
{
    const string ReportPath = "Temp/ThinVeinMediumChecks.txt";
    const double WorldBudgetSeconds = 170;
    static readonly int[] Seeds = { -717317, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        20, 31337, -42, 99991, 123456, -5000000, 777, 2024, 4711, 65535 };
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

    static void Synthetic()
    {
        var gold = CreateOre(BlockType.GoldOre);
        var iron = CreateOre(BlockType.IronOre);
        var stone = ScriptableObject.CreateInstance<Block>();
        stone.id = BlockType.Stone;
        try
        {
            const int w = 40, h = 24, seed = 4242;
            var defaults = new ThinVeinMediumSettings();
            Block[] Grid() { var grid = new Block[w * h]; for (int i = 0; i < grid.Length; i++) grid[i] = stone; return grid; }

            // Band 2 x 12 (24 blocks): previously all Small, now floor(24 / 8 + u) = 3 Medium.
            var band = Grid();
            for (int y = 2; y < 4; y++) for (int x = 4; x < 16; x++) band[y * w + x] = gold;
            var legacy = OreVeins.Build(band, w, h, seed);
            Check(Count(legacy, band, gold, OreRichness.Small) == 24, "Band: vorher nicht nur Small.");
            var after = OreVeins.Build(band, w, h, seed, defaults);
            int bandMedium = Count(after, band, gold, OreRichness.Medium);
            Check(bandMedium == 3, "Band 2x12: " + bandMedium + " statt 3 Medium.");
            Check(Count(after, band, gold, OreRichness.Rich) == 0, "Band: Rich entstanden.");
            for (int i = 0; i < band.Length; i++)
                if (band[i] == gold && after[i] == OreRichness.Medium)
                    Check(i % w >= 5 && i % w <= 14, "Band: Medium in der äussersten Spalte bei x=" + (i % w));

            // Line 1 x 20: 2 or 3 Medium, never at the two tips.
            var line = Grid();
            for (int x = 10; x < 30; x++) line[10 * w + x] = gold;
            after = OreVeins.Build(line, w, h, seed, defaults);
            int lineMedium = Count(after, line, gold, OreRichness.Medium);
            Check(lineMedium >= 2 && lineMedium <= 3, "Linie 1x20: " + lineMedium + " Medium.");
            Check(after[10 * w + 10] == OreRichness.Small && after[10 * w + 29] == OreRichness.Small, "Linie: Medium an einer Spitze.");
            for (int x = 10; x < 30; x++)
                if (after[10 * w + x] == OreRichness.Medium) Check(x >= 13 && x <= 26, "Linie: Medium zu nahe am Ende bei x=" + x);

            // Too short (7 blocks): unchanged.
            var shortVein = Grid();
            for (int x = 3; x < 10; x++) shortVein[5 * w + x] = gold;
            Check(Count(OreVeins.Build(shortVein, w, h, seed, defaults), shortVein, gold, OreRichness.Medium) == 0, "Kurze Ader (7) bekam Medium.");

            // Thick disc keeps the previous richness exactly.
            var disc = Grid();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if ((x - 20) * (x - 20) + (y - 12) * (y - 12) < 40) disc[y * w + x] = iron;
            Check(OreVeins.Build(disc, w, h, seed).SequenceEqual(OreVeins.Build(disc, w, h, seed, defaults)), "Dicke Ader verändert.");

            // Three blocks thick: Small rim, Rich spine, no Medium. Unchanged by default, extended on request.
            var spine = Grid();
            for (int y = 6; y < 9; y++) for (int x = 5; x < 19; x++) spine[y * w + x] = iron;
            legacy = OreVeins.Build(spine, w, h, seed);
            int spineRich = Count(legacy, spine, iron, OreRichness.Rich);
            Check(spineRich > 0 && Count(legacy, spine, iron, OreRichness.Medium) == 0, "Rich-Kern-Ader: Ausgangslage unerwartet.");
            Check(legacy.SequenceEqual(OreVeins.Build(spine, w, h, seed, defaults)), "Rich-Kern-Ader ohne Schalter verändert.");
            var withCores = new ThinVeinMediumSettings { includeRichCores = true };
            after = OreVeins.Build(spine, w, h, seed, withCores);
            int spineMedium = Count(after, spine, iron, OreRichness.Medium);
            Check(spineMedium >= 1 && spineMedium <= 4, "Rich-Kern-Ader mit Schalter: " + spineMedium + " Medium.");
            for (int i = 0; i < spine.Length; i++)
                if (legacy[i] == OreRichness.Rich) Check(after[i] == OreRichness.Rich, "Rich-Zelle verändert.");

            // Off, null, per-ore entries and an inverted minimum/maximum.
            Check(OreVeins.Build(band, w, h, seed, new ThinVeinMediumSettings { enabled = false }).SequenceEqual(OreVeins.Build(band, w, h, seed)), "Ausgeschaltet nicht wie vorher.");
            var goldOff = new ThinVeinMediumSettings { ores = new[] { new ThinVeinMediumOre { ore = BlockType.GoldOre, enabled = false } } };
            Check(Count(OreVeins.Build(band, w, h, seed, goldOff), band, gold, OreRichness.Medium) == 0, "Gold-Eintrag aus wirkt nicht.");
            var goldLong = new ThinVeinMediumSettings { ores = new[] { new ThinVeinMediumOre { ore = BlockType.GoldOre, minimumLength = 30 } } };
            Check(Count(OreVeins.Build(band, w, h, seed, goldLong), band, gold, OreRichness.Medium) == 0, "Gold-Mindestlänge 30 wirkt nicht.");
            var goldOne = new ThinVeinMediumSettings { ores = new[] { new ThinVeinMediumOre { ore = BlockType.GoldOre, maximumMedium = 1 } } };
            Check(Count(OreVeins.Build(band, w, h, seed, goldOne), band, gold, OreRichness.Medium) == 1, "Gold-Maximum 1 wirkt nicht.");
            var ironOnly = new ThinVeinMediumSettings { ores = new[] { new ThinVeinMediumOre { ore = BlockType.IronOre, enabled = false } } };
            Check(Count(OreVeins.Build(band, w, h, seed, ironOnly), band, gold, OreRichness.Medium) == 3, "Eintrag eines anderen Erzes wirkt auf Gold.");
            var inverted = new ThinVeinMediumSettings { minimumMedium = 5, maximumMedium = 2 };
            Check(Count(OreVeins.Build(band, w, h, seed, inverted), band, gold, OreRichness.Medium) == 2, "Minimum über Maximum nicht begrenzt.");
            var many = new ThinVeinMediumSettings { blocksPerMedium = 1, maximumMedium = 20 };
            Check(Count(OreVeins.Build(band, w, h, seed, many), band, gold, OreRichness.Medium) == 20, "Maximum 20 nicht erreicht.");

            // Two ores side by side are separate veins.
            var pair = Grid();
            for (int x = 2; x < 14; x++) { pair[3 * w + x] = gold; pair[4 * w + x] = iron; }
            after = OreVeins.Build(pair, w, h, seed, defaults);
            Check(Count(after, pair, gold, OreRichness.Medium) >= 1 && Count(after, pair, iron, OreRichness.Medium) >= 1, "Nebeneinander liegende Adern nicht getrennt behandelt.");

            // Seed stability and independence from UnityEngine.Random (state neither read nor changed).
            var saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(1);
            var first = OreVeins.Build(band, w, h, seed, defaults);
            var state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(987654);
            var second = OreVeins.Build(band, w, h, seed, defaults);
            UnityEngine.Random.InitState(1);
            Check(state.Equals(UnityEngine.Random.state), "Build hat UnityEngine.Random verändert.");
            UnityEngine.Random.state = saved;
            Check(first.SequenceEqual(second), "Ergebnis hängt von UnityEngine.Random ab.");
            var lineBase = OreVeins.Build(line, w, h, seed, defaults);
            bool differs = false;
            for (int s = 1; s < 40 && !differs; s++) differs = !lineBase.SequenceEqual(OreVeins.Build(line, w, h, seed + s, defaults));
            Check(differs, "Platzierung reagiert nicht auf den Seed.");

            // GPS: one field, defaults, profile still valid.
            Check(GpsSchema.Fields(typeof(GpsPreferences)).Count(field => field.name == "thinVeinMedium") == 1, "GPS-Feld thinVeinMedium fehlt.");
            Check(GpsSchema.Sections.Any(section => section.tab == "Erzverteilung" && section.fields.Any(field => field.name == "thinVeinMedium")), "GPS-Abschnitt fehlt.");
            var shipped = new GpsPreferences().thinVeinMedium;
            Check(shipped != null && shipped.enabled && shipped.minimumLength == 8 && shipped.minimumMedium == 1 && shipped.maximumMedium == 4, "GPS-Standardwerte falsch.");
            Check(GpsSettings.ValidateDocument(GpsSettings.Document, out string gpsError), "GPS-Profil ungültig: " + gpsError);
            Line("Synthetisch: Band 2x12 → " + bandMedium + " Medium, Linie 1x20 → " + lineMedium + ", Rich-Kern mit Schalter → " + spineMedium +
                 ". GPS aktuell = Standard: " + (JsonUtility.ToJson(ThinVeinMediumSettings.Current) == JsonUtility.ToJson(new ThinVeinMediumSettings())));
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

    // Thin: no cell has all four neighbours in the vein, so the distance-based richness left it entirely Small.
    static bool IsThin(int[] cells, Block[] blocks, int width, int height)
    {
        var ore = blocks[cells[0]];
        foreach (int i in cells)
        {
            int x = i % width, y = i / width;
            if (x > 0 && y > 0 && x + 1 < width && y + 1 < height && blocks[i - 1] == ore && blocks[i + 1] == ore &&
                blocks[i - width] == ore && blocks[i + width] == ore) return false;
        }
        return true;
    }

    // Position along the longest path through the vein: 0 and 1 are its ends.
    static Dictionary<int, float> Positions(int[] cells, int width, int height)
    {
        var set = new HashSet<int>(cells);
        Dictionary<int, int> Distances(int source, out int far)
        {
            var distance = new Dictionary<int, int> { [source] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(source);
            far = source;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue(), d = distance[cell];
                if (d > distance[far] || (d == distance[far] && cell < far)) far = cell;
                int x = cell % width, y = cell / width;
                foreach (int next in new[] { x > 0 ? cell - 1 : -1, x + 1 < width ? cell + 1 : -1, y > 0 ? cell - width : -1, y + 1 < height ? cell + width : -1 })
                    if (next >= 0 && set.Contains(next) && !distance.ContainsKey(next)) { distance[next] = d + 1; queue.Enqueue(next); }
            }
            return distance;
        }
        Distances(cells[0], out int a);
        var fromA = Distances(a, out int b);
        var fromB = Distances(b, out _);
        return cells.ToDictionary(cell => cell, cell => fromA[cell] + fromB[cell] == 0 ? .5f : fromA[cell] / (float)(fromA[cell] + fromB[cell]));
    }

    sealed class Tally
    {
        public int veins, longVeins, thinLong, allSmallBefore, allSmallAfter, mediumOk, coreNoMedium;
        public long cells, mediumBefore, mediumAfter, mediumCores, valueBefore, valueAfter, valueCores;
    }

    static int Half(OreRichness level) => OreTile.ExpectedDropHalfUnits(level);

    static string Worlds()
    {
        var setup = ReadSetup();
        int width = setup.width, height = setup.height;
        var defaults = new ThinVeinMediumSettings();
        var cores = new ThinVeinMediumSettings { includeRichCores = true };
        var tallies = new Dictionary<(int ore, int layer), Tally>();
        Tally Get(int ore, int layer) => tallies.TryGetValue((ore, layer), out var tally) ? tally : tallies[(ore, layer)] = new Tally();
        var names = new Dictionary<int, string>();
        var bins = new int[5];
        int placed = 0, tips = 0, seedsDone = 0, samples = 0;
        long legacyMs = 0, newMs = 0, veinMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var samplesText = new StringBuilder();
        var sampledOres = new HashSet<BlockType>();
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
            var withCores = OreVeins.Build(blocks, width, height, seed, cores);
            Check(UnityEngine.Random.state.Equals(randomBefore), "Seed " + seed + ": Generierung hat UnityEngine.Random verändert.");
            Check(after.SequenceEqual(OreVeins.Build(blocks, width, height, seed, defaults)), "Seed " + seed + ": zweiter Build weicht ab.");
            if (seedsDone < 2)
            {
                var again = Generate(setup, seed, out _);
                Check(blocks.SequenceEqual(again), "Seed " + seed + ": zweite Generierung ergibt andere Erzbelegung.");
                Check(after.SequenceEqual(OreVeins.Build(again, width, height, seed, defaults)), "Seed " + seed + ": zweite Generierung ergibt andere Erzgrössen.");
            }

            foreach (var cells in Veins(blocks, width, height))
            {
                var ore = blocks[cells[0]];
                names[(int)ore.id] = ore.displayName;
                defaults.Resolve(ore.id, out int minimumLength, out _, out _);
                bool thin = IsThin(cells, blocks, width, height);
                int layer = setup.Layer(cells[cells.Length / 2] / width);
                int mediumBefore = 0, mediumAfter = 0, mediumCores = 0, richBefore = 0;
                long valueBefore = 0, valueAfter = 0, valueCores = 0;
                foreach (int i in cells)
                {
                    if (before[i] == OreRichness.Medium) mediumBefore++;
                    if (before[i] == OreRichness.Rich) richBefore++;
                    if (after[i] == OreRichness.Medium) mediumAfter++;
                    if (withCores[i] == OreRichness.Medium) mediumCores++;
                    valueBefore += Half(before[i]); valueAfter += Half(after[i]); valueCores += Half(withCores[i]);
                    if (after[i] != before[i])
                        Check(before[i] == OreRichness.Small && after[i] == OreRichness.Medium, "Seed " + seed + ": unerlaubte Änderung " + before[i] + "→" + after[i]);
                    if (before[i] == OreRichness.Rich) Check(withCores[i] == OreRichness.Rich, "Seed " + seed + ": Rich-Zelle verändert.");
                }
                bool qualifies = thin && cells.Length >= minimumLength;
                if (!qualifies) Check(mediumAfter == mediumBefore, "Seed " + seed + ": nicht dünne Ader verändert.");
                if (mediumAfter != mediumBefore)
                {
                    var positions = Positions(cells, width, height);
                    var set = new HashSet<int>(cells);
                    foreach (int i in cells)
                    {
                        if (before[i] == after[i]) continue;
                        float t = positions[i];
                        bins[Mathf.Min(4, (int)(Mathf.Min(t, 1f - t) * 10f))]++;
                        int x = i % width, y = i / width, sides = 0;
                        if (x > 0 && set.Contains(i - 1)) sides++;
                        if (x + 1 < width && set.Contains(i + 1)) sides++;
                        if (y > 0 && set.Contains(i - width)) sides++;
                        if (y + 1 < height && set.Contains(i + width)) sides++;
                        if (sides <= 1) tips++;
                        placed++;
                    }
                }
                if (qualifies && samples < 10 && seedsDone == 0 && sampledOres.Add(ore.id))
                {
                    samples++;
                    samplesText.AppendLine(Sample("VEIN", seed, ore, layer, cells, before, after, width));
                }
                foreach (var key in new[] { ((int)ore.id, layer), ((int)ore.id, -1), (-1, layer), (-1, -1) })
                {
                    var tally = Get(key.Item1, key.Item2);
                    tally.veins++;
                    tally.cells += cells.Length;
                    tally.mediumBefore += mediumBefore; tally.mediumAfter += mediumAfter; tally.mediumCores += mediumCores;
                    tally.valueBefore += valueBefore; tally.valueAfter += valueAfter; tally.valueCores += valueCores;
                    if (cells.Length < minimumLength) continue;
                    tally.longVeins++;
                    if (mediumBefore == 0 && richBefore > 0) tally.coreNoMedium++;
                    if (!thin) continue;
                    tally.thinLong++;
                    if (mediumBefore == 0) tally.allSmallBefore++;
                    if (mediumAfter == 0) tally.allSmallAfter++;
                    if (mediumAfter >= 1 && mediumAfter <= 4) tally.mediumOk++;
                }
            }
            if (seedsDone == 0)
            {
                // Two veins with a Rich spine for the optional switch.
                int coreSamples = 0;
                foreach (var cells in Veins(blocks, width, height))
                {
                    if (coreSamples >= 2 || cells.Length < 12 || cells.Length > 30) continue;
                    bool hasRich = cells.Any(i => before[i] == OreRichness.Rich), hasMedium = cells.Any(i => before[i] == OreRichness.Medium);
                    if (!hasRich || hasMedium) continue;
                    coreSamples++;
                    samplesText.AppendLine(Sample("CORE", seed, blocks[cells[0]], setup.Layer(cells[0] / width), cells, before, withCores, width));
                }
            }
            seedsDone++;
        }

        var total = Get(-1, -1);
        Check(total.thinLong > 0, "Keine dünnen Adern gefunden.");
        Check(total.allSmallAfter == 0, total.allSmallAfter + " dünne Adern ab Mindestlänge ohne Medium.");
        Check(total.mediumOk == total.thinLong, (total.thinLong - total.mediumOk) + " dünne Adern ausserhalb 1–4 Medium.");
        Check(tips * 100 <= placed, "Zu viele Medium an Aderspitzen: " + tips + " von " + placed);
        Check(bins[0] * 20 <= placed, "Zu viele Medium im äussersten Zehntel: " + bins[0] + " von " + placed);
        double valueChange = 100.0 * (total.valueAfter - total.valueBefore) / Math.Max(1, total.valueBefore);
        Check(valueChange >= 0 && valueChange < 2, "Gesamtwert verschiebt sich zu stark: " + valueChange.ToString("0.00") + " %");

        Line("Welten: " + seedsDone + " Seeds (" + string.Join(", ", Seeds.Take(seedsDone)) + "), " + width + "x" + height +
             ", GPS-Werte, Höhlen und Altarkammer wie im Spiel. Standard: Mindestlänge " + defaults.minimumLength + ", " +
             defaults.minimumMedium + "–" + defaults.maximumMedium + " Medium, 1 je " + defaults.blocksPerMedium + " Blöcke, Streuung " + defaults.spreadPercent + " %.");
        Line("Spalten: Adern | ab Mindestlänge | davon dünn | dünn nur Small vorher → nachher | dünn mit 1–4 Medium | Medium-Anteil vorher → nachher | Wert Δ | Rich-Kern ohne Medium | Medium/Wert mit Schalter Rich-Kern");
        void Row(string label, Tally t)
        {
            double cells = Math.Max(1, t.cells);
            Line(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0,-14} {1,6} {2,6} {3,5} | {4,4} ({5,4:0.0}%) → {6} | {7,4} | M {8,4:0.0}% → {9,4:0.0}% | {10,5:+0.00;-0.00}% | {11,5} | M {12,4:0.0}% {13,5:+0.00;-0.00}%",
                label, t.veins, t.longVeins, t.thinLong, t.allSmallBefore, 100.0 * t.allSmallBefore / Math.Max(1, t.longVeins), t.allSmallAfter, t.mediumOk,
                100 * t.mediumBefore / cells, 100 * t.mediumAfter / cells, 100.0 * (t.valueAfter - t.valueBefore) / Math.Max(1, t.valueBefore),
                t.coreNoMedium, 100 * t.mediumCores / cells, 100.0 * (t.valueCores - t.valueBefore) / Math.Max(1, t.valueBefore)));
        }
        foreach (var key in tallies.Keys.Where(key => key.layer == -1 && key.ore >= 0).OrderBy(key => key.ore))
            Row(names.TryGetValue(key.ore, out var name) ? name : ((BlockType)key.ore).ToString(), tallies[key]);
        foreach (var key in tallies.Keys.Where(key => key.ore == -1 && key.layer >= 0).OrderBy(key => key.layer))
            Row("Ebene " + (key.layer + 1), tallies[key]);
        Row("Gesamt", total);
        foreach (var key in tallies.Keys.Where(key => key.ore >= 0 && key.layer >= 0).OrderBy(key => key.ore).ThenBy(key => key.layer))
        {
            var t = tallies[key];
            if (t.thinLong > 0) report.Append((names.TryGetValue(key.ore, out var n) ? n : key.ore.ToString()) + " E" + (key.layer + 1) + ": " + t.allSmallBefore + "/" + t.longVeins + "; ");
        }
        Line("");
        Line("Lage der neuen Medium (Abstand zum nächsten Aderende, 0–10/10–20/20–30/30–40/40–50 %): " + string.Join("/", bins) +
             "; an Spitzen: " + tips + " von " + placed + ".");
        Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Zeit je Welt: Adern wachsen {0:0} ms, Reichhaltigkeit vorher {1:0} ms, nachher {2:0} ms (+{3:0} ms).",
            veinMs / (double)seedsDone, legacyMs / (double)seedsDone, newMs / (double)seedsDone, (newMs - legacyMs) / (double)seedsDone));
        report.Append(samplesText);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} Seeds: dünne Adern ab Mindestlänge {1}, vorher nur Small {2}, nachher mit 1–4 Medium {3}; Wert {4:+0.00;-0.00} % (mit Rich-Kern-Schalter {5:+0.00;-0.00} %); Reichhaltigkeit +{6:0} ms je Welt.",
            seedsDone, total.thinLong, total.allSmallBefore, total.mediumOk, valueChange,
            100.0 * (total.valueCores - total.valueBefore) / Math.Max(1, total.valueBefore), (newMs - legacyMs) / (double)seedsDone);
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
