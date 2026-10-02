using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class TerrainColliderBatchChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        Check(Application.isPlaying, "Run in Play Mode.");
        var source = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("Collider batch checks",
            new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D));
        var root = new GameObject("Collider batch checks", typeof(Grid));
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = new Vector3(10000, 0, 0);
        root.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
        try
        {
            var terrainObject = new GameObject("Test terrain", typeof(Tilemap), typeof(TilemapRenderer));
            terrainObject.transform.SetParent(root.transform, false);
            terrainObject.layer = 31;
            var map = terrainObject.AddComponent<MapGenerator>();
            map.enabled = false;
            map.mapWidth = map.mapHeight = 64;
            map.registry = source.registry;
            var appearance = terrainObject.AddComponent<UniformStoneAppearance>();
            appearance.enabled = false;
            appearance.colliderInset = .08f;
            var body = terrainObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var sourceComposite = terrainObject.AddComponent<CompositeCollider2D>();
            sourceComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            sourceComposite.vertexDistance = .001f;
            terrainObject.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
            var owner = terrainObject.AddComponent<TerrainColliderChunks>();
            var terrain = map.Terrain;
            terrain.orientation = source.Terrain.orientation;
            terrain.orientationMatrix = source.Terrain.orientationMatrix;
            var tile = source.registry.GetById(BlockType.Stone).variants[0];
            for (int y = -63; y <= 0; y++)
            for (int x = -32; x < 32; x++) terrain.SetTile(new Vector3Int(x, y, 0), tile);
            // Many exposed sides exercise the expensive composite path updates.
            for (int y = -46; y < -33; y += 2)
            for (int x = -14; x < -2; x += 2) terrain.SetTile(new Vector3Int(x, y, 0), null);
            root.SetActive(true);
            foreach (var behaviour in terrainObject.GetComponents<MonoBehaviour>())
                if (behaviour != owner) behaviour.enabled = false;
            typeof(MapGenerator).GetField("isGenerated", Private).SetValue(map, true);
            typeof(MapGenerator).GetField("generatedWidth", Private).SetValue(map, 64);
            typeof(MapGenerator).GetField("generatedHeight", Private).SetValue(map, 64);
            typeof(TerrainColliderChunks).GetMethod("Rebuild", Private).Invoke(owner, null);
            owner.EnsureAnimalCollisionAt(terrain.GetCellCenterWorld(new Vector3Int(0, -31, 0)));

            var chunks = (Tilemap[,])typeof(TerrainColliderChunks).GetField("chunks", Private).GetValue(owner);
            var dirty = (HashSet<Tilemap>)typeof(TerrainColliderChunks).GetField("dirtyChunks", Private).GetValue(owner);
            var flush = typeof(TerrainColliderChunks).GetMethod("FlushDirtyChunks", Private);
            bool Solid(Vector3Int cell)
            {
                Vector2 point = terrain.GetCellCenterWorld(cell);
                foreach (var chunk in chunks)
                    if (chunk && chunk.GetComponent<CompositeCollider2D>().OverlapPoint(point)) return true;
                return false;
            }
            var removed = new[] { new Vector3Int(-1, -32, 0), new Vector3Int(0, -32, 0),
                new Vector3Int(-1, -31, 0), new Vector3Int(0, -31, 0) };
            foreach (var cell in removed) Check(Solid(cell), "Initial collider missing: " + cell);
            foreach (var cell in removed) terrain.SetTile(cell, null);
            Check(dirty.Count == 4, "Expected four distinct queued chunks at the corner, got " + dirty.Count);
            foreach (var cell in removed) Check(Solid(cell), "Callback rebuilt collider before batch flush.");
            flush.Invoke(owner, null);
            Check(dirty.Count == 0, "Dirty queue did not drain.");
            foreach (var cell in removed) Check(!Solid(cell), "Removed cell still collides: " + cell);
            foreach (var cell in removed) terrain.SetTile(cell, tile);
            typeof(TerrainColliderChunks).GetMethod("FixedUpdate", Private).Invoke(owner, null);
            foreach (var cell in removed) Check(Solid(cell), "FixedUpdate did not restore collision.");

            terrain.SetTile(removed[0], null);
            typeof(TerrainColliderChunks).GetMethod("LateUpdate", Private).Invoke(owner, null);
            Check(!Solid(removed[0]), "LateUpdate did not apply mining change.");
            terrain.SetTile(removed[0], tile);
            owner.EnsureAnimalCollisionAt(terrain.GetCellCenterWorld(removed[0]));
            Check(Solid(removed[0]) && dirty.Count == 0, "Immediate animal collision query missed changes.");

            // A falling body must land on the rebuilt floor across the chunk seam.
            foreach (var cell in removed) terrain.SetTile(cell, null);
            flush.Invoke(owner, null);
            var falling = new GameObject("Falling collision probe", typeof(Rigidbody2D), typeof(BoxCollider2D));
            falling.transform.SetParent(root.transform, false);
            falling.layer = 31;
            var probe = falling.GetComponent<Rigidbody2D>();
            probe.position = terrain.GetCellCenterWorld(new Vector3Int(0, -30, 0));
            falling.GetComponent<BoxCollider2D>().size = new Vector2(.5f, .5f);
            Physics2D.SyncTransforms();
            var previousSimulation = Physics2D.simulationMode;
            var previousIgnored = Physics2D.GetIgnoreLayerCollision(31, 31);
            try
            {
                Physics2D.IgnoreLayerCollision(31, 31, false);
                Physics2D.simulationMode = SimulationMode2D.Script;
                var physicsScene = UnityEngine.PhysicsSceneExtensions2D.GetPhysicsScene2D(root.scene);
                for (int i = 0; i < 100; i++) physicsScene.Simulate(.02f);
                float floor = terrain.CellToWorld(new Vector3Int(0, -32, 0)).y
                    - appearance.colliderInset * root.GetComponent<Grid>().cellSize.y;
                Check(probe.position.y >= floor + .23f && probe.position.y <= floor + .4f,
                    "Falling probe failed to land on rebuilt floor: " + probe.position.y);
                Check(Mathf.Abs(probe.linearVelocity.y) < .05f, "Probe did not settle on floor.");
            }
            finally
            {
                Physics2D.simulationMode = previousSimulation;
                Physics2D.IgnoreLayerCollision(31, 31, previousIgnored);
            }

            var chunkWithEdges = chunks[1, 1];
            var polygon = chunkWithEdges.GetComponent<PolygonCollider2D>();
            var composite = chunkWithEdges.GetComponent<CompositeCollider2D>();
            var paths = new Vector2[polygon.pathCount][];
            for (int i = 0; i < paths.Length; i++) paths[i] = polygon.GetPath(i);
            double[] Measure(CompositeCollider2D.GenerationType mode)
            {
                composite.generationType = mode;
                var times = new double[7];
                for (int run = 0; run < times.Length; run++)
                {
                    var timer = Stopwatch.StartNew();
                    for (int i = 0; i < paths.Length; i++) polygon.SetPath(i, paths[i]);
                    if (mode == CompositeCollider2D.GenerationType.Manual) composite.GenerateGeometry();
                    timer.Stop();
                    times[run] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                return times;
            }
            var synchronous = Measure(CompositeCollider2D.GenerationType.Synchronous);
            int previousPoints = composite.pointCount, previousPaths = composite.pathCount;
            var expectedGeometry = new Vector2[previousPaths][];
            for (int i = 0; i < previousPaths; i++)
            {
                expectedGeometry[i] = new Vector2[composite.GetPathPointCount(i)];
                composite.GetPath(i, expectedGeometry[i]);
            }
            var manual = Measure(CompositeCollider2D.GenerationType.Manual);
            Check(composite.pointCount == previousPoints && composite.pathCount == previousPaths,
                "Batched geometry differs from synchronous geometry.");
            for (int i = 0; i < previousPaths; i++)
            {
                var actual = new Vector2[composite.GetPathPointCount(i)];
                composite.GetPath(i, actual);
                Check(actual.Length == expectedGeometry[i].Length, "Geometry path length differs.");
                for (int j = 0; j < actual.Length; j++)
                    Check((actual[j] - expectedGeometry[i][j]).sqrMagnitude < .00000001f,
                        "Geometry vertex differs after batching.");
            }
            foreach (var chunk in chunks)
                if (chunk) Check(chunk.GetComponent<CompositeCollider2D>().generationType ==
                    CompositeCollider2D.GenerationType.Manual, "Chunk uses synchronous geometry.");

            for (int y = -47; y <= -32; y++)
            for (int x = -16; x < 0; x++) terrain.SetTile(new Vector3Int(x, y, 0), null);
            flush.Invoke(owner, null);
            Check(composite.pathCount == 0, "Empty chunk retained collision geometry.");
            return new { passed = true, queuedChunks = 4, fallingBodyLanded = true,
                exposedPaths = paths.Length, synchronousMedianMs = synchronous[3], manualMedianMs = manual[3] };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            Physics2D.SyncTransforms();
        }
    }
}
