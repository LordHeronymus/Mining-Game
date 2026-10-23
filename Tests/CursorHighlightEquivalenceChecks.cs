using System;
using System.Reflection;
using UnityEngine;

// Edit Mode: TileMiner builds its two cursor textures once per session and computes one quadrant that it
// mirrors. Compares both pixel sets with a full 512² per-pixel loop; every value must match exactly.
public static class CursorHighlightEquivalenceChecks
{
    public static object Main()
    {
        if (Application.isPlaying) return "Requires Edit Mode.";
        var create = typeof(TileMiner).GetMethod("HighlightPixels", BindingFlags.Static | BindingFlags.NonPublic);
        if (create == null) throw new Exception("TileMiner.HighlightPixels not found.");
        int compared = 0;
        foreach (bool cornersOnly in new[] { false, true })
        {
            var actual = (Color[])create.Invoke(null, new object[] { cornersOnly });
            var expected = Original(cornersOnly);
            if (actual.Length != expected.Length) throw new Exception("FAIL: pixel count differs");
            for (int i = 0; i < expected.Length; i++)
                if (!actual[i].Equals(expected[i])) // exact, unlike the tolerant == operator
                    throw new Exception($"FAIL: cornersOnly={cornersOnly} pixel {i % 512},{i / 512} {actual[i]} != {expected[i]}");
            compared += expected.Length;
        }
        return new { passed = true, compared };
    }

    // Full 512² per-pixel loop over the same per-pixel formula, without mirroring.
    static Color[] Original(bool cornersOnly)
    {
        const int resolution = 512;
        var pixel = typeof(TileMiner).GetMethod("HighlightPixel", BindingFlags.Static | BindingFlags.NonPublic);
        if (pixel == null) throw new Exception("TileMiner.HighlightPixel not found.");
        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
            pixels[y * resolution + x] = (Color)pixel.Invoke(null, new object[] {
                new Vector2(Mathf.Abs((x + .5f) / resolution - .5f), Mathf.Abs((y + .5f) / resolution - .5f)), cornersOnly });
        return pixels;
    }
}
