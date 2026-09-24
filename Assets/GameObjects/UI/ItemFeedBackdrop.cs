using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ItemFeedBackdrop : MaskableGraphic
{
    const int Columns = 64;
    const int Rows = 24;
    float opacity = .7f;

    public void SetOpacity(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(opacity, value)) return;
        opacity = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float feather = 9f;
        float radius = rect.height * .5f - feather;
        float straightHalf = Mathf.Max(0f, rect.width * .5f - radius - feather);
        for (int row = 0; row <= Rows; row++)
        {
            float v = row / (float)Rows;
            for (int column = 0; column <= Columns; column++)
            {
                float u = column / (float)Columns;
                float x = (u - .5f) * rect.width;
                float y = (v - .5f) * rect.height;
                float endX = Mathf.Max(0f, Mathf.Abs(x) - straightHalf);
                float distance = Mathf.Sqrt(endX * endX + y * y);
                float billow = Mathf.Sin(u * Mathf.PI) *
                    (1.5f * Mathf.Sin(u * 3f * Mathf.PI) + .8f * Mathf.Sin(u * 7f * Mathf.PI));
                float edge = Mathf.Clamp01((distance - radius - billow + feather) / (2f * feather));
                Color tint = color;
                tint.a *= opacity * (1f - Mathf.SmoothStep(0f, 1f, edge));
                mesh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), tint, Vector2.zero);
            }
        }

        int stride = Columns + 1;
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            int bottomLeft = row * stride + column;
            mesh.AddTriangle(bottomLeft, bottomLeft + stride, bottomLeft + stride + 1);
            mesh.AddTriangle(bottomLeft, bottomLeft + stride + 1, bottomLeft + 1);
        }
    }
}
