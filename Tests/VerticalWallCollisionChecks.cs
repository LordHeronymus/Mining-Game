using System;
using UnityEngine;

public static class VerticalWallCollisionChecks
{
    public static string Main()
    {
        var appearance = UnityEngine.Object.FindFirstObjectByType<UniformStoneAppearance>();
        if (!appearance) throw new Exception("Terrain appearance is missing.");
        var map = appearance.GetComponent<MapGenerator>();
        var shapes = new TerrainCollisionShape(appearance, map.Terrain);
        if (!shapes.Ready) throw new Exception("Terrain collision masks are unavailable.");
        try
        {
            float leftWall = -1f, rightWall = -1f;
            for (int variant = 0; variant < 12; variant++)
            {
                var left = shapes.Outline(1, variant, false);
                var right = shapes.Outline(2, variant, false);
                float leftX = 1f, rightX = 0f;
                foreach (var point in left) leftX = Mathf.Min(leftX, point.x);
                foreach (var point in right) rightX = Mathf.Max(rightX, point.x);
                if (leftWall >= 0f && Mathf.Abs(leftX - leftWall) > .0001f)
                    throw new Exception($"Stacked left wall tiles form a ledge: variant={variant}, previous={leftWall}, current={leftX}.");
                if (rightWall >= 0f && Mathf.Abs(rightX - rightWall) > .0001f)
                    throw new Exception($"Stacked right wall tiles form a ledge: variant={variant}, previous={rightWall}, current={rightX}.");
                leftWall = leftX;
                rightWall = rightX;

                for (int sample = 6; sample <= 34; sample++)
                {
                    float y = sample / 40f;
                    float actualRight = EdgeAt(right, y, true);
                    float actualLeft = EdgeAt(left, y, false);
                    float lo = .5f, hi = 1f;
                    for (int i = 0; i < 16; i++)
                    {
                        float mid = (lo + hi) * .5f;
                        if (shapes.Distance(new Vector2(mid, y), 2, variant, false) >= 0f) lo = mid;
                        else hi = mid;
                    }
                    if (actualRight - lo > .04f)
                        throw new Exception($"Right collider protrudes: variant={variant}, y={y}, offset={actualRight-lo}.");

                    lo = 0f; hi = .5f;
                    for (int i = 0; i < 16; i++)
                    {
                        float mid = (lo + hi) * .5f;
                        if (shapes.Distance(new Vector2(mid, y), 1, variant, false) >= 0f) hi = mid;
                        else lo = mid;
                    }
                    if (hi - actualLeft > .04f)
                        throw new Exception($"Left collider protrudes: variant={variant}, y={y}, offset={hi-actualLeft}.");
                }
            }
            return "PASS: straight stacked sides and close visual alignment across 12 terrain variants.";
        }
        finally { shapes.Dispose(); }
    }

    static float EdgeAt(Vector2[] outline, float y, bool right)
    {
        float edge = right ? float.NegativeInfinity : float.PositiveInfinity;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            if (Mathf.Abs(a.y - b.y) < .00001f || y < Mathf.Min(a.y, b.y) || y > Mathf.Max(a.y, b.y))
                continue;
            float x = Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y));
            edge = right ? Mathf.Max(edge, x) : Mathf.Min(edge, x);
        }
        return edge;
    }
}
