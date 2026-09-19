using System;
using System.Collections.Generic;
using UnityEngine;

// Shared by the live map and the editor overview so both use the same ore rules.
public sealed class MapGenerationSampler
{
    const int Bins = 256;
    readonly Block stone;
    readonly Block[] noiseBlocks;
    readonly float[][] thresholds;
    readonly int seed;

    public MapGenerationSampler(BlockRegistry registry, int seed, int mapHeight)
    {
        if (!registry) throw new ArgumentNullException(nameof(registry));
        this.seed = seed;
        stone = registry.GetById(BlockType.Stone);

        var blocks = new List<Block>();
        var rowThresholds = new List<float[]>();
        if (registry.blocks != null)
        {
            foreach (var block in registry.blocks)
            {
                if (!block || !block.spawnWithNoise) continue;
                blocks.Add(block);
                float[] cdf = BuildCdf(block, seed);
                var perRow = new float[Mathf.Max(0, mapHeight)];
                for (int y = 0; y < perRow.Length; y++)
                {
                    float depth = (float)y / mapHeight;
                    float rarity = block.rarityCurve != null ? block.rarityCurve.Evaluate(depth) : 0f;
                    float q = 1f - Mathf.Clamp01(rarity);
                    int bin = Array.FindIndex(cdf, value => value >= q);
                    if (bin < 0) bin = Bins - 1;
                    perRow[y] = (bin + 0.5f) / Bins;
                }
                rowThresholds.Add(perRow);
            }
        }
        noiseBlocks = blocks.ToArray();
        thresholds = rowThresholds.ToArray();
    }

    public Block GetBlock(int x, int y)
    {
        for (int i = 0; i < noiseBlocks.Length; i++)
        {
            Block block = noiseBlocks[i];
            float nx = (x + seed + block.noiseSeedOffset) * block.noiseScale;
            float ny = (y + seed + block.noiseSeedOffset) * block.noiseScale;
            if (Mathf.PerlinNoise(nx, ny) > thresholds[i][y])
                return block;
        }
        return stone;
    }

    static float[] BuildCdf(Block block, int seed)
    {
        var histogram = new int[Bins];
        var cdf = new float[Bins];
        for (int x = 0; x < Bins; x++)
            for (int y = 0; y < Bins; y++)
            {
                float nx = (x + seed + block.noiseSeedOffset) * block.noiseScale;
                float ny = (y + seed + block.noiseSeedOffset) * block.noiseScale;
                float noise = Mathf.PerlinNoise(nx, ny);
                int bin = Mathf.Clamp(Mathf.FloorToInt(noise * Bins), 0, Bins - 1);
                histogram[bin]++;
            }

        int sum = 0;
        for (int i = 0; i < Bins; i++)
        {
            sum += histogram[i];
            cdf[i] = (float)sum / (Bins * Bins);
        }
        return cdf;
    }
}
