using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class SetupGrassCarpet
{
    const string Folder = "Assets/GameObjects/Map/Blocks/Sprites/Grass/Carpet";

    public static string RebuildSeams()
    {
        var source = ImportTexture(Folder + "/GrassCarpetStrip.png");
        var processed = BuildSeamlessStrip(source);
        return $"Seamless grass texture rebuilt ({processed.width}×{processed.height})";
    }

    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map) throw new Exception("MapGenerator missing");
        const string backup = "Assets/_SceneBackups/BeforeGrassCarpet_20260926.unity";
        if (!System.IO.File.Exists(backup)) EditorSceneManager.SaveScene(map.gameObject.scene, backup, true);

        var sourceStrip = ImportTexture(Folder + "/GrassCarpetStrip.png");
        var strip = BuildSeamlessStrip(sourceStrip);
        var single = ImportTexture(Folder + "/GrassCarpetSingle.png");
        float pieceWidth = strip.width / 6f;
        var pieces = new Tile[6];
        for (int i = 0; i < pieces.Length; i++)
        {
            var sprite = GetSprite("GrassCarpetSeamlessSprite_" + i,
                strip, new Rect(i * pieceWidth, 0, pieceWidth, strip.height), pieceWidth / .5f);
            bool leftEnd = i == 0, rightEnd = i == pieces.Length - 1;
            float horizontalOffset = leftEnd ? -.035f : rightEnd ? .035f : 0f;
            float horizontalScale = leftEnd || rightEnd ? 1.14f : 1f;
            pieces[i] = GetTile("GrassCarpetTile_" + i, sprite,
                new Vector3(horizontalScale, .55f, 1f), horizontalOffset);
        }

        var singleSprite = GetSprite("GrassCarpetSingleSprite", single,
            new Rect(0, 0, single.width, single.height), single.width / .5f);
        var singleTile = GetTile("GrassCarpetSingleTile", singleSprite, new Vector3(1.35f, 2f, 1f), 0f);
        map.SetGrassVariants(new TileBase[] { pieces[1], pieces[2], pieces[3], pieces[4] }, true);
        map.SetGrassEdgeTiles(pieces[0], pieces[5], singleTile);
        map.SyncGrassFromTerrain();
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(map.GrassOverlay);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(map.gameObject.scene);
        return $"Grass carpet installed ({strip.width}×{strip.height}; single {single.width}×{single.height})";
    }

    static Texture2D ImportTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new Exception("Texture missing: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.isReadable = true;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D BuildSeamlessStrip(Texture2D source)
    {
        int pieceWidth = source.width / 6;
        int width = pieceWidth * 6, height = source.height;
        const int blend = 64;
        int patchCenter = 1540;
        var input = source.GetPixels32();
        var output = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int piece = x / pieceWidth, within = x % pieceWidth;
                Color color = input[y * source.width + x];
                if (piece > 0 && within < blend)
                {
                    float t = 1f - within / (float)(blend - 1);
                    t = t * t * (3f - 2f * t);
                    color = Color.Lerp(color, input[y * source.width + patchCenter + within], t);
                }
                if (piece < 5 && within >= pieceWidth - blend)
                {
                    int k = within - (pieceWidth - blend);
                    float t = k / (float)(blend - 1);
                    t = t * t * (3f - 2f * t);
                    color = Color.Lerp(color, input[y * source.width + patchCenter - blend + k], t);
                }
                output[y * width + x] = color;
            }
        var processed = new Texture2D(width, height, TextureFormat.RGBA32, false);
        processed.SetPixels32(output);
        processed.Apply();
        string path = Folder + "/GrassCarpetSeamless.png";
        System.IO.File.WriteAllBytes(path, processed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(processed);
        return ImportTexture(path);
    }

    static Sprite GetSprite(string name, Texture2D texture, Rect rect, float pixelsPerUnit)
    {
        string path = Folder + "/" + name + ".asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite) return sprite;
        sprite = Sprite.Create(texture, rect, new Vector2(.5f, .38f), pixelsPerUnit,
            0, SpriteMeshType.FullRect);
        sprite.name = name;
        AssetDatabase.CreateAsset(sprite, path);
        return sprite;
    }

    static Tile GetTile(string name, Sprite sprite, Vector3 scale, float horizontalOffset)
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
        tile.transform = Matrix4x4.TRS(new Vector3(horizontalOffset, .25f, 0), Quaternion.identity, scale);
        EditorUtility.SetDirty(tile);
        return tile;
    }
}
