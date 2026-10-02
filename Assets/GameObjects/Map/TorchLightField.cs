using System.Collections.Generic;
using UnityEngine;

public sealed class TorchLightField
{
    internal readonly struct CellLight
    {
        public readonly int index;
        public readonly float brightness;
        public CellLight(int index, float brightness)
        { this.index = index; this.brightness = brightness; }
    }
    public readonly struct Source
    {
        public readonly int x;
        public readonly int y;
        public readonly float intensity;

        public Source(int x, int y, float intensity)
        {
            this.x = x;
            this.y = y;
            this.intensity = intensity;
        }
    }

    const float MinimumLight = 1f / 255f;
    const float WallSurfaceLight = .75f;
    readonly int width;
    readonly int height;
    readonly bool[] solid;
    readonly float[] light;
    readonly bool[] queued;
    readonly float horizontalFalloff;
    readonly float verticalFalloff;
    readonly float diagonalFalloff;
    readonly Queue<int> pending = new Queue<int>();
    readonly List<int> litCells = new List<int>();
    readonly HashSet<int> affected = new HashSet<int>();

    public TorchLightField(int width, int height, bool[] solid, float cellWidth,
        float cellHeight, float falloffDistance)
    {
        this.width = width;
        this.height = height;
        this.solid = (bool[])solid.Clone();
        light = new float[solid.Length];
        queued = new bool[solid.Length];
        float distance = Mathf.Max(.01f, falloffDistance);
        horizontalFalloff = Mathf.Exp(-Mathf.Max(.01f, cellWidth) / distance);
        verticalFalloff = Mathf.Exp(-Mathf.Max(.01f, cellHeight) / distance);
        diagonalFalloff = Mathf.Exp(-new Vector2(cellWidth, cellHeight).magnitude / distance);
    }

    public float Get(int x, int y)
        => x < 0 || x >= width || y < 0 || y >= height ? 0f : light[y * width + x];

    public float Get(int index) => light[index];

    public bool SetSolid(int x, int y, bool value)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        int index = y * width + x;
        if (solid[index] == value) return false;
        solid[index] = value;
        return true;
    }

    public IEnumerable<int> Rebuild(IReadOnlyList<Source> sources)
    {
        affected.Clear();
        foreach (int index in litCells)
        {
            light[index] = 0f;
            affected.Add(index);
        }
        litCells.Clear();

        foreach (Source source in sources)
        {
            if (source.x < 0 || source.x >= width || source.y < 0 || source.y >= height)
                continue;
            int index = source.y * width + source.x;
            if (!solid[index]) Improve(index, source.intensity, true);
        }

        Propagate();
        return affected;
    }

    // Removing walls can only increase light. Restore a cached single-source field
    // and propagate from the changed neighbourhood instead of visiting every route again.
    internal IEnumerable<int> RebuildOpened(IReadOnlyList<CellLight> cached,
        Source source, IReadOnlyList<int> opened)
    {
        foreach (int index in litCells) light[index] = 0f;
        litCells.Clear();
        affected.Clear();
        foreach (var cell in cached)
        {
            light[cell.index] = cell.brightness;
            litCells.Add(cell.index);
        }
        if (!IsSolid(source.x, source.y))
            Improve(source.y * width + source.x, source.intensity, true);
        foreach (int index in opened)
        {
            int x = index % width, y = index / width;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (IsSolid(nx, ny)) continue;
                int next = ny * width + nx;
                if (light[next] <= 0f || queued[next]) continue;
                queued[next] = true;
                pending.Enqueue(next);
            }
        }
        Propagate();
        return litCells;
    }

    void Propagate()
    {
        while (pending.Count > 0)
        {
            int index = pending.Dequeue();
            queued[index] = false;
            int x = index % width, y = index / width;
            float value = light[index];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    bool isWall = solid[next];
                    if (dx != 0 && dy != 0)
                    {
                        bool horizontalWall = solid[y * width + nx];
                        bool verticalWall = solid[ny * width + x];
                        if (isWall ? horizontalWall && verticalWall :
                            horizontalWall || verticalWall) continue;
                    }
                    float falloff = dx == 0 ? verticalFalloff :
                        dy == 0 ? horizontalFalloff : diagonalFalloff;
                    Improve(next, value * falloff * (isWall ? WallSurfaceLight : 1f), !isWall);
                }
        }
    }

    void Improve(int index, float value, bool propagate)
    {
        if (value < MinimumLight || value <= light[index] + .00001f) return;
        if (light[index] == 0f) litCells.Add(index);
        light[index] = value;
        affected.Add(index);
        if (!propagate || queued[index]) return;
        queued[index] = true;
        pending.Enqueue(index);
    }

    public bool IsSolid(int x, int y)
        => x < 0 || x >= width || y < 0 || y >= height || solid[y * width + x];
}

public sealed class SmoothHeadlampField
{
    static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("Mining.Headlamp");
    static readonly Unity.Profiling.ProfilerMarker CacheMarker = new Unity.Profiling.ProfilerMarker("Mining.HeadlampCache");
    sealed class SampleCache
    {
        public int x = int.MinValue, y = int.MinValue;
        public float intensity;
        public bool valid;
        public readonly List<TorchLightField.CellLight> cells = new List<TorchLightField.CellLight>();
        public readonly HashSet<int> indices = new HashSet<int>();
        public readonly List<int> opened = new List<int>();
    }

    readonly int width;
    readonly int height;
    readonly TorchLightField sampleField;
    readonly float[] light;
    readonly float[] weights = new float[4];
    readonly List<int> litCells = new List<int>();
    readonly HashSet<int> affected = new HashSet<int>();
    readonly List<TorchLightField.Source> source = new List<TorchLightField.Source>(1);
    readonly SampleCache[] samples = { new SampleCache(), new SampleCache(), new SampleCache(), new SampleCache() };
    readonly SampleCache[] previousSamples = new SampleCache[4];
    readonly bool[] reused = new bool[4];
    bool terrainDirty;
    int cachedX = int.MinValue, cachedY = int.MinValue;
    float cachedIntensity = float.NaN;

