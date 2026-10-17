using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupGrassSurface
{
    const string Source = "Assets/ZZZ New Assets";
    const string Sprites = "Assets/GameObjects/Map/Blocks/Sprites/Grass";
    const string Tiles = "Assets/GameObjects/Map/Blocks/Grass";

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "NEEDS_EDIT_MODE";
        Folder(Sprites);
        Folder(Tiles);
        var variants = new TileBase[4];
        for (int i = 0; i < variants.Length; i++)
        {
            string name = "Grass_Overlay_" + (i + 1).ToString("00");
            string source = Source + "/" + name + ".png";
            string destination = Sprites + "/" + name + ".png";
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(destination))
            {
                string guid = AssetDatabase.AssetPathToGUID(source);
                if (string.IsNullOrEmpty(guid)) throw new Exception("Missing grass sprite: " + source);
                string error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                if (AssetDatabase.AssetPathToGUID(destination) != guid)
                    throw new Exception("Sprite GUID changed: " + name);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != height) throw new Exception("Grass sprite is not square: " + name);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = width / .5f;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            string tilePath = Tiles + "/" + name + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (!tile)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destination);
            tile.colliderType = Tile.ColliderType.None;
            tile.color = Color.white;
            tile.flags = TileFlags.LockColor;
            tile.transform = Matrix4x4.identity;
            EditorUtility.SetDirty(tile);
            variants[i] = tile;
        }

        int maps = 0;
        foreach (var map in UnityEngine.Object.FindObjectsByType<MapGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!map.gameObject.scene.IsValid()) continue;
            map.SetGrassVariants(variants);
            var overlay = map.EnsureGrassOverlay();
            map.SyncGrassFromTerrain();
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(overlay);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if (!EditorSceneManager.SaveScene(map.gameObject.scene)) throw new Exception("Could not save map scene");
            maps++;
        }
        AssetDatabase.SaveAssets();
        return new { success = true, variants = variants.Length, maps };
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Folder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
