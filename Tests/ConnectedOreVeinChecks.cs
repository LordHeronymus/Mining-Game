using System;
using System.Collections.Generic;
using UnityEngine;

public static class ConnectedOreVeinChecks
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry) throw new Exception("Missing configured map.");
        int width = map.mapWidth, height = map.mapHeight, seed = map.ActiveSeed;
        var sampler = new MapGenerationSampler(map.registry, seed, height, map.layers,
            map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
            map.useOreSettings ? map.oreSettings : null);
        var caves = map.CreateCaveMask(seed, width, height, default);
        var raw = new Block[checked(width * height)];
        var before = new Dictionary<Block, int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (caves[index]) continue;
                var block = sampler.GetBlock(x, y);
                raw[index] = block;
                if (!block || !block.HasOreOverlays) continue;
                before.TryGetValue(block, out int count);
                before[block] = count + 1;
            }
        var shaped = (Block[])raw.Clone();
        ConnectedOreVeins.Generate(shaped, width, height, seed, sampler,
            map.GetMinimumVeinSize, (x, y) => caves[y * width + x]);
        var repeated = (Block[])raw.Clone();
        ConnectedOreVeins.Generate(repeated, width, height, seed, sampler,
            map.GetMinimumVeinSize, (x, y) => caves[y * width + x]);
        for (int i = 0; i < shaped.Length; i++)
            if (shaped[i] != repeated[i]) throw new Exception("Same seed changed the vein layout.");
        var after = new Dictionary<Block, int>();
        for (int i = 0; i < shaped.Length; i++)
        {
            var block = shaped[i];
            if (caves[i] && block) throw new Exception("Ore filled a cave.");
            if (!block || !block.HasOreOverlays) continue;
            if (!sampler.CanPlaceOre(block, i / width))
                throw new Exception("Ore crossed a disabled depth.");
            after.TryGetValue(block, out int count);
            after[block] = count + 1;
        }
        foreach (var pair in before)
            if (!after.TryGetValue(pair.Key, out int count) || count != pair.Value)
                throw new Exception("Ore count changed for " + pair.Key.id);
        int maxEightRowOreDifference = 0;
        for (int start = 0; start < height; start += 8)
        {
            int originalOre = 0, shapedOre = 0;
            for (int y = start; y < Math.Min(height, start + 8); y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (raw[index] && raw[index].HasOreOverlays) originalOre++;
                    if (shaped[index] && shaped[index].HasOreOverlays) shapedOre++;
                }
            maxEightRowOreDifference = Math.Max(maxEightRowOreDifference,
                Math.Abs(originalOre - shapedOre));
        }
        for (int band = 0; band < height;)
        {
            int end = Math.Min(height, Math.Min(band + ConnectedOreVeins.DepthBand,
                sampler.LayerEnd(band)));
            var expected = new Dictionary<Block, int>();
            var actual = new Dictionary<Block, int>();
            for (int y = band; y < end; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (raw[index] && raw[index].HasOreOverlays)
                    {
                        expected.TryGetValue(raw[index], out int count);
                        expected[raw[index]] = count + 1;
                    }
                    if (shaped[index] && shaped[index].HasOreOverlays)
                    {
                        actual.TryGetValue(shaped[index], out int count);
                        actual[shaped[index]] = count + 1;
                    }
                }
            foreach (var pair in expected)
                if (!actual.TryGetValue(pair.Key, out int count) || count != pair.Value)
                    throw new Exception("Ore budget changed in a depth band.");
            band = end;
        }
        var visited = new bool[shaped.Length];
        var queue = new int[shaped.Length];
        int smallVeinCells = 0, veinCount = 0;
        var smallVeins = new List<string>();
        for (int start = 0; start < shaped.Length; start++)
        {
            var ore = shaped[start];
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
                if (y + 1 < height) Add(index + width);
            }
            veinCount++;
            if (tail < map.GetMinimumVeinSize(ore))
            {
                smallVeinCells += tail;
                if (smallVeins.Count < 12)
                    smallVeins.Add(ore.id + "@" + (start / width) + ":" + tail);
            }

            void Add(int index)
            {
                if (visited[index] || shaped[index] != ore) return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }
        const int denseWidth = 64, denseHeight = 64;
        var denseSampler = new MapGenerationSampler(map.registry, seed, denseHeight, map.layers,
            AnimationCurve.Constant(0f, 1f, 1f), 100f, map.transitionThickness,
            map.useOreSettings ? map.oreSettings : null);
        var dense = new Block[denseWidth * denseHeight];
        var denseCounts = new Dictionary<Block, int>();
        for (int y = 0; y < denseHeight; y++)
            for (int x = 0; x < denseWidth; x++)
            {
                int index = y * denseWidth + x;
                dense[index] = denseSampler.GetBlock(x, y);
                if (!dense[index] || !dense[index].HasOreOverlays) continue;
                denseCounts.TryGetValue(dense[index], out int count);
                denseCounts[dense[index]] = count + 1;
            }
        ConnectedOreVeins.Generate(dense, denseWidth, denseHeight, seed, denseSampler,
            map.GetMinimumVeinSize);
        foreach (var pair in denseCounts)
        {
            int count = 0;
            foreach (var block in dense) if (block == pair.Key) count++;
            if (count != pair.Value) throw new Exception("Full-density budget changed.");
        }
        return new { passed = true, width, height, caveCells = Array.FindAll(caves, cell => cell).Length,
            oreCells = Total(after), oreTypes = after.Count, veinCount, smallVeinCells,
            maxEightRowOreDifference, fullDensityOreCells = Total(denseCounts),
            smallVeins = string.Join(";", smallVeins) };
    }

    static int Total(Dictionary<Block, int> counts)
    {
        int result = 0;
        foreach (var entry in counts) result += entry.Value;
        return result;
    }
}
