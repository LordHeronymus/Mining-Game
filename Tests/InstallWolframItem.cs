#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class InstallWolframItem
{
    public static string Run()
    {
        const string iconPath = "Assets/GameObjects/Items/Sprites/Ores/Wolfram_ItemIcon.png";
        const string itemPath = "Assets/GameObjects/Items/Ores/Wolfram.asset";
        var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Wolfram icon image is missing.");
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
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
        importer.SaveAndReimport();

        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        if (!icon) throw new InvalidOperationException("Wolfram icon did not import as one sprite.");
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, itemPath);
        }
        item.item = Item.Tungsten;
        item.category = ItemCategory.Ore;
        item.displayName = "Wolfram";
        item.icon = icon;
        item.themeColor = new Color32(92, 91, 87, 255);
        item.worth = 0;
        item.weight = 1f;
        EditorUtility.SetDirty(item);

        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        return $"Created {item.displayName} ({item.item}), icon {width}x{height}, worth {item.worth}";
    }
}
#endif
