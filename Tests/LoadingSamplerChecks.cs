using System;
using System.IO;
using UnityEngine;

public static class LoadingSamplerChecks
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        int comparisons = 0;
        foreach (int seed in new[] { 17, 91234, -100 })
        {
            var synchronous = new MapGenerationSampler(map.registry, seed, 1200, map.layers,
                map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
                map.useOreSettings ? map.oreSettings : null);
            var sliced = new MapGenerationSampler(map.registry, seed, 1200, map.layers,
                map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
                map.useOreSettings ? map.oreSettings : null, prepareNoise: false);
            var steps = sliced.PrepareNoiseSteps();
            while (steps.MoveNext()) { }
            var random = new System.Random(seed);
            for (int i = 0; i < 12000; i++)
            {
                int x = random.Next(-300, 300), y = random.Next(1200);
                if (synchronous.GetBlock(x, y) != sliced.GetBlock(x, y))
                    throw new Exception($"Sampling changed at seed {seed}, {x}/{y}");
                comparisons++;
            }
        }
        string result = $"PASS: {comparisons} synchronous/incremental ore-noise comparisons across three seeds and all world depths.";
        File.WriteAllText("Temp/LoadingSamplerChecks.txt", result);
        return result;
    }
}
