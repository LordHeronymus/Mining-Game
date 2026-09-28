using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InstallLayer3SeamlessBackground
{
    const string Source = "Assets/Design/Layer3SlateGenerated.png";
    const string Target = "Assets/AB Sprites/Parralax BG/Untergrund_02_Geschichteter_Schiefer_Tileable.png";

    public static string Main()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Run in Edit Mode.");

        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(File.ReadAllBytes(Source)))
            throw new InvalidOperationException("Could not read generated slate image.");
        int width = source.width, height = source.height;
        var original = source.GetPixels32();
        var shifted = new Color32[original.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            shifted[y * width + x] = original[((y + height / 2) % height) * width + (x + width / 2) % width];

        // Move the two original joins to the middle and cover each with an
        // overlapping patch from an uninterrupted part of the same painting.
        var pixels = (Color32[])shifted.Clone();
        int xRadius = width / 8, xOffset = width / 4;
        for (int y = 0; y < height; y++)
        for (int x = width / 2 - xRadius; x <= width / 2 + xRadius; x++)
        {
            float distance = Mathf.Abs(x - width / 2f) / xRadius;
            float weight = .5f + .5f * Mathf.Cos(Mathf.PI * distance);
            pixels[y * width + x] = Color.Lerp(shifted[y * width + x],
                shifted[y * width + (x + xOffset) % width], weight);
        }
        var afterHorizontal = (Color32[])pixels.Clone();
        int yRadius = height / 8, yOffset = height / 4;
        for (int y = height / 2 - yRadius; y <= height / 2 + yRadius; y++)
        for (int x = 0; x < width; x++)
        {
            float distance = Mathf.Abs(y - height / 2f) / yRadius;
            float weight = .5f + .5f * Mathf.Cos(Mathf.PI * distance);
            pixels[y * width + x] = Color.Lerp(afterHorizontal[y * width + x],
                afterHorizontal[((y + yOffset) % height) * width + x], weight);
        }

        // Match both border rows and columns exactly after the visual join is repaired.
        for (int y = 0; y < height; y++)
        {
            Color left = pixels[y * width], right = pixels[y * width + width - 1];
            Color difference = right - left;
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color value = pixels[index];
                value += difference * (.5f - x / (float)(width - 1));
                value.a = 1f;
                pixels[index] = value;
            }
        }
        for (int x = 0; x < width; x++)
        {
            Color top = pixels[(height - 1) * width + x], bottom = pixels[x];
            Color difference = top - bottom;
            for (int y = 0; y < height; y++)
            {
                int index = y * width + x;
                Color value = pixels[index];
                value += difference * (.5f - y / (float)(height - 1));
                value.a = 1f;
                pixels[index] = value;
            }
        }

        var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        output.SetPixels32(pixels);
        output.Apply();
        File.WriteAllBytes(Target, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(source);
        UnityEngine.Object.DestroyImmediate(output);

        AssetDatabase.ImportAsset(Target, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Target);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();

        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Target);
        if (!map || map.layers == null || map.layers.Length < 3 || !sprite)
            throw new InvalidOperationException("Layer 3 or its new sprite is missing.");
        map.layers[2].backgroundSprite = sprite;
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        return $"Layer 3 uses a {width}x{height} non-mirrored tileable slate sprite.";
    }
}
