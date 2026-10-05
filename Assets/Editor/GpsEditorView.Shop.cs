using UnityEngine;

public sealed partial class GpsEditorView
{
    void DrawShopIconPreview(ItemSO ore)
    {
        var area = GUILayoutUtility.GetRect(1, 186, GUILayout.ExpandWidth(true));
        float zoom = Mathf.Min(.75f, area.width / 608f);
        var previous = GUI.matrix;
        GUI.BeginGroup(area);
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(zoom, zoom, 1));
            DrawPreviewSprite(new Rect(0, 0, 608, 232), HomeUi.Sprite("SelectionCardNormal"), 5.5f);
            if (ore.icon)
            {
                var source = ore.icon.rect;
                float fit = Mathf.Min(260 / source.width, 194 / source.height);
                var size = source.size * fit;
                var center = new Vector2(150 + ore.iconOffset.x, 115 - ore.iconOffset.y);
                var matrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(ore.iconScale.x * (ore.iconFlipX ? -1 : 1),
                    ore.iconScale.y * (ore.iconFlipY ? -1 : 1)), center);
                DrawPreviewSprite(new Rect(center - size * .5f, size), ore.icon);
                GUI.matrix = matrix;
            }
            var name = new GUIStyle(Theme.Label) { fontSize = 43, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            GUI.Label(new Rect(300, 28, 282, 72), ore.displayName, name);
            DrawPreviewSprite(new Rect(482, 155, 100, 56), HomeUi.Sprite("SelectionCardNormal"), 8);
            var count = new GUIStyle(name) { fontSize = 41, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(482, 155, 100, 56), "1", count);
        }
        finally { GUI.matrix = previous; GUI.EndGroup(); }
    }

    static void DrawPreviewSprite(Rect rect, Sprite sprite, float slicing = 0)
    {
        if (!sprite) return;
        var texture = sprite.texture; var source = sprite.textureRect;
        if (slicing <= 0)
        {
            GUI.DrawTextureWithTexCoords(rect, texture, new Rect(source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height));
            return;
        }
        var b = sprite.border;
        float left = Mathf.Min(b.x / slicing, rect.width * .5f), right = Mathf.Min(b.z / slicing, rect.width * .5f);
        float top = Mathf.Min(b.w / slicing, rect.height * .5f), bottom = Mathf.Min(b.y / slicing, rect.height * .5f);
        float[] x = { rect.x, rect.x + left, rect.xMax - right, rect.xMax };
        float[] y = { rect.y, rect.y + top, rect.yMax - bottom, rect.yMax };
        float[] u = { source.x, source.x + b.x, source.xMax - b.z, source.xMax };
        float[] v = { source.yMax, source.yMax - b.w, source.y + b.y, source.y };
        for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
            GUI.DrawTextureWithTexCoords(new Rect(x[col], y[row], x[col + 1] - x[col], y[row + 1] - y[row]), texture,
                new Rect(u[col] / texture.width, v[row + 1] / texture.height,
                    (u[col + 1] - u[col]) / texture.width, (v[row] - v[row + 1]) / texture.height));
    }
}
