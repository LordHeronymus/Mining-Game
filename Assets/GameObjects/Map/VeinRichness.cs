using System;
using System.Collections.Generic;
using UnityEngine;

// GPS → Erzverteilung → Erz-Anteile (GpsPreferences.veinRichness). Only shapes newly generated worlds:
// saves store their ore tiles. Docs/Erz-Reichhaltigkeit.md.
[Serializable]
public sealed class VeinRichnessShare
{
    [Range(0f, 100f), InspectorName("Medium (%)")] public float mediumPercent;
    [Range(0f, 100f), InspectorName("Rich (%)")] public float richPercent;
}

[Serializable]
public sealed class VeinRichnessSettings
{
    [InspectorName("Aktiv")] public bool enabled = true;
    [Range(1, 200), InspectorName("Mindestgrösse (Erzblöcke)")] public int minimumSize = 4;
    [InspectorName("Dünn (1–2 Blöcke breit)")] public VeinRichnessShare thin = new() { mediumPercent = 33f, richPercent = 4f };
    [InspectorName("Mittel (3–4 Blöcke breit)")] public VeinRichnessShare medium = new() { mediumPercent = 34f, richPercent = 8f };
    [InspectorName("Dick (ab 5 Blöcken breit)")] public VeinRichnessShare thick = new() { mediumPercent = 36f, richPercent = 12f };
    [Range(0f, 100f), InspectorName("Schwankung je Ader (± %)")] public float variationPercent = 25f;
    [Range(0f, 100f), InspectorName("Streuung entlang dünner Adern (%)")] public float spreadPercent = 50f;
    [Range(0f, 1f), InspectorName("Dicke Stellen bevorzugen")] public float thicknessPreference = .65f;
    [InspectorName("Faktor je Erz")] public VeinRichnessOre[] ores = Array.Empty<VeinRichnessOre>();

    public static VeinRichnessSettings Current => GpsSettings.Preferences?.veinRichness ?? new VeinRichnessSettings();

    public VeinRichnessShare Share(int maximumDepth) => maximumDepth <= 1 ? thin : maximumDepth == 2 ? medium : thick;

    // Multiplies Medium and Rich of one ore; ores without an entry use 1.
    public float Factor(BlockType ore)
    {
        if (ores != null)
            foreach (var entry in ores)
                if (entry != null && entry.ore == ore) return Mathf.Max(0f, entry.factor);
        return 1f;
    }
}

[Serializable]
public sealed class VeinRichnessOre
{
    [OreOnly] public BlockType ore;
    [Range(0f, 5f), InspectorName("Faktor (Medium und Rich ×)")] public float factor = 1f;
}

// One rule for every vein: its thickness (deepest distance to the vein boundary) picks a share of Medium and Rich,
// varied per vein and scaled per ore. Rich goes to the deepest cells and Medium around them. Veins at most two
// blocks thick have no inner cells; there both spread along the middle of the vein, preferring thicker spots and
// never the tips. Everything derives from OreVeins.Hash, so it is seed-stable and leaves UnityEngine.Random untouched.
public sealed class VeinRichness
{
    const uint VariationStream = 0x7A1D0005u, MediumStream = 0x7A1D0006u, RichStream = 0x7A1D0007u,
        CenterStream = 0x7A1D0002u, TargetStream = 0x7A1D0003u, NoiseStream = 0x7A1D0004u, RichTargetStream = 0x7A1D0008u;

    readonly VeinRichnessSettings settings;
    readonly Dictionary<int, int> local = new();
    int[] fromA = Array.Empty<int>(), fromB = Array.Empty<int>(), bfs = Array.Empty<int>();
    long[] order = Array.Empty<long>();
    int pathLength;
    readonly List<Candidate> candidates = new();
    readonly HashSet<int> chosen = new();

    struct Candidate { public int cell; public float position, thickness, noise; public bool tip; }

    public VeinRichness(VeinRichnessSettings settings) => this.settings = settings;

