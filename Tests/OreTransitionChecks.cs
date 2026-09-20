using System;
using System.Reflection;
using UnityEngine;

public static class OreTransitionChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static object Main()
    {
        var stone = ScriptableObject.CreateInstance<Block>(); stone.id = BlockType.Stone;
        var oldOre = ScriptableObject.CreateInstance<Block>(); oldOre.id = BlockType.CopperOre;
        var newOre = ScriptableObject.CreateInstance<Block>(); newOre.id = BlockType.GoldOre;
        var lastingOre = ScriptableObject.CreateInstance<Block>(); lastingOre.id = BlockType.IronOre;
        var registry = ScriptableObject.CreateInstance<BlockRegistry>();
        try
        {
            foreach (var ore in new[] { oldOre, newOre, lastingOre })
            {
                ore.spawnWithNoise = true;
                ore.oreFrequencyPercent = 10f;
            }
            registry.blocks = new[] { stone, oldOre, newOre, lastingOre };
            typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(registry, null);
            var layers = new[]
            {
                new MapLayer { name = "1", startDepth = 0, stone = stone,
                    ores = new[] { BlockType.CopperOre, BlockType.IronOre } },
                new MapLayer { name = "2", startDepth = 300, stone = stone,
                    ores = new[] { BlockType.GoldOre, BlockType.IronOre } },
                new MapLayer { name = "3", startDepth = 600, stone = stone,
                    ores = new[] { BlockType.IronOre } }
            };
            var density = AnimationCurve.Constant(0f, 1f, 1f);
            var transition = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            MapGenerationSampler Sample(AnimationCurve shape, int width) =>
                new MapGenerationSampler(registry, 12345, 800, layers, density, 100f, 15, shape, width);
            var normal = Sample(transition, 100);
            float Share(MapGenerationSampler sampler, Block ore, int depth)
            {
                int count = 0;
                for (int x = 0; x < 2048; x++) if (sampler.GetBlock(x * 37, depth) == ore) count++;
                return count / 2048f;
            }
            float oldFull = Share(normal, oldOre, 150);
            float oldFading = Share(normal, oldOre, 250);
            float oldEnd = Share(normal, oldOre, 299);
            float newStart = Share(normal, newOre, 300);
            float newRising = Share(normal, newOre, 350);
            float newFull = Share(normal, newOre, 450);
            float newEnd = Share(normal, newOre, 599);
            Check(oldFull > .40f && oldFull < .60f && newFull > .40f && newFull < .60f,
                "Full ore weights did not balance equally.");
            Check(oldFading > .20f && oldFading < .44f && newRising > .20f && newRising < .44f,
                "Ore weights did not fade over 100 rows.");
            Check(oldEnd == 0f && newStart == 0f && newEnd == 0f,
                "Ore remained at an entrance or exit boundary.");
            for (int x = 0; x < 128; x++)
            {
                Check(normal.GetBlock(x, 299) == lastingOre && normal.GetBlock(x, 300) == lastingOre,
                    "Global ore coverage dropped while a lasting ore was available.");
                Check(normal.GetBlock(x, 799) == lastingOre,
                    "Lasting ore faded at the map bottom without leaving a layer.");
                Check(normal.GetBlock(x, 0) == stone,
                    "Ore spawned at zero surface density.");
            }
            float surfaceMiddle = Share(normal, oldOre, 5) + Share(normal, lastingOre, 5);
            float surfaceEnd = Share(normal, oldOre, 10) + Share(normal, lastingOre, 10);
            Check(surfaceMiddle > .4f && surfaceMiddle < .6f && surfaceEnd == 1f,
                "Surface ore density did not rise linearly to the curve start.");
            var delayed = Sample(new AnimationCurve(new Keyframe(0f, 0f),
                new Keyframe(.8f, 0f), new Keyframe(1f, 1f)), 100);
            Check(Share(delayed, newOre, 350) == 0f && Share(delayed, newOre, 400) > .40f,
                "Editing the transition curve did not change the fade shape.");
            var shorter = Sample(transition, 50);
            Check(Share(shorter, newOre, 350) > newRising + .08f,
                "Transition width did not change the fade duration.");
            return new { passed = true, oldFull, oldFading, oldEnd, newStart, newRising, newFull, newEnd };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(stone);
            UnityEngine.Object.DestroyImmediate(oldOre);
            UnityEngine.Object.DestroyImmediate(newOre);
            UnityEngine.Object.DestroyImmediate(lastingOre);
        }
    }
}
