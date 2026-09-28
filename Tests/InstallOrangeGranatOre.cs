#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallOrangeGranatOre
{
    const string Blocks = "Assets/GameObjects/Map/Blocks";
    const string Source = "Assets/ZZZ New Assets/OrangeGranat";
    const string Sprites = Blocks + "/Sprites/Ores/Faceted/OrangeGranat";
    const string Tiles = Blocks + "/Ores/OrangeGranat";
    const string ItemPath = "Assets/GameObjects/Items/Ores/OrangeGranat.asset";

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        Folder(Sprites);
        Folder(Tiles);
        Folder("Assets/GameObjects/Items/Ores");

        string[] names =
        {
            "OrangeGranat_01_rich", "OrangeGranat_02_rich", "OrangeGranat_03_rich",
            "OrangeGranat_04_medium", "OrangeGranat_05_medium",
            "OrangeGranat_06_small", "OrangeGranat_07_small"
        };
        foreach (string name in names)
        {
            string from = Source + "/" + name + ".png";
            string to = Sprites + "/" + name + ".png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(to))
            {
                if (!AssetDatabase.LoadAssetAtPath<Texture2D>(from))
                    throw new InvalidOperationException("Missing Orange Granat sprite: " + from);
                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var gold = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Gold.asset");
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        if (!gold || !registry) throw new InvalidOperationException("Gold or BlockRegistry asset is missing.");

        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(ItemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, ItemPath);
        }

        string blockPath = Blocks + "/OrangeGranat.asset";
        var block = AssetDatabase.LoadAssetAtPath<Block>(blockPath);
        if (!block)
        {
            block = ScriptableObject.CreateInstance<Block>();
            AssetDatabase.CreateAsset(block, blockPath);
        }
        block.id = BlockType.OrangeGarnetOre;
        block.displayName = "Orange Granat";
        block.itemDrop = item;
        block.digSound = gold.digSound;
        block.breakSound = gold.breakSound;
        block.hardness = 1.5f;
        block.hardnessIndex = 3f;
        block.isSolid = true;
        block.variants = Array.Empty<TileBase>();
        block.spawnWithNoise = true;
        block.oreFrequencyPercent = 1f;
        block.veinSizeIndex = 8;
        block.noiseSeedOffset = 9000;

        var groups = new[] { new List<OreTile>(), new List<OreTile>(), new List<OreTile>() };
        for (int i = 0; i < names.Length; i++)
        {
            string spritePath = Sprites + "/" + names[i] + ".png";
            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing Orange Granat sprite: " + spritePath);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new InvalidOperationException("Orange Granat sprite is not square: " + spritePath);
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
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Center;
            textureSettings.spritePivot = new Vector2(.5f, .5f);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            textureSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(textureSettings);
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize = 2048;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (!sprite) throw new InvalidOperationException("Sprite import failed: " + spritePath);
            if (i == 0)
            {
                item.item = Item.OrangeGarnet;
                item.category = ItemCategory.Ore;
                item.displayName = "Orange Granat";
                item.icon = sprite;
                item.themeColor = new Color32(255, 112, 24, 255);
                item.worth = 0;
                item.weight = 1f;
                EditorUtility.SetDirty(item);
            }

            string tilePath = Tiles + "/" + names[i] + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<OreTile>(tilePath);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<OreTile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = sprite;
            tile.block = block;
            tile.richness = i < 3 ? OreRichness.Rich : i < 5 ? OreRichness.Medium : OreRichness.Small;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.None;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            groups[(int)tile.richness].Add(tile);
            EditorUtility.SetDirty(tile);
        }

        block.smallOre = groups[(int)OreRichness.Small].ToArray();
        block.mediumOre = groups[(int)OreRichness.Medium].ToArray();
        block.richOre = groups[(int)OreRichness.Rich].ToArray();
        EditorUtility.SetDirty(block);

        if (registry.blocks == null) registry.blocks = Array.Empty<Block>();
        if (!registry.blocks.Contains(block))
        {
            Array.Resize(ref registry.blocks, registry.blocks.Length + 1);
            registry.blocks[registry.blocks.Length - 1] = block;
        }
        typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(registry, null);
        EditorUtility.SetDirty(registry);

        int configuredMaps = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            var settings = (map.oreSettings ?? Array.Empty<OreDistributionSetting>()).ToList();
            if (!settings.Any(entry => entry != null && entry.ore == BlockType.OrangeGarnetOre))
            {
                settings.Add(new OreDistributionSetting
                {
                    ore = BlockType.OrangeGarnetOre,
                    layerIndices = Array.Empty<int>(),
                    baseWeight = 1f,
                    weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    baseVeinSize = 8f,
                    veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
                });
            }
            map.oreSettings = settings.ToArray();
            map.useOreSettings = true;
            int paletteIndex = (int)BlockType.OrangeGarnetOre;
            if (map.mapOverviewColors == null || map.mapOverviewColors.Length <= paletteIndex)
            {
                Array.Resize(ref map.mapOverviewColors, paletteIndex + 1);
                map.mapOverviewColors[paletteIndex] = new Color32(255, 112, 24, 255);
            }
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            configuredMaps++;
        }

        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        return new { block = blockPath, item = ItemPath, spriteCount = names.Length,
            small = block.smallOre.Length, medium = block.mediumOre.Length, rich = block.richOre.Length,
            settingsAdded = configuredMaps, activeLayers = 0, worth = item.worth };
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        Folder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
#endif
