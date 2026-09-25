using System;
using UnityEngine;

public static class FourLayerChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.layers != null && map.layers.Length == 4, "Four layers are required.");
        int[] starts = Array.ConvertAll(map.layers, layer => layer.startDepth);
        for (int i = 0; i < starts.Length; i++)
        {
            var layer = map.layers[i];
            Check(layer != null && layer.stone && layer.backgroundSprite &&
                (i == 0 ? layer.startDepth == 0 : layer.startDepth > starts[i - 1]),
                "Layer " + (i + 1) + " is incomplete.");
            Check(layer.transitionWidth >= 0, "Invalid transition width in layer " + (i + 1));
            Check(layer.stoneHardness > 0f &&
                Mathf.Approximately(map.GetHardnessAt(new Vector3Int(0, -starts[i], 0), layer.stone),
                    layer.stoneHardness), "Layer hardness is not applied.");
        }
        Check(map.layers[0].stone.id == BlockType.Dirt, "Layer 1 must use dirt.");
        var ore = map.registry.GetById(BlockType.GoldOre);
        Check(ore && Mathf.Approximately(map.GetHardnessAt(new Vector3Int(0, -starts[3], 0), ore),
            ore.hardness), "Ore hardness was replaced by layer hardness.");
        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, map.GeneratedHeight,
            map.layers, map.oreDensityCurve, map.oreDensityMultiplierPercent,
            map.transitionThickness);
        Check(sampler.DirtEndDepth == starts[1], "The surface layer ends at the wrong depth.");
        for (int x = 0; x < 80; x++)
        {
            int pureDirtEnd = starts[1] - Mathf.Min(map.layers[1].transitionWidth, starts[1]) - 1;
            Check(sampler.GetBaseBlock(x, pureDirtEnd) == map.layers[0].stone, "Pure dirt ends too early.");
            for (int i = 1; i < starts.Length; i++)
                Check(sampler.GetBaseBlock(x, starts[i]) == map.layers[i].stone,
                    "Layer does not begin at its configured depth: " + i);
        }
        return new { passed = true, layers = map.layers.Length, starts };
    }
}
