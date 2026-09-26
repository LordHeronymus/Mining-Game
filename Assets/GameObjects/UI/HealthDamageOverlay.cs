using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class HealthDamageOverlay : MaskableGraphic
{
    float strength;

    public float Strength
    {
        get => strength;
        set
        {
            value = Mathf.Clamp(value, 0f, 2f);
            if (Mathf.Approximately(strength, value)) return;
            strength = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (strength <= 0f) return;

        var rect = rectTransform.rect;
        float width = rect.width, height = rect.height;
        if (width <= 0f || height <= 0f) return;

        float depth = Mathf.Min(width, height) * .125f * (1f + .3f * Mathf.Max(0f, strength - 1f));
        const int segments = 96;
        const int bands = 12;
        for (int side = 0; side < 4; side++)
        {
            float length = side < 2 ? width : height;
            int firstVertex = mesh.currentVertCount;
            for (int band = 0; band <= bands; band++)
            {
                float across = band / (float)bands;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, across);
                var color = new Color(Mathf.Lerp(.25f, .29f, across), .008f, .012f,
                    strength * .72f * fade);
                for (int i = 0; i <= segments; i++)
                {
                    float along = i / (float)segments;
                    float inset = depth * across * EdgeDepth(side, along);
                    Vector2 point;
                    switch (side)
                    {
                        case 0: point = new Vector2(rect.xMin + length * along, rect.yMax - inset); break;
                        case 1: point = new Vector2(rect.xMin + length * along, rect.yMin + inset); break;
                        case 2: point = new Vector2(rect.xMin + inset, rect.yMin + length * along); break;
                        default: point = new Vector2(rect.xMax - inset, rect.yMin + length * along); break;
                    }
                    var vertex = UIVertex.simpleVert;
                    vertex.position = point;
                    vertex.color = color;
                    mesh.AddVert(vertex);
                }
            }
            for (int band = 0; band < bands; band++)
            {
                int row = firstVertex + band * (segments + 1);
                for (int i = 0; i < segments; i++)
                {
                    int next = row + segments + 1;
                    mesh.AddTriangle(row + i, row + i + 1, next + i + 1);
                    mesh.AddTriangle(row + i, next + i + 1, next + i);
                }
            }
        }
    }

    static float EdgeDepth(int side, float along)
    {
        float x = along * 36f * .84f + side * 7.17f;
        return .73f + .16f * Mathf.Sin(x) + .08f * Mathf.Sin(x * 2.27f + 1.4f);
    }
}
