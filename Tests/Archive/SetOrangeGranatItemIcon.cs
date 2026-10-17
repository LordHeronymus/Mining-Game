#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class SetOrangeGranatItemIcon
{
    public static object Main()
    {
        const string iconPath = "Assets/GameObjects/Items/Sprites/Ores/OrangeGranat_ItemIcon_Cut.png";
        const string itemPath = "Assets/GameObjects/Items/Ores/OrangeGranat.asset";
        var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        var item = AssetDatabase.LoadAssetAtPath<ItemSO>(itemPath);
        if (!importer || !item || item.item != Item.OrangeGarnet)
            throw new InvalidOperationException("Orange Granat icon or item asset is missing.");

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
        if (!icon) throw new InvalidOperationException("Orange Granat cut-gem icon did not import as a sprite.");
        item.icon = icon;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        return new { item = item.displayName, icon = iconPath, transparentCorners = true };
    }
}
#endif
