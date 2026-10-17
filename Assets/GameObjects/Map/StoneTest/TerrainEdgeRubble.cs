using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

// Small, lit geometry projects beyond tile bounds; it has no collision.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class TerrainEdgeRubble : MonoBehaviour
{
    const int MeshChunkSize = 16, CellsPerChunk = MeshChunkSize * MeshChunkSize;
    // A cell's stones depend on its 3x3 neighbourhood, so a chunk reads its cells plus a one-cell border.
    const int OccupancySize = MeshChunkSize + 2;
    const int PebbleVertices = 13, PebbleIndices = 36;

    sealed class ChunkMesh
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
        // Generated geometry of every cell in row order. A mined block regenerates only the cells whose exposed
        // edges it changes and copies the rest.
        public List<Vector3> vertices = new();
        public List<Color> colors = new();
        public List<Vector4> edges = new();
        public readonly int[] cellStart = new int[CellsPerChunk + 1];
        public readonly bool[] dirtyCells = new bool[CellsPerChunk];
        public bool allDirty = true;
    }

    // Chunks are built completely (independent of the camera) for the view plus one chunk of margin and kept
    // until they are two chunks away, so camera movement alone never rebuilds geometry. Margin chunks are
    // prepared within a small time budget per frame before they become visible. While the loading screen covers
    // the world every chunk counts as margin.
    const double PrebuildMilliseconds = 1.0;
    MapGenerator map;
    GameObject visual;
    Material edgeMaterial;
    TerrainCollisionShape shape;
    Vector4 lastSettings;
    bool lastMasked;
    int lastSeed;
    MaterialPropertyBlock appliedProperties;
    readonly Dictionary<Vector2Int, ChunkMesh> chunks = new();
    readonly HashSet<Vector2Int> dirtyChunks = new();
    readonly List<Vector2Int> removeChunks = new();
    readonly List<Vector2Int> buildOrder = new();
    readonly Stack<ChunkMesh> pool = new();
    // Scratch geometry of the chunk being built; swapped with the chunk's own lists afterwards.
    List<Vector3> vertices = new();
    List<Color> colors = new();
    List<Vector4> edgeAnchors = new();
    readonly List<Vector2> uvs = new();
    readonly List<int> indices = new();
    readonly int[] cellStart = new int[CellsPerChunk + 1];
    readonly List<Vector2> sideStones = new();
    readonly TileBase[] tileBuffer = new TileBase[OccupancySize * OccupancySize];
    readonly bool[] occupied = new bool[OccupancySize * OccupancySize];
    int occupancyX, occupancyY;
    Matrix4x4 worldToLocal;
    Vector4 currentEdge;
    readonly Stopwatch buildClock = new();
    static readonly Vector3Int[] directions = { Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up };

    void OnEnable()
    {
        map = GetComponent<MapGenerator>();
        Tilemap.tilemapPositionsChanged += Changed;
    }

    void Changed(Tilemap tiles, NativeArray<Vector3Int> positions)
    {
        if (!map || tiles != map.Terrain || chunks.Count == 0) return;
        foreach (var position in positions)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var cell = position + new Vector3Int(dx, dy, 0);
            var key = ChunkOf(cell);
            if (!chunks.TryGetValue(key, out var part)) continue;
            part.dirtyCells[(cell.y - key.y * MeshChunkSize) * MeshChunkSize + cell.x - key.x * MeshChunkSize] = true;
            dirtyChunks.Add(key);
        }
    }

    public void Apply(MaterialPropertyBlock props, bool propertiesChanged, Camera view = null)
    {
        var camera = view ? view : Camera.main;
        if (!enabled || !map || !camera) return;
        var appearance = GetComponent<UniformStoneAppearance>();
        var settings = appearance ? new Vector4(appearance.rubbleAmount, appearance.rubbleMinSize,
            appearance.rubbleMaxSize, appearance.rubbleProtrusionPercent) : new Vector4(2, .08f, .24f, 50);
        bool masked = appearance && appearance.useTerrainMasks;
        if (masked && (shape == null || !shape.Matches))
        {
            shape?.Dispose();
            shape = new TerrainCollisionShape(appearance, map.Terrain);
            MarkAllChunksDirty();
        }
        if (settings != lastSettings || masked != lastMasked || map.ActiveSeed != lastSeed)
            MarkAllChunksDirty();
        lastSettings = settings;
        lastMasked = masked;
        lastSeed = map.ActiveSeed;

        if (!visual)
        {
            visual = new GameObject("Terrain edge rubble");
            visual.hideFlags = HideFlags.HideAndDontSave;
            visual.layer = gameObject.layer;
            visual.transform.SetParent(transform, false);
            MarkAllChunksDirty();
        }
        var source = map.Terrain.GetComponent<TilemapRenderer>();
        if (!edgeMaterial) edgeMaterial = Resources.Load<Material>("TerrainEdgeRubble");
        if (!edgeMaterial || !source) return;

        float h = camera.orthographicSize + 2, w = h * camera.aspect + 2;
        var p = camera.transform.position;
        var lo = ChunkOf(map.Terrain.WorldToCell(p - new Vector3(w, h, 0)));
        var hi = ChunkOf(map.Terrain.WorldToCell(p + new Vector3(w, h, 0)));

        RemoveChunksOutside(lo - Vector2Int.one * 2, hi + Vector2Int.one * 2);
        EnsureChunksInside(lo - Vector2Int.one, hi + Vector2Int.one, source, props);
        if (propertiesChanged || appliedProperties != props)
        {
            foreach (var part in chunks.Values) part.renderer.SetPropertyBlock(props);
            appliedProperties = props;
        }
        if (dirtyChunks.Count == 0) return;
        // Visible chunks are built at once; margin chunks within the time budget, nearest first.
        buildOrder.Clear();
        buildOrder.AddRange(dirtyChunks);
        var center = (lo + hi) / 2;
        buildOrder.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
        bool covered = Application.isPlaying && LoadingProgress.Active;
        worldToLocal = transform.worldToLocalMatrix;
        buildClock.Restart();
        foreach (var key in buildOrder)
        {
            bool visible = !covered && key.x >= lo.x && key.x <= hi.x && key.y >= lo.y && key.y <= hi.y;
            if (!visible && buildClock.Elapsed.TotalMilliseconds >= PrebuildMilliseconds) continue;
            if (chunks.TryGetValue(key, out var part)) BuildChunk(key, part, settings, masked, appearance);
            dirtyChunks.Remove(key);
        }
    }

    void EnsureChunksInside(Vector2Int min, Vector2Int max, TilemapRenderer source, MaterialPropertyBlock props)
    {
        for (int y = min.y; y <= max.y; y++)
        for (int x = min.x; x <= max.x; x++)
        {
            var key = new Vector2Int(x, y);
            if (chunks.ContainsKey(key)) continue;
            var part = pool.Count > 0 ? pool.Pop() : CreateChunk();
            part.gameObject.SetActive(true);
            part.mesh.Clear();
            part.renderer.sharedMaterial = edgeMaterial;
            part.renderer.sortingLayerID = source.sortingLayerID;
            part.renderer.sortingOrder = source.sortingOrder - 1;
            part.renderer.SetPropertyBlock(props);
            part.allDirty = true;
            chunks.Add(key, part);
            dirtyChunks.Add(key);
        }
    }

    ChunkMesh CreateChunk()
    {
        var child = new GameObject("Terrain rubble chunk");
        child.hideFlags = HideFlags.HideAndDontSave;
        child.layer = gameObject.layer;
        child.transform.SetParent(visual.transform, false);
        var mesh = new Mesh { name = "Terrain rubble chunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        return new ChunkMesh { gameObject = child, mesh = mesh, renderer = child.AddComponent<MeshRenderer>() };
    }

    void RemoveChunksOutside(Vector2Int min, Vector2Int max)
    {
        removeChunks.Clear();
        foreach (var key in chunks.Keys)
            if (key.x < min.x || key.x > max.x || key.y < min.y || key.y > max.y) removeChunks.Add(key);
        foreach (var key in removeChunks)
        {
            var part = chunks[key];
            part.gameObject.SetActive(false);
            pool.Push(part);
            chunks.Remove(key);
            dirtyChunks.Remove(key);
        }
    }

    void BuildChunk(Vector2Int key, ChunkMesh part, Vector4 settings, bool masked, UniformStoneAppearance appearance)
    {
        int xMin = key.x * MeshChunkSize, yMin = key.y * MeshChunkSize;
        ReadOccupancy(xMin - 1, yMin - 1);
        vertices.Clear(); colors.Clear(); edgeAnchors.Clear();
        float size = map.Terrain.layoutGrid.cellSize.x;
        bool useShape = masked && shape != null && shape.Ready && appearance && appearance.edgeDepth > 0;
        for (int index = 0; index < CellsPerChunk; index++)
        {
            cellStart[index] = vertices.Count;
            if (part.allDirty || part.dirtyCells[index])
                BuildCell(new Vector3Int(xMin + index % MeshChunkSize, yMin + index / MeshChunkSize, 0), settings, masked, useShape, size);
            else
                for (int vertex = part.cellStart[index]; vertex < part.cellStart[index + 1]; vertex++)
                {
                    vertices.Add(part.vertices[vertex]); colors.Add(part.colors[vertex]); edgeAnchors.Add(part.edges[vertex]);
                }
        }
        cellStart[CellsPerChunk] = vertices.Count;
        System.Array.Copy(cellStart, part.cellStart, cellStart.Length);
        System.Array.Clear(part.dirtyCells, 0, CellsPerChunk);
        part.allDirty = false;
        (part.vertices, vertices) = (vertices, part.vertices);
        (part.colors, colors) = (colors, part.colors);
        (part.edges, edgeAnchors) = (edgeAnchors, part.edges);
        UploadMesh(part, size);
    }

    void UploadMesh(ChunkMesh part, float size)
    {
        int count = part.vertices.Count, pebbles = count / PebbleVertices;
        // Every pebble is the same triangle fan, so the index and UV lists only grow.
        while (uvs.Count < count) uvs.Add(Vector2.one * .5f);
        for (int pebble = indices.Count / PebbleIndices; pebble < pebbles; pebble++)
        {
            int start = pebble * PebbleVertices;
            for (int n = 0; n < PebbleVertices - 1; n++)
            { indices.Add(start); indices.Add(start + 1 + n); indices.Add(start + 1 + (n + 1) % (PebbleVertices - 1)); }
        }
        part.mesh.Clear();
        part.mesh.SetVertices(part.vertices); part.mesh.SetColors(part.colors); part.mesh.SetUVs(0, uvs, 0, count);
        part.mesh.SetUVs(1, part.edges); part.mesh.SetTriangles(indices, 0, pebbles * PebbleIndices, 0);
        part.mesh.RecalculateBounds();
        var meshBounds = part.mesh.bounds;
        meshBounds.Expand(size);
        part.mesh.bounds = meshBounds;
    }

    // One block read instead of a native HasTile call for every neighbour test.
    void ReadOccupancy(int x, int y)
    {
        occupancyX = x; occupancyY = y;
        map.Terrain.GetTilesBlockNonAlloc(new BoundsInt(x, y, 0, OccupancySize, OccupancySize, 1), tileBuffer);
        for (int i = 0; i < tileBuffer.Length; i++) occupied[i] = tileBuffer[i];
        System.Array.Clear(tileBuffer, 0, tileBuffer.Length);
    }

    bool HasTile(Vector3Int cell) => occupied[(cell.y - occupancyY) * OccupancySize + cell.x - occupancyX];

    // Same bits as TerrainCollisionShape.Neighbours, read from the chunk's occupancy.
    int Neighbours(Vector3Int cell)
    {
        int bits = 0;
        for (int i = 0; i < 8; i++) if (!HasTile(cell + TerrainCollisionShape.NeighbourOffsets[i])) bits |= 1 << i;
        if (cell.y == 0) bits &= ~(8 | 32 | 128);
        return bits;
    }

    void BuildCell(Vector3Int cell, Vector4 settings, bool masked, bool useShape, float size)
    {
        if (!HasTile(cell)) return;
        Vector3 center = map.Terrain.GetCellCenterWorld(cell);
        int bits = useShape ? Neighbours(cell) : 0, variant = useShape ? TerrainCollisionShape.Variant(cell) : 0;
        for (int side = 0; side < 4; side++)
        {
            var direction = directions[side];
            if (HasTile(cell + direction)) continue;
            // Preserve the grassy skyline; rubble decorates excavated walls and floors.
            if (cell.y == 0 && side == 3) continue;
            Vector3 normal = (Vector3)direction, tangent = new Vector3(-normal.y, normal.x, 0);
            float edgeIndex = side == 0 ? cell.x : side == 1 ? cell.x + 1 : side == 2 ? cell.y : cell.y + 1;
            currentEdge = new Vector4(normal.x, normal.y, edgeIndex, masked ? 0 : 1);
            sideStones.Clear();
            var along = Vector3Int.RoundToInt(tangent);
            bool startCorner = !HasTile(cell - along), endCorner = !HasTile(cell + along);
            float amount = Mathf.Clamp(settings.x, 0, 8);
            uint countHash = OreVeins.Hash(map.ActiveSeed, cell.x, cell.y, (uint)(7717 + side * 97));
            int count = Mathf.FloorToInt(amount) + ((countHash & 65535) / 65536f < amount % 1 ? 1 : 0);
            for (int attempt = 0; attempt < count; attempt++)
            {
                uint hash = OreVeins.Hash(map.ActiveSeed, cell.x, cell.y, (uint)(7919 + side * 97 + attempt * 311));
                float minSize = Mathf.Clamp(settings.y, .02f, .6f), maxSize = Mathf.Clamp(settings.z, minSize, .6f);
                float radius = .5f * Mathf.Lerp(minSize, maxSize, ((hash >> 8) & 255) / 255f);
                float minOffset = -.48f + radius + (startCorner ? .06f : 0), maxOffset = .48f - radius - (endCorner ? .06f : 0);
                if (minOffset > maxOffset) continue;
                float offset = Mathf.Lerp(minOffset, maxOffset, ((hash >> 16) & 255) / 255f);
                bool overlaps = false;
                foreach (var stone in sideStones)
                    if (Mathf.Abs(stone.x - offset) < stone.y + radius + .025f) { overlaps = true; break; }
                if (overlaps) continue;
                sideStones.Add(new Vector2(offset, radius));
                Vector3 origin = center + normal * (size * .5f) + tangent * (offset * size);
                Vector3 stoneNormal = normal;
                if (useShape)
                {
                    Vector2 end = Vector2.one * .5f + (Vector2)(normal * .5f + tangent * offset);
                    float inner = 0, outer = 1;
                    for (int step = 0; step < 14; step++)
                    {
                        float mid = (inner + outer) * .5f;
                        if (shape.Distance(Vector2.Lerp(Vector2.one * .5f, end, mid), bits, variant, cell.y == 0) >= 0) inner = mid;
                        else outer = mid;
                    }
                    Vector2 f = Vector2.Lerp(Vector2.one * .5f, end, inner);
                    origin = map.Terrain.CellToWorld(cell) + (Vector3)f * size;
                    const float e = .001f;
                    float dx = shape.Distance(f + Vector2.right * e, bits, variant, cell.y == 0) - shape.Distance(f - Vector2.right * e, bits, variant, cell.y == 0);
                    float dy = shape.Distance(f + Vector2.up * e, bits, variant, cell.y == 0) - shape.Distance(f - Vector2.up * e, bits, variant, cell.y == 0);
                    if (dx * dx + dy * dy > .00000001f) stoneNormal = -new Vector3(dx, dy, 0).normalized;
                }
                // Match the requested visible fraction of the normal diameter. Terrain covers the rest.
                origin += stoneNormal * ((Mathf.Clamp01(settings.w * .01f) - .5f) * 2 * radius * size * .85f);
                Pebble(origin, radius * size, hash, stoneNormal, new Vector3(-stoneNormal.y, stoneNormal.x, 0));
            }
        }
    }

    void MarkAllChunksDirty()
    {
        foreach (var pair in chunks) { pair.Value.allDirty = true; dirtyChunks.Add(pair.Key); }
    }

    static Vector2Int ChunkOf(Vector3Int cell)
        => new(Mathf.FloorToInt(cell.x / (float)MeshChunkSize), Mathf.FloorToInt(cell.y / (float)MeshChunkSize));

    void Pebble(Vector3 world, float radius, uint hash, Vector3 normal, Vector3 tangent)
    {
        const int count = PebbleVertices - 1;
        Add(world, new Color(1.03f, 1.01f, .98f, .25f));
        for (int n = 0; n < count; n++)
        {
            float angle = n * Mathf.PI * 2 / count;
            float jitter = n == 3 || n == 9 ? 1 : .78f + ((hash >> ((n * 3) % 29)) & 7) / 7f * .22f;
            var delta = tangent * (Mathf.Cos(angle) * radius * jitter) + normal * (Mathf.Sin(angle) * radius * jitter * .85f);
            float shade = .84f + .10f * delta.y / radius - .05f * delta.x / radius;
            Add(world + delta, new Color(shade, shade, shade, .25f));
        }
    }

    void Add(Vector3 world, Color color)
    {
        vertices.Add(worldToLocal.MultiplyPoint3x4(world)); colors.Add(color); edgeAnchors.Add(currentEdge);
    }

    void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= Changed;
        shape?.Dispose(); shape = null;
        foreach (var part in chunks.Values) DestroyChunk(part);
        foreach (var part in pool) DestroyChunk(part);
        chunks.Clear(); dirtyChunks.Clear(); removeChunks.Clear(); pool.Clear();
        if (visual)
        {
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
        }
        visual = null; appliedProperties = null;
    }

    static void DestroyChunk(ChunkMesh part)
    {
        if (Application.isPlaying) { Destroy(part.gameObject); Destroy(part.mesh); }
        else { DestroyImmediate(part.gameObject); DestroyImmediate(part.mesh); }
    }
}