    // cells[0..count) is one 4-connected vein of a single ore, cells[0] its lowest index; depth is the distance to
    // the vein boundary (1 on the boundary). Writes the richness of every cell of the vein.
    public void Apply(int[] cells, int count, int maximumDepth, int[] depth, Block[] blocks, OreRichness[] richness,
        int width, int height, int seed)
    {
        for (int i = 0; i < count; i++) richness[cells[i]] = OreRichness.Small;
        var ore = blocks[cells[0]];
        if (!ore || count < Mathf.Max(1, settings.minimumSize)) return;
        int start = cells[0], sx = start % width, sy = start / width;
        var share = settings.Share(maximumDepth);
        float scale = settings.Factor(ore.id) *
            (1f + (Unit(OreVeins.Hash(seed, sx, sy, VariationStream)) * 2f - 1f) * Mathf.Clamp01(settings.variationPercent / 100f));
        int rich = Round(count * Mathf.Clamp01(share.richPercent / 100f) * scale, OreVeins.Hash(seed, sx, sy, RichStream));
        int medium = Round(count * Mathf.Clamp01(share.mediumPercent / 100f) * scale, OreVeins.Hash(seed, sx, sy, MediumStream));
        rich = Mathf.Clamp(rich, 0, count);
        medium = Mathf.Clamp(medium, 0, count - rich);
        if (rich + medium == 0) return;

        if (maximumDepth > 1)
        {
            // Deepest first; the hash only orders cells of equal depth. Keys are unique, so the order is exact.
            if (order.Length < count) order = new long[count];
            for (int i = 0; i < count; i++)
            {
                int cell = cells[i];
                long noise = OreVeins.Hash(seed, cell % width, cell / width, NoiseStream) & 0xffff;
                order[i] = ((long)(63 - Mathf.Min(63, depth[cell])) << 56) | ((65535L - noise) << 40) | (uint)cell;
            }
            Array.Sort(order, 0, count);
            for (int i = 0; i < rich + medium; i++)
                richness[(int)(order[i] & 0xffffffffL)] = i < rich ? OreRichness.Rich : OreRichness.Medium;
            return;
        }

        Prepare(cells, count, width, height, seed);
        // A Rich cell in a thin vein sits close to its middle; Medium follows along the central share.
        Place(rich, OreRichness.Rich, Mathf.Clamp01(settings.spreadPercent / 100f) * .5f, richness, width, height, seed, sx, sy, RichTargetStream);
        Place(medium, OreRichness.Medium, Mathf.Clamp01(settings.spreadPercent / 100f), richness, width, height, seed, sx, sy, TargetStream);
    }

    void Prepare(int[] cells, int count, int width, int height, int seed)
    {
        local.Clear();
        for (int i = 0; i < count; i++) local[cells[i]] = i;
        if (fromA.Length < count) { fromA = new int[count]; fromB = new int[count]; bfs = new int[count]; }
        // The longest path through the vein approximates its axis: distances to both of its ends give each
        // cell a position from 0 (one end) to 1 (other end).
        int endA = Distances(cells[0], fromA, count, width, height);
        int endB = Distances(endA, fromA, count, width, height);
        Distances(endB, fromB, count, width, height);
        candidates.Clear();
        chosen.Clear();
        for (int i = 0; i < count; i++)
        {
            int cell = cells[i], x = cell % width, y = cell / width, sides = 0, around = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || nx >= width || ny < 0 || ny >= height || !local.ContainsKey(ny * width + nx)) continue;
                    around++;
                    if (dx == 0 || dy == 0) sides++;
                }
            int span = fromA[i] + fromB[i];
            candidates.Add(new Candidate
            {
                cell = cell, position = span == 0 ? .5f : fromA[i] / (float)span,
                // A plain line has two neighbours around a cell, a two-block band five.
                thickness = Mathf.Clamp01((around - 2) / 3f),
                noise = (OreVeins.Hash(seed, x, y, NoiseStream) & 0xffff) / 65535f, tip = sides <= 1
            });
        }
        pathLength = Mathf.Max(1, fromA[local[endB]]);
    }

    // Evenly spaced targets cover the central share of the vein; the centre shifts a little per vein.
    void Place(int wanted, OreRichness level, float spread, OreRichness[] richness, int width, int height, int seed, int sx, int sy, uint stream)
    {
        wanted = Mathf.Min(wanted, candidates.Count - chosen.Count);
        if (wanted <= 0) return;
        float center = .5f + (Unit(OreVeins.Hash(seed, sx, sy, CenterStream + (uint)level)) - .5f) * (1f - spread) * .5f;
        float preference = Mathf.Clamp01(settings.thicknessPreference);
        for (int target = 0; target < wanted; target++)
        {
            float jitter = (Unit(OreVeins.Hash(seed, sx + target, sy, stream)) - .5f) * spread / wanted * .5f;
            float goal = center + ((target + .5f) / wanted - .5f) * spread + jitter;
            int best = -1;
            float bestScore = float.NegativeInfinity;
            foreach (var candidate in candidates)
            {
                if (chosen.Contains(candidate.cell)) continue;
                float score = -Mathf.Abs(candidate.position - goal) * pathLength + preference * 2f * candidate.thickness +
                    .5f * candidate.noise - (candidate.tip ? 3f : 0f);
                if (level == OreRichness.Medium && Next(candidate.cell, width, height, OreRichness.Rich, richness)) score += preference;
                if (Next(candidate.cell, width, height, level, richness)) score -= 1.5f;
                if (score < bestScore || (score == bestScore && candidate.cell > best)) continue;
                bestScore = score;
                best = candidate.cell;
            }
            chosen.Add(best);
            richness[best] = level;
        }
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

    bool Next(int cell, int width, int height, OreRichness level, OreRichness[] richness)
    {
        int x = cell % width, y = cell / width;
        return (x > 0 && Is(cell - 1)) || (x + 1 < width && Is(cell + 1)) || (y > 0 && Is(cell - width)) || (y + 1 < height && Is(cell + width));
        bool Is(int other) => chosen.Contains(other) && richness[other] == level;
    }

    // Rounds up with the probability of the fraction, so small veins get their share on average.
    static int Round(float value, uint hash) => Mathf.FloorToInt(Mathf.Max(0f, value) + Unit(hash));

    static float Unit(uint hash) => (hash >> 8) / 16777216f;
}
