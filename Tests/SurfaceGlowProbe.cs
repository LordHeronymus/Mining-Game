// Diagnose (temporär): Zustand des Oberflächen-Glows. In a running game at night with a lit shaft:
// unity command run_script --file Tests/SurfaceGlowProbe.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SurfaceGlowProbe
{
    static int CountMagenta(Camera cam)
    {
        var rt = new RenderTexture(1280, 720, 24);
        var old = cam.targetTexture; float size = cam.orthographicSize;
        cam.targetTexture = rt; cam.orthographicSize = 5f; cam.Render();
        cam.targetTexture = old; cam.orthographicSize = size;
        var active = RenderTexture.active; RenderTexture.active = rt;
        var shot = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); shot.Apply();
        RenderTexture.active = active; rt.Release();
        int count = shot.GetPixels32().Count(p => p.r > 200 && p.b > 200 && p.g < 120);
        Object.DestroyImmediate(shot);
        return count;
    }

    public static object Main()
    {
        if (!EditorApplication.isPlaying) return "Requires a running game (Play → Neues Spiel).";
        var go = GameObject.Find("Surface light glow");
        if (!go) return "no glow object";
        var r = go.GetComponent<MeshRenderer>();
        var m = r.sharedMaterial;
        var tex = (Texture2D)m.mainTexture;
        var cam = Camera.main;
        var lines = new List<string>();

        // GPU content of the light texture: rows around the top terrain row at the map centre.
        var readback = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, readback);
        var prev = RenderTexture.active; RenderTexture.active = readback;
        var gpu = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
        gpu.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); gpu.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(readback);
        int x = tex.width / 2, rows = MapLighting.SurfaceLightRows;
        for (int pixelY = tex.height - rows - 3; pixelY < tex.height - rows + 4; pixelY++)
            lines.Add($"gpu row {pixelY} (cell y {pixelY - (tex.height - rows - 1)}): a {gpu.GetPixel(x, pixelY).a:0.00}");
        Object.DestroyImmediate(gpu);

        var oldColor = m.GetColor("_GlowColor");
        int oldOrder = r.sortingOrder;
        m.SetColor("_GlowColor", new Color(8f, 0f, 8f, 1f));
        lines.Add($"magenta with light texture, order {oldOrder}: {CountMagenta(cam)}");
        m.mainTexture = Texture2D.whiteTexture;
        lines.Add($"magenta with white texture, order {oldOrder}: {CountMagenta(cam)}");
        r.sortingOrder = 32767;
        lines.Add($"magenta with white texture, order 32767: {CountMagenta(cam)}");
        r.sortingLayerName = "Default";
        lines.Add($"magenta with white texture, Default/32767: {CountMagenta(cam)}");
        r.sortingLayerName = "Background"; r.sortingOrder = oldOrder;
        m.mainTexture = tex; m.SetColor("_GlowColor", oldColor);
        foreach (var other in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (other.enabled && other.sortingLayerName == "Background" && other.sortingOrder >= 25 && other.isVisible)
                lines.Add($"background renderer {other.name} ({other.GetType().Name}) order {other.sortingOrder} shader {(other.sharedMaterial ? other.sharedMaterial.shader.name : "-")}");
        return string.Join("\n", lines);
    }
}
