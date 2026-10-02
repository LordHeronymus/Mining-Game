using System;
using System.Collections.Generic;
using UnityEngine;

public static class ConnectedOreVeins
{
    public const int DepthBand = 24;

    public static void Generate(Block[] blocks, int width, int height, int seed,
        MapGenerationSampler sampler, Func<Block, int> minimumSize,
        Func<int, int, bool> reserved = null, int depthOffset = 0)
    {
        var steps = GenerateSteps(blocks, width, height, seed, sampler, minimumSize, reserved, depthOffset);
        while (steps.MoveNext()) { }
    }
    public static System.Collections.IEnumerator GenerateSteps(Block[] blocks, int width, int height, int seed,
        MapGenerationSampler sampler, Func<Block, int> minimumSize,
        Func<int, int, bool> reserved = null, int depthOffset = 0, Action<float> progress = null)
    {
        if (blocks == null || width <= 0 || height <= 0 || blocks.Length != checked(width * height))
            throw new ArgumentException("Invalid vein grid dimensions.");
        if (sampler == null) throw new ArgumentNullException(nameof(sampler));
        if (minimumSize == null) throw new ArgumentNullException(nameof(minimumSize));

        var original = (Block[])blocks.Clone();
        var frontierMarks = new int[blocks.Length];
        int stamp = 0;
        for (int i = 0; i < blocks.Length; i++)
            if (IsOre(blocks[i])) blocks[i] = sampler.GetBaseBlock(i % width, i / width + depthOffset);

        for (int bandStart = 0; bandStart < height;)
        {
            int bandEnd = Mathf.Min(height, Mathf.Min(bandStart + DepthBand,
                sampler.LayerEnd(bandStart + depthOffset) - depthOffset));
            if (bandEnd <= bandStart) throw new InvalidOperationException("Invalid ore depth band.");
            var candidates = new Dictionary<Block, List<int>>();
            for (int y = bandStart; y < bandEnd; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    var ore = original[index];
                    if (!IsOre(ore)) continue;
                    if (!candidates.TryGetValue(ore, out var cells)) candidates.Add(ore, cells = new List<int>());
                    cells.Add(index);
                }

            var ores = new List<Block>(candidates.Keys);
            ores.Sort((a, b) =>
            {
                int count = candidates[a].Count.CompareTo(candidates[b].Count);
                return count != 0 ? count : a.id.CompareTo(b.id);
            });
            foreach (var ore in ores)
            {
                var source = candidates[ore];
                source.Sort((a, b) =>
                {
                    uint ah = OreVeins.Hash(seed, a % width, a / width, (uint)ore.id + 0x72a1u);
                    uint bh = OreVeins.Hash(seed, b % width, b / width, (uint)ore.id + 0x72a1u);
                    int order = ah.CompareTo(bh);
                    return order != 0 ? order : a.CompareTo(b);
                });
                int remaining = source.Count;
                int minimum = Mathf.Max(1, minimumSize(ore));
                int sourceCursor = 0;
                while (remaining > 0)
                {
                    int origin = -1;
                    while (sourceCursor < source.Count && origin < 0)
                    {
                        int candidate = source[sourceCursor++];
                        if (Available(candidate, ore)) origin = candidate;
                    }
                    if (origin < 0) break;
                    int indexSize = sampler.VeinSizeIndex(ore, origin / width + depthOffset);
                    int typicalSize = Mathf.Clamp(Mathf.RoundToInt(indexSize * indexSize * .18f),
                        minimum, 2048);
                    float variation = .75f + (OreVeins.Hash(seed, origin % width, origin / width,
                        (uint)ore.id + 0x55c9u) & 0xffff) / 65535f * .5f;
                    int goal = Mathf.Min(remaining, Mathf.Max(minimum, Mathf.RoundToInt(typicalSize * variation)));
                    if (remaining - goal < minimum) goal = remaining;
                    int grown = Grow(origin, ore, goal, bandStart, bandEnd, indexSize);
                    remaining -= grown;
                }

                // The original cells are reserved for their ore. They guarantee that even
                // narrow bands or crowded terrain keep the exact configured ore budget.
                if (remaining > 0)
                    foreach (int index in source)
                    {
                        if (remaining == 0) break;
                        if (!Available(index, ore)) continue;
                        blocks[index] = ore;
                        remaining--;
                    }
                if (remaining != 0)
                    throw new InvalidOperationException("Connected vein growth could not preserve the ore budget.");
            }
            foreach (var ore in ores) ConsolidateSmallVeins(ore, bandStart, bandEnd);
            bandStart = bandEnd;
            progress?.Invoke(bandStart / (float)height);
            yield return null;
        }

        bool Available(int index, Block ore)
        {
            int x = index % width, y = index / width;
            var current = blocks[index];
            var source = original[index];
            return current && (current.IsStone || current.id == BlockType.Dirt) &&
                (!IsOre(source) || source == ore) && sampler.CanPlaceOre(ore, y + depthOffset) &&
                (reserved == null || !reserved(x, y));
        }

        int Grow(int origin, Block ore, int goal, int start, int end, int indexSize)
        {
            stamp++;
            var frontier = new List<int>(Mathf.Min(goal * 3, 1024));
            int sx = origin % width, sy = origin / width;
            uint shape = OreVeins.Hash(seed, sx, sy, (uint)ore.id + 0x9171u);
            float angle = (shape & 0xffff) / 65535f * Mathf.PI;
            float axisX = Mathf.Cos(angle), axisY = Mathf.Sin(angle);
            float phase = ((shape >> 16) & 0xffff) / 65535f * Mathf.PI * 2f;
            blocks[origin] = ore;
            int placed = 1;
            AddNeighbors(origin);
            while (placed < goal && frontier.Count > 0)
            {
                int bestAt = -1;
                float bestScore = float.NegativeInfinity;
                for (int f = 0; f < frontier.Count; f++)
                {
                    int index = frontier[f];
                    if (!Available(index, ore)) continue;
                    int x = index % width, y = index / width;
                    float dx = x - sx, dy = y - sy;
                    float along = dx * axisX + dy * axisY;
                    float across = dy * axisX - dx * axisY;
                    float bend = Mathf.Sin(along * .23f + phase) * Mathf.Min(2f, indexSize * .12f);
                    float noise = (OreVeins.Hash(seed, x, y, (uint)ore.id + 0x6123u) & 0xffff) / 65535f;
                    float score = (original[index] == ore ? .65f : 0f) +
                        SameNeighbors(x, y, ore) * .28f - Mathf.Abs(across - bend) * .15f -
                        Mathf.Abs(along) * .022f + noise * .48f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestAt = f;
                }
                if (bestAt < 0) break;
                int chosen = frontier[bestAt];
                frontier[bestAt] = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);
                blocks[chosen] = ore;
                placed++;
                AddNeighbors(chosen);
            }
            return placed;

            void AddNeighbors(int index)
            {
                int x = index % width, y = index / width;
                if (x > 0) Add(index - 1);
                if (x + 1 < width) Add(index + 1);
                if (y > start) Add(index - width);
                if (y + 1 < end) Add(index + width);
            }

            void Add(int index)
            {
                if (frontierMarks[index] == stamp || !Available(index, ore)) return;
                frontierMarks[index] = stamp;
                frontier.Add(index);
            }
        }

