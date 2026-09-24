using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public static class TreeGrassPriorityChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static string Run()
    {
        Check(Application.isPlaying, "Play Mode required");
        var originalTrees = Object.FindFirstObjectByType<SurfaceTrees>();
        var originalGrass = originalTrees.Map.GetComponent<SurfaceTallGrass>();
        var scene = SceneManager.CreateScene("TreeGrassPriorityChecks", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root = new GameObject("Priority fixture", typeof(Grid));
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        var terrainObject = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer));
        terrainObject.transform.SetParent(root.transform, false);
        var map = terrainObject.AddComponent<MapGenerator>();
        var terrain = terrainObject.GetComponent<Tilemap>();
        var tile = ScriptableObject.CreateInstance<Tile>();
        var grass = terrainObject.AddComponent<SurfaceTallGrass>();
        var forestObject = new GameObject("Trees");
        forestObject.transform.SetParent(root.transform, false);
        var trees = forestObject.AddComponent<SurfaceTrees>();
        try
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(originalGrass), grass);
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(originalTrees), trees);
            Set(grass, "map", map); Set(trees, "map", map);
            Set(grass, "spawnQuietRadius", 0f); Set(grass, "maximumPatches", 35);
            map.mapWidth = 160; map.mapHeight = 2; map.enabled = false;
            for (int x = -80; x < 80; x++) terrain.SetTile(new Vector3Int(x, 0), tile);
            root.SetActive(true);
            Set(map, "isGenerated", true); Set(map, "generatedWidth", 160); Set(map, "generatedHeight", 2); Set(map, "generatedSeed", 54321);

            // Deliberately invoke grass first, the problematic subscription order.
            Call(grass, "RequestRebuild");
            Check(grass.PatchCount == 0 && (bool)Get(grass, "rebuildPending"), "Grass must wait for trees");
            Call(trees, "ResetTrees");
            Check(trees.ActiveCount > 0, "Fixture must plant trees");
            Call(grass, "Update");
            Check(grass.PatchCount > 0, "Grass must populate after trees");
            foreach (var patch in grass.ActivePatches)
                Check(!trees.Protects(new Vector3Int(patch.SurfaceCellX, 0)), "Initial grass overlaps tree neighborhood");

            var population = (HashSet<ChoppableTree>)Get(trees, "trees");
            var firstPositions = population.Select(t => t.SurfaceCellX).OrderBy(x => x).ToArray();
            Call(trees, "ResetTrees"); Call(grass, "RequestRebuild"); Call(grass, "Update");
            Check(firstPositions.SequenceEqual(population.Select(t => t.SurfaceCellX).OrderBy(x => x)), "Grass changed seeded tree positions");

            foreach (var tree in population.ToArray()) Object.DestroyImmediate(tree.gameObject);
            population.Clear();
            Call(grass, "Clear");
            for (int x = -80; x < 80; x++) Call(grass, "Spawn", x, terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x);
            var patches = (Dictionary<int, TallGrassPatch>)Get(grass, "patches");
            var before = new Dictionary<int, TallGrassPatch>(patches);
            var fiber = (ItemSO)Get(grass, "fiber");
            int fibers = InventoryManager.Instance.GetCount(fiber);
            Check((bool)Call(trees, "TryPlant", false), "Dense grass prevented tree regrowth");
            int planted = population.Single().SurfaceCellX;
            Check(grass.PatchCount == 157, "Tree must remove exactly its cell and both neighbors");
            for (int x = planted - 1; x <= planted + 1; x++)
                Check(!patches.ContainsKey(x) && !before[x].gameObject.activeSelf, "Displaced grass must vanish immediately");
            Check(patches.ContainsKey(planted - 2) && patches.ContainsKey(planted + 2), "Distant grass removed");
            Check(InventoryManager.Instance.GetCount(fiber) == fibers, "Tree displacement must not award fibers");

            var sites = (List<int>)Get(grass, "sites");
            sites.Clear(); sites.AddRange(new[] { planted - 1, planted, planted + 1 });
            Call(grass, "Clear"); Set(grass, "nextRespawn", 0f); Call(grass, "Update");
            Check(grass.PatchCount == 0, "Grass regrew inside tree neighborhood");
            return "PASS: deferred grass, both handler orders, seed-stable trees, regrowth through dense grass, removal x-1/x/x+1, immediate hiding, no fiber reward, no grass regrowth under trees.";
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
