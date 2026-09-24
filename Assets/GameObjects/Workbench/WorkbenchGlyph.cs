using UnityEngine;
using UnityEngine.UI;

// Vector UI marks stay sharp at every canvas scale and do not depend on font glyphs.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WorkbenchGlyph : MaskableGraphic
{
    public enum Shape { Star, Search, Check }
    public Shape shape;
    public bool filled;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * .43f;
        float stroke = Mathf.Max(1.5f, radius * .12f);
        if (shape == Shape.Star)
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = (90 - i * 36) * Mathf.Deg2Rad;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * (i % 2 == 0 ? 1 : .46f);
            }
            for (int i = 0; i < 10; i++)
                if (filled) Triangle(vh, center, points[i], points[(i + 1) % 10]);
                else Line(vh, points[i], points[(i + 1) % 10], stroke);
        }
        else if (shape == Shape.Search)
        {
            center += new Vector2(-radius * .2f, radius * .2f);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12, b = (i + 1) * Mathf.PI / 12;
                Line(vh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * .64f,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius * .64f, stroke);
            }
            Line(vh, center + new Vector2(.43f, -.43f) * radius, center + new Vector2(1.05f, -1.05f) * radius, stroke);
        }
        else if (filled)
        {
            Line(vh, center + new Vector2(-.65f, 0) * radius, center + new Vector2(-.15f, -.5f) * radius, stroke * 1.5f);
            Line(vh, center + new Vector2(-.15f, -.5f) * radius, center + new Vector2(.75f, .6f) * radius, stroke * 1.5f);
        }
    }

    void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        Triangle(vh, a - normal, a + normal, b + normal);
        Triangle(vh, a - normal, b + normal, b - normal);
    }

    void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero); vh.AddVert(c, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
