using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupHudV1Art
{
    const string Folder = "Assets/Resources/HUDv1/";
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var frames = Load("Frames");
        var vitals = Bounds(frames, new RectInt(0, frames.height / 2, frames.width, frames.height / 2));
        var tray = Bounds(frames, new RectInt(0, 0, frames.width, frames.height / 2));
        Save("Vitals", "Frames", vitals, new Vector4(100, 80, 100, 80));
        Save("Tray", "Frames", tray, new Vector4(100, 80, 100, 80));
        // The vertical rail uses the painted brass, including its bevel, from the panel's left edge.
        Save("Divider", "Frames", new Rect(vitals.x + 12, vitals.y + 82, 30, vitals.height - 164), Vector4.zero);
        Save("DividerCap", "Frames", new Rect(vitals.x, vitals.yMax - 80, 92, 80), Vector4.zero);
        var sockets = Load("Sockets");
        var boxes = new Rect[3];
        for (int i = 0; i < 3; i++)
            boxes[i] = Bounds(sockets, new RectInt(i * sockets.width / 3, 0, sockets.width / 3, sockets.height));
        // Crop every state to the same painted frame, excluding the outer hover/selection halo.
        float sx = sockets.width / 2172f, sy = sockets.height / 724f;
        var core = new[] { new Rect(139, 132, 503, 463), new Rect(841, 132, 503, 463), new Rect(1540, 132, 503, 463) };
        for (int i = 0; i < 3; i++)
        {
            var r = core[i];
            r = new Rect(r.x * sx, sockets.height - (r.y + r.height) * sy, r.width * sx, r.height * sy);
            Save(new[] {"SocketNormal", "SocketHover", "SocketSelected"}[i], "Sockets", r, new Vector4(78 * sx, 68 * sy, 78 * sx, 68 * sy));
        }
        if (File.Exists(Folder + "Symbols.png"))
        {
            var symbols=Load("Symbols");
            Save("Heart","Symbols",Bounds(symbols,new RectInt(0,0,symbols.width/2,symbols.height)),Vector4.zero);
            Save("Bolt","Symbols",Bounds(symbols,new RectInt(symbols.width/2,0,symbols.width/2,symbols.height)),Vector4.zero);
            UnityEngine.Object.DestroyImmediate(symbols);
        }
        UnityEngine.Object.DestroyImmediate(frames); UnityEngine.Object.DestroyImmediate(sockets);
        AssetDatabase.SaveAssets();
        return new { vitals, tray, sockets = boxes };
    }
    static Texture2D Load(string name)
    {
        var path = Folder + name + ".png";
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(path)); return t;
    }
    static Rect Bounds(Texture2D t, RectInt region)
    {
        var pixels = t.GetPixels32(); int xmin = t.width, ymin = t.height, xmax = -1, ymax = -1;
        for (int y = region.yMin; y < region.yMax; y++) for (int x = region.xMin; x < region.xMax; x++)
        {
            if (pixels[y * t.width + x].a < 235) continue;
            xmin = Math.Min(xmin, x); ymin = Math.Min(ymin, y); xmax = Math.Max(xmax, x); ymax = Math.Max(ymax, y);
        }
        if (xmax < xmin) throw new Exception("Missing opaque art");
        return new Rect(xmin, ymin, xmax - xmin + 1, ymax - ymin + 1);
    }
    static void Save(string name, string source, Rect rect, Vector4 borders)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + source + ".png");
        var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, borders);
        sprite.name = "HUD V1 " + name;
        string path = Folder + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing) { EditorUtility.CopySerialized(sprite, existing); UnityEngine.Object.DestroyImmediate(sprite); EditorUtility.SetDirty(existing); }
        else AssetDatabase.CreateAsset(sprite, path);
    }
}
