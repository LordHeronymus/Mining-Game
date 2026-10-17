using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MigrateOreDistributionSettings
{
    public static object Main()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!map || !map.registry || map.layers == null || map.layers.Length == 0)
            throw new InvalidOperationException("Map layers and registry are required.");
        if (map.useOreSettings)
            throw new InvalidOperationException("Ore settings have already been migrated.");
        var ores = map.registry.blocks.Where(block => block && block.HasOreOverlays)
            .OrderBy(block => block.id).ToArray();
        Undo.RecordObject(map, "Migrate ore distribution settings");
        map.oreSettings = ores.Select(block => new OreDistributionSetting
        {
            ore = block.id,
            layerIndices = Enumerable.Range(0, map.layers.Length).Where(index =>
                map.layers[index].ores != null &&
                Array.IndexOf(map.layers[index].ores, block.id) >= 0).ToArray(),
            baseWeight = block.OreWeight,
            weightCurve = AnimationCurve.Constant(0f, 1f, 1f),
            baseVeinSize = block.veinSizeIndex,
            veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f)
        }).ToArray();
        map.useOreSettings = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        if (!EditorSceneManager.SaveScene(map.gameObject.scene))
            throw new InvalidOperationException("Could not save the scene.");
        return new { ores = map.oreSettings.Select(entry => new
            { name = entry.ore.ToString(), layers = entry.layerIndices,
                weight = entry.baseWeight, veinSize = entry.baseVeinSize }).ToArray() };
    }
}
