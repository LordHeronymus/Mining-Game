using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class MapLayerChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Index(BlockRegistry registry) => typeof(BlockRegistry).GetMethod("BuildIndex",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(registry, null);

    public static object Main()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        var sourceMap = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var registry = sourceMap.registry;
        Check(sourceMap.layers.Length == 3, "Three default layers missing.");
        Check(sourceMap.layers[0].startDepth == 0 && sourceMap.layers[1].startDepth == 300 &&
            sourceMap.layers[2].startDepth == 800, "Default layer boundaries incorrect.");
        Check(sourceMap.layers.Select(layer => layer.stone).Distinct().Count() == 3, "Stone assets are shared.");
        var layers = sourceMap.layers.Select((layer, i) => new MapLayer {
            name = layer.name, startDepth = i * 64, stone = layer.stone, ores = (BlockType[])layer.ores.Clone()
        }).ToArray();
        Check(layers[0].ores.OrderBy(id => id).SequenceEqual(new[] {BlockType.CopperOre, BlockType.Coal,
            BlockType.IronOre, BlockType.SilverOre}.OrderBy(id => id)), "Layer 1 ore list incorrect.");
        Check(layers[1].ores.Length == 6 && layers[1].ores.Contains(BlockType.GoldOre) &&
            layers[1].ores.Contains(BlockType.PlatinumOre), "Layer 2 ore list incorrect.");
        Check(layers[2].ores.Length == 7 && layers[2].ores.Contains(BlockType.DiamondOre), "Diamond not prepared.");

        var temporary = new List<UnityEngine.Object>();
        var previous = SceneManager.GetActiveScene();
        Scene scene = default;
        try
        {
            // Test every ore independently at 100%: forbidden layers must still contain only stone.
            var testRegistry = ScriptableObject.CreateInstance<BlockRegistry>(); temporary.Add(testRegistry);
            var ore = ScriptableObject.CreateInstance<Block>(); temporary.Add(ore);
            ore.spawnWithNoise = true; ore.oreFrequencyPercent = 100;
            testRegistry.blocks = layers.Select(layer => layer.stone).Concat(new[] { ore }).ToArray();
            foreach (var type in layers[2].ores)
            {
                ore.id = type; Index(testRegistry);
                var forced = new MapGenerationSampler(testRegistry, 12345, 192, layers, AnimationCurve.Constant(0, 1, 100));
                for (int y = 0; y < 192; y++)
                {
                    var layer = layers[y / 64];
                    Check(forced.GetBlock(7, y) == (y >= 4 && layer.ores.Contains(type) ? ore : layer.stone),
                        "Ore crossed its layer boundary: " + type + " at " + y);
                }
            }
            var noOres = new[] { new MapLayer { name = "Empty", stone = layers[0].stone, ores = Array.Empty<BlockType>() } };
            Check(new MapGenerationSampler(testRegistry, 1, 192, noOres).GetBlock(0, 100) == layers[0].stone,
                "An empty ore list generated ore.");
            noOres[0].ores = null;
            Check(new MapGenerationSampler(testRegistry, 1, 192, noOres).GetBlock(0, 100) == layers[0].stone,
                "A null ore list generated ore.");
            noOres[0].ores = new[] { BlockType.DiamondOre };
            Check(new MapGenerationSampler(registry, 1, 192, noOres).GetBlock(0, 100) == layers[0].stone,
                "Missing diamond asset did not fall back to stone.");
            var reversed = layers.Reverse().ToArray();
            var sampler = new MapGenerationSampler(registry, 42319, 192, layers);
            var reordered = new MapGenerationSampler(registry, 42319, 192, reversed);
            Check(sampler.GetStone(63) == layers[0].stone && sampler.GetStone(64) == layers[1].stone &&
                sampler.GetStone(127) == layers[1].stone && sampler.GetStone(128) == layers[2].stone &&
                sampler.GetStone(500) == layers[2].stone, "Stone layer boundary incorrect.");

            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Layer Test Grid", typeof(Grid));
            grid.GetComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0);
            var terrain = new GameObject("Layer Test Map", typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGenerator));
            terrain.transform.SetParent(grid.transform, false);
            var map = terrain.GetComponent<MapGenerator>(); map.enabled = false;
            map.registry = registry; map.layers = layers; map.seed = 42319; map.mapWidth = 128; map.mapHeight = 192;
            map.GenerateMap();
            var found = new HashSet<BlockType>[3] {new HashSet<BlockType>(), new HashSet<BlockType>(), new HashSet<BlockType>()};
            Vector3Int oreCell = default; int ores = 0;
            for (int y = 0; y < map.mapHeight; y++) for (int x = 0; x < map.mapWidth; x++)
            {
                var cell = new Vector3Int(x - map.mapWidth / 2, -y, 0);
                var layer = layers[y / 64];
                var block = map.GetBlockAt(cell);
                Check(block && block == sampler.GetBlock(x, y) && block == reordered.GetBlock(x, y),
                    "Live map and preview disagree.");
                Check(registry.FromTile(map.Terrain.GetTile(cell)) == sampler.GetBaseBlock(x,y),
                    "Wrong stone substrate or registry identity at " + cell);
                if (y < 4) Check((block.id == BlockType.Dirt || block.IsStone) && !map.GetOreAt(cell), "Ore generated above depth 4.");
                if (block.HasOreOverlays)
                {
                    Check(layer.ores.Contains(block.id), "Disallowed ore generated.");
                    Check(map.GetOreAt(cell), "Missing ore overlay.");
                    found[y / 64].Add(block.id); ores++; oreCell = cell;
                }
            }
            Check(ores > 100, "Too few ores for meaningful generation coverage.");
            Check(found[1].Contains(BlockType.GoldOre) && found[1].Contains(BlockType.PlatinumOre),
                "Layer 2 gold/platinum did not generate.");
            var bounds = map.Terrain.cellBounds;
            var before = map.Terrain.GetTilesBlock(bounds);
            var beforeOres = map.OreOverlay.GetTilesBlock(bounds);
            var matrix = map.OreOverlay.GetTransformMatrix(oreCell);
            map.GenerateMap();
            Check(before.SequenceEqual(map.Terrain.GetTilesBlock(bounds)) && beforeOres.SequenceEqual(map.OreOverlay.GetTilesBlock(bounds)) &&
                matrix == map.OreOverlay.GetTransformMatrix(oreCell), "Seed regeneration changed tiles or rotation.");
            Check(map.RemoveBlock(oreCell) && !map.GetBlockAt(oreCell) && !map.GetOreAt(oreCell) && !map.Terrain.HasTile(oreCell),
                "Mining did not remove both layers.");

            var savedOres = layers.Select(layer => layer.ores).ToArray();
            foreach (var layer in layers) layer.ores = Array.Empty<BlockType>();
            map.GenerateMap();
            for (int i = 0; i < layers.Length; i++) layers[i].ores = savedOres[i];
            var hole = new Vector3Int(0, -150, 0);
            map.RemoveBlock(hole);
            int migrated = (int)typeof(OreOverlaySetup).GetMethod("MigratePreview", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { map, registry.GetById(BlockType.CopperOre) });
            Check(migrated > 0 && !map.Terrain.HasTile(hole), "Ore migration failed or refilled a mined hole.");
            int migratedInDirt = 0;
            for (int y = 0; y < map.mapHeight; y++) for (int x = 0; x < map.mapWidth; x++)
            {
                var cell = new Vector3Int(x - map.mapWidth / 2, -y, 0);
                if (cell == hole) continue;
                Check(registry.FromTile(map.Terrain.GetTile(cell)) == sampler.GetBaseBlock(x,y),
                    "Ore migration changed the layer stone.");
                var overlay = map.GetOreAt(cell);
                if (overlay && sampler.IsDirtAt(x,y)) migratedInDirt++;
                Check(!overlay || (y >= 4 && overlay.block.id == BlockType.CopperOre),
                    "Ore migration crossed a layer boundary.");
            }

            Check(migratedInDirt > 0, "Ore migration did not populate dirt.");

            // Invalid layer settings must fail before touching the existing generated terrain.
            int preserved = map.Terrain.GetUsedTilesCount();
            layers[1].startDepth = 0;
            bool rejected = false;
            try { map.GenerateMap(); } catch (ArgumentException) { rejected = true; }
            Check(rejected && map.Terrain.GetUsedTilesCount() == preserved && map.IsGenerated,
                "Invalid layer boundaries damaged the map.");
            layers[1].startDepth = 64;
            layers[1].stone = null;
            rejected = false;
            try { map.GenerateMap(); } catch (ArgumentException) { rejected = true; }
            Check(rejected && map.IsGenerated, "Missing layer stone was not safely rejected.");
            return new { passed = true, checkedCells = map.mapWidth * map.mapHeight, oreCells = ores,
                layerOres = found.Select(set => set.Select(id => id.ToString()).OrderBy(id => id).ToArray()).ToArray() };
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            foreach (var asset in temporary) UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
