using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class OrePerTypeSettingsChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Index(BlockRegistry registry) => typeof(BlockRegistry)
        .GetMethod("BuildIndex", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(registry, null);

    public static object Main()
    {
        var sceneMap = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(sceneMap && sceneMap.useOreSettings && sceneMap.oreSettings != null &&
            sceneMap.oreSettings.Length == 6, "The active map has not been migrated.");
        Check(sceneMap.oreSettings.Select(entry => entry.ore).Distinct().Count() == 6,
            "An ore has more than one configuration.");
        foreach (var entry in sceneMap.oreSettings)
            Check(entry.weightCurve != null && entry.veinSizeCurve != null &&
                Mathf.Approximately(entry.weightCurve.Evaluate(.5f), 1f) &&
                Mathf.Approximately(entry.veinSizeCurve.Evaluate(.5f), 1f),
                "Migration changed an ore curve.");
        var oldSampler = new MapGenerationSampler(sceneMap.registry, sceneMap.ActiveSeed,
            sceneMap.GeneratedHeight, sceneMap.layers, sceneMap.oreDensityCurve,
            sceneMap.oreDensityMultiplierPercent, sceneMap.transitionThickness,
            null);
        var migratedSampler = new MapGenerationSampler(sceneMap.registry, sceneMap.ActiveSeed,
            sceneMap.GeneratedHeight, sceneMap.layers, sceneMap.oreDensityCurve,
            sceneMap.oreDensityMultiplierPercent, sceneMap.transitionThickness,
            sceneMap.oreSettings);
        for (int y = 1; y < sceneMap.GeneratedHeight; y += 13)
            for (int x = 0; x < 80; x += 3)
                Check(oldSampler.GetBlock(x, y) == migratedSampler.GetBlock(x, y),
                    "Migration changed the generated map at " + x + ", " + y);

        var stone = ScriptableObject.CreateInstance<Block>(); stone.id = BlockType.Stone;
        var iron = ScriptableObject.CreateInstance<Block>(); iron.id = BlockType.IronOre;
        var copper = ScriptableObject.CreateInstance<Block>(); copper.id = BlockType.CopperOre;
        var registry = ScriptableObject.CreateInstance<BlockRegistry>();
        try
        {
            registry.blocks = new[] { stone, iron, copper }; Index(registry);
            var layers = new[]
            {
                new MapLayer { name = "One", startDepth = 0, stone = stone },
                new MapLayer { name = "Two", startDepth = 40, stone = stone },
                new MapLayer { name = "Three", startDepth = 80, stone = stone },
                new MapLayer { name = "Four", startDepth = 120, stone = stone }
            };
            var ironSetting = new OreDistributionSetting
            {
                ore = iron.id, layerIndices = new[] { 0, 1, 2 }, baseWeight = 1f,
                weightCurve = AnimationCurve.Linear(0f, 1f, 1f, 3f),
                baseVeinSize = 8f, veinSizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f)
            };
            var copperSetting = new OreDistributionSetting
            {
                ore = copper.id, layerIndices = new[] { 0, 1, 2, 3 }, baseWeight = 1f,
                weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                baseVeinSize = 8f, veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
            };
            var sampler = new MapGenerationSampler(registry, 76543, 160, layers,
                AnimationCurve.Constant(0f, 1f, 1f), 50f,
                oreSettings: new[] { ironSetting, copperSetting });
            float Share(int first, int last, out float total)
            {
                int ironCount = 0, oreCount = 0;
                for (int y = first; y < last; y++)
                    for (int x = 0; x < 1600; x++)
                    {
                        var block = sampler.GetBlock(x * 17, y);
                        if (block == iron) ironCount++;
                        if (block == iron || block == copper) oreCount++;
                    }
                total = (float)oreCount / ((last - first) * 1600);
                return (float)ironCount / oreCount;
            }
            float top = Share(4, 16, out float topDensity);
            float middle = Share(44, 56, out float middleDensity);
            float bottom = Share(104, 116, out float bottomDensity);
            Check(top < middle && middle < bottom && bottom > top + .1f,
                "The weight curve restarted at a layer boundary.");
            Check(Mathf.Abs(topDensity - .5f) < .07f &&
                Mathf.Abs(middleDensity - .5f) < .07f &&
                Mathf.Abs(bottomDensity - .5f) < .07f,
                "Relative weights changed the global ore density.");
            Check(sampler.GetBlock(3, 130) != iron, "Iron appeared in a disabled layer.");
            ironSetting.layerIndices = new[] { 0, 2 };
            var gapSampler = new MapGenerationSampler(registry, 76543, 160, layers,
                AnimationCurve.Constant(0f, 1f, 1f), 100f,
                oreSettings: new[] { ironSetting, copperSetting });
            for (int x = 0; x < 80; x++)
                Check(gapSampler.GetBlock(x, 60) != iron, "Iron appeared in an unselected layer.");
            ironSetting.layerIndices = new[] { 0, 1, 2 };
            var sizeSampler = new MapGenerationSampler(registry, 76543, 160, layers,
                AnimationCurve.Constant(0f, 1f, 1f), 25f,
                oreSettings: new[] { ironSetting });
            float Edges(int first, int last)
            {
                int filled = 0, edges = 0;
                for (int y = first; y < last; y++)
                    for (int x = 0; x < 800; x++)
                        if (sizeSampler.GetBlock(x, y) == iron)
                        {
                            filled++;
                            if (sizeSampler.GetBlock(x + 1, y) != iron) edges++;
                            if (sizeSampler.GetBlock(x, y + 1) != iron) edges++;
                        }
                return (float)edges / filled;
            }
            float topEdges = Edges(2, 22);
            float bottomEdges = Edges(98, 118);
            Check(bottomEdges < topEdges * .8f,
                "The vein-size curve did not enlarge veins across layers.");
            return new { passed = true, ironShares = new[] { top, middle, bottom },
                densities = new[] { topDensity, middleDensity, bottomDensity },
                veinEdgeRatios = new[] { topEdges, bottomEdges } };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(stone);
            UnityEngine.Object.DestroyImmediate(iron);
            UnityEngine.Object.DestroyImmediate(copper);
        }
    }
}
