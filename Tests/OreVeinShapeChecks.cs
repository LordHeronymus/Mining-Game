using System;
using System.Linq;
using UnityEngine;

public static class OreVeinShapeChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.registry, "Missing map.");
        var ore = map.registry.blocks.First(block => block && block.HasOreOverlays &&
            block.id != BlockType.UltroniumOre);
        var stone = map.registry.GetById(BlockType.Stone);
        var shape = Enumerable.Repeat(stone, 25).ToArray();
        shape[1 + 1 * 5] = ore;
        shape[2 + 1 * 5] = ore;
        shape[3 + 1 * 5] = ore;
        shape[3 + 2 * 5] = ore;
        int moved = OreVeins.CompactThinTips(shape, 5, 5, (x, y) => stone,
            (block, y) => true);
        Check(moved > 0 && shape[1 + 1 * 5] == stone && shape[2 + 2 * 5] == ore,
            "A thin L-shaped tip was not compacted.");
        Check(shape.Count(block => block == ore) == 4, "Compaction changed ore amount.");

        var restricted = Enumerable.Repeat(stone, 25).ToArray();
        restricted[1 + 1 * 5] = ore;
        restricted[2 + 1 * 5] = ore;
        restricted[3 + 1 * 5] = ore;
        restricted[3 + 2 * 5] = ore;
        OreVeins.CompactThinTips(restricted, 5, 5, (x, y) => stone,
            (block, y) => y != 2);
        Check(restricted[2 + 2 * 5] == stone, "Compaction crossed a forbidden depth.");

        int width = 120, height = 160;
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, height, null,
            AnimationCurve.Constant(0f, 1f, 1f), 25f);
        var first = new Block[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) first[y * width + x] = sampler.GetBlock(x, y);
        var second = (Block[])first.Clone();
        int[] before = map.registry.blocks.Where(block => block && block.HasOreOverlays)
            .Select(block => first.Count(cell => cell == block)).ToArray();
        int changed = OreVeins.CompactThinTips(first, width, height, sampler.GetBaseBlock,
            sampler.CanPlaceOre);
        OreVeins.CompactThinTips(second, width, height, sampler.GetBaseBlock,
            sampler.CanPlaceOre);
        Check(first.SequenceEqual(second), "Vein shape depends on execution order.");
        int[] after = map.registry.blocks.Where(block => block && block.HasOreOverlays)
            .Select(block => first.Count(cell => cell == block)).ToArray();
        Check(before.SequenceEqual(after), "Compaction changed density or ore weights.");
        return new { passed = true, moved = changed, totalCells = first.Length };
    }
}
