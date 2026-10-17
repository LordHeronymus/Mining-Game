#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallWolframOre
{
    const string Blocks = "Assets/GameObjects/Map/Blocks";
    const string Sprites = Blocks + "/Sprites/Ores/Faceted/Wolfram";
    const string Tiles = Blocks + "/Ores/Wolfram";

    public static string Run()
    {
        Folder(Tiles);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var item = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Ores/Wolfram.asset");
        var platinum = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Platinum.asset");
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        if (!item || item.item != Item.Tungsten || !platinum || !registry)
            throw new InvalidOperationException("Wolfram item, Platinum reference or block registry is missing.");

        string blockPath = Blocks + "/Wolfram.asset";
        var block = AssetDatabase.LoadAssetAtPath<Block>(blockPath);
        if (!block)
        {
            block = ScriptableObject.CreateInstance<Block>();
            AssetDatabase.CreateAsset(block, blockPath);
        }
        block.id = BlockType.TungstenOre;
        block.displayName = "Wolfram";
        block.itemDrop = item;
        block.digSound = platinum.digSound;
        block.breakSound = platinum.breakSound;
        block.hardness = 3.8f;
        block.hardnessIndex = 6f;
        block.isSolid = true;
        block.variants = Array.Empty<TileBase>();
        block.spawnWithNoise = true;
        block.oreFrequencyPercent = .9f;
        block.veinSizeIndex = 9;
        block.noiseSeedOffset = 8000;

        var groups = new[] { new List<OreTile>(), new List<OreTile>(), new List<OreTile>() };
        string[] suffixes = { "01_small", "02_small", "03_medium", "04_medium",
            "05_rich", "06_rich", "07_rich" };
        for (int i = 0; i < suffixes.Length; i++)
        {
            string name = "Wolfram_" + suffixes[i];
            string spritePath = Sprites + "/" + name + ".png";
            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing Wolfram sprite: " + spritePath);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new InvalidOperationException("Wolfram sprite is not square: " + spritePath);
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
            if (!sprite) throw new InvalidOperationException("Sprite import failed: " + spritePath);
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
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            groups[(int)tile.richness].Add(tile);
            EditorUtility.SetDirty(tile);
        }
        block.smallOre = groups[0].ToArray();
        block.mediumOre = groups[1].ToArray();
        block.richOre = groups[2].ToArray();
        EditorUtility.SetDirty(block);

        if (registry.blocks == null) registry.blocks = Array.Empty<Block>();
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
            if (map.layers == null || map.layers.Length == 0) continue;
            int[] deepLayers = Enumerable.Range(Math.Max(0, map.layers.Length - 2),
                Math.Min(2, map.layers.Length)).ToArray();
            var list = (map.oreSettings ?? Array.Empty<OreDistributionSetting>()).ToList();
            var setting = list.FirstOrDefault(entry => entry != null && entry.ore == BlockType.TungstenOre);
            if (setting == null)
            {
                setting = new OreDistributionSetting
                {
                    ore = BlockType.TungstenOre,
                    layerIndices = deepLayers,
                    baseWeight = .9f,
                    weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    baseVeinSize = 9f,
                    veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
                };
                list.Add(setting);
            }
            map.oreSettings = list.ToArray();
            map.useOreSettings = true;
            foreach (int layerIndex in deepLayers)
            {
                var layer = map.layers[layerIndex];
                var ores = (layer.ores ?? Array.Empty<BlockType>()).ToList();
                if (!ores.Contains(BlockType.TungstenOre))
                {
                    ores.Add(BlockType.TungstenOre);
                    layer.ores = ores.ToArray();
                }
            }
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            maps++;
        }
        AssetDatabase.SaveAssets();
        return $"Wolfram installed: 2 small, 2 medium, 3 rich; configured maps: {maps}";
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
