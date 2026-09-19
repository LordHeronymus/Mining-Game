using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1150)]
public sealed class SurfaceFireflies : MonoBehaviour
{
    public MapGenerator map;
    public SkyController sky;
    public Material material;
    [Range(0, 80), InspectorName("Anzahl")] public int count = 28;
    [UnityEngine.Serialization.FormerlySerializedAs("radius")]
    [Min(0), InspectorName("Abstand ausserhalb der Kamera")] public float cameraMargin = 10;
    [InspectorName("Flughöhe")] public Vector2 altitude = new Vector2(.35f, 2.6f);
    [Min(.01f), InspectorName("Leuchtgrösse")] public float glowSize = .14f;
    [Min(.01f), InspectorName("Fluggeschwindigkeit")] public float flightSpeed = .3f;
    [Min(.1f), InspectorName("Pulsdauer (s)")] public float pulseDuration = 3.6f;
    [Min(.1f), InspectorName("Ein-/Ausblenden (s)")] public float fadeDuration = 2;
    [InspectorName("Leuchtfarbe")] public Color glowColor = new Color(.78f, 1, .24f);

    sealed class Firefly
    {
        public float anchorX, anchorY, phase, size, age, fade;
    }
    const string GeneratedName = "Fireflies (generated)";
    readonly List<Firefly> flies = new List<Firefly>();
    readonly System.Random random = new System.Random();
    CritterMesh geometry;
    MapGenerator subscribedMap;
    float visibility, referenceRetry;
    public int ActiveCount => flies.Count;
    public float Visibility => visibility;

    void OnEnable()
    {
        CritterMesh.RemoveGenerated(transform, GeneratedName);
        if (material) geometry = new CritterMesh(transform, GeneratedName, material, 23);
        if (map) { subscribedMap = map; map.Generated += Clear; }
        Clear();
    }
    void Clear() { flies.Clear(); visibility = 0; }
    void Update() => Tick(Time.deltaTime);

    void Tick(float dt)
    {
        if (geometry == null) return;
        geometry.Clear();
        referenceRetry -= dt;
        if (referenceRetry <= 0)
        {
            if (!sky) sky = FindFirstObjectByType<SkyController>();
            referenceRetry = 1;
        }
        if (!map || !map.IsGenerated) { Clear(); geometry.Upload(); return; }
        float ground = map.Terrain.CellToWorld(new Vector3Int(0, 1, 0)).y;
        var camera = Camera.main;
        bool visibleSurface = TryGetSpawnArea(camera, ground, out float left, out float right);
        bool night = sky && sky.isActiveAndEnabled && sky.IsNight && visibleSurface;
        visibility = Mathf.MoveTowards(visibility, night ? 1 : 0, dt / Mathf.Max(.1f, fadeDuration));
        if (visibility <= 0 && !night) { Clear(); geometry.Upload(); return; }
        int limit = Mathf.Clamp(count, 0, 80);
        for (int i = flies.Count - 1; i >= 0; i--)
        {
            var fly = flies[i];
            fly.age += dt;
            bool tooFar = !visibleSurface || fly.anchorX < left - 1 || fly.anchorX > right + 1;
            fly.fade = Mathf.MoveTowards(fly.fade, tooFar ? 0 : 1, dt / Mathf.Max(.1f, fadeDuration));
            if (flies.Count > limit || (tooFar && fly.fade <= 0)) { flies.RemoveAt(i); continue; }
            float t = fly.age * Mathf.Max(.01f, flightSpeed), phase = fly.phase;
            float x = fly.anchorX + Mathf.Sin(t * .85f + phase) * .65f + Mathf.Sin(t * 1.73f + phase * 3) * .2f;
            float y = fly.anchorY + Mathf.Sin(t * 1.3f + phase) * .22f + Mathf.Cos(t * .47f + phase) * .12f;
            float pulse = .16f + .84f * Mathf.Pow(.5f + .5f * Mathf.Sin(fly.age * Mathf.PI * 2 / Mathf.Max(.1f, pulseDuration) + phase), 2);
            Color color = glowColor;
            color.a *= visibility * fly.fade * pulse * (sky && sky.isActiveAndEnabled ? sky.NightBlend : 0);
            geometry.Begin(new Vector2(x, ground + Mathf.Max(.12f, y)), 1, 1, Color.white);
            geometry.Quad(Mathf.Max(.01f, glowSize) * fly.size, color);
        }
        // No new insects during dawn. Existing ones finish fading, then their state is released.
        if (night)
        {
            if (right > left)
                while (flies.Count < limit)
                    flies.Add(new Firefly { anchorX = Random(left, right), anchorY = Random(Mathf.Max(.15f, Mathf.Min(altitude.x, altitude.y)),
                        Mathf.Max(.15f, Mathf.Max(altitude.x, altitude.y))), phase = Random(0, Mathf.PI * 2), size = Random(.7f, 1.2f) });
        }
        geometry.Upload();
    }

    bool TryGetSpawnArea(Camera camera, float ground, out float left, out float right)
    {
        left = right = 0;
        if (!camera) return false;
        // Intersect the actual camera view with the animals' world plane, including camera zoom/aspect.
        var plane = new Plane(Vector3.forward, Vector3.zero);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            var ray = camera.ViewportPointToRay(new Vector3(corner & 1, corner >> 1, 0));
            if (!plane.Raycast(ray, out float distance)) return false;
            var point = ray.GetPoint(distance);
            minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
        }
        float margin = Mathf.Max(0, cameraMargin);
        var bounds = map.Terrain.cellBounds;
        left = Mathf.Max(minX - margin, map.Terrain.CellToWorld(new Vector3Int(bounds.xMin, 0, 0)).x);
        right = Mathf.Min(maxX + margin, map.Terrain.CellToWorld(new Vector3Int(bounds.xMax, 0, 0)).x);
        float lowest = ground + Mathf.Max(.12f, Mathf.Min(altitude.x, altitude.y) - .34f);
        float highest = ground + Mathf.Max(.15f, Mathf.Max(altitude.x, altitude.y)) + .34f;
        return right > left && highest >= minY - margin && lowest <= maxY + margin;
    }

    float Random(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    void OnDisable()
    {
        if (subscribedMap) subscribedMap.Generated -= Clear;
        subscribedMap = null; Clear(); geometry?.Dispose(); geometry = null;
    }
}
