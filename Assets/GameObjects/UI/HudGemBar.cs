using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class HudGemBar : MaskableGraphic
{
    [SerializeField, Range(0f, 1f)] float fillAmount = 1f;
    [SerializeField] Color gemColor = new Color(.95f, .08f, .11f);
    float flashAmount;

    public float FlashAmount
    {
        get => flashAmount;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(flashAmount, value)) return;
            flashAmount = value;
            SetVerticesDirty();
        }
    }

    public float FillAmount
    {
        get => fillAmount;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(fillAmount, value)) return;
            fillAmount = value;
            SetVerticesDirty();
        }
    }

    public Color GemColor
    {
        get => gemColor;
        set { gemColor = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        float scaleX = rect.width / 172f;
        float scaleY = rect.height / 17f;

        Vector2 Point(float x, float y) => new Vector2(rect.xMin + x * scaleX, rect.yMin + y * scaleY);
        void Quad(float x0, float y0, float x1, float y1, Color top, Color bottom)
        {
            int index = mesh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = bottom; vertex.position = Point(x0, y0); mesh.AddVert(vertex);
            vertex.position = Point(x1, y0); mesh.AddVert(vertex);
            vertex.color = top; vertex.position = Point(x1, y1); mesh.AddVert(vertex);
            vertex.position = Point(x0, y1); mesh.AddVert(vertex);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
        void Facet(float x, float y, float width, float height, Color color, float strength)
        {
            int start = mesh.currentVertCount;
            var points = new[]
            {
                Point(x + 3, y), Point(x + width - 3, y),
                Point(x + width, y + 3), Point(x + width, y + height - 3),
                Point(x + width - 3, y + height), Point(x + 3, y + height),
                Point(x, y + height - 3), Point(x, y + 3)
            };
            var center = Point(x + width * .48f, y + height * .56f);
            var vertex = UIVertex.simpleVert;
            vertex.position = center;
            vertex.color = Color.Lerp(new Color(.11f, .065f, .055f), color * 1.22f, strength);
            mesh.AddVert(vertex);
            float[] lights = { .48f, .68f, .96f, .69f, .34f, .42f, .84f, .62f };
            for (int i = 0; i < points.Length; i++)
            {
                vertex.position = points[i];
                vertex.color = Color.Lerp(new Color(.12f, .075f, .06f), color * lights[i], strength);
                mesh.AddVert(vertex);
            }
            for (int i = 0; i < points.Length; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
        }

        var bronze = Color.Lerp(new Color(.67f, .37f, .12f), new Color(1f, .84f, .58f), flashAmount);
        var dark = new Color(.085f, .045f, .035f);
        Quad(12, 0, 160, 17, bronze, new Color(.23f, .11f, .045f));
        Quad(13, 1, 159, 16, dark, new Color(.035f, .019f, .017f));
        for (int i = 0; i < 8; i++)
        {
            float x = 15 + i * 18f;
            float strength = Mathf.Clamp01(fillAmount * 8f - i);
            Quad(x, 2, x + 16, 15, new Color(.40f, .22f, .10f), new Color(.17f, .09f, .055f));
            Facet(x + 1, 3, 14, 11, Color.Lerp(gemColor, Color.white, flashAmount * .85f), strength);
            if (strength > 0f)
                Quad(x + 4, 11, x + 11, 12, new Color(1f, .94f, .70f, .65f * strength),
                    new Color(1f, .94f, .70f, .12f * strength));
        }

        void EndCap(float x)
        {
            int start = mesh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.position = Point(x + 6, 8.5f); vertex.color = new Color(.55f, .30f, .09f); mesh.AddVert(vertex);
            var points = new[] { Point(x + 6, 1), Point(x + 12, 8.5f), Point(x + 6, 16), Point(x, 8.5f) };
            var colors = new[] { new Color(.85f, .52f, .15f), new Color(.39f, .20f, .055f),
                new Color(1f, .76f, .30f), new Color(.77f, .43f, .10f) };
            for (int i = 0; i < 4; i++)
            { vertex.position = points[i]; vertex.color = colors[i]; mesh.AddVert(vertex); }
            for (int i = 0; i < 4; i++) mesh.AddTriangle(start, start + i + 1, start + (i + 1) % 4 + 1);
            Facet(x + 3, 5, 6, 7, new Color(1f, .80f, .30f), .9f);
        }
        EndCap(0);
        EndCap(160);
    }
}
