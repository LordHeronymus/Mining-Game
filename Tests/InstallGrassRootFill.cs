using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallGrassRootFill
{
    const string Folder = "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet/";
    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        const string sourcePath = "Assets/Design/GrassRootFillSource.png";
        AssetDatabase.ImportAsset(sourcePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        var pixels = source.GetPixels32();
        int minX = source.width, minY = source.height, maxX = 0, maxY = 0;
        for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
                if (pixels[y * source.width + x].a > 96)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
        int width = maxX - minX + 1, height = maxY - minY + 1;
        var clean = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var pixel = pixels[(minY + y) * source.width + minX + x];
                pixel.a = (byte)Mathf.RoundToInt(Mathf.Clamp01((pixel.a - 26f) / 225f) * 255f);
                clean[y * width + x] = pixel;
            }
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        output.SetPixels32(clean); output.Apply();
        string path = Folder + "GrassRootFill.png";
        System.IO.File.WriteAllBytes(path, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(output);
        AssetDatabase.ImportAsset(path);
        importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "GrassRootFillSprite.asset");
        if (!sprite)
        {
            sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), width / .13f, 0, SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite, Folder + "GrassRootFillSprite.asset");
        }
        var left = Tile("GrassRootFillLeft", sprite, -.17f, 1f);
        var right = Tile("GrassRootFillRight", sprite, .17f, -1f);
        var fill = map.GetComponent<GrassRootFill>();
        if (!fill) fill = map.gameObject.AddComponent<GrassRootFill>();
        fill.Configure(map, left, right);
        EditorUtility.SetDirty(fill);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return "Small additive grass root leaves installed";
    }
    static Tile Tile(string name, Sprite sprite, float x, float flip)
    {
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(Folder + name + ".asset");
        if (!tile)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, Folder + name + ".asset");
        }
        tile.sprite = sprite;
        tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
        tile.transform = Matrix4x4.TRS(new Vector3(x, .25f, 0), Quaternion.identity, new Vector3(flip, 1, 1));
        EditorUtility.SetDirty(tile);
        return tile;
    }
}
