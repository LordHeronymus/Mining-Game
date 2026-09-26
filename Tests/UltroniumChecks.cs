#if UNITY_EDITOR
using System;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class UltroniumChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.registry, "Map or registry missing.");
        var block = map.registry.GetById(BlockType.UltroniumOre);
        Check(block && block.displayName == "Ultronium", "Ultronium block is not registered.");
        Check(block.itemDrop && block.itemDrop.item == Item.Ultronium, "Ultronium item is missing.");
        Check(block.smallOre.Length == 2 && block.mediumOre.Length == 2 && block.richOre.Length == 3,
            "Ultronium needs 2 small, 2 medium and 3 rich overlays.");
        var expectedHashes = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Ultronium_01_small.png"] = "DE2B1B792CC889BEBFBFED4F492D1A0358CA968DDE3F92247D5F8910A28D18A9",
            ["Ultronium_02_small.png"] = "EFFF35BA31E10A6135AE9FCDCE298F883457BE897478230421F025565DB3263A",
            ["Ultronium_03_medium.png"] = "B7CCC2664B645702B2493C927E1769F4E0E900ADB27F350C0B5B555E7DD76682",
            ["Ultronium_04_medium.png"] = "692C7EE7A57129E7ADCF206E74117EECAD7C9CA610E0D86935EDBDE0F4613782",
            ["Ultronium_05_rich.png"] = "1C7E263E4B63C2004C0BE5ECE69760A5E9046C5601B33E33640595B817B5B1FC",
            ["Ultronium_06_rich.png"] = "76507C3AFE61838C191AB02EB37D2EF52A3CDD564BC1675A8953240D34DFD9BC",
            ["Ultronium_07_rich.png"] = "B7FE11C5DB7BF6F8DEC823FD1C3945E787E6D7D3ABF952B8A035EDD4AA3879D2"
        };
        foreach (var tile in block.smallOre.Concat(block.mediumOre).Concat(block.richOre))
        {
            Check(tile && tile.sprite && tile.block == block, "Broken Ultronium tile reference.");
            Check(Mathf.Abs(tile.color.a - .98f) < .001f,
                "Ultronium tile is missing its glow marker.");
            Check(tile.colliderType == UnityEngine.Tilemaps.Tile.ColliderType.None,
                "Ultronium overlays must not have colliders.");
            Check(Mathf.Abs(tile.sprite.bounds.size.x - .5f) < .001f &&
                Mathf.Abs(tile.sprite.bounds.size.y - .5f) < .001f, "Incorrect Ultronium sprite size.");
            string path = AssetDatabase.GetAssetPath(tile.sprite);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(importer && importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear &&
                importer.textureCompression == TextureImporterCompression.Uncompressed &&
                importer.alphaSource == TextureImporterAlphaSource.FromInput,
                "Incorrect Ultronium texture import: " + path);
            string filename = System.IO.Path.GetFileName(path);
            Check(expectedHashes.TryGetValue(filename, out string expectedHash),
                "Unexpected Ultronium sprite slot: " + filename);
            using var sha = SHA256.Create();
            string actualHash = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path)))
                .Replace("-", "");
            Check(actualHash == expectedHash,
                "Ultronium sprite pixels or alpha were changed: " + filename);
        }

        var entry = map.oreSettings.SingleOrDefault(candidate =>
            candidate != null && candidate.ore == BlockType.UltroniumOre);
        Check(entry != null, "Ultronium is missing from Gameplay Settings > Erzverteilung > Erze.");
        Check(entry.layerIndices.SequenceEqual(new[] { map.layers.Length - 1 }),
            "Ultronium must initially be restricted to the deepest layer.");
        Check(entry.baseWeight > 0f && entry.weightCurve != null && entry.veinSizeCurve != null,
            "Ultronium distribution is incomplete.");

        var isolatedCell = new[] { block };
        Check(OreVeins.PruneSmallVeins(isolatedCell, 1, 1, 100,
            (x, y) => map.layers[^1].stone) == 0 && isolatedCell[0] == block,
            "The global minimum vein size removed isolated Ultronium.");

        var isolated = new OreDistributionSetting
        {
            ore = BlockType.UltroniumOre,
            layerIndices = new[] { map.layers.Length - 1 },
            baseWeight = 1f,
            weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
            baseVeinSize = 12f,
            veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
        };
        var sampler = new MapGenerationSampler(map.registry, 73491, map.GeneratedHeight, map.layers,
            AnimationCurve.Constant(0f, 1f, 1f), 100f, map.transitionThickness, new[] { isolated });
        int deepest = map.layers[^1].startDepth;
        for (int y = 0; y < deepest; y += 7)
            for (int x = 0; x < 40; x += 3)
                Check(sampler.GetBlock(x, y) != block, "Ultronium appeared above the deepest layer.");
        int found = 0;
        for (int y = deepest; y < map.GeneratedHeight; y += 3)
            for (int x = 0; x < 120; x++)
                if (sampler.GetBlock(x, y) == block) found++;
        Check(found > 0, "Ultronium cannot generate in the deepest layer.");

        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        Check(catalog && catalog.items.Contains(block.itemDrop), "Ultronium is missing from ItemCatalog.");
        return new { passed = true, variants = 7, deepestLayer = map.layers.Length, sampleCount = found,
            weight = entry.baseWeight, veinSize = entry.baseVeinSize };
    }
}
#endif
