using System;
using System.Collections.Generic;
using UnityEngine;

// GPS → Erzverteilung → Dünne Adern (GpsPreferences.thinVeinMedium). Only shapes newly generated worlds:
// saves store their ore tiles. Docs/Erz-Reichhaltigkeit.md.
[Serializable]
public sealed class ThinVeinMediumSettings
{
    [InspectorName("Aktiv")] public bool enabled = true;
    [Range(2, 200), InspectorName("Mindestlänge (Erzblöcke)")] public int minimumLength = 8;
    [Range(0, 20), InspectorName("Medium min. pro Ader")] public int minimumMedium = 1;
    [Range(0, 20), InspectorName("Medium max. pro Ader")] public int maximumMedium = 4;
    [Range(1f, 100f), InspectorName("Erzblöcke pro Medium")] public float blocksPerMedium = 8f;
    [Range(0f, 100f), InspectorName("Streuung entlang der Ader (%)")] public float spreadPercent = 60f;
    [Range(0f, 1f), InspectorName("Dicke Stellen bevorzugen")] public float thicknessPreference = .5f;
    [InspectorName("Auch Adern mit Rich-Kern ohne Medium")] public bool includeRichCores;
    [InspectorName("Je Erz")] public ThinVeinMediumOre[] ores = Array.Empty<ThinVeinMediumOre>();

    public static ThinVeinMediumSettings Current => GpsSettings.Preferences?.thinVeinMedium ?? new ThinVeinMediumSettings();

    // False when the ore's own entry switches it off. Ore entries override the length and the maximum.
    public bool Resolve(BlockType ore, out int minimumLengthBlocks, out int minimum, out int maximum)
    {
        ThinVeinMediumOre entry = null;
        if (ores != null)
            foreach (var candidate in ores)
                if (candidate != null && candidate.ore == ore) { entry = candidate; break; }
        minimumLengthBlocks = Mathf.Max(2, entry != null && entry.minimumLength > 0 ? entry.minimumLength : minimumLength);
        maximum = Mathf.Max(0, entry != null && entry.maximumMedium > 0 ? entry.maximumMedium : maximumMedium);
        minimum = Mathf.Clamp(minimumMedium, 0, maximum);
        return enabled && (entry == null || entry.enabled);
    }
}

[Serializable]
public sealed class ThinVeinMediumOre
{
    [OreOnly] public BlockType ore;
    [InspectorName("Aktiv")] public bool enabled = true;
    [Range(0, 200), InspectorName("Mindestlänge (0 = global)")] public int minimumLength;
    [Range(0, 20), InspectorName("Medium max. (0 = global)")] public int maximumMedium;
}

// Richness grows with the distance to the vein boundary (OreVeins.BuildSteps). In a vein at most two blocks
// thick every cell lies on that boundary, so it would stay entirely Small however long it is. This raises a few
// of its Small cells to Medium: around the middle of the vein, preferring thicker spots and never its tips.
// Everything derives from OreVeins.Hash, so the result is seed-stable and leaves UnityEngine.Random untouched.
public sealed class ThinVeinMedium
{
    const uint CountStream = 0x7A1D0001u, CenterStream = 0x7A1D0002u, TargetStream = 0x7A1D0003u, NoiseStream = 0x7A1D0004u;

    readonly ThinVeinMediumSettings settings;
    readonly Dictionary<int, int> local = new();
    int[] fromA = Array.Empty<int>(), fromB = Array.Empty<int>(), bfs = Array.Empty<int>();
    readonly List<Candidate> candidates = new();
    readonly HashSet<int> chosen = new();

    struct Candidate { public int cell; public float position, thickness, noise; public bool richNeighbor, tip; }

    public int VeinsRaised { get; private set; }
    public int CellsRaised { get; private set; }

    public ThinVeinMedium(ThinVeinMediumSettings settings) => this.settings = settings;

