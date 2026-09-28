using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(1150)]
public sealed class CaveFireflies : MonoBehaviour
{
    const int MaximumActiveFlies = 90;
    const float DespawnMargin = 2.5f;
    const float GlowRadius = .105f;
    const string GeneratedName = "Cave Fireflies (generated)";

    sealed class Firefly
    {
        public Vector2 anchor;
        public float colorPosition, phase, age, size, fade = 1f;
    }

    public MapGenerator map;
    public Material material;

    readonly List<Firefly> flies = new List<Firefly>();
    readonly System.Random random = new System.Random();
    CritterMesh geometry;
    Material runtimeMaterial;
    MapGenerator subscribedMap;
    float nextSpawn;

    public int ActiveCount => flies.Count;

    void Awake()
    {
        if (!map) map = GetComponent<MapGenerator>();
    }

    void OnEnable()
    {
        if (!map) map = GetComponent<MapGenerator>();
        CritterMesh.RemoveGenerated(transform, GeneratedName);
        if (!material)
        {
            var surfaceFlies = FindFirstObjectByType<SurfaceFireflies>();
            if (surfaceFlies) material = surfaceFlies.material;
        }
        if (material) runtimeMaterial = new Material(material);
        else
        {
            var shader = Shader.Find("Mining/Firefly Glow");
            if (shader) runtimeMaterial = new Material(shader);
        }
        if (runtimeMaterial) geometry = new CritterMesh(transform, GeneratedName, runtimeMaterial, 23);
        if (runtimeMaterial && runtimeMaterial.HasProperty("_CoreColorMix"))
            runtimeMaterial.SetFloat("_CoreColorMix", .22f);
        if (map) { subscribedMap = map; map.Generated += ResetPopulation; }
        ResetPopulation();
    }

    void OnDisable()
    {
        if (subscribedMap) subscribedMap.Generated -= ResetPopulation;
        subscribedMap = null;
        flies.Clear();
        geometry?.Dispose();
        geometry = null;
        if (runtimeMaterial)
        {
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
        }
        runtimeMaterial = null;
    }

    void ResetPopulation()
    {
        flies.Clear();
        nextSpawn = 0f;
    }

    void Update() => Tick(Time.deltaTime);

    void Tick(float deltaTime)
    {
        if (geometry == null) return;
        geometry.Clear();
        if (!map || !map.IsGenerated || map.IsGenerationStreaming || !map.caveGeneration.enabled)
        {
            flies.Clear();
            geometry.Upload();
            return;
        }

        if (runtimeMaterial && runtimeMaterial.HasProperty("_Brightness"))
            runtimeMaterial.SetFloat("_Brightness", Mathf.Clamp(map.caveFireflyBrightness, 0f, 200f) / 100f);

        if (!TryGetCameraBounds(Camera.main, out var view))
        {
            flies.Clear();
            geometry.Upload();
            return;
        }

        for (int i = flies.Count - 1; i >= 0; i--)
        {
            var fly = flies[i];
            fly.age += deltaTime;
            var cell = map.Terrain.WorldToCell(fly.anchor);
            bool valid = map.IsGeneratedCaveCell(cell);
            bool inView = valid && view.Contains(fly.anchor, DespawnMargin);
            fly.fade = Mathf.MoveTowards(fly.fade, inView ? 1f : 0f, deltaTime / .45f);
            if (!valid || fly.fade <= 0f)
            {
                flies.RemoveAt(i);
                continue;
            }

            float t = fly.age * .55f + fly.phase;
            var position = fly.anchor + new Vector2(Mathf.Sin(t * .73f) * .16f + Mathf.Sin(t * 1.41f) * .055f,
                Mathf.Sin(t * .91f) * .12f + Mathf.Cos(t * .43f) * .045f);
            if (!map.IsGeneratedCaveCell(map.Terrain.WorldToCell(position))) position = fly.anchor;
            float pulse = .28f + .72f * Mathf.Pow(.5f + .5f * Mathf.Sin(fly.age * 2.2f + fly.phase), 2f);
            Color color = SpectrumColor(fly.colorPosition);
            color.a = Mathf.Clamp01(fly.fade * pulse * .9f);
            geometry.Begin(position, 1f, 1f, Color.white);
            geometry.Quad(GlowRadius * fly.size, color);
        }

        float spawnRate = Mathf.Clamp(map.caveFireflySpawnRate, 0f, 60f);
        if (spawnRate <= 0f)
            nextSpawn = 0f;
        else
        {
            nextSpawn -= deltaTime;
            if (nextSpawn <= 0f)
            {
                bool canSpawn = flies.Count < MaximumActiveFlies && TrySpawnGroup(view);
                float interval = 60f / spawnRate;
                nextSpawn = canSpawn ? interval * Random(.72f, 1.28f) : Mathf.Min(1f, interval);
            }
        }

        geometry.Upload();
    }