    public SmoothHeadlampField(int width, int height, bool[] solid, float cellWidth,
        float cellHeight, float falloffDistance)
    {
        this.width = width;
        this.height = height;
        sampleField = new TorchLightField(width, height, solid, cellWidth, cellHeight, falloffDistance);
        light = new float[solid.Length];
    }

    public float Get(int x, int y)
        => x < 0 || x >= width || y < 0 || y >= height ? 0f : light[y * width + x];

    public float Get(int index) => light[index];

    public bool SetSolid(int x, int y, bool value)
    {
        if (!sampleField.SetSolid(x, y, value)) return false;
        int index = y * width + x;
        foreach (var cache in samples)
        {
            if (!cache.valid) continue;
            bool touches = cache.opened.Count > 0 ||
                (Mathf.Abs(x - cache.x) <= 1 && Mathf.Abs(y - cache.y) <= 1);
            for (int dy = -1; dy <= 1 && !touches; dy++)
            for (int dx = -1; dx <= 1 && !touches; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    touches = cache.indices.Contains(ny * width + nx);
            }
            if (!touches) continue;
            if (value) { cache.valid = false; cache.opened.Clear(); }
            else cache.opened.Add(index);
            terrainDirty = true;
        }
        // MapLighting only needs to blend/upload again if a cached field can change.
        return terrainDirty || cachedX == int.MinValue;
    }

    public IEnumerable<int> Rebuild(float x, float y, float intensity, bool active)
    {
        using var profile = UpdateMarker.Auto();
        affected.Clear();
        foreach (int index in litCells)
        {
            light[index] = 0f;
            affected.Add(index);
        }
        litCells.Clear();
        if (!active || intensity <= 0f) return affected;

        int floorX = Mathf.FloorToInt(x), floorY = Mathf.FloorToInt(y);
        if (terrainDirty || floorX != cachedX || floorY != cachedY ||
            !Mathf.Approximately(intensity, cachedIntensity))
        {
            CacheSamples(floorX, floorY, intensity);
            cachedX = floorX;
            cachedY = floorY;
            cachedIntensity = intensity;
            terrainDirty = false;
        }

        float fractionX = x - floorX, fractionY = y - floorY;
        int sourceCellX = Mathf.RoundToInt(x), sourceCellY = Mathf.RoundToInt(y);
        float totalWeight = 0f;
        for (int i = 0; i < 4; i++)
        {
            int anchorX = floorX + (i & 1), anchorY = floorY + (i >> 1);
            float weight = ((i & 1) == 0 ? 1f - fractionX : fractionX) *
                ((i & 2) == 0 ? 1f - fractionY : fractionY);
            if (sampleField.IsSolid(anchorX, anchorY) ||
                (anchorX != sourceCellX && anchorY != sourceCellY &&
                 sampleField.IsSolid(sourceCellX, anchorY) &&
                 sampleField.IsSolid(anchorX, sourceCellY)))
                weight = 0f;
            weights[i] = weight;
            totalWeight += weight;
        }
        if (totalWeight <= 0f) return affected;

        for (int i = 0; i < 4; i++)
        {
            float weight = weights[i] / totalWeight;
            if (weight <= 0f) continue;
            foreach (var sample in samples[i].cells)
            {
                if (light[sample.index] == 0f) litCells.Add(sample.index);
                light[sample.index] += sample.brightness * weight;
                affected.Add(sample.index);
            }
        }
        return affected;
    }

    void CacheSamples(int floorX, int floorY, float intensity)
    {
        using var profile = CacheMarker.Auto();
        // Adjacent anchor squares share two fields; retain those when the lamp moves.
        System.Array.Copy(samples, previousSamples, 4);
        System.Array.Clear(reused, 0, 4);
        System.Array.Clear(samples, 0, 4);
        for (int i = 0; i < 4; i++)
        for (int j = 0; j < 4; j++)
        {
            var cache = previousSamples[j];
            if (reused[j] || cache.x != floorX + (i & 1) || cache.y != floorY + (i >> 1)) continue;
            samples[i] = cache;
            reused[j] = true;
            break;
        }
        int spare = 0;
        for (int i = 0; i < 4; i++)
        {
            if (samples[i] != null) continue;
            while (reused[spare]) spare++;
            samples[i] = previousSamples[spare];
            reused[spare] = true;
            samples[i].valid = false;
            samples[i].opened.Clear();
        }
        for (int i = 0; i < 4; i++)
        {
            var cache = samples[i];
            int x = floorX + (i & 1), y = floorY + (i >> 1);
            bool same = cache.valid && cache.x == x && cache.y == y &&
                Mathf.Approximately(cache.intensity, intensity);
            if (same && cache.opened.Count == 0) continue;
            IEnumerable<int> lit;
            if (same)
                lit = sampleField.RebuildOpened(cache.cells, new TorchLightField.Source(x, y, intensity), cache.opened);
            else
            {
                source.Clear();
                source.Add(new TorchLightField.Source(x, y, intensity));
                lit = sampleField.Rebuild(source);
            }
            cache.cells.Clear();
            cache.indices.Clear();
            foreach (int index in lit)
            {
                float brightness = sampleField.Get(index);
                if (brightness <= 0f) continue;
                cache.cells.Add(new TorchLightField.CellLight(index, brightness));
                cache.indices.Add(index);
            }
            cache.x = x; cache.y = y; cache.intensity = intensity; cache.valid = true;
            cache.opened.Clear();
        }
    }
}
