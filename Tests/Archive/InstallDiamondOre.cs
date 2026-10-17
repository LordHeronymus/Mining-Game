#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallDiamondOre
{
    const string Blocks = "Assets/GameObjects/Map/Blocks";
    const string Inbox = "Assets/ZZZ New Assets/Diamond";
    const string Sprites = Blocks + "/Sprites/Ores/Faceted/Diamond";
    const string Tiles = Blocks + "/Ores/Diamond";
    const string ItemPath = "Assets/GameObjects/Items/Ores/Diamond.asset";

    static readonly string[] Names =
    {
        "Diamond_01_rich", "Diamond_02_rich", "Diamond_03_rich",
        "Diamond_04_medium", "Diamond_05_medium",
        "Diamond_06_small", "Diamond_07_small"
    };

    static readonly string[] Sources =
    {
        "C:/Users/jlang/AppData/Local/Temp/codex-clipboard-c6c5fabc-42cf-4064-a916-c27d2515f7c2.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-9f067866-475f-44a0-935f-e2215fb82ad5.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-8f3dec2b-1e93-42c5-8b5a-04dcd151e1c0.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-e1c5b095-24e2-445b-a84a-332ee02d2e16.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-2c3a3fc9-d9f9-45d7-8f50-e6c07cfa700a.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-152e4c37-3105-46bc-910b-653e437b416c.png",
        "C:/Users/jlang/.codex/generated_images/01a0bab8-21d1-75f0-bcd0-39dce70ebd99/exec-50e6b9ec-fd68-4655-9315-7417b3f24146.png"
    };

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install Diamond in Edit Mode.");

        Folder(Inbox);
        Folder(Sprites);
        Folder(Tiles);

        for (int i = 0; i < Names.Length; i++)
        {
            if (!File.Exists(Sources[i])) throw new FileNotFoundException("Missing Diamond source image", Sources[i]);
            string staged = Path.GetFullPath(Inbox + "/" + Names[i] + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(staged));
            File.Copy(Sources[i], staged, true);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var stoneOre = AssetDatabase.LoadAssetAtPath<Block>(Blocks + "/Gold.asset");
        var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(Blocks + "/BlockRegistry.asset");
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(ItemPath);
        if (!stoneOre || !registry || !item)
            throw new InvalidOperationException("Gold, BlockRegistry, or the existing Diamond item asset is missing.");

        string blockPath = Blocks + "/Diamond.asset";
        var block = AssetDatabase.LoadAssetAtPath<Block>(blockPath);
        if (!block)
        {
            block = ScriptableObject.CreateInstance<Block>();
            AssetDatabase.CreateAsset(block, blockPath);
        }
        block.name = "Diamond";
        block.id = BlockType.DiamondOre;
        block.displayName = "Diamant";
        block.itemDrop = item;
        block.digSound = stoneOre.digSound;
        block.breakSound = stoneOre.breakSound;
        block.hardness = 3.5f;
        block.hardnessIndex = 4f;
        block.isSolid = true;
        block.variants = Array.Empty<TileBase>();
        block.spawnWithNoise = true;
        block.oreFrequencyPercent = 1f;
        block.veinSizeIndex = 8;
        block.noiseSeedOffset = 10000;
        block.MigrateGenerationSettings();

        item.item = Item.Diamond;
        item.category = ItemCategory.Ore;
        item.displayName = "Diamant";
        // Keep the existing cut-diamond inventory icon and economy values.
        EditorUtility.SetDirty(item);

        var groups = new[] { new List<OreTile>(), new List<OreTile>(), new List<OreTile>() };
        int commonWidth = -1;
        int commonHeight = -1;
        for (int i = 0; i < Names.Length; i++)
        {
            string stagedPath = Inbox + "/" + Names[i] + ".png";
            string spritePath = Sprites + "/" + Names[i] + ".png";
            string error;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath))
            {
                File.Copy(Path.GetFullPath(stagedPath), Path.GetFullPath(spritePath), true);
                AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.DeleteAsset(stagedPath);
            }
            else
            {
                error = AssetDatabase.MoveAsset(stagedPath, spritePath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }

            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing Diamond texture importer: " + spritePath);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new InvalidOperationException("Diamond sprite must use a square canvas: " + spritePath);
            if (commonWidth < 0) { commonWidth = width; commonHeight = height; }
            if (width != commonWidth || height != commonHeight)
                throw new InvalidOperationException("All Diamond sprites must use the same canvas size.");

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
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "WebGL" })
            {
                var platformSettings = importer.GetPlatformTextureSettings(platform);
                platformSettings.maxTextureSize = 2048;
                platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                platformSettings.crunchedCompression = false;
                platformSettings.format = TextureImporterFormat.RGBA32;
                importer.SetPlatformTextureSettings(platformSettings);
            }
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (!sprite || Mathf.Abs(sprite.bounds.size.x - .5f) > .001f || Mathf.Abs(sprite.bounds.size.y - .5f) > .001f)
                throw new InvalidOperationException("Diamond sprite import size or pivot is invalid: " + spritePath);

            string tilePath = Tiles + "/" + Names[i] + ".asset";
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
        int migratedPreviewCells = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            if (!map.registry || map.layers == null || map.layers.Length == 0) continue;
            var settings = (map.oreSettings ?? Array.Empty<OreDistributionSetting>()).ToList();
            var setting = settings.FirstOrDefault(entry => entry != null && entry.ore == BlockType.DiamondOre);
            if (setting == null)
            {
                int[] eligibleLayers = Enumerable.Range(0, map.layers.Length)
                    .Where(index => map.layers[index] != null && map.layers[index].ores != null &&
                        Array.IndexOf(map.layers[index].ores, BlockType.DiamondOre) >= 0).ToArray();
                if (eligibleLayers.Length == 0) eligibleLayers = new[] { Mathf.Min(3, map.layers.Length - 1) };
                setting = new OreDistributionSetting
                {
                    ore = BlockType.DiamondOre,
                    layerIndices = eligibleLayers,
                    baseWeight = 1f,
                    weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    baseVeinSize = 8f,
                    veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    minimumVeinSize = 4
                };
                settings.Add(setting);
            }
            else
            {
                if (setting.layerIndices == null || setting.layerIndices.Length == 0)
                    setting.layerIndices = new[] { Mathf.Min(3, map.layers.Length - 1) };
                if (setting.weightCurve == null) setting.weightCurve = AnimationCurve.Constant(0f, 1f, 1f);
                if (setting.veinSizeCurve == null) setting.veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f);
            }

            foreach (int layerIndex in setting.layerIndices)
            {
                if (layerIndex < 0 || layerIndex >= map.layers.Length || map.layers[layerIndex] == null) continue;
                var ores = (map.layers[layerIndex].ores ?? Array.Empty<BlockType>()).ToList();
                if (!ores.Contains(BlockType.DiamondOre))
                {
                    ores.Add(BlockType.DiamondOre);
                    map.layers[layerIndex].ores = ores.ToArray();
                }
            }
            map.oreSettings = settings.ToArray();
            map.useOreSettings = true;
            Undo.RecordObject(map, "Install Diamond ore");
            map.EnsureOreOverlay();
            migratedPreviewCells += MigratePreview(map, block);
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            configuredMaps++;
        }

        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
            if (map && !string.IsNullOrEmpty(map.gameObject.scene.path))
                EditorSceneManager.SaveScene(map.gameObject.scene);

        if (block.smallOre.Length != 2 || block.mediumOre.Length != 2 || block.richOre.Length != 3)
            throw new InvalidOperationException("Diamond richness variants were not assigned 2/2/3.");
        if (registry.GetById(BlockType.DiamondOre) != block)
            throw new InvalidOperationException("Diamond block was not registered.");

        return new
        {
            block = blockPath,
            sprites = Names.Length,
            small = block.smallOre.Length,
            medium = block.mediumOre.Length,
            rich = block.richOre.Length,
            configuredMaps,
            migratedPreviewCells,
            defaultLayers = string.Join(",", settingsLayerNames(block, configuredMaps))
        };
    }

    public static object ResetPreviewTransforms()
    {
        int resetCells = 0;
        var maps = UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None);
        foreach (var map in maps)
        {
            if (!map || !map.OreOverlay) continue;
            var overlay = map.OreOverlay;
            foreach (var cell in overlay.cellBounds.allPositionsWithin)
            {
                if (!overlay.GetTile<OreTile>(cell)) continue;
                overlay.SetTileFlags(cell, TileFlags.None);
                var ore = overlay.GetTile<OreTile>(cell);
                overlay.SetTransformMatrix(cell, ore.transform);
                resetCells++;
            }
            EditorUtility.SetDirty(overlay);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
        foreach (var map in maps)
            if (map && !string.IsNullOrEmpty(map.gameObject.scene.path))
                EditorSceneManager.SaveScene(map.gameObject.scene);
        return new { maps = maps.Length, resetCells };
    }

    static int MigratePreview(MapGenerator map, Block newOre)
    {
        int width = map.GeneratedWidth, height = map.GeneratedHeight;
        if (!map.registry || width <= 0 || height <= 0) return 0;
        var blocks = new Block[checked(width * height)];
        var generated = new Block[blocks.Length];
        int left = -width / 2;
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, height, map.layers,
            map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
            map.useOreSettings ? map.oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector3Int(left + x, -y, 0);
                var current = map.GetBlockAt(cell);
                int index = y * width + x;
                blocks[index] = current;
                if (current && !map.IsCellProtected(cell) && (current.IsStone || current.id == BlockType.Dirt))
                    generated[index] = sampler.GetBlock(x, y);
            }

        ConnectedOreVeins.Generate(generated, width, height, map.ActiveSeed, sampler, map.GetMinimumVeinSize,
            (x, y) => map.IsCellProtected(new Vector3Int(left + x, -y, 0)));
        for (int i = 0; i < blocks.Length; i++)
            if (generated[i] == newOre && blocks[i] && (blocks[i].IsStone || blocks[i].id == BlockType.Dirt))
                blocks[i] = newOre;

        var richness = OreVeins.Build(blocks, width, height, map.ActiveSeed);
        int changed = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var currentBlock = blocks[y * width + x];
                var cell = new Vector3Int(left + x, -y, 0);
                if (currentBlock != newOre || map.GetOreAt(cell)) continue;
                var variants = newOre.GetOreVariants(richness[y * width + x]);
                if (variants == null || variants.Length == 0) continue;
                var tile = variants[OreVeins.Hash(map.ActiveSeed, x, y, 0x5678u) % (uint)variants.Length];
                var stone = map.registry.FromTile(map.Terrain.GetTile(cell));
                if (!stone || (!stone.IsStone && stone.id != BlockType.Dirt)) continue;
                var stoneVariants = stone.variants;
                if (stoneVariants == null || stoneVariants.Length == 0) continue;
                map.Terrain.SetTile(cell, stoneVariants[OreVeins.Hash(map.ActiveSeed, x, y, 0x1234u) % (uint)stoneVariants.Length]);
                map.OreOverlay.SetTile(cell, tile);
                map.OreOverlay.SetTileFlags(cell, TileFlags.None);
                map.OreOverlay.SetTransformMatrix(cell, tile.transform);
                changed++;
            }
        EditorUtility.SetDirty(map.Terrain);
        EditorUtility.SetDirty(map.OreOverlay);
        return changed;
    }

    static string[] settingsLayerNames(Block block, int configuredMaps)
    {
        var result = new List<string>();
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            if (!map || !map.registry || map.registry.GetById(block.id) != block) continue;
            var setting = (map.oreSettings ?? Array.Empty<OreDistributionSetting>())
                .FirstOrDefault(entry => entry != null && entry.ore == block.id);
            if (setting == null || map.layers == null) continue;
            result.AddRange(setting.layerIndices.Where(i => i >= 0 && i < map.layers.Length && map.layers[i] != null)
                .Select(i => map.layers[i].name));
        }
        return result.Distinct().ToArray();
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
