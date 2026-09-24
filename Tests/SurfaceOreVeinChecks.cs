using System;
using System.Reflection;
using UnityEngine;

public static class SurfaceOreVeinChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        const int width = 2048, height = 40, surfaceDepth = 20;
        var stone = ScriptableObject.CreateInstance<Block>();
        var ore = ScriptableObject.CreateInstance<Block>();
        var overlay = ScriptableObject.CreateInstance<OreTile>();
        var registry = ScriptableObject.CreateInstance<BlockRegistry>();
        try
        {
            stone.id = BlockType.Stone;
            ore.id = BlockType.CopperOre;
            ore.spawnWithNoise = true;
            ore.oreFrequencyPercent = 1f;
            ore.veinSizeIndex = 12;
            ore.smallOre = ore.mediumOre = ore.richOre = new[] { overlay };
            registry.blocks = new[] { stone, ore };
            typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(registry, null);

            var density = AnimationCurve.Constant(0f, 1f, .35f);
            var normal = new MapGenerationSampler(registry, 92741, height, null, density, 100f,
                surfaceOreRampDepth: surfaceDepth, surfaceOreVeinSizePercent: 100f);
            var smaller = new MapGenerationSampler(registry, 92741, height, null, density, 100f,
                surfaceOreRampDepth: surfaceDepth, surfaceOreVeinSizePercent: 50f);
            var normalBlocks = Sample(normal);
            var smallerBlocks = Sample(smaller);
            OreVeins.PruneSmallVeins(normalBlocks, width, height, 4, normal.GetBaseBlock);
            OreVeins.PruneSmallVeins(smallerBlocks, width, height, 4, smaller.GetBaseBlock);

            var normalSurface = Measure(normalBlocks, 0, surfaceDepth);
            var smallerSurface = Measure(smallerBlocks, 0, surfaceDepth);
            Check(smallerSurface.count > normalSurface.count * 1.35f,
                "The surface setting did not create substantially more veins.");
            Check(smallerSurface.averageSize < normalSurface.averageSize * .8f,
                "The surface setting did not make veins substantially smaller.");
            Check(Mathf.Abs(smallerSurface.coverage - normalSurface.coverage) < .04f,
                "The surface vein size changed ore coverage too strongly.");
            for (int y = surfaceDepth; y < height; y++)
                for (int x = 0; x < width; x++)
                    Check(smaller.GetBlock(x, y) == normal.GetBlock(x, y),
                        "Surface vein size affected blocks below the initial area.");

            var mapObject = new GameObject("Surface vein default check");
            try
            {
                var map = mapObject.AddComponent<MapGenerator>();
                Check(Mathf.Approximately(map.surfaceOreVeinSizePercent, 50f),
                    "The default surface vein size is not 50 percent.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mapObject); }

            return new
            {
                passed = true,
                normalVeins = normalSurface.count,
                smallerVeins = smallerSurface.count,
                veinCountRatio = smallerSurface.count / (float)normalSurface.count,
                normalAverageSize = normalSurface.averageSize,
                smallerAverageSize = smallerSurface.averageSize,
                normalCoverage = normalSurface.coverage,
                smallerCoverage = smallerSurface.coverage
            };

            Block[] Sample(MapGenerationSampler sampler)
            {
                var blocks = new Block[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) blocks[y * width + x] = sampler.GetBlock(x, y);
                return blocks;
            }

            (int count, float averageSize, float coverage) Measure(Block[] blocks, int firstRow, int rowCount)
            {
                var visited = new bool[blocks.Length];
                var queue = new int[blocks.Length];
                int count = 0, cells = 0, head = 0, tail = 0;
                int lastRow = firstRow + rowCount;
                for (int start = firstRow * width; start < lastRow * width; start++)
                {
                    if (visited[start] || blocks[start] != ore) continue;
                    count++;
                    head = 0;
                    tail = 0;
                    queue[tail++] = start;
                    visited[start] = true;
                    while (head < tail)
                    {
                        int index = queue[head++], x = index % width, y = index / width;
                        cells++;
                        if (x > 0) Visit(index - 1);
                        if (x + 1 < width) Visit(index + 1);
                        if (y > firstRow) Visit(index - width);
                        if (y + 1 < lastRow) Visit(index + width);
                    }
                }
                return (count, cells / (float)Math.Max(1, count), cells / (float)(width * rowCount));

                void Visit(int index)
                {
                    if (visited[index] || blocks[index] != ore) return;
                    visited[index] = true;
                    queue[tail++] = index;
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(registry);
            UnityEngine.Object.DestroyImmediate(overlay);
            UnityEngine.Object.DestroyImmediate(ore);
            UnityEngine.Object.DestroyImmediate(stone);
        }
    }
}
