using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupFiveLayers
{
    const string BackgroundPath = "Assets/AB Sprites/Parralax BG/Untergrund_04_Tiefstein2.png";
    const string BlockPath = "Assets/GameObjects/Map/Blocks/LayerStones/Stone_Layer4.asset";
    const string UniformTilePath = "Assets/GameObjects/Map/StoneTest/Layer3/DeepStone.asset";
    const string UniformTexturePath = "Assets/GameObjects/Map/StoneTest/Layer3/DeepStone.png";

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map) throw new Exception("MapGenerator fehlt.");
        var block = AssetDatabase.LoadAssetAtPath<Block>(BlockPath);
        var tile = AssetDatabase.LoadAssetAtPath<StoneTestTile>(UniformTilePath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(UniformTexturePath);
        if (!block || block.id != BlockType.StoneLayer4 || !tile || !texture)
            throw new Exception("Tiefgestein 2 ist unvollständig.");
        var terrainImporter = AssetImporter.GetAtPath(UniformTexturePath) as TextureImporter;
        if (!terrainImporter) throw new Exception("Tiefgestein-2-Texturimport fehlt.");
        terrainImporter.textureType = TextureImporterType.Sprite;
        terrainImporter.spriteImportMode = SpriteImportMode.Single;
        terrainImporter.spritePixelsPerUnit = texture.width / .5f;
        terrainImporter.mipmapEnabled = true;
        terrainImporter.wrapMode = TextureWrapMode.Repeat;
        terrainImporter.filterMode = FilterMode.Trilinear;
        terrainImporter.textureCompression = TextureImporterCompression.Uncompressed;
        terrainImporter.SaveAndReimport();
        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(UniformTexturePath);
        Undo.RecordObject(tile, "Tiefgestein-2-Block zuweisen");
        tile.block = block;
        tile.colliderType = Tile.ColliderType.Grid;
        EditorUtility.SetDirty(tile);

        AssetDatabase.ImportAsset(BackgroundPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
        if (!importer) throw new Exception("Layer-5-Hintergrund konnte nicht importiert werden.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = false;
        importer.wrapMode = TextureWrapMode.Mirror;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192;
        importer.SaveAndReimport();
        var background = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        if (!background) throw new Exception("Layer-5-Hintergrundsprite fehlt.");

        Undo.RecordObject(map, "Layer 5 hinzufügen");
        var layers = map.layers == null ? Array.Empty<MapLayer>() : map.layers;
        int existing = Array.FindIndex(layers, entry => entry != null && entry.stone == block);
        var layer = existing >= 0 ? layers[existing] : new MapLayer();
        layer.name = "Layer 5";
        layer.startDepth = 900;
        layer.transitionWidth = 30;
        layer.backgroundSprite = background;
        layer.stone = block;
        layer.ores = Array.Empty<BlockType>();
        if (existing < 0)
        {
            Array.Resize(ref layers, layers.Length + 1);
            layers[layers.Length - 1] = layer;
        }
        map.layers = layers.OrderBy(entry => entry.startDepth).ToArray();
        map.layerFourTile = tile;
        EditorUtility.SetDirty(map);

        var appearance = map.GetComponent<UniformStoneAppearance>();
        if (!appearance) throw new Exception("UniformStoneAppearance fehlt.");
        Undo.RecordObject(appearance, "Tiefgestein 2 zuweisen");
        appearance.layerFourTexture = texture;
        EditorUtility.SetDirty(appearance);

        var sprites = new[]
        {
            "TS2_01_Ruhig.png", "TS2_02_Verdichtet.png", "TS2_03_Geschichtet.png",
            "TS2_04_Feiner_Riss.png", "TS2_01_Ruhig.png", "TS2_02_Verdichtet.png"
        };
        for (int i = 0; i < block.variants.Length && i < sprites.Length; i++)
        {
            var variant = block.variants[i] as RuleTile;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/GameObjects/Map/Blocks/Sprites/DeepStone/Layer4/" + sprites[i]);
            if (!variant || !sprite) throw new Exception("Tiefgestein-2-Variante " + (i + 1) + " fehlt.");
            Undo.RecordObject(variant, "Tiefgestein-2-Sprite zuweisen");
            variant.m_DefaultSprite = sprite;
            variant.m_DefaultColliderType = Tile.ColliderType.Grid;
            EditorUtility.SetDirty(variant);
        }

        AssetDatabase.SaveAssets();
        map.GenerateMap();
        appearance.RefreshAppearance();
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return new
        {
            passed = true,
            layers = map.layers.Length,
            start = layer.startDepth,
            transition = layer.transitionWidth,
            stone = block.displayName,
            background = BackgroundPath
        };
    }
}
