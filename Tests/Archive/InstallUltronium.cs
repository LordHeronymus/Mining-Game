#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallUltronium
{
    const string Blocks = "Assets/GameObjects/Map/Blocks";
    const string Sprites = Blocks + "/Sprites/Ores/Faceted/Ultronium";
    const string Tiles = Blocks + "/Ores/Ultronium";
    const string ItemPath = "Assets/GameObjects/Items/Ores/Ultronium.asset";

    public static string Run()
    {
        Folder(Tiles);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var platinum = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Platinum.asset");
        if (!platinum) throw new InvalidOperationException("Platinum reference block is missing.");

        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(ItemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, ItemPath);
        }
        item.item = Item.Ultronium;
        item.category = ItemCategory.Ore;
        item.displayName = "Ultronium";
        item.themeColor = new Color32(159, 90, 255, 255);
        item.worth = 1000;

        var blockPath = Blocks + "/Ultronium.asset";
        var block = AssetDatabase.LoadAssetAtPath<Block>(blockPath);
        if (!block)
        {
            block = ScriptableObject.CreateInstance<Block>();
            AssetDatabase.CreateAsset(block, blockPath);
        }
        block.id = BlockType.UltroniumOre;
        block.displayName = "Ultronium";
        block.itemDrop = item;
        block.digSound = platinum.digSound;
        block.breakSound = platinum.breakSound;
        block.hardness = 4.5f;
        block.isSolid = true;
        block.variants = Array.Empty<TileBase>();
        block.spawnWithNoise = true;
        block.oreFrequencyPercent = .25f;
        block.veinSizeIndex = 12;
        block.noiseSeedOffset = 6000;

        var groups = new[] { new List<OreTile>(), new List<OreTile>(), new List<OreTile>() };
        string[] suffixes = { "01_small", "02_small", "03_medium", "04_medium", "05_rich", "06_rich", "07_rich" };
        for (int i = 0; i < suffixes.Length; i++)
        {
            string name = "Ultronium_" + suffixes[i];
            string spritePath = Sprites + "/" + name + ".png";
            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing Ultronium sprite: " + spritePath);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new InvalidOperationException("Ultronium sprites need square canvases.");
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
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.maxTextureSize = 2048;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            string tilePath = Tiles + "/" + name + ".asset";
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
            tile.color = new Color(1f, 1f, 1f, .98f);
            tile.transform = Matrix4x4.identity;
            groups[(int)tile.richness].Add(tile);
            EditorUtility.SetDirty(tile);
        }

        block.smallOre = groups[0].ToArray();
        block.mediumOre = groups[1].ToArray();
        block.richOre = groups[2].ToArray();
        item.icon = block.mediumOre[0].sprite;
        EditorUtility.SetDirty(block);
        EditorUtility.SetDirty(item);

        var overlayMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/GameObjects/Map/OreOverlayLit.mat");
        if (!overlayMaterial || !overlayMaterial.HasProperty("_UltroniumGlow"))
            throw new InvalidOperationException("Ultronium glow shader is missing.");
        overlayMaterial.SetFloat("_UltroniumGlow", 1.15f);
        overlayMaterial.SetColor("_UltroniumGlowColor", new Color(.24f, .12f, 1.2f, 1f));
        EditorUtility.SetDirty(overlayMaterial);

        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        if (!registry) throw new InvalidOperationException("Block registry is missing.");
        if (!registry.blocks.Contains(block))
        {
            Array.Resize(ref registry.blocks, registry.blocks.Length + 1);
            registry.blocks[^1] = block;
            EditorUtility.SetDirty(registry);
        }
        typeof(BlockRegistry).GetMethod("BuildIndex", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(registry, null);

        int maps = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            var list = (map.oreSettings ?? Array.Empty<OreDistributionSetting>()).ToList();
            var setting = list.FirstOrDefault(entry => entry != null && entry.ore == BlockType.UltroniumOre);
            if (setting == null)
            {
                setting = new OreDistributionSetting
                {
                    ore = BlockType.UltroniumOre,
                    layerIndices = new[] { Math.Max(0, map.layers.Length - 1) },
                    baseWeight = .25f,
                    weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    baseVeinSize = 12f,
                    veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
                };
                list.Add(setting);
            }
            else if (setting.layerIndices == null || setting.layerIndices.Length == 0)
                setting.layerIndices = new[] { Math.Max(0, map.layers.Length - 1) };
            map.oreSettings = list.ToArray();
            map.useOreSettings = true;
            if (map.layers != null && map.layers.Length > 0)
            {
                var deepest = map.layers[^1];
                var ores = (deepest.ores ?? Array.Empty<BlockType>()).ToList();
                if (!ores.Contains(BlockType.UltroniumOre))
                {
                    ores.Add(BlockType.UltroniumOre);
                    deepest.ores = ores.ToArray();
                }
            }
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            maps++;
        }

        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        return $"Ultronium installed: 2 small, 2 medium, 3 rich; configured maps: {maps}";
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
