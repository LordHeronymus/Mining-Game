using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallGrassHangV2
{
    const string Folder = "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet";
    const string KeyPath = "Assets/Design/GrassHangV2Key.png";
    const string SpritePath = Folder + "/GrassHangV2.png";

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map) throw new Exception("MapGenerator missing");
        const string backup = "Assets/_SceneBackups/BeforeGrassHangV2_20260926.unity";
        if (!System.IO.File.Exists(backup)) EditorSceneManager.SaveScene(map.gameObject.scene, backup, true);
        AssetDatabase.ImportAsset("Assets/Design/GrassHangV2.prompts.txt");

        var key = ImportTexture(KeyPath);
        var pixels = key.GetPixels32();
        var cutout = new Color32[pixels.Length];
        int minX = key.width, minY = key.height, maxX = -1, maxY = -1;
        int opaque = 0;
        for (int y = 0; y < key.height; y++)
            for (int x = 0; x < key.width; x++)
            {
                int index = y * key.width + x;
                var pixel = pixels[index];
                float distance = Mathf.Max(255f - pixel.r, pixel.g, 255f - pixel.b);
                float alpha = Mathf.Clamp01((distance - 38f) / 145f);
                if (alpha < .06f) alpha = 0f;
                if (alpha > .98f) alpha = 1f;
                if (alpha <= 0f) continue;
                byte red = (byte)Mathf.Clamp(Mathf.RoundToInt((pixel.r - (1f - alpha) * 255f) / alpha), 0, 255);
                byte green = (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.g / alpha), 0, 255);
                byte blue = (byte)Mathf.Clamp(Mathf.RoundToInt((pixel.b - (1f - alpha) * 255f) / alpha), 0, 255);
                cutout[index] = new Color32(red, green, blue, (byte)Mathf.RoundToInt(alpha * 255f));
                if (alpha <= .5f) continue;
                opaque++;
                minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
            }
        if (opaque < 10000) throw new Exception("Hanging leaf cutout is empty");
        var output = new Texture2D(key.width, key.height, TextureFormat.RGBA32, false);
        output.SetPixels32(cutout);
        output.Apply();
        System.IO.File.WriteAllBytes(SpritePath, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(output);
        var texture = ImportTexture(SpritePath);
        var rect = Rect.MinMaxRect(Mathf.Max(0, minX - 8), Mathf.Max(0, minY - 8),
            Mathf.Min(key.width, maxX + 9), Mathf.Min(key.height, maxY + 9));
        string spriteAssetPath = Folder + "/GrassHangV2Sprite.asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteAssetPath);
        if (sprite && sprite.rect != rect)
        {
            AssetDatabase.DeleteAsset(spriteAssetPath);
            sprite = null;
        }
        if (!sprite)
        {
            sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), rect.width / .45f,
                0, SpriteMeshType.FullRect);
            sprite.name = "GrassHangV2Sprite";
            AssetDatabase.CreateAsset(sprite, spriteAssetPath);
        }
        var left = GetTile("GrassHangLeftTile", sprite, -.35f, 1f);
        var right = GetTile("GrassHangRightTile", sprite, .35f, -1f);
        map.SetGrassHangTiles(left, right);
        map.EnsureGrassHangOverlays();
        map.SyncGrassFromTerrain();
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(map.GrassHangLeftOverlay);
        EditorUtility.SetDirty(map.GrassHangRightOverlay);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return $"V2 grass hang installed: {opaque} opaque pixels, sprite {rect.width}×{rect.height}";
    }

    static Texture2D ImportTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new Exception("Texture missing: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.isReadable = true;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Tile GetTile(string name, Sprite sprite, float xOffset, float xScale)
    {
        string path = Folder + "/" + name + ".asset";
        var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (!tile)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, path);
        }
        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.None;
        tile.transform = Matrix4x4.TRS(new Vector3(xOffset, .09f, 0), Quaternion.identity,
            new Vector3(xScale, 1.35f, 1f));
        EditorUtility.SetDirty(tile);
        return tile;
    }
}
