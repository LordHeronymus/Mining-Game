using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class AudioSpeakerIcon : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        float unit = Mathf.Min(rect.width, rect.height);
        if (unit <= 0f) return;
        Vector2 Point(float x, float y) => new(rect.xMin + x * unit, rect.yMin + y * unit);

        AddPolygon(mesh, new[]
        {
            Point(.12f, .39f), Point(.34f, .39f), Point(.60f, .18f),
            Point(.60f, .82f), Point(.34f, .61f), Point(.12f, .61f)
        });
        AddArc(mesh, Point(.47f, .5f), .25f * unit, -.88f, .88f, .045f * unit, 12);
        AddArc(mesh, Point(.47f, .5f), .39f * unit, -.84f, .84f, .045f * unit, 14);
    }

    void AddPolygon(VertexHelper mesh, Vector2[] points)
    {
        int start = mesh.currentVertCount;
        foreach (var point in points) AddVertex(mesh, point);
        for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
    }

    void AddArc(VertexHelper mesh, Vector2 center, float radius, float startAngle,
        float endAngle, float thickness, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
            float a1 = Mathf.Lerp(startAngle, endAngle, (i + 1f) / segments);
            Vector2 p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            Vector2 normal = new Vector2(-(p1 - p0).y, (p1 - p0).x).normalized * thickness * .5f;
            int start = mesh.currentVertCount;
            AddVertex(mesh, p0 - normal);
            AddVertex(mesh, p0 + normal);
            AddVertex(mesh, p1 + normal);
            AddVertex(mesh, p1 - normal);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    void AddVertex(VertexHelper mesh, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        mesh.AddVert(vertex);
    }
}
