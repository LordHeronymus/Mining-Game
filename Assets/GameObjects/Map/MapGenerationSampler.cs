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
    readonly float[] configuredWeightsByRow;
    readonly int[] configuredVeinSizeByRow;
    readonly Dictionary<int, float[]>[] dynamicNoiseCdfs;
    readonly Vector2[] noiseOffsets;
    readonly int[] layerStarts;
    readonly int[] layerTransitions;
    readonly Block[] layerStones;
    readonly int[][] layerOres;
    readonly int firstStoneBoundary;
    readonly int transitionThickness;
    readonly bool explicitSurfaceLayer;

    public MapGenerationSampler(BlockRegistry registry, int seed, int mapHeight, MapLayer[] layers = null,
        AnimationCurve oreDensityCurve = null, float oreDensityMultiplierPercent = 50f,
        int transitionThickness = DefaultTransitionThickness, OreDistributionSetting[] oreSettings = null)
    {
        if (!registry) throw new ArgumentNullException(nameof(registry));
        if (mapHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mapHeight));
        surfaceSeed = seed;
        this.transitionThickness = Mathf.Clamp(transitionThickness, 1, 100);
        stone = registry.GetById(BlockType.Stone);
        dirt = registry.GetById(BlockType.Dirt);
        densityByRow = new float[mapHeight];
        for (int y = 0; y < mapHeight; y++)
        {
            float depth = (float)y / Mathf.Max(1, mapHeight - 1);
            float shape = oreDensityCurve == null ? 1f : oreDensityCurve.Evaluate(depth);
            densityByRow[y] = float.IsNaN(shape) || float.IsInfinity(shape) ||
                float.IsNaN(oreDensityMultiplierPercent) || float.IsInfinity(oreDensityMultiplierPercent)
                ? 0f : Mathf.Clamp01(shape * Mathf.Clamp(oreDensityMultiplierPercent, 0f, 100f) / 100f);
        }
        var blocks = new List<Block>();
        if (registry.blocks != null)
            foreach (var block in registry.blocks)
            {
                if (!block || block.IsStone || block.id == BlockType.Dirt) continue;
                bool available = oreSettings == null ? block.spawnWithNoise && block.OreWeight > 0f &&
                    (layers == null || Array.Exists(layers, layer => layer != null &&
                        layer.ores != null && Array.IndexOf(layer.ores, block.id) >= 0)) :
                    Array.Exists(oreSettings, entry => entry != null && entry.ore == block.id &&
                        entry.baseWeight > 0f && entry.layerIndices != null && entry.layerIndices.Length > 0);
                if (!available) continue;
                blocks.Add(block);
            }
        // Stable ordering only resolves ties and floating-point sums, never ore priority.
        blocks.Sort((a, b) => a.id.CompareTo(b.id));
        noiseBlocks = blocks.ToArray();
        noiseCdfs = new float[blocks.Count][];
        dynamicNoiseCdfs = new Dictionary<int, float[]>[blocks.Count];
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
            dynamicNoiseCdfs[i] = new Dictionary<int, float[]>();
        }
        if (layers == null || layers.Length == 0) return;
        var ordered = (MapLayer[])layers.Clone();
        if (Array.Exists(ordered, layer => layer == null))
            throw new ArgumentException("Map layers must not contain empty entries.");
        Array.Sort(ordered, (a, b) => a.startDepth.CompareTo(b.startDepth));
        layerStarts = new int[ordered.Length];
        layerTransitions = new int[ordered.Length];
        layerStones = new Block[ordered.Length];
        layerOres = new int[ordered.Length][];
        for (int i = 0; i < ordered.Length; i++)
        {
            var layer = ordered[i];
            if ((i == 0 && layer.startDepth != 0) || (i > 0 && layer.startDepth <= ordered[i - 1].startDepth))
                throw new ArgumentException("Map layers must start at depth 0 and have distinct, increasing start depths.");
            if (!layer.stone || (!layer.stone.IsStone && !(i == 0 && layer.stone.id == BlockType.Dirt)))
                throw new ArgumentException("Missing or invalid stone for " + layer.name);
            layerStarts[i] = layer.startDepth;
            layerTransitions[i] = Mathf.Max(0, layer.transitionWidth);
            layerStones[i] = layer.stone;
            var allowed = new List<int>();
            for (int j = 0; j < noiseBlocks.Length; j++)
                if (oreSettings == null ? layer.ores != null &&
                    Array.IndexOf(layer.ores, noiseBlocks[j].id) >= 0 :
                    Array.Exists(oreSettings, entry => entry != null && entry.ore == noiseBlocks[j].id &&
                        entry.layerIndices != null && Array.IndexOf(entry.layerIndices,
                            Array.IndexOf(layers, layer)) >= 0)) allowed.Add(j);
            layerOres[i] = allowed.ToArray();
        }
        if (oreSettings != null)
        {
            configuredWeightsByRow = new float[mapHeight * noiseBlocks.Length];
            configuredVeinSizeByRow = new int[configuredWeightsByRow.Length];
            for (int ore = 0; ore < noiseBlocks.Length; ore++)
            {
                var entry = Array.Find(oreSettings, candidate => candidate != null &&
                    candidate.ore == noiseBlocks[ore].id);
                if (entry == null) continue;
                int first = -1, last = -1;
                for (int layer = 0; layer < layerStarts.Length; layer++)
                    if (Array.IndexOf(layerOres[layer], ore) >= 0)
                    {
                        if (first < 0) first = layer;
                        last = layer;
                    }
                if (first < 0) continue;
                int start = layerStarts[first];
                int end = last + 1 < layerStarts.Length ?
                    Mathf.Min(layerStarts[last + 1], mapHeight) : mapHeight;
                for (int y = start; y < end; y++)
                {
                    int layerIndex = LayerIndex(y);
                    if (Array.IndexOf(layerOres[layerIndex], ore) < 0) continue;
                    float progress = end - start <= 1 ? 0f :
                        Mathf.Clamp01((float)(y - start) / (end - start - 1));
                    int index = y * noiseBlocks.Length + ore;
                    configuredWeightsByRow[index] = Nonnegative(entry.baseWeight *
                        CurveFactor(entry.weightCurve, progress));
                    float size = entry.baseVeinSize * CurveFactor(entry.veinSizeCurve, progress);
                    configuredVeinSizeByRow[index] = Mathf.Clamp(Mathf.RoundToInt(Nonnegative(size)), 1, 1000);
                }
            }
        }
        explicitSurfaceLayer = layerStones[0] && layerStones[0].id == BlockType.Dirt;
        firstStoneBoundary = ordered.Length > 1 ? ordered[1].startDepth : -1;
    }

    static float Nonnegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ?
        0f : Mathf.Max(0f, value);

    static float CurveFactor(AnimationCurve curve, float progress) =>
        curve == null || curve.length == 0 ? 1f : Nonnegative(curve.Evaluate(progress));

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
    public int DirtEndDepth => explicitSurfaceLayer && layerStarts.Length > 1
        ? layerStarts[1] : SurfaceDirtRows + transitionThickness;

    public bool IsDirtAt(int x, int depth)
    {
        if (explicitSurfaceLayer)
        {
            if (layerStarts.Length < 2) return true;
            if (depth >= layerStarts[1]) return false;
            int width = Mathf.Min(layerTransitions[1], layerStarts[1]);
            int start = layerStarts[1] - width;
            return depth < start || (width > 0 && ChoosePreviousLayer(x, depth, start, width, 1));
        }
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
        if (explicitSurfaceLayer)
        {
            int active = LayerIndex(depth);
            int next = active + 1;
            if (next < layerStarts.Length)
            {
                int width = Mathf.Min(layerTransitions[next], layerStarts[next] - layerStarts[active]);
                int start = layerStarts[next] - width;
                if (width > 0 && depth >= start && depth < layerStarts[next] &&
                    !ChoosePreviousLayer(x, depth, start, width, next)) return layerStones[next];
            }
            return layerStones[active];
        }
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

    bool ChoosePreviousLayer(int x, int depth, int start, int width, int next)
    {
        float t = (depth - start + .5f) / width;
        float offset = (OreVeins.Hash(surfaceSeed, next, 0, 0x5171u) & 0xffff) / 64f;
        float clusters = Mathf.PerlinNoise(x * .18f + offset, depth * .24f + offset);
        float chance = Mathf.Clamp01(1f - t + (clusters - .5f) * .8f * Mathf.Sin(t * Mathf.PI));
        float sample = (OreVeins.Hash(surfaceSeed, x, depth, 0x5172u + (uint)next) & 0xffffff) / 16777216f;
        return sample < chance;
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
        Block chosen = null;
        for (int candidate = 0; candidate < count; candidate++)
        {
            int i = allowed == null ? candidate : allowed[candidate];
            int rowIndex = Math.Min(y, densityByRow.Length - 1) * noiseBlocks.Length + i;
            float weight = configuredWeightsByRow == null ? weights[i] : configuredWeightsByRow[rowIndex];
            if (weight <= 0f) continue;
            int veinIndex = configuredVeinSizeByRow == null ? Mathf.RoundToInt(1f / scales[i]) :
                configuredVeinSizeByRow[rowIndex];
            float scale = 1f / Mathf.Max(1, veinIndex);
            float uniform;
            if (veinIndex == Mathf.RoundToInt(1f / scales[i]))
                uniform = UniformNoise(SampleNoise(x, y, scales[i], noiseOffsets[i]), noiseCdfs[i]);
            else
                uniform = UniformNoise(SampleNoise(x, y, scale, noiseOffsets[i]), DynamicCdf(i, veinIndex));
            double score = -Math.Log(uniform) / weight;
            totalWeight += weight;
            if (score < bestScore) { bestScore = score; chosen = noiseBlocks[i]; }
        }
        if (!chosen) return baseStone;
        // Exponential race: winner probability is weight/sum, independent of the
        // minimum score. Its CDF supplies the total ore gate while retaining veins.
        return density >= 1f || bestScore * totalWeight < -Math.Log(1d - density) ? chosen : baseStone;
    }

    float[] DynamicCdf(int ore, int veinIndex)
    {
        if (dynamicNoiseCdfs[ore].TryGetValue(veinIndex, out var cdf)) return cdf;
        float scale = 1f / veinIndex;
        cdf = BuildCdf(scale, noiseOffsets[ore], 96);
        dynamicNoiseCdfs[ore].Add(veinIndex, cdf);
        return cdf;
    }

    static float[] BuildCdf(float scale, Vector2 offset, int samples = Bins)
    {
        var histogram = new int[Bins];
        var cdf = new float[Bins + 1];
        for (int x = 0; x < samples; x++)
            for (int y = 0; y < samples; y++)
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
            cdf[i + 1] = (float)sum / (samples * samples);
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
