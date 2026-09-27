using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ArtifactDiscoveryGraphic : MaskableGraphic
{
    public enum Kind { Frame, Particles }
    public Kind kind;
    public Color theme;
    public Color secondaryTheme = new Color32(255, 190, 72, 255);
    public int shardCount = 16;
    public float shardDistance = 1f;
    public Vector2 shardCenter;
    public float shardSizeMin = 8f;
    public float shardSizeMax = 17f;
    public float shardRotationFrequency = .6f;
    public float shardRotationAngle = 8f;
    float age;
    static readonly Color Gold = new Color32(236, 174, 71, 255);
    static readonly Color PaleGold = new Color32(255, 234, 172, 255);
    static readonly Vector2[] Crystals = { new(-410, 335), new(424, 68), new(367, -260), new(-356, -290), new(270, 345) };

    public void SetTime(float seconds) { age = seconds; SetVerticesDirty(); }

    public void SetShards(int count, float minSize, float maxSize, float rotationFrequency, float rotationAngle)
    {
        shardCount = Mathf.Clamp(count, 0, 60);
        shardSizeMin = Mathf.Clamp(minSize, 2f, 40f);
        shardSizeMax = Mathf.Clamp(maxSize, shardSizeMin, 40f);
        shardRotationFrequency = Mathf.Clamp(rotationFrequency, 0f, 3f);
        shardRotationAngle = Mathf.Clamp(rotationAngle, 0f, 25f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (kind == Kind.Frame)
        {
            Ribbon(mesh, 488, 740);
            Ribbon(mesh, -486, 740);
            Ribbon(mesh, -370, 615);
            return;
        }

        for (int i = 0; i < 180; i++)
        {
            float angle = Hash(i, 0) * Mathf.PI * 2;
            float radius = 240 + Hash(i, 1) * 360;
            Vector2 p = new Vector2(Mathf.Cos(angle) * radius, 85 + Mathf.Sin(angle) * radius * .8f);
            p.x += Mathf.Sin(age * .65f + i) * 8;
            p.y += age * (7 + Hash(i, 2) * 15);
            float twinkle = .35f + .65f * Mathf.Pow(.5f + .5f * Mathf.Sin(age * (2 + Hash(i, 3) * 3) + i * 7), 2);
            float size = .45f + Hash(i, 4) * 1.15f;
            Color tint = i % 3 == 0 ? Gold : Color.Lerp(theme, Color.white, .3f);
            Glow(mesh, p, size * 4, tint, twinkle * .09f);
            Glow(mesh, p, size, Color.Lerp(tint, Color.white, .45f), twinkle);
            if (i % 17 == 0)
            {
                Line(mesh, p - Vector2.right * size * 3, p + Vector2.right * size * 3, .65f, WithAlpha(PaleGold, twinkle * .7f));
                Line(mesh, p - Vector2.up * size * 4, p + Vector2.up * size * 4, .65f, WithAlpha(PaleGold, twinkle * .7f));
            }
        }

        DrawRoundSparks(mesh);

        for (int i = 0; i < shardCount; i++)
        {
            Vector2 origin;
            if (i < Crystals.Length) origin = Crystals[i];
            else
            {
                float angle = i * 2.3999632f + Hash(i, 5) * .4f;
                float radius = 420f + Hash(i, 6) * 360f;
                origin = new Vector2(Mathf.Cos(angle) * radius,
                    Mathf.Clamp(60f + Mathf.Sin(angle) * radius * .67f, -470f, 470f));
                if (origin.y < -260f && Mathf.Abs(origin.x) < 520f)
                    origin.x = Mathf.Sign(origin.x == 0f ? Mathf.Cos(angle) : origin.x) *
                        (540f + Hash(i, 8) * 150f);
            }
            Vector2 p = origin + new Vector2(Mathf.Sin(age + i) * 5, Mathf.Sin(age * .8f + i * 2) * 8);
            p = shardCenter + (p - shardCenter) * Mathf.Clamp(shardDistance, .25f, 2f);
            float size = Mathf.Lerp(shardSizeMin, shardSizeMax, Hash(i, 7));
            Glow(mesh, p, size * 2, theme, .07f);
            Vector2 top = p + new Vector2(-size * .2f, size * 1.3f), bottom = p + new Vector2(size * .27f, -size);
            Vector2 left = p + new Vector2(-size * .6f, 0), right = p + new Vector2(size * .6f, 2);
            float radians = shardRotationFrequency <= 0f ? 0f :
                Mathf.Sin(age * Mathf.PI * 2f * shardRotationFrequency + Hash(i, 9) * Mathf.PI * 2f) *
                shardRotationAngle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            top = RotateAround(top, p, cosine, sine);
            bottom = RotateAround(bottom, p, cosine, sine);
            left = RotateAround(left, p, cosine, sine);
            right = RotateAround(right, p, cosine, sine);
            Triangle(mesh, top, left, bottom, Color.Lerp(theme, Color.white, .65f), theme, Color.Lerp(theme, Color.black, .2f));
            Triangle(mesh, top, bottom, right, Color.Lerp(theme, Color.white, .85f), theme, Color.Lerp(theme, Color.white, .2f));
            Line(mesh, top, bottom, .65f, Color.Lerp(theme, Color.white, .7f));
        }
    }

    static float Hash(int i, int channel) => Mathf.Repeat(Mathf.Sin(i * 71.71f + channel * 113.13f + 1) * 43758.5453f, 1);

    void DrawRoundSparks(VertexHelper mesh)
    {
        for (int i = 0; i < 9; i++)
        {
            float interval = Mathf.Lerp(1.8f, 3.4f, Hash(i, 20));
            float clock = age + Hash(i, 21) * interval;
            int cycle = Mathf.FloorToInt(clock / interval);
            float elapsed = clock - cycle * interval;
            float lifetime = Mathf.Lerp(.65f, 1.15f, Hash(i, 22));
            if (elapsed >= lifetime) continue;
            float brightness = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .1f)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - .1f) / (lifetime - .1f))));
            int seed = i + cycle * 37;
            float angle = Hash(seed, 23) * Mathf.PI * 2f;
            float radius = Mathf.Lerp(320f, 570f, Hash(seed, 24));
            Vector2 position = shardCenter + new Vector2(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * .7f + elapsed * 12f);
            float size = Mathf.Lerp(2.5f, 4.5f, Hash(seed, 25));
            Glow(mesh, position, size * 8f, secondaryTheme, brightness * .2f);
            Glow(mesh, position, size * 3f, secondaryTheme, brightness * .7f);
            Glow(mesh, position, size, Color.Lerp(secondaryTheme, Color.white, .8f), brightness);
        }
    }

    static Vector2 RotateAround(Vector2 point, Vector2 center, float cosine, float sine)
    {
        Vector2 offset = point - center;
        return center + new Vector2(offset.x * cosine - offset.y * sine,
            offset.x * sine + offset.y * cosine);
    }

    static void Ribbon(VertexHelper mesh, float y, float halfWidth)
    {
        const int segments = 96;
        for (int i = 0; i < segments; i++)
        {
            float x0 = Mathf.Lerp(-halfWidth, halfWidth, i / (float)segments);
            float x1 = Mathf.Lerp(-halfWidth, halfWidth, (i + 1) / (float)segments);
            float alpha = Mathf.Pow(Mathf.Sin((i + .5f) / segments * Mathf.PI), .55f);
            if (Mathf.Abs((x0 + x1) * .5f) < 18) continue;
            Color tint = Color.Lerp(Gold, PaleGold, .35f + .3f * Mathf.Sin(i * 1.7f));
            Line(mesh, new Vector2(x0, y), new Vector2(x1, y), 8, WithAlpha(Gold, alpha * .08f));
            Line(mesh, new Vector2(x0, y), new Vector2(x1, y), 1.5f, WithAlpha(tint, alpha));
        }
        Vector2 p = new(0, y);
        Vector2[] corners = { p + Vector2.up * 16, p + Vector2.right * 13, p + Vector2.down * 16, p - Vector2.right * 13 };
        Glow(mesh, p, 26, Gold, .05f);
        for (int i = 0; i < 4; i++) Line(mesh, corners[i], corners[(i + 1) % 4], 2.5f, PaleGold);
        Glow(mesh, new Vector2(-63, y), 13, Gold, .1f);
        Glow(mesh, new Vector2(-63, y), 4, PaleGold, .7f);
    }

    static Color WithAlpha(Color c, float alpha) { c.a = alpha; return c; }

    static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
    {
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * width * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(from - normal, tint, Vector2.zero);
        mesh.AddVert(from + normal, tint, Vector2.zero);
        mesh.AddVert(to + normal, tint, Vector2.zero);
        mesh.AddVert(to - normal, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(a, ca, Vector2.zero); mesh.AddVert(b, cb, Vector2.zero); mesh.AddVert(c, cc, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }

    static void Glow(VertexHelper mesh, Vector2 p, float radius, Color tint, float opacity)
    {
        const int sides = 16;
        int start = mesh.currentVertCount;
        mesh.AddVert(p, WithAlpha(tint, opacity), Vector2.zero);
        for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                Vector2 offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * (ring == 0 ? .28f : 1);
                mesh.AddVert(p + offset, WithAlpha(tint, ring == 0 ? opacity * .25f : 0), Vector2.zero);
            }
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            mesh.AddTriangle(start, start + 1 + i, start + 1 + next);
            mesh.AddTriangle(start + 1 + i, start + 1 + sides + i, start + 1 + sides + next);
            mesh.AddTriangle(start + 1 + i, start + 1 + sides + next, start + 1 + next);
        }
    }
}
