using System;
using UnityEngine;

public static class OreVeins
{
    // Independent streams are stable even when another system consumes random numbers.
    public static uint Hash(int seed, int x, int y, uint stream)
    {
        unchecked
        {
            uint value = (uint)seed ^ ((uint)x * 0x9e3779b9u) ^ ((uint)y * 0x85ebca6bu) ^ stream;
            value ^= value >> 16; value *= 0x7feb352du;
            value ^= value >> 15; value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }
    }

    // Distance from the actual boundary includes holes and competing ore types.
    // Normalize each connected vein separately to make its core richer than its rim.
    public static OreRichness[] Build(Block[] blocks, int width, int height, int seed)
    {
        if (width <= 0 || height <= 0 || blocks.Length != checked(width * height))
            throw new ArgumentException("Invalid vein grid dimensions.");
        int count = blocks.Length;
        var result = new OreRichness[count];
        var distance = new int[count];
        var queue = new int[count];
        int head = 0, tail = 0;
        for (int i = 0; i < count; i++)
        {
            var block = blocks[i];
            if (!block || !block.HasOreOverlays) continue;
            int x = i % width, y = i / width;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1 ||
                blocks[i - 1] != block || blocks[i + 1] != block ||
                blocks[i - width] != block || blocks[i + width] != block)
            {
                distance[i] = 1;
                queue[tail++] = i;
            }
        }
        while (head < tail)
        {
            int i = queue[head++], x = i % width, y = i / width;
            if (x > 0) Visit(i, i - 1);
            if (x + 1 < width) Visit(i, i + 1);
            if (y > 0) Visit(i, i - width);
            if (y + 1 < height) Visit(i, i + width);
        }

        var visited = new bool[count];
        float offset = (Hash(seed, 0, 0, 0x314159u) & 0xffff) * .01f;
        for (int start = 0; start < count; start++)
        {
            if (distance[start] == 0 || visited[start]) continue;
            head = 0; tail = 0;
            queue[tail++] = start; visited[start] = true;
            int maxDistance = 1;
            while (head < tail)
            {
                int i = queue[head++], x = i % width, y = i / width;
                maxDistance = Math.Max(maxDistance, distance[i]);
                if (x > 0) Connect(i, i - 1);
                if (x + 1 < width) Connect(i, i + 1);
                if (y > 0) Connect(i, i - width);
                if (y + 1 < height) Connect(i, i + width);
            }
            for (int n = 0; n < tail; n++)
            {
                int i = queue[n];
                if (maxDistance == 1 || distance[i] == 1) continue;
                float depth = (distance[i] - 1f) / (maxDistance - 1f);
                float variation = (Mathf.PerlinNoise(i % width * .31f + offset,
                    i / width * .31f + offset) - .5f) * .3f;
                depth += variation;
                result[i] = depth < .32f ? OreRichness.Small :
                    depth < .72f ? OreRichness.Medium : OreRichness.Rich;
            }
        }
        return result;

        void Visit(int from, int to)
        {
            if (distance[to] != 0 || blocks[to] != blocks[from]) return;
            distance[to] = distance[from] + 1;
            queue[tail++] = to;
        }
        void Connect(int from, int to)
        {
            if (visited[to] || blocks[to] != blocks[from]) return;
            visited[to] = true;
            queue[tail++] = to;
        }
    }
}
