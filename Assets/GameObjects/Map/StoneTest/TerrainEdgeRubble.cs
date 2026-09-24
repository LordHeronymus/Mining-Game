using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Small, lit geometry projects beyond tile bounds; it has no collision.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class TerrainEdgeRubble : MonoBehaviour
{
    const int MeshChunkSize = 16;

    sealed class ChunkMesh
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    MapGenerator map;
    GameObject visual;
    Material edgeMaterial;
    BoundsInt lastBounds;
    bool hasLastBounds;
    TerrainCollisionShape shape;
    Vector4 lastSettings;
    bool lastMasked;
    int lastSeed;
    readonly Dictionary<Vector2Int, ChunkMesh> chunks = new();
    readonly HashSet<Vector2Int> dirtyChunks = new();
    readonly List<Vector2Int> removeChunks = new();
    readonly List<Vector3> vertices = new();
    readonly List<Color> colors = new();
    readonly List<Vector2> uvs = new();
    readonly List<Vector4> edgeAnchors = new();
    readonly List<int> indices = new();
    readonly List<Vector2> sideStones = new();
    Vector4 currentEdge;
    static readonly Vector3Int[] directions = { Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up };

    void OnEnable()
    {
        map = GetComponent<MapGenerator>();
        Tilemap.tilemapTileChanged += Changed;
        hasLastBounds = false;
    }

    void Changed(Tilemap tiles, Tilemap.SyncTile[] changes)
    {
        if (!map || tiles != map.Terrain || changes == null || !hasLastBounds) return;
        foreach (var change in changes)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var cell = change.position + new Vector3Int(dx, dy, 0);
            if (lastBounds.Contains(cell)) dirtyChunks.Add(ChunkOf(cell));
        }
    }

    public void Apply(MaterialPropertyBlock props, Camera view = null)
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
        var lo = map.Terrain.WorldToCell(p - new Vector3(w, h, 0));
        var hi = map.Terrain.WorldToCell(p + new Vector3(w, h, 0));
        var bounds = new BoundsInt(lo.x, lo.y, 0, hi.x - lo.x + 1, hi.y - lo.y + 1, 1);
        if (!hasLastBounds)
        {
            lastBounds = bounds;
            hasLastBounds = true;
        }
        else if (bounds != lastBounds)
        {
            MarkDifference(lastBounds, bounds);
            lastBounds = bounds;
        }

        RemoveChunksOutside(bounds);
        EnsureChunksInside(bounds);
        foreach (var pair in chunks)
        {
            var part = pair.Value;
            part.renderer.sharedMaterial = edgeMaterial;
            part.renderer.sortingLayerID = source.sortingLayerID;
            part.renderer.sortingOrder = source.sortingOrder - 1;
            part.renderer.SetPropertyBlock(props);
        }
        foreach (var key in dirtyChunks)
            if (chunks.TryGetValue(key, out var part)) BuildChunk(key, part, bounds, settings, masked, appearance);
        dirtyChunks.Clear();
    }

    void EnsureChunksInside(BoundsInt bounds)
    {
        int minX = Mathf.FloorToInt(bounds.xMin / (float)MeshChunkSize);
        int maxX = Mathf.FloorToInt((bounds.xMax - 1) / (float)MeshChunkSize);
        int minY = Mathf.FloorToInt(bounds.yMin / (float)MeshChunkSize);
        int maxY = Mathf.FloorToInt((bounds.yMax - 1) / (float)MeshChunkSize);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            var key = new Vector2Int(x, y);
            if (chunks.ContainsKey(key)) continue;
            var child = new GameObject($"Terrain rubble {x}, {y}");
            child.hideFlags = HideFlags.HideAndDontSave;
            child.layer = gameObject.layer;
            child.transform.SetParent(visual.transform, false);
            var mesh = new Mesh { name = $"Terrain rubble {x}, {y}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            chunks.Add(key, new ChunkMesh { gameObject = child, mesh = mesh, renderer = renderer });
            dirtyChunks.Add(key);
        }
    }

    void RemoveChunksOutside(BoundsInt bounds)
    {
        removeChunks.Clear();
        foreach (var pair in chunks)
        {
            int left = pair.Key.x * MeshChunkSize, bottom = pair.Key.y * MeshChunkSize;
            bool intersects = left < bounds.xMax && left + MeshChunkSize > bounds.xMin &&
                              bottom < bounds.yMax && bottom + MeshChunkSize > bounds.yMin;
            if (!intersects) removeChunks.Add(pair.Key);
        }
        foreach (var key in removeChunks)
        {
            var part = chunks[key];
            part.renderer.enabled = false;
            if (Application.isPlaying)
            {
                Destroy(part.gameObject);
                Destroy(part.mesh);
            }
            else
            {
                DestroyImmediate(part.gameObject);
                DestroyImmediate(part.mesh);
            }
            chunks.Remove(key);
            dirtyChunks.Remove(key);
        }
    }

    void BuildChunk(Vector2Int key, ChunkMesh part, BoundsInt viewBounds, Vector4 settings, bool masked,
        UniformStoneAppearance appearance)
    {
        int left = key.x * MeshChunkSize, bottom = key.y * MeshChunkSize;
        int xMin = Mathf.Max(left, viewBounds.xMin), yMin = Mathf.Max(bottom, viewBounds.yMin);
        int xMax = Mathf.Min(left + MeshChunkSize, viewBounds.xMax), yMax = Mathf.Min(bottom + MeshChunkSize, viewBounds.yMax);
        vertices.Clear(); colors.Clear(); uvs.Clear(); edgeAnchors.Clear(); indices.Clear();
        float size = map.Terrain.layoutGrid.cellSize.x;
        for (int y = yMin; y < yMax; y++)
        for (int x = xMin; x < xMax; x++)
        {
            var cell = new Vector3Int(x, y, 0);
            if (!map.Terrain.HasTile(cell)) continue;
            Vector3 center = map.Terrain.GetCellCenterWorld(cell);
            for (int side = 0; side < 4; side++)
            {
                var direction = directions[side];
                if (map.Terrain.HasTile(cell + direction)) continue;
                // Preserve the grassy skyline; rubble decorates excavated walls and floors.
                if (cell.y == 0 && side == 3) continue;
                Vector3 normal = (Vector3)direction, tangent = new Vector3(-normal.y, normal.x, 0);
                float edgeIndex = side == 0 ? cell.x : side == 1 ? cell.x + 1 : side == 2 ? cell.y : cell.y + 1;
                currentEdge = new Vector4(normal.x, normal.y, edgeIndex, masked ? 0 : 1);
                sideStones.Clear();
                var along = Vector3Int.RoundToInt(tangent);
                bool startCorner = !map.Terrain.HasTile(cell - along), endCorner = !map.Terrain.HasTile(cell + along);
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
                    if (masked && shape != null && shape.Ready && appearance && appearance.edgeDepth > 0)
                    {
                        int bits = shape.Neighbours(cell), variant = TerrainCollisionShape.Variant(cell);
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
        part.mesh.Clear();
        part.mesh.SetVertices(vertices); part.mesh.SetColors(colors); part.mesh.SetUVs(0, uvs);
        part.mesh.SetUVs(1, edgeAnchors); part.mesh.SetTriangles(indices, 0); part.mesh.RecalculateBounds();
        var meshBounds = part.mesh.bounds;
        meshBounds.Expand(size);
        part.mesh.bounds = meshBounds;
    }

    void MarkAllChunksDirty()
    {
        foreach (var key in chunks.Keys) dirtyChunks.Add(key);
    }

    void MarkDifference(BoundsInt a, BoundsInt b)
    {
        MarkDifferenceOneWay(a, b);
        MarkDifferenceOneWay(b, a);
    }

    void MarkDifferenceOneWay(BoundsInt from, BoundsInt subtract)
    {
        int ix0 = Mathf.Max(from.xMin, subtract.xMin), iy0 = Mathf.Max(from.yMin, subtract.yMin);
        int ix1 = Mathf.Min(from.xMax, subtract.xMax), iy1 = Mathf.Min(from.yMax, subtract.yMax);
        if (ix0 >= ix1 || iy0 >= iy1) { MarkRect(from.xMin, from.yMin, from.xMax, from.yMax); return; }
        MarkRect(from.xMin, from.yMin, ix0, from.yMax);
        MarkRect(ix1, from.yMin, from.xMax, from.yMax);
        MarkRect(ix0, from.yMin, ix1, iy0);
        MarkRect(ix0, iy1, ix1, from.yMax);
    }

    void MarkRect(int xMin, int yMin, int xMax, int yMax)
    {
        if (xMin >= xMax || yMin >= yMax) return;
        int minX = Mathf.FloorToInt(xMin / (float)MeshChunkSize), maxX = Mathf.FloorToInt((xMax - 1) / (float)MeshChunkSize);
        int minY = Mathf.FloorToInt(yMin / (float)MeshChunkSize), maxY = Mathf.FloorToInt((yMax - 1) / (float)MeshChunkSize);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++) dirtyChunks.Add(new Vector2Int(x, y));
    }

    static Vector2Int ChunkOf(Vector3Int cell)
        => new(Mathf.FloorToInt(cell.x / (float)MeshChunkSize), Mathf.FloorToInt(cell.y / (float)MeshChunkSize));

    void Pebble(Vector3 world, float radius, uint hash, Vector3 normal, Vector3 tangent)
    {
        const int count = 12; int start = vertices.Count;
        Add(world, new Color(1.03f, 1.01f, .98f, .25f));
        for (int n = 0; n < count; n++)
        {
            float angle = n * Mathf.PI * 2 / count;
            float jitter = n == 3 || n == 9 ? 1 : .78f + ((hash >> ((n * 3) % 29)) & 7) / 7f * .22f;
            var delta = tangent * (Mathf.Cos(angle) * radius * jitter) + normal * (Mathf.Sin(angle) * radius * jitter * .85f);
            float shade = .84f + .10f * delta.y / radius - .05f * delta.x / radius;
            Add(world + delta, new Color(shade, shade, shade, .25f));
            indices.Add(start); indices.Add(start + 1 + n); indices.Add(start + 1 + (n + 1) % count);
        }
    }

    void Add(Vector3 world, Color color)
    {
        vertices.Add(transform.InverseTransformPoint(world)); colors.Add(color); uvs.Add(Vector2.one * .5f); edgeAnchors.Add(currentEdge);
    }

    void OnDisable()
    {
        Tilemap.tilemapTileChanged -= Changed;
        shape?.Dispose(); shape = null;
        foreach (var part in chunks.Values)
        {
            if (Application.isPlaying) { Destroy(part.gameObject); Destroy(part.mesh); }
            else { DestroyImmediate(part.gameObject); DestroyImmediate(part.mesh); }
        }
        chunks.Clear(); dirtyChunks.Clear(); removeChunks.Clear();
        if (visual)
        {
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
        }
        visual = null; hasLastBounds = false;
    }
}
