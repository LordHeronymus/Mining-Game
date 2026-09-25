using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class OreOverlaySetup
{
    const string Blocks = "Assets/GameObjects/Map/Blocks";

    public static string InstallCopper() => Install("Copper",
        new[] { "Kupfer_01_Small", "Kupfer_02_Small", "Kupfer_03_Medium", "Kupfer_04_Medium",
            "Kupfer_05_Rich", "Kupfer_06_Rich", "Kupfer_07_Rich" },
        new[] { "Kupfer_01_small", "Kupfer_02_small", "Kupfer_03_medium", "Kupfer_04_medium",
            "Kupfer_05_rich", "Kupfer_06_rich", "Kupfer_07_rich" });

    // The supplied "gold 6 m" artwork is rich, as confirmed by the artist.
    public static string InstallGold() => Install("Gold",
        new[] { "gold 1 s", "gold 2 s", "gold 3 m", "gold 4 m", "gold 5 l", "gold 6 m", "gold 7 l" },
        new[] { "Gold_01_small", "Gold_02_small", "Gold_03_medium", "Gold_04_medium",
            "Gold_05_rich", "Gold_06_rich", "Gold_07_rich" });

    public static string InstallSilver() => Install("Silver",
        new[] { "Silber_01_Small", "Silber_02_Small", "Silber_03_Medium", "Silber_04_Medium",
            "Silber_05_Rich", "Silber_06_Rich", "Silber_07_Rich" },
        new[] { "Silber_01_small", "Silber_02_small", "Silber_03_medium", "Silber_04_medium",
            "Silber_05_rich", "Silber_06_rich", "Silber_07_rich" });

    public static string InstallIron()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install iron in edit mode.");
        string result = Install("Iron",
            new[] { "Eisen_01_Small", "Eisen_02_Small", "Eisen_03_Medium", "Eisen_04_Medium",
                "Eisen_05_Rich", "Eisen_06_Rich", "Eisen_07_Rich" },
            new[] { "Eisen_01_small", "Eisen_02_small", "Eisen_03_medium", "Eisen_04_medium",
                "Eisen_05_rich", "Eisen_06_rich", "Eisen_07_rich" });
        var block = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Iron.asset");
        block.itemDrop.icon = block.mediumOre[0].sprite;
        EditorUtility.SetDirty(block.itemDrop);
        AssetDatabase.SaveAssetIfDirty(block.itemDrop);
        return result;
    }

    public static string InstallCoal()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install coal in edit mode.");
        var stone = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Stone.asset");
        var block = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Coal.asset");
        if (!block)
        {
            block = ScriptableObject.CreateInstance<Block>();
            block.id = BlockType.Coal;
            block.displayName = "Kohle";
            block.hardness = stone.hardness;
            block.points = stone.points;
            block.digSound = SoundType.DigOre;
            block.breakSound = SoundType.BreakOre;
            block.spawnWithNoise = true;
            block.oreFrequencyPercent = 5f;
            block.veinSizeIndex = 10;
            block.noiseSeedOffset = 5000;
            block.variants = Array.Empty<TileBase>();
            block.MigrateGenerationSettings();
            AssetDatabase.CreateAsset(block, Blocks + "/Coal.asset");
        }
        const string itemPath = "Assets/GameObjects/Items/Ores/Coal.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, itemPath);
        }
        item.item = Item.Coal;
        item.category = ItemCategory.Ore;
        item.displayName = "Kohle";
        item.worth = 3;
        block.itemDrop = item;
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        if (Array.IndexOf(registry.blocks, block) < 0)
        {
            var serialized = new SerializedObject(registry);
            var entries = serialized.FindProperty("blocks");
            int index = entries.arraySize++;
            entries.GetArrayElementAtIndex(index).objectReferenceValue = block;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(registry);
        }
        string result = Install("Coal",
            new[] { "Kohle_01_Small", "Kohle_02_Small", "Kohle_03_Medium", "Kohle_04_Medium",
                "Kohle_05_Rich", "Kohle_06_Rich", "Kohle_07_Rich" },
            new[] { "Kohle_01_small", "Kohle_02_small", "Kohle_03_medium", "Kohle_04_medium",
                "Kohle_05_rich", "Kohle_06_rich", "Kohle_07_rich" });
        item.icon = block.mediumOre[0].sprite;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssetIfDirty(item);
        int added = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
            added += MigratePreview(map, block);
        return result + "; new coal preview cells: " + added;
    }

    public static string InstallPlatinum()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install platinum in edit mode.");
        var gold = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Gold.asset");
        var block = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Platinum.asset");
        if (!block)
        {
            block = UnityEngine.Object.Instantiate(gold);
            block.name = "Platinum";
            block.id = BlockType.PlatinumOre;
            block.displayName = "Platin";
            block.hardness = gold.hardness * 1.5f;
            block.noiseSeedOffset = 4000;
            block.variants = Array.Empty<TileBase>();
            block.smallOre = block.mediumOre = block.richOre = Array.Empty<OreTile>();
            block.itemDrop = null;
            AssetDatabase.CreateAsset(block, Blocks + "/Platinum.asset");
        }
        const string itemPath = "Assets/GameObjects/Items/Ores/Platinum.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            item.item = Item.Platinum;
            item.category = ItemCategory.Ore;
            item.displayName = "Platin";
            item.worth = checked(gold.itemDrop.worth * 2);
            AssetDatabase.CreateAsset(item, itemPath);
        }
        block.itemDrop = item;
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        if (Array.IndexOf(registry.blocks, block) < 0)
        {
            // Append so existing ores retain their generation priority and serialized IDs.
            var serialized = new SerializedObject(registry);
            var entries = serialized.FindProperty("blocks");
            int index = entries.arraySize++;
            entries.GetArrayElementAtIndex(index).objectReferenceValue = block;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(registry);
        }
        string result = Install("Platinum",
            new[] { "Platin_01_Small", "Platin_02_Small", "Platin_03_Medium", "Platin_04_Medium",
                "Platin_05_Rich", "Platin_06_Rich", "Platin_07_Rich" },
            new[] { "Platin_01_small", "Platin_02_small", "Platin_03_medium", "Platin_04_medium",
                "Platin_05_rich", "Platin_06_rich", "Platin_07_rich" });
        item.icon = block.mediumOre[0].sprite;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssetIfDirty(item);
        int added = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
            added += MigratePreview(map, block);
        return result + "; new platinum preview cells: " + added;
    }

    // Explicit one-time migration; never runs automatically on import or play-mode changes.
    static string Install(string oreName, string[] sources, string[] names)
    {
        string Sprites = Blocks + "/Sprites/Ores/" + oreName;
        string Tiles = Blocks + "/Ores/" + oreName;
        Folder(Sprites); Folder(Tiles);
        var block = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/" + oreName + ".asset");
        var groups = new[] { new List<OreTile>(), new List<OreTile>(), new List<OreTile>() };
        for (int i = 0; i < names.Length; i++)
        {
            string path = Sprites + "/" + names[i] + ".png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(path))
            {
                string source = "Assets/ZZZ New Assets/" + sources[i] + ".png";
                string guid = AssetDatabase.AssetPathToGUID(source);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Missing source: " + source);
                string error = AssetDatabase.MoveAsset(source, path);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                if (AssetDatabase.AssetPathToGUID(path) != guid) throw new InvalidOperationException("Asset GUID changed.");
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new InvalidOperationException("Ore overlays must use square canvases.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = width / .5f;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "WebGL" })
            {
                var p = importer.GetPlatformTextureSettings(platform);
                p.maxTextureSize = 2048;
                p.textureCompression = TextureImporterCompression.Uncompressed;
                p.crunchedCompression = false;
                p.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(p);
            }
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite || Mathf.Abs(sprite.bounds.size.x - .5f) > .001f)
                throw new InvalidOperationException("Incorrect ore sprite size: " + path);
            string tilePath = Tiles + "/" + names[i] + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<OreTile>(tilePath);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<OreTile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = sprite;
            tile.block = block;
            tile.richness = i < 2 ? OreRichness.Small : i < 4 ? OreRichness.Medium : OreRichness.Rich;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.None;
            tile.color = Color.white;
            // A full rectangular sprite clips every fragment to the common half-unit canvas.
            tile.transform = Matrix4x4.identity;
            groups[(int)tile.richness].Add(tile);
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssetIfDirty(tile);
        }
        Undo.RecordObject(block, "Configure " + oreName + " overlays");
        block.smallOre = groups[0].ToArray();
        block.mediumOre = groups[1].ToArray();
        block.richOre = groups[2].ToArray();
        EditorUtility.SetDirty(block);
        AssetDatabase.SaveAssetIfDirty(block);
        int migrated = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(map, "Configure ore layer");
            map.EnsureOreOverlay();
            migrated += MigratePreview(map);
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
        return oreName + " overlays: 2 small, 2 medium, 3 rich; migrated preview cells: " + migrated;
    }

    static int MigratePreview(MapGenerator map, Block newOre = null)
    {
        int width = map.GeneratedWidth, height = map.GeneratedHeight;
        if (!map.registry || width <= 0 || height <= 0) return 0;
        var blocks = new Block[checked(width * height)];
        int left = -width / 2;
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, height, map.layers,
            map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
            map.useOreSettings ? map.oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var current = map.GetBlockAt(new Vector3Int(left + x, -y, 0));
                // Populate the new ore only in intact stone or dirt, preserving mined holes and other ores.
                blocks[y * width + x] = newOre && current && (current.IsStone || current.id == BlockType.Dirt) && sampler.GetBlock(x, y) == newOre
                    ? newOre : current;
            }
        if (newOre)
            OreVeins.PruneSmallVeins(blocks, width, height, map.minimumOreVeinSize,
                sampler.GetBaseBlock, newOre);
        var richness = OreVeins.Build(blocks, width, height, map.ActiveSeed);
        int changed = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var block = blocks[y * width + x];
                var cell = new Vector3Int(left + x, -y, 0);
                if (!block || !block.HasOreOverlays || map.GetOreAt(cell)) continue;
                var variants = block.GetOreVariants(richness[y * width + x]);
                var ore = variants[OreVeins.Hash(map.ActiveSeed, x, y, 0x5678u) % (uint)variants.Length];
                var currentStone = map.registry.FromTile(map.Terrain.GetTile(cell));
                var stone = currentStone && (currentStone.IsStone || currentStone.id == BlockType.Dirt) ? currentStone : sampler.GetBaseBlock(x, y);
                map.Terrain.SetTile(cell, stone.variants[OreVeins.Hash(map.ActiveSeed, x, y, 0x1234u) % (uint)stone.variants.Length]);
                map.OreOverlay.SetTile(cell, ore);
                map.OreOverlay.SetTileFlags(cell, TileFlags.None);
                map.OreOverlay.SetTransformMatrix(cell, ore.transform);
                changed++;
            }
        EditorUtility.SetDirty(map.Terrain);
        EditorUtility.SetDirty(map.OreOverlay);
        return changed;
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