        int SameNeighbors(int x, int y, Block ore)
        {
            int count = 0;
            int index = y * width + x;
            if (x > 0 && blocks[index - 1] == ore) count++;
            if (x + 1 < width && blocks[index + 1] == ore) count++;
            if (y > 0 && blocks[index - width] == ore) count++;
            if (y + 1 < height && blocks[index + width] == ore) count++;
            return count;
        }

        void ConsolidateSmallVeins(Block ore, int start, int end)
        {
            int minimum = Mathf.Max(1, minimumSize(ore));
            if (minimum <= 1) return;
            var visited = new bool[(end - start) * width];
            var queue = new int[(end - start) * width];
            var small = new List<List<int>>();
            var established = new List<int>();
            for (int y = start; y < end; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x, local = (y - start) * width + x;
                    if (blocks[index] != ore || visited[local]) continue;
                    int head = 0, tail = 0;
                    queue[tail++] = index;
                    visited[local] = true;
                    while (head < tail)
                    {
                        int current = queue[head++], cx = current % width, cy = current / width;
                        if (cx > 0) Visit(current - 1);
                        if (cx + 1 < width) Visit(current + 1);
                        if (cy > start) Visit(current - width);
                        if (cy + 1 < end) Visit(current + width);
                    }
                    var component = new List<int>(tail);
                    for (int i = 0; i < tail; i++) component.Add(queue[i]);
                    if (tail < minimum) small.Add(component);
                    else established.AddRange(component);

                    void Visit(int neighbor)
                    {
                        int localIndex = neighbor - start * width;
                        if (visited[localIndex] || blocks[neighbor] != ore) return;
                        visited[localIndex] = true;
                        queue[tail++] = neighbor;
                    }
                }

            if (established.Count == 0 && small.Count > 1)
            {
                int largest = 0;
                for (int i = 1; i < small.Count; i++)
                    if (small[i].Count > small[largest].Count) largest = i;
                established.AddRange(small[largest]);
                small.RemoveAt(largest);
            }
            foreach (var component in small)
            {
                if (established.Count == 0) break;
                foreach (int index in component)
                    blocks[index] = sampler.GetBaseBlock(index % width, index / width + depthOffset);
                int previousCount = established.Count;
                bool complete = true;
                foreach (int source in component)
                {
                    int best = -1, bestDistance = int.MaxValue;
                    foreach (int body in established)
                    {
                        int bx = body % width, by = body / width;
                        Consider(bx - 1, by);
                        Consider(bx + 1, by);
                        Consider(bx, by - 1);
                        Consider(bx, by + 1);
                    }
                    if (best < 0) { complete = false; break; }
                    blocks[best] = ore;
                    established.Add(best);

                    void Consider(int x, int y)
                    {
                        if (x < 0 || x >= width || y < start || y >= end) return;
                        int candidate = y * width + x;
                        if (!Available(candidate, ore)) return;
                        int dx = x - source % width, dy = y - source / width;
                        int distance = dx * dx + dy * dy;
                        if (distance >= bestDistance) return;
                        bestDistance = distance;
                        best = candidate;
                    }
                }
                if (complete) continue;
                for (int i = established.Count - 1; i >= previousCount; i--)
                {
                    int index = established[i];
                    blocks[index] = sampler.GetBaseBlock(index % width, index / width + depthOffset);
                }
                established.RemoveRange(previousCount, established.Count - previousCount);
                foreach (int index in component) blocks[index] = ore;
            }
        }
    }

    static bool IsOre(Block block) => block && block.HasOreOverlays;
}
