using System;
using UnityEngine;

public static class ConnectedOreVeinSizeChecks
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) throw new Exception("Missing registry.");
        const int width = 160, height = 160, seed = 24681;
        var stone = map.registry.GetById(BlockType.Stone);
        var layer = new MapLayer { name = "Test", startDepth = 0, stone = stone };
        var small = Measure(5f);
        var large = Measure(15f);
        if (large.meanSize <= small.meanSize * 1.5f)
            throw new Exception("Increasing the vein size did not enlarge generated veins.");
        return new { passed = true, small, large };

        (int cells, int veins, float meanSize) Measure(float size)
        {
            var setting = new OreDistributionSetting
            {
                ore = BlockType.IronOre,
                layerIndices = new[] { 0 },
                baseWeight = 1f,
                weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                baseVeinSize = size,
                veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f),
                minimumVeinSize = 4
            };
            var sampler = new MapGenerationSampler(map.registry, seed, height,
                new[] { layer }, AnimationCurve.Constant(0f, 1f, 1f), 10f,
                map.transitionThickness, new[] { setting });
            var cells = new Block[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) cells[y * width + x] = sampler.GetBlock(x, y);
            ConnectedOreVeins.Generate(cells, width, height, seed, sampler, ore => 4);
            var iron = map.registry.GetById(BlockType.IronOre);
            var visited = new bool[cells.Length];
            var queue = new int[cells.Length];
            int count = 0, veins = 0;
            for (int start = 0; start < cells.Length; start++)
            {
                if (cells[start] != iron || visited[start]) continue;
                int head = 0, tail = 0;
                queue[tail++] = start;
                visited[start] = true;
                while (head < tail)
                {
                    int index = queue[head++], x = index % width, y = index / width;
                    if (x > 0) Add(index - 1);
                    if (x + 1 < width) Add(index + 1);
                    if (y > 0) Add(index - width);
                    if (y + 1 < height) Add(index + width);
                }
                count += tail;
                veins++;

                void Add(int index)
                {
                    if (visited[index] || cells[index] != iron) return;
                    visited[index] = true;
                    queue[tail++] = index;
                }
            }
            return (count, veins, count / (float)Math.Max(1, veins));
        }
    }
}