    bool TrySpawnGroup(ViewBounds view)
    {
        var terrain = map.Terrain;
        int mapMinX = -map.GeneratedWidth / 2;
        int mapMaxX = mapMinX + map.GeneratedWidth - 1;
        int mapMinY = 1 - map.GeneratedHeight;
        int mapMaxY = 0;
        var minCell = terrain.WorldToCell(new Vector3(view.minX, view.minY, terrain.transform.position.z));
        var maxCell = terrain.WorldToCell(new Vector3(view.maxX, view.maxY, terrain.transform.position.z));
        int minX = Mathf.Max(mapMinX, Mathf.Min(minCell.x, maxCell.x));
        int maxX = Mathf.Min(mapMaxX, Mathf.Max(minCell.x, maxCell.x));
        int minY = Mathf.Max(mapMinY, Mathf.Min(minCell.y, maxCell.y));
        int maxY = Mathf.Min(mapMaxY, Mathf.Max(minCell.y, maxCell.y));
        if (maxX < minX || maxY < minY) return false;

        Vector3Int anchorCell = default;
        bool found = false;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            var candidate = new Vector3Int(Random(minX, maxX + 1), Random(minY, maxY + 1), 0);
            if (!map.IsGeneratedCaveCell(candidate)) continue;
            if (TooCloseToExisting(terrain.GetCellCenterWorld(candidate))) continue;
            anchorCell = candidate;
            found = true;
            break;
        }
        if (!found) return false;

        bool grouped = random.NextDouble() < .7d;
        int minimum = Mathf.Clamp(Mathf.Min(map.caveFireflyGroupSizeMin, map.caveFireflyGroupSizeMax), 1, 12);
        int maximum = Mathf.Clamp(Mathf.Max(map.caveFireflyGroupSizeMin, map.caveFireflyGroupSizeMax), minimum, 12);
        int desired = grouped ? Random(minimum, maximum + 1) : 1;
        AddFirefly(terrain.GetCellCenterWorld(anchorCell));

        for (int i = 1; i < desired && flies.Count < MaximumActiveFlies; i++)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                int dx = Random(-3, 4), dy = Random(-2, 3);
                if (dx == 0 && dy == 0) continue;
                var candidate = anchorCell + new Vector3Int(dx, dy, 0);
                if (candidate.x < minX || candidate.x > maxX || candidate.y < minY || candidate.y > maxY ||
                    !map.IsGeneratedCaveCell(candidate)) continue;
                var position = terrain.GetCellCenterWorld(candidate);
                if (TooCloseToExisting(position)) continue;
                AddFirefly(position);
                break;
            }
        }
        return true;
    }

    bool TooCloseToExisting(Vector2 position)
    {
        for (int i = 0; i < flies.Count; i++)
            if (Vector2.SqrMagnitude(flies[i].anchor - position) < .18f * .18f) return true;
        return false;
    }

    void AddFirefly(Vector2 position)
    {
        flies.Add(new Firefly
        {
            anchor = position,
            colorPosition = (float)random.NextDouble(),
            phase = Random(0f, Mathf.PI * 2f),
            age = Random(0f, 8f),
            size = Random(.75f, 1.2f)
        });
    }

    Color SpectrumColor(float position)
    {
        if (position < .5f) return Color.Lerp(map.caveFireflyBlue, map.caveFireflyGreen, position * 2f);
        return Color.Lerp(map.caveFireflyGreen, map.caveFireflyYellow, (position - .5f) * 2f);
    }

    bool TryGetCameraBounds(Camera camera, out ViewBounds bounds)
    {
        bounds = default;
        if (!camera) return false;
        var plane = new Plane(Vector3.forward, map.transform.position);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            var ray = camera.ViewportPointToRay(new Vector3(corner & 1, corner >> 1, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            var point = ray.GetPoint(distance);
            minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
        }
        bounds = new ViewBounds(minX, maxX, minY, maxY);
        return maxX > minX && maxY > minY;
    }

    int Random(int minimumInclusive, int maximumExclusive) => minimumInclusive >= maximumExclusive
        ? minimumInclusive : minimumInclusive + random.Next(maximumExclusive - minimumInclusive);

    float Random(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, (float)random.NextDouble());

    readonly struct ViewBounds
    {
        public readonly float minX, maxX, minY, maxY;
        public ViewBounds(float minX, float maxX, float minY, float maxY)
        { this.minX = minX; this.maxX = maxX; this.minY = minY; this.maxY = maxY; }
        public bool Contains(Vector2 point, float margin) => point.x >= minX - margin && point.x <= maxX + margin &&
            point.y >= minY - margin && point.y <= maxY + margin;
    }
}
