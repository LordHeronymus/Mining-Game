using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ItemFeedAccent : MaskableGraphic
{
    const int RibbonSegments = 48;
    const int SparkSides = 16;

    float age;
    int sparkCount = 7;
    float sparkIntensity = 1f;
    float sparkRiseHeight = 14f;
    float sparkBrightness = 1f;
    float lineBrightness = 1f;
    public int Seed { get; set; }

    public void ConfigureSparks(int count, float intensity, float riseHeight,
        float particleBrightness, float accentBrightness)
    {
        count = Mathf.Clamp(count, 0, 24);
        intensity = Mathf.Clamp(intensity, 0f, 3f);
        riseHeight = Mathf.Clamp(riseHeight, 0f, 40f);
        particleBrightness = Mathf.Clamp(particleBrightness, 0f, 5f);
        accentBrightness = Mathf.Clamp(accentBrightness, 0f, 5f);
        if (sparkCount == count && Mathf.Approximately(sparkIntensity, intensity) &&
            Mathf.Approximately(sparkRiseHeight, riseHeight) &&
            Mathf.Approximately(sparkBrightness, particleBrightness) &&
            Mathf.Approximately(lineBrightness, accentBrightness)) return;
        sparkCount = count;
        sparkIntensity = intensity;
        sparkRiseHeight = riseHeight;
        sparkBrightness = particleBrightness;
        lineBrightness = accentBrightness;
        SetVerticesDirty();
    }

    public void SetAge(float value)
    {
        age = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float y = rect.yMin + 8f;
        Color tint = color;
        if (lineBrightness > 0f)
        {
            float bloom = Mathf.Max(0f, lineBrightness - 1f);
            Color hotTint = Color.Lerp(tint, Color.white, Mathf.Clamp01(bloom * .22f));
            SoftRibbon(mesh, rect, y, 6f + bloom * 2.2f, tint,
                Mathf.Min(.75f, .14f * lineBrightness));
            SoftRibbon(mesh, rect, y, 2.7f + bloom * .7f, tint,
                Mathf.Min(1f, .24f * lineBrightness));
            SoftRibbon(mesh, rect, y, .85f + bloom * .15f, hotTint,
                Mathf.Min(1f, .9f * lineBrightness));
        }

        for (int i = 0; i < sparkCount && sparkIntensity > 0f && sparkBrightness > 0f; i++)
        {
            float phase = Mathf.Repeat(age * (.45f + Hash(i, 1) * .3f) + Hash(i, 2), 1f);
            float visibility = Mathf.Sin(phase * Mathf.PI);
            visibility *= visibility;
            if (visibility < .025f) continue;

            float x = rect.xMin + rect.width * (.08f + Hash(i, 3) * .86f)
                + phase * (2f + Hash(i, 4) * 5f);
            float py = y + 1.5f + phase * sparkRiseHeight * (.55f + Hash(i, 5) * .45f);
            float size = (.9f + Hash(i, 6) * 1.1f) * Mathf.Sqrt(sparkIntensity);
            float bloom = Mathf.Max(0f, sparkBrightness - 1f);
            Color hotTint = Color.Lerp(tint, Color.white, Mathf.Clamp01(bloom * .23f));
            float glow = Mathf.Min(.8f, visibility * sparkIntensity * (.16f + bloom * .15f));
            SoftSpark(mesh, x, py, size * (2.9f + bloom * .65f), tint, glow);
            SoftSpark(mesh, x, py, size * (1f + bloom * .1f), hotTint,
                Mathf.Min(1f, visibility * sparkIntensity * sparkBrightness * .8f));
            if (sparkBrightness > 1.5f)
            {
                float trail = Mathf.Min(.45f, visibility * (sparkBrightness - 1f) * .1f);
                SoftSpark(mesh, x - 1.3f, py - size * 1.3f, size * .85f, tint, trail);
                SoftSpark(mesh, x - 2.1f, py - size * 2.2f, size * .55f, tint, trail * .6f);
            }
        }
    }

    float Hash(int index, int stream) => Mathf.Repeat(
        Mathf.Sin((index + 1) * 91.177f + Seed * .01f + stream * 47.719f) * 43758.5453f, 1f);

    static void SoftRibbon(VertexHelper mesh, Rect rect, float baseline, float halfHeight,
        Color tint, float opacity)
    {
        int start = mesh.currentVertCount;
        for (int column = 0; column <= RibbonSegments; column++)
        {
            float t = column / (float)RibbonSegments;
            float envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.5f);
            float x = rect.xMin + rect.width * t;
            float center = baseline + Mathf.Sin(t * Mathf.PI * 1.4f) * .65f;
            float radius = halfHeight * (.65f + envelope * .35f);
            float alpha = opacity * envelope;
            Add(mesh, x, center - radius, tint, 0f);
            Add(mesh, x, center - radius * .42f, tint, alpha * .28f);
            Add(mesh, x, center, tint, alpha);
            Add(mesh, x, center + radius * .42f, tint, alpha * .28f);
            Add(mesh, x, center + radius, tint, 0f);
        }
        for (int column = 0; column < RibbonSegments; column++)
        {
            int left = start + column * 5;
            int right = left + 5;
            for (int row = 0; row < 4; row++)
            {
                mesh.AddTriangle(left + row, left + row + 1, right + row + 1);
                mesh.AddTriangle(left + row, right + row + 1, right + row);
            }
        }
    }

    static void SoftSpark(VertexHelper mesh, float x, float y, float radius, Color tint, float opacity)
    {
        int start = mesh.currentVertCount;
        Add(mesh, x, y, tint, opacity);
        for (int ring = 0; ring < 2; ring++)
        {
            float distance = ring == 0 ? radius * .42f : radius;
            float alpha = ring == 0 ? opacity * .25f : 0f;
            for (int side = 0; side < SparkSides; side++)
            {
                float angle = side * Mathf.PI * 2f / SparkSides;
                Add(mesh, x + Mathf.Cos(angle) * distance,
                    y + Mathf.Sin(angle) * distance, tint, alpha);
            }
        }
        for (int side = 0; side < SparkSides; side++)
        {
            int next = (side + 1) % SparkSides;
            mesh.AddTriangle(start, start + 1 + side, start + 1 + next);
            int inner = start + 1 + side;
            int outer = start + 1 + SparkSides + side;
            int nextInner = start + 1 + next;
            int nextOuter = start + 1 + SparkSides + next;
            mesh.AddTriangle(inner, outer, nextOuter);
            mesh.AddTriangle(inner, nextOuter, nextInner);
        }
    }

    static void Add(VertexHelper mesh, float x, float y, Color tint, float alpha)
    {
        tint.a *= alpha;
        mesh.AddVert(new Vector3(x, y), tint, Vector2.zero);
    }
}
