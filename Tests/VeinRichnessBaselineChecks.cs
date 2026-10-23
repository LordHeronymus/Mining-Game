using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Measures today's richness per vein thickness (thin = max. boundary distance 1, medium = 2, thick = 3+) and size,
// as the starting point for VeinRichness defaults. Worlds as in the game (GPS values, caves, altar chamber).
// Variants: legacy (no thin-vein Medium), ThinVeinMedium defaults and the user's thin-vein values from 23.10.2026.
// Report: Temp/VeinRichnessBaselineChecks.txt.
public static class VeinRichnessBaselineChecks
{
    const string ReportPath = "Temp/VeinRichnessBaselineChecks.txt";
    static readonly int[] Seeds = { -717317, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    static readonly string[] Classes = { "dünn", "mittel", "dick" };
    static readonly int[] SizeLimits = { 4, 8, 16, 32, 64, int.MaxValue };

    sealed class Tally { public long veins, cells, medium, rich; }

    public static string Main()
    {
        if (Application.isPlaying) return "FAIL: Edit Mode erforderlich.";
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) return "FAIL: MapGenerator mit Registry fehlt.";
        var record = GpsSettings.Document.records.Find(entry => entry.key == GpsSchema.ComponentKey(map));
        T Value<T>(string name, T fallback)
        {
            var node = record?.fields.Find(field => field.name == name);
            return node == null ? fallback : (T)GpsCodec.Write(node, GpsSettings.Profile.Resolve);
        }
        int width = Value("mapWidth", map.mapWidth), height = Value("mapHeight", map.mapHeight), offsetX = -width / 2;
        var layers = Value("layers", map.layers);
        var oreSettings = Value("oreSettings", map.oreSettings);
        var density = Value("oreDensityCurve", map.oreDensityCurve);
        float densityPercent = Value("oreDensityMultiplierPercent", map.oreDensityMultiplierPercent);
        bool useOreSettings = Value("useOreSettings", map.useOreSettings);
        var caveSettings = Value("caveGeneration", map.caveGeneration);
        int Minimum(Block ore)
        {
            var setting = oreSettings == null || !ore ? null : Array.Find(oreSettings, entry => entry != null && entry.ore == ore.id);
            if (setting != null && setting.minimumVeinSize > 0) return Mathf.Max(1, setting.minimumVeinSize);
            return ore && ore.id == BlockType.UltroniumOre ? 1 : Mathf.Max(1, map.minimumOreVeinSize);
        }
        var user = new ThinVeinMediumSettings { minimumLength = 4, minimumMedium = 1, maximumMedium = 10, blocksPerMedium = 3,
            spreadPercent = 50, thicknessPreference = .65f };
        var variants = new[] { ("vorher", (ThinVeinMediumSettings)null), ("ThinVeinMedium Standard", new ThinVeinMediumSettings()), ("Benutzer 23.10.", user) };
        var tallies = new Dictionary<(int variant, int ore, int cls, int size), Tally>();
        Tally Get((int, int, int, int) key) => tallies.TryGetValue(key, out var t) ? t : tallies[key] = new Tally();
        var names = new Dictionary<int, string>();
        long totalCells = 0;
        foreach (int seed in Seeds)
        {
            var sampler = new MapGenerationSampler(map.registry, seed, height, layers, density, densityPercent, map.transitionThickness,
                useOreSettings ? oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
            var altar = map.AltarChamber;
            var chamber = altar ? altar.ChooseLayout(seed, width, height) : default;
            var caves = CaveGenerator.Generate(caveSettings, seed, width, height, reserved: (x, y) => chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
            var blocks = new Block[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var cell = new Vector3Int(x + offsetX, -y, 0);
                    int index = y * width + x;
                    blocks[index] = chamber.IsOpen(cell) || caves[index] ? null : chamber.IsShell(cell) ? sampler.GetBaseBlock(x, y) : sampler.GetBlock(x, y);
                }
            ConnectedOreVeins.Generate(blocks, width, height, seed, sampler, Minimum,
                (x, y) => caves[y * width + x] || chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
            var depth = Depth(blocks, width, height);
            var results = variants.Select(v => OreVeins.Build(blocks, width, height, seed, v.Item2)).ToArray();
            foreach (var cells in Veins(blocks, width, height))
            {
                var ore = blocks[cells[0]];
                names[(int)ore.id] = ore.displayName;
                int maxDepth = cells.Max(i => depth[i]);
                int cls = Mathf.Min(2, maxDepth - 1), size = Array.FindIndex(SizeLimits, limit => cells.Length < limit);
                totalCells += cells.Length;
                for (int v = 0; v < results.Length; v++)
                    foreach (var key in new[] { (v, -1, cls, -1), (v, -1, cls, size), (v, (int)ore.id, cls, -1), (v, -1, -1, -1) })
                    {
                        var t = Get(key);
                        t.veins++; t.cells += cells.Length;
                        foreach (int i in cells) { if (results[v][i] == OreRichness.Medium) t.medium++; else if (results[v][i] == OreRichness.Rich) t.rich++; }
                    }
            }
        }
        var report = new StringBuilder();
        report.AppendLine(Seeds.Length + " Seeds, " + width + "x" + height + ". Zeilen: Adern | Zellen (Anteil aller Erzzellen) | Medium % | Rich % | Wert je Zelle (Erz)");
        string Row(string label, Tally t) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0,-30} {1,7} {2,8} ({3,4:0.0}%) M {4,5:0.0}% R {5,5:0.0}% W {6:0.000}", label, t.veins, t.cells, 100.0 * t.cells / Math.Max(1, totalCells),
            100.0 * t.medium / Math.Max(1, t.cells), 100.0 * t.rich / Math.Max(1, t.cells), (t.cells + .5 * t.medium + t.rich) / Math.Max(1.0, t.cells));
        for (int v = 0; v < variants.Length; v++)
        {
            report.AppendLine("== " + variants[v].Item1);
            for (int c = 0; c < 3; c++)
            {
                report.AppendLine(Row(Classes[c], Get((v, -1, c, -1))));
                if (v != 1)
                    for (int s = 0; s < SizeLimits.Length; s++)
                        if (tallies.TryGetValue((v, -1, c, s), out var bucket))
                            report.AppendLine(Row("  " + (s == 0 ? 1 : SizeLimits[s - 1]) + (s + 1 < SizeLimits.Length ? "–" + (SizeLimits[s] - 1) : "+") + " Blöcke", bucket));
            }
            report.AppendLine(Row("gesamt", Get((v, -1, -1, -1))));
        }
        report.AppendLine("== vorher je Erz (dünn/mittel/dick: M%/R%)");
        foreach (var ore in names.Keys.OrderBy(id => id))
            report.AppendLine(names[ore] + ": " + string.Join(" | ", Enumerable.Range(0, 3).Select(c => tallies.TryGetValue((0, ore, c, -1), out var t)
                ? (100.0 * t.medium / t.cells).ToString("0") + "/" + (100.0 * t.rich / t.cells).ToString("0") + " (" + t.cells + ")" : "-")));
        Directory.CreateDirectory("Temp");
        File.WriteAllText(ReportPath, report.ToString());
        return "PASS Messung " + Seeds.Length + " Seeds.";
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
}
