using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class FiveLayerChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        Check(map && map.layers != null && map.layers.Length == 5, "Five layers are required.");
        var layer = map.layers[4];
        Check(layer != null && layer.name == "Layer 5" && layer.startDepth == 900 &&
            layer.transitionWidth == 30, "Layer 5 depth or transition is incorrect.");
        Check(layer.stone && layer.stone.id == BlockType.StoneLayer4 && layer.backgroundSprite,
            "Layer 5 stone or background is missing.");
        Check(layer.ores == null || layer.ores.Length == 0,
            "Layer 5 legacy ores were changed without user configuration.");
        var appearance = map.GetComponent<UniformStoneAppearance>();
        Check(map.layerFourTile && map.layerFourTile.block == layer.stone &&
            appearance && appearance.layerFourTexture, "Layer 5 terrain material is incomplete.");
        var tileColor = typeof(UniformStoneAppearance).GetMethod("TileColor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Check(tileColor != null, "Terrain material mask cannot be inspected.");
        var layer4Color = (Color32)tileColor.Invoke(appearance,
            new object[] { map.Terrain.GetTile(new Vector3Int(0, -600, 0)) });
        var layer5Color = (Color32)tileColor.Invoke(appearance,
            new object[] { map.Terrain.GetTile(new Vector3Int(0, -1000, 0)) });
        Check(layer4Color.a == 128 && layer5Color.a == 255,
            "Terrain material mask does not distinguish Layer 4 and Layer 5.");

        var sampler = new MapGenerationSampler(map.registry, map.ActiveSeed, map.mapHeight,
            map.layers, map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness,
            map.useOreSettings ? map.oreSettings ?? Array.Empty<OreDistributionSetting>() : null);
        int previous = 0, next = 0;
        for (int x = 0; x < 128; x++)
        {
            Check(sampler.GetBaseBlock(x, 869) != layer.stone, "Layer 5 transition begins too early.");
            if (sampler.GetBaseBlock(x, 885) == layer.stone) next++; else previous++;
            Check(sampler.GetBaseBlock(x, 900) == layer.stone, "Layer 5 does not begin at depth 900.");
        }
        Check(previous > 0 && next > 0, "Layer 5 transition does not mix both stones.");
        var layout = UltroniumChamberLayout.Choose(map.ActiveSeed, map.mapWidth, map.mapHeight,
            map.layers, map.GetComponent<MapWorldBorders>()?.sidePaddingCells ?? 0);
        Check(layout.valid && -layout.origin.y >= 900,
            "The Ultronium chamber is not inside Layer 5.");

        var colorMethod = typeof(MapOverviewWindow).GetMethod("ColorFor",
            BindingFlags.Static | BindingFlags.NonPublic);
        Check(colorMethod != null && ((Color32)colorMethod.Invoke(null,
            new object[] { BlockType.StoneLayer4 })).a == 255, "Map overview lacks Layer 5.");
        foreach (var shaderPath in new[]
        {
            "Assets/GameObjects/Map/DirtTerrainLit.shader",
            "Assets/GameObjects/Map/TerrainEdgeLit.shader",
            "Assets/GameObjects/Map/TerrainFrayedLit.shader",
            "Assets/GameObjects/Background/Underground/FixedUnderground.shader"
        })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Check(shader && !ShaderUtil.ShaderHasError(shader), "Shader error: " + shaderPath);
        }
        return new
        {
            passed = true,
            layers = map.layers.Length,
            start = layer.startDepth,
            transition = layer.transitionWidth,
            chamberDepth = -layout.origin.y,
            background = AssetDatabase.GetAssetPath(layer.backgroundSprite)
        };
    }
}
