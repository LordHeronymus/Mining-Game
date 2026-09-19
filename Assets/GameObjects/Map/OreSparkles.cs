using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Unity.Profiling;

[DisallowMultipleComponent, RequireComponent(typeof(Tilemap), typeof(MapGenerator))]
public sealed class OreSparkles : MonoBehaviour
{
    public Material sparkleMaterial;
    [Min(0.1f)] public float intervalPerBlock = 3f;
    [Min(0.1f)] public float lifetime = 1.2f;
    [Range(0.02f, 1f)] public float size = 0.65f;
    [Range(0f, 1f)] public float opacity = 0.9f;

    const int Capacity = 32;
    readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[Capacity];
    readonly System.Random random = new System.Random();
    Tilemap tiles;
    MapGenerator map;
    MapLighting lighting;
    ParticleSystem particles;
    sealed class Schedule
    {
        public Vector3Int cell;
        public float due;
    }
    sealed class ScheduleComparer : IComparer<Schedule>
    {
        public int Compare(Schedule a, Schedule b)
        {
            int order = a.due.CompareTo(b.due);
            if (order == 0) order = a.cell.x.CompareTo(b.cell.x);
            if (order == 0) order = a.cell.y.CompareTo(b.cell.y);
            if (order == 0) order = a.cell.z.CompareTo(b.cell.z);
            return order;
        }
    }
    readonly Dictionary<Vector3Int, Schedule> schedules = new Dictionary<Vector3Int, Schedule>();
    readonly SortedSet<Schedule> queue = new SortedSet<Schedule>(new ScheduleComparer());
    BoundsInt scanBounds;
    int scanIndex;
    float nextScan;
    const int ScanBudget = 512;
    const int ScheduleBudget = 64;
    static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("Mining.OreSparkles");

