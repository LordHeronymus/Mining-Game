using System;
using System.Collections.Generic;
using UnityEngine;

// Shared by live maps, the overview, and ore installation.
public sealed class MapGenerationSampler
{

    public const int SurfaceDirtRows = 15;
    public const int DefaultTransitionThickness = 15;
    const int Bins = 256;
    readonly Block stone, dirt;
    readonly int surfaceSeed;
    readonly Block[] noiseBlocks;
    readonly float[][] noiseCdfs;
    readonly float[] scales, weights, densityByRow;
    readonly float[] transitionWeightsByRow;
    readonly float[] veinSizeByRow;
    readonly float[][][] sizeNoiseCdfs;
    readonly Vector2[] noiseOffsets;
    readonly int[] layerStarts;
    readonly Block[] layerStones;
    readonly int[][] layerOres;
    readonly int firstStoneBoundary;
    readonly int transitionThickness;

    public MapGenerationSampler(BlockRegistry registry, int seed, int mapHeight, MapLayer[] layers = null,
        AnimationCurve oreDensityCurve = null, float oreDensityMultiplierPercent = 50f,
        int transitionThickness = DefaultTransitionThickness, AnimationCurve oreTransitionCurve = null,
        int oreTransitionDepth = 100, AnimationCurve oreVeinSizeCurve = null,
        int surfaceOreRampDepth = 10, AnimationCurve surfaceOreRampCurve = null,
        float surfaceOreVeinSizePercent = 50f)
    {
        if (!registry) throw new ArgumentNullException(nameof(registry));
        if (mapHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mapHeight));
        surfaceSeed = seed;
        this.transitionThickness = Mathf.Clamp(transitionThickness, 1, 100);
        stone = registry.GetById(BlockType.Stone);
        dirt = registry.GetById(BlockType.Dirt);
        densityByRow = new float[mapHeight];
        int surfaceRampDepth = Mathf.Max(0, surfaceOreRampDepth);
        float initialShape = oreDensityCurve == null ? .1f : oreDensityCurve.Evaluate(0f);
        for (int y = 0; y < mapHeight; y++)
        {
            float depth = Mathf.Clamp01((float)(y - surfaceRampDepth) / Mathf.Max(1, mapHeight - surfaceRampDepth - 1));
            float shape = y < surfaceRampDepth ? initialShape * RampFactor(surfaceOreRampCurve, (float)y / surfaceRampDepth) :
                oreDensityCurve == null ? Mathf.Lerp(.1f, 1f, depth) : oreDensityCurve.Evaluate(depth);
            densityByRow[y] = float.IsNaN(shape) || float.IsNaN(oreDensityMultiplierPercent)
                ? 0f : Mathf.Clamp01(shape) * Mathf.Clamp(oreDensityMultiplierPercent, 0f, 100f) / 100f;
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
        bool hasTransitionSize = oreVeinSizeCurve != null && oreVeinSizeCurve.length > 0;
        float surfaceVeinSize = Mathf.Clamp(surfaceOreVeinSizePercent, 1f, 100f) / 100f;
        if ((surfaceRampDepth > 0 && surfaceVeinSize < 1f) || hasTransitionSize)
        {
            veinSizeByRow = new float[mapHeight * noiseBlocks.Length];
            for (int i = 0; i < veinSizeByRow.Length; i++) veinSizeByRow[i] = 1f;
            for (int y = 0; y < Mathf.Min(surfaceRampDepth, mapHeight); y++)
                for (int ore = 0; ore < noiseBlocks.Length; ore++)
                    veinSizeByRow[y * noiseBlocks.Length + ore] = surfaceVeinSize;
            sizeNoiseCdfs = new float[noiseBlocks.Length][][];
            for (int i = 0; i < noiseBlocks.Length; i++)
            {
                var cdfs = new float[5][];
                for (int step = 0; step < 4; step++)
                    cdfs[step] = BuildCdf(scales[i], noiseOffsets[i], step / 4f);
                cdfs[4] = noiseCdfs[i];
                sizeNoiseCdfs[i] = cdfs;
            }
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
        firstStoneBoundary = ordered.Length > 1 ? ordered[1].startDepth : -1;
        if (oreTransitionCurve == null || oreTransitionCurve.length == 0 || oreTransitionDepth <= 0) return;
        transitionWeightsByRow = new float[mapHeight * noiseBlocks.Length];
        for (int ore = 0; ore < noiseBlocks.Length; ore++)
        {
            int runStart = -1;
            for (int layer = 0; layer <= layerStarts.Length; layer++)
            {
                bool allowed = layer < layerStarts.Length && layerStarts[layer] < mapHeight &&
                    Array.IndexOf(layerOres[layer], ore) >= 0;
                if (allowed && runStart < 0) runStart = layerStarts[layer];
                if (allowed || runStart < 0) continue;
                int runEnd = layer == layerStarts.Length ? mapHeight : Mathf.Min(layerStarts[layer], mapHeight);
                FillTransitionWeights(ore, runStart, runEnd, runStart > 0, runEnd < mapHeight,
                    oreTransitionCurve, oreTransitionDepth, oreVeinSizeCurve);
                runStart = -1;
            }
        }
    }

    static float RampFactor(AnimationCurve curve, float progress)
    {
        if (progress <= 0f) return 0f;
        if (progress >= 1f) return 1f;
        if (curve == null || curve.length == 0) return progress;
        float start = curve.Evaluate(0f), end = curve.Evaluate(1f);
        if (float.IsNaN(start) || float.IsNaN(end) || float.IsInfinity(start) ||
            float.IsInfinity(end) || Mathf.Approximately(start, end)) return progress;
        float value = (curve.Evaluate(progress) - start) / (end - start);
        return float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
    }

    void FillTransitionWeights(int ore, int start, int end, bool fadeIn, bool fadeOut,
        AnimationCurve curve, int requestedDepth, AnimationCurve veinSizeCurve)
    {
        int length = end - start;
        if (length <= 0) return;
        float width = Mathf.Min(requestedDepth, fadeIn && fadeOut ? (length - 1) * .5f : length - 1);
        for (int y = start; y < end; y++)
        {
            float factor = 1f;
            if (fadeIn) factor = width > 0f ? TransitionValue(curve, (y - start) / width) : 0f;
            if (fadeOut) factor = Mathf.Min(factor,
                width > 0f ? TransitionValue(curve, (end - 1 - y) / width) : 0f);
            int index = y * noiseBlocks.Length + ore;
            transitionWeightsByRow[index] = weights[ore] * factor;
            if (veinSizeByRow == null || veinSizeCurve == null || veinSizeCurve.length == 0) continue;
            float size = veinSizeCurve.Evaluate(factor);
            veinSizeByRow[index] = Mathf.Min(veinSizeByRow[index],
                float.IsNaN(size) ? 0f : Mathf.Clamp01(size));
        }
    }

    static float TransitionValue(AnimationCurve curve, float progress)
    {
        float value = curve.Evaluate(Mathf.Clamp01(progress));
        return float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
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

    public int TransitionThickness => transitionThickness;
    public int DirtEndDepth => SurfaceDirtRows + transitionThickness;

    public bool IsDirtAt(int x, int depth)
    {
        if (!dirt || depth >= DirtEndDepth) return false;
        if (depth < SurfaceDirtRows) return true;
        float t = (depth - SurfaceDirtRows + .5f) / transitionThickness;
        float offset = (OreVeins.Hash(surfaceSeed, 0, 0, 0xD171u) & 0xffff) / 64f;
        float clusters = Mathf.PerlinNoise(x * .18f + offset, depth * .24f + offset);
        float chance = Mathf.Clamp01(1f - t + (clusters - .5f) * .8f * Mathf.Sin(t * Mathf.PI));
        float sample = (OreVeins.Hash(surfaceSeed, x, depth, 0xD172u) & 0xffffff) / 16777216f;
        return sample < chance;
    }

    public int FirstStoneBoundary => firstStoneBoundary;

    public bool IsFirstLayerStoneAt(int x, int depth)
    {
        if (layerStarts == null || layerStarts.Length < 2 || firstStoneBoundary <= 0 ||
            depth < firstStoneBoundary || depth >= firstStoneBoundary + transitionThickness || LayerIndex(depth) != 1)
            return false;
        float t = (depth - firstStoneBoundary + .5f) / transitionThickness;
        float offset = (OreVeins.Hash(surfaceSeed, 0, 0, 0x5171u) & 0xffff) / 64f;
        float clusters = Mathf.PerlinNoise(x * .18f + offset, depth * .24f + offset);
        float chance = Mathf.Clamp01(1f - t + (clusters - .5f) * .8f * Mathf.Sin(t * Mathf.PI));
        float sample = (OreVeins.Hash(surfaceSeed, x, depth, 0x5172u) & 0xffffff) / 16777216f;
        return sample < chance;
    }

    public Block GetBaseBlock(int x, int depth)
    {
        if (IsDirtAt(x, depth)) return dirt;
        int layer = LayerIndex(depth);
        if (layer >= 2 && depth < layerStarts[layer] + transitionThickness)
        {
            float t = (depth - layerStarts[layer] + .5f) / transitionThickness;
            float offset = (OreVeins.Hash(surfaceSeed, layer, 0, 0x5171u) & 0xffff) / 64f;
            float clusters = Mathf.PerlinNoise(x * .18f + offset, depth * .24f + offset);
            float chance = Mathf.Clamp01(1f - t + (clusters - .5f) * .8f * Mathf.Sin(t * Mathf.PI));
            float sample = (OreVeins.Hash(surfaceSeed, x, depth, 0x5172u + (uint)layer) & 0xffffff) / 16777216f;
            if (sample < chance) return layerStones[layer - 1];
        }
        return IsFirstLayerStoneAt(x, depth) ? layerStones[0] : GetStone(depth);
    }

    public Block GetBlock(int x, int y)
    {


        int layer = LayerIndex(y);
        var baseStone = GetBaseBlock(x, y);
        float density = densityByRow[Math.Min(y, densityByRow.Length - 1)];
        if (density <= 0f) return baseStone;
        int[] allowed = layer < 0 ? null : layerOres[layer];
        int count = allowed == null ? noiseBlocks.Length : allowed.Length;
        double totalWeight = 0, bestScore = double.PositiveInfinity;
        float strongestTransition = 0f;
        Block chosen = null;
        for (int candidate = 0; candidate < count; candidate++)
        {
            int i = allowed == null ? candidate : allowed[candidate];
            float weight = transitionWeightsByRow == null ? weights[i] :
                transitionWeightsByRow[Math.Min(y, densityByRow.Length - 1) * noiseBlocks.Length + i];
            if (weight <= 0f) continue;
            int rowIndex = Math.Min(y, densityByRow.Length - 1) * noiseBlocks.Length + i;
            float uniform;
            if (veinSizeByRow == null)
                uniform = UniformNoise(SampleNoise(x, y, scales[i], noiseOffsets[i]), noiseCdfs[i]);
            else
            {
                float size = veinSizeByRow[rowIndex];
                float noise = BlendNoise(x, y, scales[i], noiseOffsets[i], size);
                float step = size * 4f;
                int lower = Mathf.Min((int)step, 3);
                var cdfs = sizeNoiseCdfs[i];
                uniform = Mathf.Lerp(UniformNoise(noise, cdfs[lower]),
                    UniformNoise(noise, cdfs[lower + 1]), step - lower);
            }
            double score = -Math.Log(uniform) / weight;
            totalWeight += weight;
            strongestTransition = Mathf.Max(strongestTransition, weight / weights[i]);
            if (score < bestScore) { bestScore = score; chosen = noiseBlocks[i]; }
        }
        if (!chosen) return baseStone;
        if (transitionWeightsByRow != null) density *= strongestTransition;
        // Exponential race: winner probability is weight/sum, independent of the
        // minimum score. Its CDF supplies the total ore gate while retaining veins.
        return density >= 1f || bestScore * totalWeight < -Math.Log(1d - density) ? chosen : baseStone;
    }

    static float[] BuildCdf(float scale, Vector2 offset, float size = 1f)
    {
        var histogram = new int[Bins];
        var cdf = new float[Bins + 1];
        for (int x = 0; x < Bins; x++)
            for (int y = 0; y < Bins; y++)
            {
                // Sample many periods even for large veins; retain lattice phase for index 1.
                float noise = BlendNoise(x * 17, y * 23, scale, offset, size);
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

    static float BlendNoise(int x, int y, float scale, Vector2 offset, float size)
    {
        float coarse = SampleNoise(x, y, scale, offset);
        return size >= 1f ? coarse : Mathf.Lerp(
            SampleNoise(x, y, Mathf.Min(1f, scale * 2f), offset), coarse, size);
    }

    static float SampleNoise(int x, int y, float scale, Vector2 offset) =>
        Mathf.PerlinNoise(x * scale + offset.x, y * scale + offset.y);
}
