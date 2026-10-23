using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Edit Mode: the 22 shop blueprint images (Resources/Shop/*Recipe.png) import at 512 px with high-quality
// compression (BC7 on Standalone, 23.10.2026). Checks the imported texture and writes a contact sheet of all
// blueprints to Temp/BlueprintTextures.png for a visual check.
public static class BlueprintTextureChecks
{
    public static object Main()
    {
        if (Application.isPlaying) return "Requires Edit Mode.";
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Shop" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("Recipe.png")) paths.Add(path);
        }
        paths.Sort(StringComparer.Ordinal);
        if (paths.Count != 22) throw new Exception("FAIL: expected 22 blueprint images, found " + paths.Count);
        const int cell = 256, columns = 6;
        int rows = (paths.Count + columns - 1) / columns;
        var sheet = new Texture2D(cell * columns, cell * rows, TextureFormat.RGBA32, false);
        sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
        var target = RenderTexture.GetTemporary(cell, cell, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var formats = new List<string>();
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                var standalone = ((TextureImporter)AssetImporter.GetAtPath(paths[i])).GetAutomaticFormat("Standalone");
                if (!texture || Mathf.Max(texture.width, texture.height) > 512)
                    throw new Exception($"FAIL: {paths[i]} is {texture?.width}x{texture?.height}, expected at most 512");
                if (standalone != TextureImporterFormat.BC7)
                    throw new Exception($"FAIL: {paths[i]} imports as {standalone} on Standalone, expected BC7");
                formats.Add(Path.GetFileNameWithoutExtension(paths[i]) + " " + texture.width + "x" + texture.height + " " + texture.format);
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var pixels = new Texture2D(cell, cell, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
                sheet.SetPixels(i % columns * cell, (rows - 1 - i / columns) * cell, cell, cell, pixels.GetPixels());
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
        Directory.CreateDirectory("Temp");
        File.WriteAllBytes("Temp/BlueprintTextures.png", sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        return new { passed = true, checkedImages = paths.Count, sheet = "Temp/BlueprintTextures.png", formats = string.Join("; ", formats) };
    }
}
