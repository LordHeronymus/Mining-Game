#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class InstallDiamondItemIcon
{
    public static object Main()
    {
        const string iconPath = "Assets/GameObjects/Items/Sprites/Ores/Diamond_ItemIcon_Cut.png";
        const string itemPath = "Assets/GameObjects/Items/Ores/Diamond.asset";
        var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Diamond icon image is missing.");
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
        if (!icon) throw new InvalidOperationException("Diamond icon did not import as a sprite.");
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<ItemSO>();
            AssetDatabase.CreateAsset(item, itemPath);
        }
        item.item = Item.Diamond;
        item.category = ItemCategory.Ore;
        item.displayName = "Diamant";
        item.icon = icon;
        item.themeColor = new Color32(74, 190, 255, 255);
        item.worth = 0;
        item.weight = 1f;
        EditorUtility.SetDirty(item);
        ItemCatalogBuilder.Refresh();
        AssetDatabase.SaveAssets();
        return new { item = item.displayName, itemId = (int)item.item, icon = iconPath,
            transparent = importer.alphaIsTransparency, worth = item.worth };
    }
}
#endif
