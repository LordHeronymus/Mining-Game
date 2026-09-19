using System.Collections.Generic;
using UnityEngine;

// Small, reusable vector mesh. All animals of one species share a draw call.
public sealed class CritterMesh
{
    readonly List<Vector3> vertices = new List<Vector3>(12000);
    readonly List<Color> colors = new List<Color>(12000);
    readonly List<Vector2> uvs = new List<Vector2>(12000);
    readonly List<int> indices = new List<int>(36000);
    readonly Mesh mesh;
    readonly Transform transform;
    public readonly MeshRenderer renderer;
    Vector2 origin;
    float size = 1, facing = 1;
    Color tint = Color.white;

    public CritterMesh(Transform owner, string name, Material material, int order)
    {
        var child = new GameObject(name) { hideFlags = HideFlags.DontSave };
        child.layer = owner.gameObject.layer;
        child.transform.SetParent(owner, false);
        transform = child.transform;
        mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerName = "Default"; renderer.sortingOrder = order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public void Clear() { vertices.Clear(); colors.Clear(); uvs.Clear(); indices.Clear(); }
    public void Begin(Vector2 position, float scale, float direction, Color color)
    { origin = position; size = scale; facing = direction; tint = color; }

    void Vertex(float x, float y, Color color, Vector2 uv)
    {
        vertices.Add(transform.InverseTransformPoint(new Vector3(origin.x + x * size * facing, origin.y + y * size, 0)));
        colors.Add(color * tint); uvs.Add(uv);
    }

    public void Ellipse(float x, float y, float rx, float ry, Color color, float angle = 0, int segments = 16)
    {
        int start = vertices.Count;
        float cos = Mathf.Cos(angle * Mathf.Deg2Rad), sin = Mathf.Sin(angle * Mathf.Deg2Rad);
        Vertex(x, y, color, Vector2.zero);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments, px = Mathf.Cos(a) * rx, py = Mathf.Sin(a) * ry;
            Vertex(x + px * cos - py * sin, y + px * sin + py * cos, color, Vector2.zero);
            indices.Add(start); indices.Add(start + 1 + i); indices.Add(start + 1 + (i + 1) % segments);
        }
    }

    public void Stroke(float x, float y, float endX, float endY, float width, Color color)
    {
        float dx = endX - x, dy = endY - y;
        Ellipse((x + endX) * .5f, (y + endY) * .5f, Mathf.Sqrt(dx * dx + dy * dy) * .5f + width, width,
            color, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 10);
    }

    public void Quad(float radius, Color color)
    {
        int start = vertices.Count;
        Vertex(-radius, -radius, color, new Vector2(0, 0));
        Vertex(radius, -radius, color, new Vector2(1, 0));
        Vertex(radius, radius, color, new Vector2(1, 1));
        Vertex(-radius, radius, color, new Vector2(0, 1));
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int start = vertices.Count;
        Vertex(a.x, a.y, color, Vector2.zero); Vertex(b.x, b.y, color, Vector2.zero); Vertex(c.x, c.y, color, Vector2.zero);
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
    }

    public void Upload()
    {
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uvs);
        mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); renderer.enabled = vertices.Count > 0;
    }

    public void Dispose() { if (renderer) Release(renderer.gameObject); if (mesh) Release(mesh); }

    public static void RemoveGenerated(Transform owner, string name)
    {
        for (int i = owner.childCount - 1; i >= 0; i--)
        {
            var child = owner.GetChild(i);
            if (child.name != name || (child.gameObject.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) continue;
            var filter = child.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) Release(filter.sharedMesh);
            Release(child.gameObject);
        }
    }

    static void Release(Object value)
    {
        if (value is GameObject go) go.SetActive(false);
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }
}
