using System;
using System.Reflection;
using UnityEngine;

public static class OreVeinTransitionChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static object Main()
    {
        var stone = ScriptableObject.CreateInstance<Block>(); stone.id = BlockType.Stone;
        var iron = ScriptableObject.CreateInstance<Block>(); iron.id = BlockType.IronOre;
        var gold = ScriptableObject.CreateInstance<Block>(); gold.id = BlockType.GoldOre;
        var registry = ScriptableObject.CreateInstance<BlockRegistry>();
        try
        {
            foreach (var ore in new[] { iron, gold })
            {
                ore.spawnWithNoise = true;
                ore.oreFrequencyPercent = 10f;
                ore.veinSizeIndex = 30;
            }
            registry.blocks = new[] { stone, iron, gold };
            typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(registry, null);
            var layers = new[]
            {
                new MapLayer { name = "1", startDepth = 0, stone = stone,
                    ores = new[] { BlockType.IronOre } },
                new MapLayer { name = "2", startDepth = 300, stone = stone,
                    ores = new[] { BlockType.IronOre, BlockType.GoldOre } },
                new MapLayer { name = "3", startDepth = 600, stone = stone,
                    ores = new[] { BlockType.IronOre } }
            };
            var density = AnimationCurve.Constant(0f, 1f, 1f);
            var fade = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            var fixedSize = new MapGenerationSampler(registry, 42819, 800, layers, density, 100f, 15,
                fade, 100, AnimationCurve.Constant(0f, 1f, 1f));
            var changingSize = new MapGenerationSampler(registry, 42819, 800, layers, density, 100f, 15,
                fade, 100, AnimationCurve.Linear(0f, .5f, 1f, 1f));
            (float share, float edgesPerOre) Measure(MapGenerationSampler sampler, int firstDepth)
            {
                int filled = 0, edges = 0;
                for (int y = firstDepth; y < firstDepth + 50; y++) for (int x = 0; x < 768; x++)
                {
                    if (sampler.GetBlock(x, y) != gold) continue;
                    filled++;
                    if (sampler.GetBlock(x + 1, y) != gold) edges++;
                    if (sampler.GetBlock(x, y + 1) != gold) edges++;
                }
                return (filled / (768f * 50f), edges / (float)filled);
            }
            var stable = Measure(fixedSize, 325);
            var small = Measure(changingSize, 325);
            var stableExit = Measure(fixedSize, 525);
            var smallExit = Measure(changingSize, 525);
            Check(small.edgesPerOre > stable.edgesPerOre * 1.08f,
                "Transition curve did not make new ore veins smaller and more scattered.");
            Check(smallExit.edgesPerOre > stableExit.edgesPerOre * 1.05f,
                "Transition curve did not shrink veins before the ore disappeared.");
            Check(Mathf.Abs(small.share - stable.share) < .04f,
                "Changing vein size distorted ore frequency.");
            Check(Mathf.Abs(smallExit.share - stableExit.share) < .04f,
                "Changing vein size distorted the exiting ore frequency.");
            var strongerCurve = new MapGenerationSampler(registry, 42819, 800, layers, density, 100f, 15,
                fade, 100, AnimationCurve.Linear(0f, .2f, 1f, 1f));
            Check(Measure(strongerCurve, 325).edgesPerOre > small.edgesPerOre * 1.05f,
                "Editing the size curve did not affect vein fragmentation.");
            for (int x = 0; x < 512; x++)
                Check(changingSize.GetBlock(x, 450) == fixedSize.GetBlock(x, 450),
                    "Vein size changed after the ore reached full probability.");
            return new { passed = true, fixedShare = stable.share, transitionShare = small.share,
                fixedEdgesPerOre = stable.edgesPerOre, transitionEdgesPerOre = small.edgesPerOre,
                exitFixedEdgesPerOre = stableExit.edgesPerOre, exitTransitionEdgesPerOre = smallExit.edgesPerOre };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(stone);
            UnityEngine.Object.DestroyImmediate(iron);
            UnityEngine.Object.DestroyImmediate(gold);
        }
    }
}
