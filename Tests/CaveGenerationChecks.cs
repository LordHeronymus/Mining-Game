using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class CaveGenerationChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var settings = new CaveGenerationSettings
        {
            enabled = true,
            minimumDepth = 24,
            walkerCount = 10,
            minimumWalkerLength = 55,
            maximumWalkerLength = 110,
            minimumTunnelRadius = 1.5f,
            maximumTunnelRadius = 4f,
            branchChancePercent = 1.5f,
            chamberChancePercent = .5f,
            minimumChamberRadius = 4f,
            maximumChamberRadius = 7f,
            densityByDepth = AnimationCurve.Linear(0f, .2f, 1f, 1f)
        };
        const int width = 120, height = 240, seed = 918273;
        bool Reserved(int x, int y) => x >= 45 && x <= 74 && y >= 150 && y <= 180;
        var first = CaveGenerator.Generate(settings, seed, width, height, 3, Reserved);
        var repeated = CaveGenerator.Generate(settings, seed, width, height, 3, Reserved);
        var changed = CaveGenerator.Generate(settings, seed + 1, width, height, 3, Reserved);
        int carved = 0, changedCells = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Check(first[index] == repeated[index], "Cave generation is not seed-stable.");
                if (first[index]) carved++;
                if (first[index] != changed[index]) changedCells++;
                if (y < settings.minimumDepth || x < 5 || x >= width - 5 || Reserved(x, y))
                    Check(!first[index], "Cave generation entered a protected area.");
            }
        Check(carved > 500, "Cave generation carved too little terrain.");
        Check(changedCells > 500, "Changing the seed did not change the cave layout.");

        settings.walkerCount = 0;
        var disabledByAmount = CaveGenerator.Generate(settings, seed, width, height, 3, Reserved);
        Check(Array.TrueForAll(disabledByAmount, value => !value), "Zero cave amount still carved terrain.");
        settings.walkerCount = 10;
        settings.enabled = false;
        var disabled = CaveGenerator.Generate(settings, seed, width, height, 3, Reserved);
        Check(Array.TrueForAll(disabled, value => !value), "Disabled caves still carved terrain.");

        var sceneMap = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(sceneMap && sceneMap.caveGeneration != null, "Scene map has no cave settings.");
        var chamber = sceneMap.AltarChamber
            ? sceneMap.AltarChamber.ChooseLayout(seed, sceneMap.mapWidth, sceneMap.mapHeight) : default;
        var stopwatch = Stopwatch.StartNew();
        var sceneMask = sceneMap.CreateCaveMask(seed, sceneMap.mapWidth, sceneMap.mapHeight, chamber);
        stopwatch.Stop();
        int sceneCarved = Array.FindAll(sceneMask, value => value).Length;
        Check(sceneCarved > 0 && sceneCarved < sceneMask.Length / 2,
            "Scene cave settings produced an invalid cave amount.");
        Check(stopwatch.ElapsedMilliseconds < 5000, "Scene cave mask generation is too slow.");

        settings.enabled = true;
        GameObject grid = null;
        int integratedCaves = 0;
        try
        {
            grid = new GameObject("Cave integration grid", typeof(Grid));
            var terrain = new GameObject("Cave integration map", typeof(Tilemap),
                typeof(TilemapRenderer), typeof(MapGenerator));
            terrain.transform.SetParent(grid.transform, false);
            var generatedMap = terrain.GetComponent<MapGenerator>();
            generatedMap.enabled = false;
            generatedMap.registry = sceneMap.registry;
            generatedMap.mapWidth = width;
            generatedMap.mapHeight = height;
            generatedMap.layers = sceneMap.layers;
            generatedMap.caveGeneration = settings;
            generatedMap.oreDensityMultiplierPercent = sceneMap.oreDensityMultiplierPercent;
            generatedMap.oreDensityCurve = sceneMap.oreDensityCurve;
            generatedMap.useOreSettings = sceneMap.useOreSettings;
            generatedMap.oreSettings = sceneMap.oreSettings;
            generatedMap.GenerateMap(seed);
            var generatedChamber = generatedMap.AltarChamber ? generatedMap.AltarChamber.Layout : default;
            var generatedMask = generatedMap.CreateCaveMask(seed, width, height, generatedChamber);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!generatedMask[y * width + x]) continue;
                    integratedCaves++;
                    var cell = new Vector3Int(x - width / 2, -y, 0);
                    Check(!generatedMap.Terrain.HasTile(cell) && !generatedMap.OreOverlay.HasTile(cell) &&
                        !generatedMap.ArtifactOverlay.HasTile(cell),
                        "Generated cave contains terrain, ore, or an artifact.");
                }
            Check(integratedCaves > 0, "MapGenerator did not integrate the cave mask.");
        }
        finally
        {
            if (grid) UnityEngine.Object.DestroyImmediate(grid);
        }

        return new { passed = true, carved, changedCells, protectedCells = 30 * 31,
            sceneCarved, scenePercent = 100f * sceneCarved / sceneMask.Length,
            sceneMilliseconds = stopwatch.ElapsedMilliseconds, integratedCaves };
    }
}
