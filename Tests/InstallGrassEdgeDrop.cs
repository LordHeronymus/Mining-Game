using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class InstallGrassEdgeDrop
{
    const string Folder = "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet";
    const string Source = "Assets/Design/GrassEdgeDropSource.png";
    const string TexturePath = Folder + "/GrassEdgeDrop.png";

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map) throw new Exception("MapGenerator missing");
        const string backup = "Assets/_SceneBackups/BeforeGrassEdgeDrop_20260926.unity";
        if (!System.IO.File.Exists(backup)) EditorSceneManager.SaveScene(map.gameObject.scene, backup, true);
        AssetDatabase.ImportAsset("Assets/Design/GrassEdgeDrop.prompt.txt");

        var source = ImportTexture(Source);
        var pixels = source.GetPixels32();
        var clean = new Color32[pixels.Length];
        int minX = source.width, minY = source.height, maxX = -1, maxY = -1;
        int visible = 0;
        for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                int index = y * source.width + x;
                var pixel = pixels[index];
                if (pixel.a < 26) continue;
                pixel.a = (byte)Mathf.RoundToInt(Mathf.Clamp01((pixel.a - 26f) / 225f) * 255f);
                clean[index] = pixel;
                if (pixel.a < 96) continue;
                visible++;
                minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
            }
        if (visible < 50000) throw new Exception("Hanging grass cutout is empty");
        var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        output.SetPixels32(clean);
        output.Apply();
        System.IO.File.WriteAllBytes(TexturePath, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(output);
        var texture = ImportTexture(TexturePath);

        var rect = Rect.MinMaxRect(Mathf.Max(0, minX - 4), Mathf.Max(0, minY - 4),
            Mathf.Min(source.width, maxX + 5), Mathf.Min(source.height, maxY + 5));
        const string spritePath = Folder + "/GrassEdgeDropSprite.asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite && sprite.rect != rect)
        {
            AssetDatabase.DeleteAsset(spritePath);
            sprite = null;
        }
        if (!sprite)
        {
            sprite = Sprite.Create(texture, rect, new Vector2(1f, .9f), rect.height / .36f,
                0, SpriteMeshType.FullRect);
            sprite.name = "GrassEdgeDropSprite";
            AssetDatabase.CreateAsset(sprite, spritePath);
        }
        // Mirror around the occupied strip, not around the cliff edge: the roots
        // sit inside the turf, while the tips curl toward the exposed wall.
        float rootInset = sprite.bounds.size.x * 2.2f;
        var left = GetTile("GrassEdgeDropLeftTile", sprite, -.37f + rootInset, 1f);
        var right = GetTile("GrassEdgeDropRightTile", sprite, .37f - rootInset, -1f);
        map.SetGrassHangTiles(left, right);
        map.EnsureGrassHangOverlays();
        map.SyncGrassFromTerrain();
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(map.GrassHangLeftOverlay);
        EditorUtility.SetDirty(map.GrassHangRightOverlay);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return $"Edge-drop grass installed: {visible} visible pixels, sprite {rect.width}×{rect.height}";
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
        tile.transform = Matrix4x4.TRS(new Vector3(xOffset, .30f, 0), Quaternion.identity,
            new Vector3(xScale * 2.2f, 1f, 1f));
        EditorUtility.SetDirty(tile);
        return tile;
    }
}