    // cells[0..count) is one 4-connected vein of a single ore and cells[0] its lowest index.
    public int Apply(int[] cells, int count, Block[] blocks, OreRichness[] richness, int width, int height, int seed)
    {
        if (settings == null || !settings.enabled || count < 2) return 0;
        var ore = blocks[cells[0]];
        if (!ore || !settings.Resolve(ore.id, out int minimumLength, out int minimumMedium, out int maximumMedium) ||
            count < minimumLength || maximumMedium <= 0) return 0;
        bool rich = false;
        for (int i = 0; i < count; i++)
        {
            var level = richness[cells[i]];
            if (level == OreRichness.Medium) return 0;
            rich |= level == OreRichness.Rich;
        }
        if (rich && !settings.includeRichCores) return 0;

        int start = cells[0], sx = start % width, sy = start / width;
        int wanted = Mathf.FloorToInt(count / Mathf.Max(1f, settings.blocksPerMedium) + Unit(OreVeins.Hash(seed, sx, sy, CountStream)));
        wanted = Mathf.Clamp(wanted, minimumMedium, maximumMedium);

        local.Clear();
        for (int i = 0; i < count; i++) local[cells[i]] = i;
        if (fromA.Length < count) { fromA = new int[count]; fromB = new int[count]; bfs = new int[count]; }
        // The longest path through the vein approximates its axis: distances to both of its ends give each
        // cell a position from 0 (one end) to 1 (other end).
        int endA = Distances(start, fromA, count, width, height);
        int endB = Distances(endA, fromA, count, width, height);
        Distances(endB, fromB, count, width, height);
        int length = Mathf.Max(1, fromA[local[endB]]);

        candidates.Clear();
        for (int i = 0; i < count; i++)
        {
            int cell = cells[i];
            if (richness[cell] != OreRichness.Small) continue;
            int x = cell % width, y = cell / width, sides = 0, around = 0;
            bool richNeighbor = false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int neighbor = ny * width + nx;
                    if (!local.ContainsKey(neighbor)) continue;
                    around++;
                    if (dx != 0 && dy != 0) continue;
                    sides++;
                    richNeighbor |= richness[neighbor] == OreRichness.Rich;
                }
            int span = fromA[i] + fromB[i];
            candidates.Add(new Candidate
            {
                cell = cell, position = span == 0 ? .5f : fromA[i] / (float)span,
                // A plain line has two neighbours around a cell, a two-block band five.
                thickness = Mathf.Clamp01((around - 2) / 3f),
                noise = (OreVeins.Hash(seed, x, y, NoiseStream) & 0xffff) / 65535f,
                richNeighbor = richNeighbor, tip = sides <= 1
            });
        }
        wanted = Mathf.Min(wanted, candidates.Count);
        if (wanted <= 0) return 0;

        // Evenly spaced targets cover the central share of the vein; the centre shifts a little per vein.
        float spread = Mathf.Clamp01(settings.spreadPercent / 100f);
        float center = .5f + (Unit(OreVeins.Hash(seed, sx, sy, CenterStream)) - .5f) * (1f - spread) * .5f;
        float preference = Mathf.Clamp01(settings.thicknessPreference);
        chosen.Clear();
        for (int target = 0; target < wanted; target++)
        {
            float jitter = (Unit(OreVeins.Hash(seed, sx + target, sy, TargetStream)) - .5f) * spread / wanted * .5f;
            float goal = center + ((target + .5f) / wanted - .5f) * spread + jitter;
            int best = -1;
            float bestScore = float.NegativeInfinity;
            foreach (var candidate in candidates)
            {
                if (chosen.Contains(candidate.cell)) continue;
                float score = -Mathf.Abs(candidate.position - goal) * length +
                    preference * (2f * candidate.thickness + (candidate.richNeighbor ? 1f : 0f)) +
                    .5f * candidate.noise - (candidate.tip ? 3f : 0f);
                if (NextToChosen(candidate.cell, width, height)) score -= 1.5f;
                if (score < bestScore || (score == bestScore && candidate.cell > best)) continue;
                bestScore = score;
                best = candidate.cell;
            }
            chosen.Add(best);
            richness[best] = OreRichness.Medium;
        }
        VeinsRaised++;
        CellsRaised += wanted;
        return wanted;
    }

    // Breadth-first distances inside the vein; returns the farthest cell (lowest index on ties).
    int Distances(int source, int[] distance, int count, int width, int height)
    {
        for (int i = 0; i < count; i++) distance[i] = -1;
        int head = 0, tail = 0, far = source;
        bfs[tail++] = source;
        distance[local[source]] = 0;
        while (head < tail)
        {
            int cell = bfs[head++], d = distance[local[cell]], farDistance = distance[local[far]];
            if (d > farDistance || (d == farDistance && cell < far)) far = cell;
            int x = cell % width, y = cell / width;
            if (x > 0) Visit(cell - 1);
            if (x + 1 < width) Visit(cell + 1);
            if (y > 0) Visit(cell - width);
            if (y + 1 < height) Visit(cell + width);

            void Visit(int neighbor)
            {
                if (!local.TryGetValue(neighbor, out int index) || distance[index] >= 0) return;
                distance[index] = d + 1;
                bfs[tail++] = neighbor;
            }
        }
        return far;
    }

    bool NextToChosen(int cell, int width, int height)
    {
        int x = cell % width, y = cell / width;
        return (x > 0 && chosen.Contains(cell - 1)) || (x + 1 < width && chosen.Contains(cell + 1)) ||
            (y > 0 && chosen.Contains(cell - width)) || (y + 1 < height && chosen.Contains(cell + width));
    }

    static float Unit(uint hash) => (hash >> 8) / 16777216f;
}
