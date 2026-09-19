using System;
using System.Collections.Generic;
using UnityEngine;

// Shared by live maps, the overview, and ore installation.
public sealed class MapGenerationSampler
{
    public const int SurfaceStoneRows = 4;
    public const int SurfaceDirtRows = 20;
    public const int DirtTransitionRows = 8;
    public const int DirtEndDepth = SurfaceDirtRows + DirtTransitionRows;
    const int Bins = 256;
    readonly Block stone, dirt;
    readonly int surfaceSeed;
    readonly Block[] noiseBlocks;
    readonly float[][] noiseCdfs;
    readonly float[] scales, weights, densityByRow;
    readonly Vector2[] noiseOffsets;
    readonly int[] layerStarts;
    readonly Block[] layerStones;
    readonly int[][] layerOres;

    public MapGenerationSampler(BlockRegistry registry, int seed, int mapHeight, MapLayer[] layers = null,
        AnimationCurve oreDensityByDepth = null)
    {
        if (!registry) throw new ArgumentNullException(nameof(registry));
        if (mapHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mapHeight));
        surfaceSeed = seed;
        stone = registry.GetById(BlockType.Stone);
        dirt = registry.GetById(BlockType.Dirt);
        densityByRow = new float[mapHeight];
        for (int y = SurfaceStoneRows; y < mapHeight; y++)
        {
            float depth = mapHeight > 1 ? (float)y / (mapHeight - 1) : 0f;
            float percent = oreDensityByDepth == null ? Mathf.Lerp(5f, 50f, depth) : oreDensityByDepth.Evaluate(depth);
            densityByRow[y] = float.IsNaN(percent) ? 0f : Mathf.Clamp01(percent / 100f);
        }
        var blocks = new List<Block>();
        if (registry.blocks != null)
            foreach (var block in registry.blocks)
            {
                if (!block || !block.spawnWithNoise || block.IsStone || block.id == BlockType.Dirt || block.OreWeight <= 0f) continue;
                if (layers != null && !Array.Exists(layers, layer => layer != null &&
                    layer.ores != null && Array.IndexOf(layer.ores, block.id) >= 0)) continue;
                blocks.Add(block);
            }
        // Stable ordering only resolves ties and floating-point sums, never ore priority.
        blocks.Sort((a, b) => a.id.CompareTo(b.id));
        noiseBlocks = blocks.ToArray();
        noiseCdfs = new float[blocks.Count][];
        scales = new float[blocks.Count];
        weights = new float[blocks.Count];
        noiseOffsets = new Vector2[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            scales[i] = block.NoiseScale;
            weights[i] = block.OreWeight;
            // Distinct fields even when assets share the same legacy noise offset.
            noiseOffsets[i] = new Vector2(
                (OreVeins.Hash(seed, (int)block.id, block.noiseSeedOffset, 0x4821u) & 0xffff) / 32f + .317f,
                (OreVeins.Hash(seed, (int)block.id, block.noiseSeedOffset, 0x7253u) & 0xffff) / 32f + .731f);
            noiseCdfs[i] = BuildCdf(scales[i], noiseOffsets[i]);
        }
        if (layers == null || layers.Length == 0) return;
        var ordered = (MapLayer[])layers.Clone();
        if (Array.Exists(ordered, layer => layer == null))
            throw new ArgumentException("Map layers must not contain empty entries.");
        Array.Sort(ordered, (a, b) => a.startDepth.CompareTo(b.startDepth));
        layerStarts = new int[ordered.Length];
        layerStones = new Block[ordered.Length];
        layerOres = new int[ordered.Length][];
        for (int i = 0; i < ordered.Length; i++)
        {
            var layer = ordered[i];
            if ((i == 0 && layer.startDepth != 0) || (i > 0 && layer.startDepth <= ordered[i - 1].startDepth))
                throw new ArgumentException("Map layers must start at depth 0 and have distinct, increasing start depths.");
            if (!layer.stone || !layer.stone.IsStone)
                throw new ArgumentException("Missing or invalid stone for " + layer.name);
            layerStarts[i] = layer.startDepth;
            layerStones[i] = layer.stone;
            var allowed = new List<int>();
            for (int j = 0; j < noiseBlocks.Length; j++)
                if (layer.ores != null && Array.IndexOf(layer.ores, noiseBlocks[j].id) >= 0) allowed.Add(j);
            layerOres[i] = allowed.ToArray();
        }
    }

    int LayerIndex(int depth)
    {
        if (layerStarts == null) return -1;
        int index = Array.BinarySearch(layerStarts, Math.Max(0, depth));
        return index >= 0 ? index : ~index - 1;
    }

    public Block GetStone(int depth)
    {
        int layer = LayerIndex(depth);
        return layer < 0 ? stone : layerStones[layer];
    }

    public bool IsDirtAt(int x, int depth)
    {
        if (!dirt || depth >= DirtEndDepth) return false;
        if (depth < SurfaceDirtRows) return true;
        float t = (depth - SurfaceDirtRows + .5f) / DirtTransitionRows;
        float offset = (OreVeins.Hash(surfaceSeed, 0, 0, 0xD171u) & 0xffff) / 64f;
        float clusters = Mathf.PerlinNoise(x * .18f + offset, depth * .24f + offset);
        float chance = Mathf.Clamp01(1f - t + (clusters - .5f) * .8f * Mathf.Sin(t * Mathf.PI));
        float sample = (OreVeins.Hash(surfaceSeed, x, depth, 0xD172u) & 0xffffff) / 16777216f;
        return sample < chance;
    }

    public Block GetBaseBlock(int x, int depth) => IsDirtAt(x, depth) ? dirt : GetStone(depth);

    public Block GetBlock(int x, int y)
    {
        // The cap applies only at the surface, not at every layer boundary.
        if (y < SurfaceStoneRows) return GetBaseBlock(x, y);
        int layer = LayerIndex(y);
        var baseStone = GetBaseBlock(x, y);
        float density = densityByRow[Math.Min(y, densityByRow.Length - 1)];
        if (density <= 0f) return baseStone;
        int[] allowed = layer < 0 ? null : layerOres[layer];
        int count = allowed == null ? noiseBlocks.Length : allowed.Length;
        double totalWeight = 0, bestScore = double.PositiveInfinity;
        Block chosen = null;
        for (int candidate = 0; candidate < count; candidate++)
        {
            int i = allowed == null ? candidate : allowed[candidate];
            float uniform = UniformNoise(SampleNoise(x, y, scales[i], noiseOffsets[i]), noiseCdfs[i]);
            double score = -Math.Log(uniform) / weights[i];
            totalWeight += weights[i];
            if (score < bestScore) { bestScore = score; chosen = noiseBlocks[i]; }
        }
        if (!chosen) return baseStone;
        // Exponential race: winner probability is weight/sum, independent of the
        // minimum score. Its CDF supplies the total ore gate while retaining veins.
        return density >= 1f || bestScore * totalWeight < -Math.Log(1d - density) ? chosen : baseStone;
    }

    static float[] BuildCdf(float scale, Vector2 offset)
    {
        var histogram = new int[Bins];
        var cdf = new float[Bins + 1];
        for (int x = 0; x < Bins; x++)
            for (int y = 0; y < Bins; y++)
            {
                // Sample many periods even for large veins; retain lattice phase for index 1.
                float noise = SampleNoise(x * 17, y * 23, scale, offset);
                int bin = Mathf.Clamp(Mathf.FloorToInt(noise * Bins), 0, Bins - 1);
                histogram[bin]++;
            }
        int sum = 0;
        for (int i = 0; i < Bins; i++)
        {
            sum += histogram[i];
            cdf[i + 1] = (float)sum / (Bins * Bins);
        }
        return cdf;
    }

    static float UniformNoise(float noise, float[] cdf)
    {
        float position = Mathf.Clamp01(noise) * Bins;
        int bin = Mathf.Min((int)position, Bins - 1);
        return Mathf.Clamp(Mathf.Lerp(cdf[bin], cdf[bin + 1], position - bin), .000001f, .999999f);
    }

    static float SampleNoise(int x, int y, float scale, Vector2 offset) =>
        Mathf.PerlinNoise(x * scale + offset.x, y * scale + offset.y);
}
