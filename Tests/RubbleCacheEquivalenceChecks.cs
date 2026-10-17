using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Play Mode, after a run has loaded: TerrainEdgeRubble keeps per-cell geometry and regenerates only the cells
// around a mined block. Mines blocks near the player, lets the incremental updates run, then compares every
// chunk mesh with a from-scratch build by the original per-chunk algorithm (HasTile, TerrainCollisionShape
// neighbours, InverseTransformPoint). The meshes must match exactly.
public static class RubbleCacheEquivalenceChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int ChunkSize = 16;

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode mit geladener Welt erforderlich.");
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var rubble = map ? map.GetComponent<TerrainEdgeRubble>() : null;
        var appearance = map ? map.GetComponent<UniformStoneAppearance>() : null;
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        if (!map || !rubble || !appearance || !player || !map.IsGenerated) throw new Exception("Geladene Welt erforderlich.");
        // Mine a small cross-shaped pit below the player across a chunk border, plus single cells.
        var start = map.Terrain.WorldToCell(player.transform.position);
        var mined = new List<Vector3Int>();
        for (int i = 1; i <= 6; i++) mined.Add(start + new Vector3Int(0, -i, 0));
        for (int i = -3; i <= 3; i++) mined.Add(start + new Vector3Int(i, -6, 0));
        foreach (var cell in mined) if (map.Terrain.HasTile(cell) && !map.IsCellProtected(cell)) map.RemoveBlock(cell);
        var props = new MaterialPropertyBlock();
        map.Terrain.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        // Changes reach the rubble through Tilemap.tilemapPositionsChanged; build on later frames.
        EditorFrames(() =>
        {
            for (int i = 0; i < 20; i++) rubble.Apply(props, false, Camera.main);
            return Compare(map, rubble, appearance);
        });
        return "läuft – Ergebnis in Temp/RubbleCacheEquivalence.txt";
    }

    static void EditorFrames(Func<string> finish)
    {
        int frames = 0;
        void Tick()
        {
            if (++frames < 4) return;
            UnityEditor.EditorApplication.update -= Tick;
            string result;
            try { result = finish(); } catch (Exception ex) { result = "FAIL: " + ex; }
            System.IO.File.WriteAllText("Temp/RubbleCacheEquivalence.txt", result);
        }
        UnityEditor.EditorApplication.update += Tick;
    }

    static string Compare(MapGenerator map, TerrainEdgeRubble rubble, UniformStoneAppearance appearance)
    {
        var chunks = (IDictionary)typeof(TerrainEdgeRubble).GetField("chunks", Private).GetValue(rubble);
        var dirty = (HashSet<Vector2Int>)typeof(TerrainEdgeRubble).GetField("dirtyChunks", Private).GetValue(rubble);
        var shape = (TerrainCollisionShape)typeof(TerrainEdgeRubble).GetField("shape", Private).GetValue(rubble);
        if (dirty.Count > 0) return $"FAIL: {dirty.Count} Chunks noch nicht gebaut";
        var settings = new Vector4(appearance.rubbleAmount, appearance.rubbleMinSize, appearance.rubbleMaxSize, appearance.rubbleProtrusionPercent);
        bool masked = appearance.useTerrainMasks;
        int compared = 0, vertices = 0;
        foreach (var keyObject in chunks.Keys.Cast<object>().ToArray())
        {
            var key = (Vector2Int)keyObject;
            var part = chunks[keyObject];
            var mesh = (Mesh)part.GetType().GetField("mesh").GetValue(part);
            var reference = new Reference(map, rubble.transform, shape, appearance, settings, masked);
            reference.Build(key);
            var actualVertices = mesh.vertices; var actualColors = mesh.colors; var actualUvs = new List<Vector2>(); mesh.GetUVs(0, actualUvs);
            var actualEdges = new List<Vector4>(); mesh.GetUVs(1, actualEdges); var actualIndices = mesh.GetTriangles(0);
            if (actualVertices.Length != reference.vertices.Count) return $"FAIL: Chunk {key} hat {actualVertices.Length} statt {reference.vertices.Count} Vertices";
            for (int i = 0; i < actualVertices.Length; i++)
                if (actualVertices[i] != reference.vertices[i] || actualColors[i] != reference.colors[i] || actualUvs[i] != reference.uvs[i] || actualEdges[i] != reference.edges[i])
                    return $"FAIL: Chunk {key} Vertex {i}: {actualVertices[i]:R} statt {reference.vertices[i]:R}";
            if (!actualIndices.SequenceEqual(reference.indices)) return $"FAIL: Chunk {key} Indizes weichen ab";
            compared++; vertices += actualVertices.Length;
        }
        return compared == 0 ? "FAIL: keine Chunks" : $"PASS: {compared} Chunks und {vertices} Vertices identisch mit dem vollständigen Neuaufbau";
    }

    // The original per-chunk algorithm, kept here as the reference for the cached implementation.
    sealed class Reference
    {
        public readonly List<Vector3> vertices = new(); public readonly List<Color> colors = new();
        public readonly List<Vector2> uvs = new(); public readonly List<Vector4> edges = new(); public readonly List<int> indices = new();
        readonly MapGenerator map; readonly Transform transform; readonly TerrainCollisionShape shape; readonly UniformStoneAppearance appearance;
        readonly Vector4 settings; readonly bool masked; Vector4 currentEdge;
        static readonly Vector3Int[] directions = { Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up };
        public Reference(MapGenerator map, Transform transform, TerrainCollisionShape shape, UniformStoneAppearance appearance, Vector4 settings, bool masked)
        { this.map = map; this.transform = transform; this.shape = shape; this.appearance = appearance; this.settings = settings; this.masked = masked; }

        public void Build(Vector2Int key)
        {
            int xMin = key.x * ChunkSize, yMin = key.y * ChunkSize, xMax = xMin + ChunkSize, yMax = yMin + ChunkSize;
            float size = map.Terrain.layoutGrid.cellSize.x;
            var sideStones = new List<Vector2>();
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
                        foreach (var stone in sideStones) if (Mathf.Abs(stone.x - offset) < stone.y + radius + .025f) { overlaps = true; break; }
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
                                if (shape.Distance(Vector2.Lerp(Vector2.one * .5f, end, mid), bits, variant, cell.y == 0) >= 0) inner = mid; else outer = mid;
                            }
                            Vector2 f = Vector2.Lerp(Vector2.one * .5f, end, inner);
                            origin = map.Terrain.CellToWorld(cell) + (Vector3)f * size;
                            const float e = .001f;
                            float dx = shape.Distance(f + Vector2.right * e, bits, variant, cell.y == 0) - shape.Distance(f - Vector2.right * e, bits, variant, cell.y == 0);
                            float dy = shape.Distance(f + Vector2.up * e, bits, variant, cell.y == 0) - shape.Distance(f - Vector2.up * e, bits, variant, cell.y == 0);
                            if (dx * dx + dy * dy > .00000001f) stoneNormal = -new Vector3(dx, dy, 0).normalized;
                        }
                        origin += stoneNormal * ((Mathf.Clamp01(settings.w * .01f) - .5f) * 2 * radius * size * .85f);
                        Pebble(origin, radius * size, hash, stoneNormal, new Vector3(-stoneNormal.y, stoneNormal.x, 0));
                    }
                }
            }
        }

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
        { vertices.Add(transform.InverseTransformPoint(world)); colors.Add(color); uvs.Add(Vector2.one * .5f); edges.Add(currentEdge); }
    }
}
