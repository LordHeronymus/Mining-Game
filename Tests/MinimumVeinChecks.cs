using System;
using UnityEditor;
using UnityEngine;

public static class MinimumVeinChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(
            "Assets/GameObjects/Map/Blocks/BlockRegistry.asset");
        var stone = registry.GetById(BlockType.Stone);
        var dirt = registry.GetById(BlockType.Dirt);
        var copper = registry.GetById(BlockType.CopperOre);
        var gold = registry.GetById(BlockType.GoldOre);
        Check(stone && dirt && copper && gold && copper.HasOreOverlays && gold.HasOreOverlays,
            "Required ore or terrain asset missing.");

        const int width = 8, height = 6;
        var blocks = new Block[width * height];
        Block Base(int x, int y) => x < 2 ? dirt : stone;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) blocks[y * width + x] = Base(x, y);
        void Put(Block ore, int x, int y) => blocks[y * width + x] = ore;

        Put(copper, 0, 0);
        Put(copper, 3, 0); Put(copper, 4, 0); Put(copper, 4, 1);
        Put(gold, 5, 0);
        Put(copper, 0, 3); Put(copper, 1, 3); Put(copper, 1, 4); Put(copper, 1, 5);
        Put(gold, 5, 3); Put(gold, 6, 3); Put(gold, 6, 4); Put(gold, 7, 4);

        var unchanged = (Block[])blocks.Clone();
        Check(OreVeins.PruneSmallVeins(unchanged, width, height, 1, Base) == 0,
            "Minimum size 1 removed ore.");
        for (int i = 0; i < blocks.Length; i++)
            Check(unchanged[i] == blocks[i], "Minimum size 1 changed the map.");

        int removed = OreVeins.PruneSmallVeins(blocks, width, height, 4, Base);
        Check(removed == 5, "Wrong number of undersized vein blocks removed.");
        Check(blocks[0] == dirt && blocks[3] == stone && blocks[5] == stone,
            "Small veins did not reveal the original terrain.");
        Check(blocks[3 * width] == copper && blocks[5 * width + 1] == copper &&
            blocks[3 * width + 5] == gold && blocks[4 * width + 7] == gold,
            "A four-block vein was removed at a map boundary.");

        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.minimumOreVeinSize == 4,
            "Default minimum vein size is not four blocks.");
        return new { passed = true, minimum = map.minimumOreVeinSize, removed };
    }
}
