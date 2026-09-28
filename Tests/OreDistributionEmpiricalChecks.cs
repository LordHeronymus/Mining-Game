using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class OreDistributionEmpiricalChecks
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry || !map.useOreSettings) throw new Exception("Missing configured map.");
        const int width = 8192, firstDepth = 5, rows = 25;
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, map.mapHeight,
            map.layers, map.oreDensityCurve, map.oreDensityMultiplierPercent,
            map.transitionThickness, map.oreSettings);
        var blocks = new Block[width * rows];
        var before = new Dictionary<Block, int>();
        var expected = new Dictionary<Block, double>();
        double expectedOreCells = 0;

        for (int row = 0; row < rows; row++)
        {
            int depth = firstDepth + row;
            float globalX = depth / (float)(map.mapHeight - 1);
            double density = Mathf.Clamp01(map.oreDensityCurve.Evaluate(globalX) *
                map.oreDensityMultiplierPercent / 100f);
            var active = map.oreSettings.Where(entry => entry != null && entry.baseWeight > 0f &&
                entry.layerIndices != null && entry.layerIndices.Contains(0) &&
                entry.weightCurve != null).ToArray();
            var weights = new List<(OreDistributionSetting entry, Block ore, double weight)>();
            foreach (var entry in active)
            {
                var ore = map.registry.GetById(entry.ore);
                if (!ore) continue;
                int lastLayer = entry.layerIndices.Max();
                int end = lastLayer + 1 < map.layers.Length ? map.layers[lastLayer + 1].startDepth : map.mapHeight;
                float progress = Mathf.Clamp01((depth - map.layers[0].startDepth) /
                    (float)Math.Max(1, end - map.layers[0].startDepth - 1));
                double weight = entry.baseWeight * entry.weightCurve.Evaluate(progress);
                weights.Add((entry, ore, weight));
            }
            double totalWeight = weights.Sum(item => item.weight);
            foreach (var item in weights)
            {
                var ore = item.ore;
                expected.TryGetValue(ore, out double current);
                expected[ore] = current + density * width * item.weight / totalWeight;
            }
            expectedOreCells += density * width;
            for (int x = 0; x < width; x++)
            {
                var block = sampler.GetBlock(x, depth);
                blocks[row * width + x] = block;
                if (block && block.HasOreOverlays)
                {
                    before.TryGetValue(block, out int count);
                    before[block] = count + 1;
                }
            }
        }

        ConnectedOreVeins.Generate(blocks, width, rows, map.ActiveSeed, sampler,
            map.GetMinimumVeinSize, null, firstDepth);
        var after = new Dictionary<Block, int>();
        foreach (var block in blocks)
            if (block && block.HasOreOverlays)
            {
                after.TryGetValue(block, out int count);
                after[block] = count + 1;
            }
        var visited = new bool[blocks.Length];
        var queue = new int[blocks.Length];
        var smallByOre = new Dictionary<Block, int>();
        var veinsByOre = new Dictionary<Block, int>();
        for (int start = 0; start < blocks.Length; start++)
        {
            var ore = blocks[start];
            if (!ore || !ore.HasOreOverlays || visited[start]) continue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            while (head < tail)
            {
                int index = queue[head++], x = index % width, y = index / width;
                if (x > 0) Add(index - 1);
                if (x + 1 < width) Add(index + 1);
                if (y > 0) Add(index - width);
                if (y + 1 < rows) Add(index + width);
            }
            veinsByOre.TryGetValue(ore, out int veins);
            veinsByOre[ore] = veins + 1;
            if (tail < map.GetMinimumVeinSize(ore))
            {
                smallByOre.TryGetValue(ore, out int small);
                smallByOre[ore] = small + tail;
            }

            void Add(int index)
            {
                if (visited[index] || blocks[index] != ore) return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }

        var results = expected.Select(pair => new
        {
            ore = pair.Key.id.ToString(),
            expectedCells = Math.Round(pair.Value, 1),
            rawCells = before.TryGetValue(pair.Key, out int raw) ? raw : 0,
            shapedCells = after.TryGetValue(pair.Key, out int shaped) ? shaped : 0,
            veins = veinsByOre.TryGetValue(pair.Key, out int veins) ? veins : 0,
            cellsInSmallVeins = smallByOre.TryGetValue(pair.Key, out int small) ? small : 0,
            expectedSharePercent = Math.Round(pair.Value / expectedOreCells * 100, 2),
            rawSharePercent = Math.Round(before.Values.Sum() == 0 ? 0 :
                (before.TryGetValue(pair.Key, out raw) ? raw : 0) / (double)before.Values.Sum() * 100, 2),
            shapedSharePercent = Math.Round(after.Values.Sum() == 0 ? 0 :
                (after.TryGetValue(pair.Key, out shaped) ? shaped : 0) / (double)after.Values.Sum() * 100, 2)
        }).ToArray();
        double rawDensity = before.Values.Sum() * 100d / (width * rows);
        double expectedDensity = expectedOreCells * 100d / (width * rows);
        if (results.Any(result => result.rawCells != result.shapedCells))
            throw new Exception("Connected veins changed the ore budget.");
        return new
        {
            passed = true,
            depthRange = firstDepth + "–" + (firstDepth + rows - 1),
            sampledCells = width * rows,
            expectedTotalOreCells = Math.Round(expectedOreCells, 1),
            rawTotalOreCells = before.Values.Sum(),
            expectedDensityPercent = Math.Round(expectedDensity, 3),
            rawDensityPercent = Math.Round(rawDensity, 3),
            shapedTotalOreCells = after.Values.Sum(),
            oreResults = results
        };
    }
}