    void OnEnable()
    {
        tiles = GetComponent<Tilemap>();
        map = GetComponent<MapGenerator>();
        lighting = GetComponent<MapLighting>();
        if (!sparkleMaterial) return;
        var child = new GameObject("Ore sparkles (generated)");
        child.hideFlags = HideFlags.DontSave;
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0;
        main.maxParticles = Capacity;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .35f), new GradientAlphaKey(0, 1) });
        color.color = gradient;
        var scale = particles.sizeOverLifetime;
        scale.enabled = true;
        scale.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
            new Keyframe(0, .35f), new Keyframe(.4f, 1), new Keyframe(1, .3f)));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = sparkleMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Specular highlights follow the darkness overlay (32760), with explicit light attenuation.
        renderer.sortingLayerID = SortingLayer.layers[SortingLayer.layers.Length - 1].id;
        renderer.sortingOrder = 32761;
        particles.Play();
        schedules.Clear(); queue.Clear(); scanIndex = 0; nextScan = 0;
        scanBounds = new BoundsInt();
    }

    void LateUpdate()
    {
        using var sample = UpdateMarker.Auto();
        if (!particles || !map.registry) return;
        var camera = Camera.main;
        // Remove glints immediately when their ore is mined or becomes completely dark.
        int count = particles.GetParticles(buffer);
        for (int i = 0; i < count; i++)
        {
            var cell = tiles.WorldToCell(buffer[i].position);
            if (!IsOre(map.registry.FromTile(tiles.GetTile(cell))) || !IsLit(cell) ||
                !camera || !InView(camera, buffer[i].position)) buffer[i].remainingLifetime = 0;
        }
        particles.SetParticles(buffer, count);
        if (camera && camera.orthographic) Tick(camera, Time.time);
    }

    bool IsLit(Vector3Int cell) => !lighting || !lighting.isActiveAndEnabled ||
        !lighting.lightingEnabled || lighting.GetBrightness(cell) > .03f;

    public static bool IsOre(Block block) => block && (block.id == BlockType.IronOre ||
        block.id == BlockType.CopperOre || block.id == BlockType.SilverOre || block.id == BlockType.GoldOre);

    static bool InView(Camera camera, Vector3 position)
    {
        Vector3 view = camera.WorldToViewportPoint(position);
        return view.z > 0 && view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1;
    }

    void Tick(Camera camera, float now)
    {
        // Incremental discovery, not random block selection. Every eligible cell gets a turn.
        float distance = Vector3.Dot(tiles.transform.position - camera.transform.position, camera.transform.forward);
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, 0);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, 0);
        for (int i = 0; i < 4; i++)
        {
            var cell = tiles.WorldToCell(camera.ViewportToWorldPoint(new Vector3(i % 2, i / 2, distance)));
            min = Vector3Int.Min(min, cell); max = Vector3Int.Max(max, cell);
        }
        var bounds = tiles.cellBounds;
        int left = Mathf.Max(min.x, bounds.xMin), right = Mathf.Min(max.x + 1, bounds.xMax);
        int bottom = Mathf.Max(min.y, bounds.yMin), top = Mathf.Min(max.y + 1, bounds.yMax);
        var visible = new BoundsInt(left, bottom, 0, Mathf.Max(0, right-left), Mathf.Max(0, top-bottom), 1);
        if (visible != scanBounds)
        {
            scanBounds = visible; scanIndex = 0; nextScan = now;
        }
        int area = visible.size.x * visible.size.y;
        if (area > 0 && now >= nextScan)
        {
            int end = Mathf.Min(scanIndex + ScanBudget, area);
            for (; scanIndex < end; scanIndex++)
            {
                var cell = new Vector3Int(left + scanIndex % visible.size.x, bottom + scanIndex / visible.size.x, 0);
                if (schedules.ContainsKey(cell) || !IsLit(cell) ||
                    !IsOre(map.registry.FromTile(tiles.GetTile(cell))) || !InView(camera, tiles.GetCellCenterWorld(cell))) continue;
                var item = new Schedule { cell = cell, due = now + (float)random.NextDouble() * Mathf.Max(.1f, intervalPerBlock) };
                schedules.Add(cell, item); queue.Add(item);
            }
            if (scanIndex >= area) { scanIndex = 0; nextScan = now + .5f; }
            else nextScan = now + .05f;
        }
        int free = Capacity - particles.particleCount;
        for (int handled = 0; handled < ScheduleBudget && queue.Count > 0; handled++)
        {
            var item = queue.Min;
            if (item.due > now) break;
            var cell = item.cell;
            var block = map.registry.FromTile(tiles.GetTile(cell));
            if (!visible.Contains(cell) || !IsOre(block) || !IsLit(cell) || !InView(camera, tiles.GetCellCenterWorld(cell)))
            {
                queue.Remove(item); schedules.Remove(cell); continue;
            }
            // Leave overdue cells at the front when full; never replace them with random newcomers.
            if (free <= 0) break;
            queue.Remove(item);
            EmitCell(cell, block);
            free--;
            item.due = now + Mathf.Max(lifetime + .15f, intervalPerBlock * (.8f + (float)random.NextDouble() * .4f));
            queue.Add(item);
        }
    }

    void EmitCell(Vector3Int cell, Block block)
    {
            Vector3 center = tiles.GetCellCenterWorld(cell);
            var localOffset = Vector3.Scale(tiles.layoutGrid.cellSize,
                new Vector3((float)random.NextDouble() * .5f - .25f, (float)random.NextDouble() * .5f - .25f));
            Vector3 position = center + tiles.transform.TransformVector(localOffset);
            Color tint = block.id == BlockType.GoldOre ? new Color(1f, .88f, .48f) :
                block.id == BlockType.CopperOre ? new Color(1f, .7f, .48f) : new Color(.83f, .93f, 1f);
            tint = Color.Lerp(tint, Color.white, .75f);
            float brightness = lighting && lighting.isActiveAndEnabled && lighting.lightingEnabled
                ? lighting.GetBrightness(cell) : 1f;
            tint.a = opacity * Mathf.Sqrt(Mathf.Clamp01(brightness));
            float cellSize = Mathf.Min(
                tiles.transform.TransformVector(Vector3.right * tiles.layoutGrid.cellSize.x).magnitude,
                tiles.transform.TransformVector(Vector3.up * tiles.layoutGrid.cellSize.y).magnitude);
            particles.Emit(new ParticleSystem.EmitParams {
                position = position, startColor = tint, startLifetime = Mathf.Max(.1f, lifetime),
                startSize = size * cellSize * (.75f + (float)random.NextDouble() * .5f),
                rotation = (float)random.NextDouble() * 40f - 20f, velocity = Vector3.zero
            }, 1);
    }

    void OnDisable()
    {
        schedules.Clear(); queue.Clear();
        if (!particles) return;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var child = particles.gameObject;
        if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        particles = null;
    }
}
