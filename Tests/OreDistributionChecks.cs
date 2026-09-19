using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class OreDistributionChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static AnimationCurve Constant(float percent) => AnimationCurve.Constant(0, 1, percent);
    static void Index(BlockRegistry r) => typeof(BlockRegistry).GetMethod("BuildIndex",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(r, null);

    public static object Main()
    {
        var stone = ScriptableObject.CreateInstance<Block>(); stone.id = BlockType.Stone;
        var a = ScriptableObject.CreateInstance<Block>(); a.id = BlockType.CopperOre; a.spawnWithNoise = true;
        var b = ScriptableObject.CreateInstance<Block>(); b.id = BlockType.Coal; b.spawnWithNoise = true;
        var registry = ScriptableObject.CreateInstance<BlockRegistry>();
        registry.blocks = new[] { a, stone }; Index(registry);
        var coverage = new List<object>();
        var depthBands = new List<object>();
        try
        {
            a.oreFrequencyPercent = 1;
            foreach (int size in new[] { 1, 7, 20, 50, 100 })
            {
                a.veinSizeIndex = size;
                foreach (float percent in new[] { 0f, 5f, 20f, 50f, 100f })
                {
                    var sample = new MapGenerationSampler(registry, 42319, 24000, null, Constant(percent));
                    int count = 0;
                    for (int y = 0; y < 384; y++) for (int x = 0; x < 384; x++)
                        if (sample.GetBlock(x * 37, 4 + y * 41) == a) count++;
                    float actual = count * 100f / (384 * 384);
                    Check(Mathf.Abs(actual - percent) < 2f, "Density mismatch: size " + size + ", " + actual + " vs " + percent);
                    if (percent == 0) Check(count == 0, "0% generated ore.");
                    if (percent == 100) Check(count == 384 * 384, "100% left gaps below the surface.");
                    for (int y = 0; y < 4; y++) for (int x = 0; x < 32; x++)
                        Check(sample.GetBlock(x, y) == stone, "Ore in the surface cap.");
                    if (percent == 20) coverage.Add(new { size, actual });
                }
            }

            a.veinSizeIndex = 7; b.veinSizeIndex = 20;
            a.oreFrequencyPercent = 1; b.oreFrequencyPercent = 3;
            registry.blocks = new[] { a, b, stone }; Index(registry);
            foreach (float percent in new[] { 10f, 50f, 100f })
            {
                var sample = new MapGenerationSampler(registry, 8753, 24000, null, Constant(percent));
                int ca = 0, cb = 0;
                for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
                {
                    var block = sample.GetBlock(x * 37, 4 + y * 41);
                    if (block == a) ca++; if (block == b) cb++;
                }
                float total = (ca + cb) * 100f / (512 * 512);
                float share = 100f * ca / (ca + cb);
                Check(Mathf.Abs(total - percent) < 2f, "Total density changed under competing weights: " + total);
                Check(Mathf.Abs(share - 25f) < 2f, "1:3 weight ratio failed: " + share);
                coverage.Add(new { percent, total, copperShare = share });
            }

            var curve = AnimationCurve.Linear(0, 5, 1, 50);
            var rising = new MapGenerationSampler(registry, -43219, 1024, null, curve);
            foreach (int depth in new[] { 4, 255, 511, 767, 1023 })
            {
                // Coherent veins fluctuate by row; measure depth bands across several vein widths.
                int first = Math.Max(4, depth - 64), last = Math.Min(1024, depth + 64);
                int count = 0;
                float expected = 0;
                for (int y = first; y < last; y++)
                {
                    expected += curve.Evaluate(y / 1023f);
                    for (int x = 0; x < 1024; x++) if (rising.GetBlock(x * 17, y) != stone) count++;
                }
                expected /= last - first;
                float actual = count * 100f / ((last - first) * 1024);
                Check(Mathf.Abs(actual - expected) < 2f, "Depth curve mismatch at " + depth + ": " + actual + " vs " + expected);
                depthBands.Add(new { fromDepth = first, toDepth = last - 1, expected, actual });
            }
            // A non-linear curve must be sampled rather than interpolated from its endpoints.
            var shaped = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 100), new Keyframe(1, 0));
            var shapedSample = new MapGenerationSampler(registry, 51, 1001, null, shaped);
            Check(shapedSample.GetBlock(10, 500) != stone && shapedSample.GetBlock(10, 1000) == stone, "Intermediate curve keys ignored.");
            Check(new MapGenerationSampler(registry, 1, 50, null, Constant(-20)).GetBlock(4, 10) == stone, "Negative density not clamped.");
            Check(new MapGenerationSampler(registry, 1, 50, null, Constant(150)).GetBlock(4, 10) != stone, "High density not clamped.");

            var original = new MapGenerationSampler(registry, 991, 1024, null, curve);
            registry.blocks = new[] { stone, b, a }; Index(registry);
            var reordered = new MapGenerationSampler(registry, 991, 1024, null, curve);
            a.oreFrequencyPercent = 10; b.oreFrequencyPercent = 30;
            var rescaled = new MapGenerationSampler(registry, 991, 1024, null, curve);
            var otherSeed = new MapGenerationSampler(registry, 992, 1024, null, curve);
            bool changedSeed = false;
            for (int y = 3; y < 1024; y += 7) for (int x = 0; x < 256; x += 3)
            {
                var expected = original.GetBlock(x, y);
                Check(expected == reordered.GetBlock(x, y), "Registry order changed generation.");
                Check(expected == rescaled.GetBlock(x, y), "Scaling all weights changed generation.");
                if (expected != otherSeed.GetBlock(x, y)) changedSeed = true;
            }
            Check(changedSeed, "Seed did not affect the map.");
            a.oreFrequencyPercent = 0; b.oreFrequencyPercent = float.NaN;
            Check(new MapGenerationSampler(registry, 1, 50, null, Constant(100)).GetBlock(2, 20) == stone, "Zero weights generated ore.");
            a.oreFrequencyPercent = 1; b.oreFrequencyPercent = 100; b.spawnWithNoise = false;
            Check(new MapGenerationSampler(registry, 1, 50, null, Constant(100)).GetBlock(2, 20) == a, "Disabled ore was selected.");

            registry.blocks = new[] { a, stone }; Index(registry);
            float EdgeRatio(int size)
            {
                a.veinSizeIndex = size;
                var sample = new MapGenerationSampler(registry, 1234, 1024, null, Constant(25));
                int filled = 0, edges = 0;
                for (int y = 3; y < 1024; y++) for (int x = 0; x < 1024; x++)
                    if (sample.GetBlock(x, y) == a)
                    {
                        filled++;
                        if (sample.GetBlock(x + 1, y) != a) edges++;
                        if (sample.GetBlock(x, y + 1) != a) edges++;
                    }
                return (float)edges / filled;
            }
            Check(EdgeRatio(30) < EdgeRatio(5) * .4f, "Larger size did not produce larger veins.");
            a.rarityCurve = AnimationCurve.Linear(0, .02f, 1, .08f); a.noiseScale = .125f;
            Check(a.MigrateGenerationSettings(), "Legacy migration did not run.");
            Check(Mathf.Approximately(a.OreWeight, 5) && a.veinSizeIndex == 8, "Legacy weights/size lost.");
            a.oreFrequencyPercent = 17; a.veinSizeIndex = 30;
            Check(!a.MigrateGenerationSettings() && a.OreWeight == 17 && a.veinSizeIndex == 30, "Migration overwrote edited settings.");
            return new { passed = true, coverage, depthBands,
                checks = "density extremes, 1:3 weights, curve keys, surface cap, reorder/scale invariance, seeds, vein size, migration" };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
            UnityEngine.Object.DestroyImmediate(stone);
        }
    }
}
