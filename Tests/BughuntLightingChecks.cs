using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Checks for the torch light fixes from 23.10.2026: blocks outside the torch light no longer
// rebuild the whole torch field (result stays bit-identical), and after loading a save the
// next "Map generieren" rebuilds the lighting again.
public static class BughuntLightingChecks
{
    const string Report = "Temp/BughuntLightingChecks.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + label + "\n");
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "");
        try
        {
            var skipped = SkippedTorchRebuildsMatchFullRebuild();
            FlameCellAndLoadedGenerationChecks();
            File.AppendAllText(Report, "PASS all checks\n");
            return new { skippedRebuilds = skipped.Item1, rebuilds = skipped.Item2 };
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error.Message + "\n");
            throw;
        }
    }

    // Mirrors MapLighting.LateUpdate: every frame applies a few solidity changes and rebuilds
    // the torch field only if one of them can change torch light. After every frame the field
    // must equal a fresh full rebuild bit for bit.
    static Tuple<int, int> SkippedTorchRebuildsMatchFullRebuild()
    {
        const int width = 96, height = 64;
        const float cell = .5f;
        var random = new System.Random(4711);
        var solid = new bool[width * height];
        for (int i = 0; i < solid.Length; i++) solid[i] = random.NextDouble() < .45;
        var sources = new List<TorchLightField.Source>
        {
            new TorchLightField.Source(12, 12, .85f),
            new TorchLightField.Source(84, 14, .85f * 2.8f),
            new TorchLightField.Source(48, 52, .85f),
        };
        solid[12 * width + 12] = solid[14 * width + 84] = false;
        // The third torch sits sealed in rock far from the others, so its whole surroundings are dark.
        for (int y = 49; y <= 55; y++)
            for (int x = 45; x <= 51; x++) solid[y * width + x] = true;

        var field = new TorchLightField(width, height, solid, cell, cell, PlacedTorch.PropagationDistance);
        field.Rebuild(sources);
        Compare(field, solid, sources, width, height, "Initial torch field matches a full rebuild");

        Check(field.Get(48, 52) == 0f && field.Get(47, 51) == 0f, "Sealed torch and its neighbours are dark");
        Check(!field.CanChangeLight(46, 50), "A dark block away from every torch skips the torch rebuild");
        Check(field.CanChangeLight(48, 52), "The cell of a sealed torch still counts as light-relevant");
        Check(field.CanChangeLight(13, 12), "A block beside a lit torch requests a rebuild");

        // Opening the sealed torch cell must light it although all neighbours were dark.
        solid[52 * width + 48] = false;
        Check(field.SetSolid(48, 52, false) && field.CanChangeLight(48, 52), "Opening the sealed torch cell requests a rebuild");
        field.Rebuild(sources);
        Check(field.Get(48, 52) > 0f, "The opened torch cell lights up");
        Compare(field, solid, sources, width, height, "Opened sealed torch matches a full rebuild");

        int skipped = 0, rebuilds = 0;
        for (int frame = 0; frame < 400; frame++)
        {
            bool dirty = false;
            int changes = 1 + random.Next(4);
            for (int change = 0; change < changes; change++)
            {
                int x, y;
                if (random.NextDouble() < .5)
                {
                    // Near a torch, so both lit and dark changes are covered.
                    var source = sources[random.Next(sources.Count)];
                    x = Mathf.Clamp(source.x + random.Next(-24, 25), 0, width - 1);
                    y = Mathf.Clamp(source.y + random.Next(-24, 25), 0, height - 1);
                }
                else { x = random.Next(width); y = random.Next(height); }
                bool value = !solid[y * width + x];
                solid[y * width + x] = value;
                if (field.SetSolid(x, y, value) && field.CanChangeLight(x, y)) dirty = true;
            }
            if (dirty) { field.Rebuild(sources); rebuilds++; }
            else skipped++;
            Compare(field, solid, sources, width, height, null);
        }
        Check(skipped > 20 && rebuilds > 20, "Random mining both skips and rebuilds (" + skipped + " skipped, " + rebuilds + " rebuilt)");
        Check(true, "Skipped torch rebuilds stay bit-identical to full rebuilds over 400 frames");
        return Tuple.Create(skipped, rebuilds);
    }

    static void Compare(TorchLightField field, bool[] solid, List<TorchLightField.Source> sources,
        int width, int height, string label)
    {
        var reference = new TorchLightField(width, height, solid, .5f, .5f, PlacedTorch.PropagationDistance);
        reference.Rebuild(sources);
        for (int i = 0; i < solid.Length; i++)
            if (field.Get(i) != reference.Get(i))
                throw new Exception("Torch light differs from a full rebuild at cell " + (i % width) + "," +
                    (i / width) + ": " + field.Get(i) + " vs " + reference.Get(i));
        if (label != null) Check(true, label);
    }

    static void FlameCellAndLoadedGenerationChecks()
    {
        var root = new GameObject("Bughunt lighting grid") { hideFlags = HideFlags.HideAndDontSave };
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            root.AddComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0f);
            var mapObject = new GameObject("Bughunt lighting map") { hideFlags = HideFlags.HideAndDontSave };
            mapObject.transform.SetParent(root.transform, false);
            var terrain = mapObject.AddComponent<Tilemap>();
            var map = mapObject.AddComponent<MapGenerator>();

            // MapLighting treats only the flame cell (besides lit cells) as able to move a torch light.
            var torchObject = new GameObject("Bughunt torch") { hideFlags = HideFlags.HideAndDontSave };
            torchObject.transform.SetParent(mapObject.transform, false);
            var holder = new Vector3Int(3, -5, 0);
            torchObject.transform.position = terrain.GetCellCenterWorld(holder);
            var torch = torchObject.AddComponent<PlacedTorch>();
            typeof(PlacedTorch).GetField("map", Private).SetValue(torch, map);
            typeof(PlacedTorch).GetField("cell", Private).SetValue(torch, holder);
            var flame = torch.FlameCell;
            Check(flame == holder + Vector3Int.up, "Torch flame lies in the cell above the holder");
            Check(terrain.WorldToCell(torch.LightPosition) == flame, "Torch light sits in the open flame cell");
            for (int y = -1; y <= 2; y++)
                for (int x = -1; x <= 1; x++)
                {
                    var around = holder + new Vector3Int(x, y, 0);
                    if (around != holder && around != flame) terrain.SetTile(around, tile);
                }
            Check(terrain.WorldToCell(torch.LightPosition) == flame, "Blocks around holder and flame do not move the torch light");
            terrain.SetTile(flame, tile);
            Check(terrain.WorldToCell(torch.LightPosition) == holder, "A block in the flame cell moves the light into the holder cell");

            LoadedSaveKeepsNextGenerationRebuild(mapObject.AddComponent<MapLighting>(), map);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
        }
    }

    // Load order: FinishSavedMapRestoreSteps reports GenerationCompleted (streaming already off),
    // then PrepareForLoading builds the field. The next GenerationCompleted (F1 "Map generieren")
    // must rebuild the lighting again instead of being swallowed.
    static void LoadedSaveKeepsNextGenerationRebuild(MapLighting lighting, MapGenerator map)
    {
        var mapField = typeof(MapLighting).GetField("map", Private);
        var rebuild = typeof(MapLighting).GetField("rebuild", Private);
        var requestRebuild = typeof(MapLighting).GetMethod("RequestRebuild", Private);
        var prepared = typeof(MapLighting).GetMethod("ExpectStreamedGenerationCompleted", Private);
        var streaming = typeof(MapGenerator).GetProperty("IsGenerationStreaming").GetSetMethod(true);
        Check(mapField != null && rebuild != null && requestRebuild != null && prepared != null && streaming != null,
            "MapLighting exposes the generation hand-over steps");
        mapField.SetValue(lighting, map);

        // Loaded save.
        streaming.Invoke(map, new object[] { false });
        rebuild.SetValue(lighting, false);
        requestRebuild.Invoke(lighting, null);
        Check((bool)rebuild.GetValue(lighting), "Restored map requests a lighting rebuild");
        rebuild.SetValue(lighting, false);
        prepared.Invoke(lighting, null);
        requestRebuild.Invoke(lighting, null);
        Check((bool)rebuild.GetValue(lighting), "After loading, the next map generation rebuilds the lighting");

        // New streamed game: the field prepared during loading is not built a second time.
        streaming.Invoke(map, new object[] { true });
        rebuild.SetValue(lighting, false);
        prepared.Invoke(lighting, null);
        streaming.Invoke(map, new object[] { false });
        requestRebuild.Invoke(lighting, null);
        Check(!(bool)rebuild.GetValue(lighting), "A streamed new map keeps the lighting prepared during loading");
        requestRebuild.Invoke(lighting, null);
        Check((bool)rebuild.GetValue(lighting), "The following map generation rebuilds the lighting again");
    }
}
