using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Free grass regrowth: any grass-covered surface cell, faster the emptier the meadow, meadows spread.
public static class GrassRegrowthChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static string Run()
    {
        Check(Application.isPlaying, "Play Mode required");
        var originalGrass = Object.FindFirstObjectByType<SurfaceTallGrass>();
        Check(originalGrass, "Fixture: SurfaceTallGrass in scene");
        var respawn = (Vector2)Get(originalGrass, "respawnSeconds");
        Check(Mathf.Approximately(respawn.x, 69f) && Mathf.Approximately(respawn.y, 138f), $"GPS Respawnzeit 69–138 s, got {respawn}");
        var fibers = (Vector2Int)Get(originalGrass, "fiberYield");
        Check(fibers == new Vector2Int(1, 3), $"GPS Fasern 1–3, got {fibers}");
        var neighborWeight = (float)Get(originalGrass, "neighborRegrowthWeight");
        Check(Mathf.Approximately(neighborWeight, 1f), $"GPS Vorzug neben Gras 1 (überall gleich), got {neighborWeight}");

        var scene = SceneManager.CreateScene("GrassRegrowthChecks", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root = new GameObject("Regrowth fixture", typeof(Grid));
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        // Far away from the real buildings, which the regrowth also avoids.
        root.transform.position = new Vector3(100000f, 0f, 0f);
        var terrainObject = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer));
        terrainObject.transform.SetParent(root.transform, false);
        var map = terrainObject.AddComponent<MapGenerator>();
        var terrain = terrainObject.GetComponent<Tilemap>();
        var overlayObject = new GameObject("Grass Overlay", typeof(Tilemap), typeof(TilemapRenderer));
        overlayObject.transform.SetParent(root.transform, false);
        var overlay = overlayObject.GetComponent<Tilemap>();
        var tile = ScriptableObject.CreateInstance<Tile>();
        var grass = terrainObject.AddComponent<SurfaceTallGrass>();
        int checks = 0;
        try
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(originalGrass), grass);
            Set(grass, "map", map); Set(map, "grassOverlay", overlay);
            map.mapWidth = 160; map.mapHeight = 2; map.enabled = false;
            const int hole = 20, bare = -30;
            for (int x = -80; x < 80; x++)
            {
                if (x == hole) continue;
                terrain.SetTile(new Vector3Int(x, 0), tile);
                if (x != bare) overlay.SetTile(new Vector3Int(x, 0), tile);
            }
            root.SetActive(true);
            // Awake applies GPS on activation, so the fixture values come after it.
            Set(grass, "spawnQuietRadius", 0f); Set(grass, "maximumPatches", 40);
            Set(grass, "maximumRegrowthSpeed", 4f); Set(grass, "neighborRegrowthWeight", 3f);
            Set(grass, "healingHerbRarityPercent", 0f); Set(grass, "healingHerbConversionLimitPercent", 0f);
            Set(map, "isGenerated", true); Set(map, "generatedWidth", 160); Set(map, "generatedHeight", 2); Set(map, "generatedSeed", 4242);
            grass.Rebuild();
            Call(grass, "SpawnPending", int.MaxValue);
            Set(grass, "respawnRandom", new System.Random(11));
            var patches = (Dictionary<int, TallGrassPatch>)Get(grass, "patches");
            var sites = (List<int>)Get(grass, "sites");

            // Speed: 1× full, 2.5× half, 4× empty.
            Call(grass, "Clear");
            Check(Mathf.Approximately(grass.RegrowthSpeed, 4f), $"Empty meadow regrows 4×, got {grass.RegrowthSpeed}"); checks++;
            for (int x = -60; x < -40; x++) Call(grass, "Spawn", x, terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x);
            Check(Mathf.Approximately(grass.RegrowthSpeed, 2.5f), $"Half meadow regrows 2.5×, got {grass.RegrowthSpeed}"); checks++;
            for (int x = 40; x < 60; x++) Call(grass, "Spawn", x, terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x);
            Check(Mathf.Approximately(grass.RegrowthSpeed, 1f), $"Full meadow regrows 1×, got {grass.RegrowthSpeed}"); checks++;

            // A full meadow grows nothing.
            Set(grass, "nextRespawn", 0f); Call(grass, "Update");
            Check(grass.PatchCount == 40, "Full meadow must not grow more"); checks++;

            // The countdown runs (speed − 1) faster with missing grass; cutting does not restart it.
            Call(grass, "Clear");
            float due = Time.time + 100f;
            Set(grass, "nextRespawn", due);
            Call(grass, "Update");
            float shortened = due - (float)Get(grass, "nextRespawn");
            Check(Mathf.Abs(shortened - 3f * Time.deltaTime) < 1e-3f, $"Countdown runs 4× with no grass: shortened {shortened}, deltaTime {Time.deltaTime}"); checks++;
            Call(grass, "Spawn", 0, terrain.GetCellCenterWorld(new Vector3Int(0, 0)).x);
            float before = (float)Get(grass, "nextRespawn");
            Call(grass, "RemovePatch", 0);
            Check(Mathf.Approximately((float)Get(grass, "nextRespawn"), before), "Cutting grass restarted the running countdown"); checks++;

            // Free sites: grass grows outside the first layout, never on a hole, its edges or bare ground.
            Call(grass, "Clear");
            sites.Clear();
            var grown = new HashSet<int>();
            for (int i = 0; i < 400 && grown.Count < 39; i++)
            {
                Set(grass, "nextRespawn", 0f); Call(grass, "Update");
                foreach (int x in patches.Keys) grown.Add(x);
                if (grass.PatchCount >= 40) Call(grass, "Clear");
            }
            Check(grown.Count > 30, $"Grass must grow on many free cells without layout sites, got {grown.Count}"); checks++;
            foreach (int x in new[] { hole - 1, hole, hole + 1, bare, -80, 79 })
            { Check(!grown.Contains(x), $"Grass grew on invalid cell {x}"); checks++; }

            // Meadows spread: neighbors of standing grass are picked about 3× as often.
            Call(grass, "Clear");
            Call(grass, "Spawn", 50, terrain.GetCellCenterWorld(new Vector3Int(50, 0)).x);
            Set(grass, "respawnRandom", new System.Random(5));
            var counts = new Dictionary<int, int>();
            const int draws = 20000;
            for (int i = 0; i < draws; i++)
            {
                int x = (int)Call(grass, "PickRegrowthSite");
                counts[x] = counts.TryGetValue(x, out int c) ? c + 1 : 1;
            }
            Check(!counts.ContainsKey(50), "Occupied cell picked"); checks++;
            int neighbors = (counts.TryGetValue(49, out int l) ? l : 0) + (counts.TryGetValue(51, out int r) ? r : 0);
            int others = draws - neighbors;
            double ratio = (neighbors / 2d) / (others / (double)(counts.Count - 2));
            Check(ratio > 2.4d && ratio < 3.6d, $"Neighbor preference about 3×, got {ratio:0.00}"); checks++;
            Set(grass, "neighborRegrowthWeight", 1f);
            neighbors = 0;
            for (int i = 0; i < draws; i++) { int x = (int)Call(grass, "PickRegrowthSite"); if (x == 49 || x == 51) neighbors++; }
            ratio = (neighbors / 2d) / ((draws - neighbors) / (double)(counts.Count - 2));
            Check(ratio > .7d && ratio < 1.3d, $"Without preference every cell is equal, got {ratio:0.00}"); checks++;

            // Anywhere on the map: the spawn area is not thinner or denser than the rest.
            Call(grass, "Clear");
            Set(grass, "spawnQuietRadius", 18f); Set(grass, "nearSpawnDensity", .15f);
            int nearSpawn = 0, nearCells = 0, allCells = 0;
            for (int x = -79; x <= 78; x++)
            {
                if (x == bare || Mathf.Abs(x - hole) <= 1) continue;
                allCells++;
                if (Mathf.Abs(x) <= 8) nearCells++;
            }
            for (int i = 0; i < draws; i++) if (Mathf.Abs((int)Call(grass, "PickRegrowthSite")) <= 8) nearSpawn++;
            double spawnRatio = (nearSpawn / (double)nearCells) / ((draws - nearSpawn) / (double)(allCells - nearCells));
            Check(spawnRatio > .8d && spawnRatio < 1.2d, $"Spawn area regrows like the rest of the map, got {spawnRatio:0.00}"); checks++;

            return $"PASS: {checks} checks (GPS 69–138 s and fibers 1–3, speed 1×/2.5×/4×, cap, faster countdown, cut keeps countdown, free sites, holes and bare ground, even without preference {ratio:0.00}, spawn area {spawnRatio:0.00}).";
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
