using System;
using System.Reflection;
using UnityEngine;

// Edit Mode: TileMiner builds its two cursor textures once per session and computes one quadrant that it
// mirrors. Compares both pixel sets with the original full 512² per-pixel loop; every value must match exactly.
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

    // The per-pixel build before the cache (TileMiner.CreateHighlightTile, 23.10.2026).
    static Color[] Original(bool cornersOnly)
    {
        const int resolution = 512;
        const float halfFrame = .425f;
        const float armLength = .85f * .22f;
        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            var p = new Vector2(Mathf.Abs((x + .5f) / resolution - .5f),
                Mathf.Abs((y + .5f) / resolution - .5f));
            float end = cornersOnly ? halfFrame - armLength : 0f;
            var horizontal = new Vector2(Mathf.Clamp(p.x, end, halfFrame), halfFrame);
            var vertical = new Vector2(halfFrame, Mathf.Clamp(p.y, end, halfFrame));
            float horizontalDistance = Vector2.Distance(p, horizontal);
            float verticalDistance = Vector2.Distance(p, vertical);
            bool nearestHorizontal = horizontalDistance < verticalDistance;
            float distance = Mathf.Min(horizontalDistance, verticalDistance);
            float taper = cornersOnly ? Mathf.SmoothStep(0f, 1f,
                ((nearestHorizontal ? horizontal.x : vertical.y) - end) / armLength) : 1f;
            float width = cornersOnly ? Mathf.Lerp(.0006f, .006f, taper) : .0045f;
            float core = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(width - .001f, width + .001f, distance));
            float halo = Mathf.Exp(-distance * distance / (.012f * .012f)) * .28f;
            float alpha = Mathf.Max(core * .98f, halo) * (cornersOnly ? taper : 1f);
            var color = Color.Lerp(new Color(1f, .57f, .12f), new Color(1f, .86f, .4f), core);
            color.a = alpha;
            pixels[y * resolution + x] = color;
        }
        return pixels;
    }
}
