using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Edit Mode checks: blueprint finds and boulders stay inside the side world borders while every inner find of a
// seed stays where it was, and F1 "Map generieren" replaces the exotic content of the previous map.
// Builds its own small map in a temporary additive scene; the open SampleScene is not changed.
public static class BughuntExoticsChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Temp/BughuntExoticsChecks.txt";
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) { File.AppendAllText(Report, "FAIL " + message + "\n"); throw new Exception(message); }
        checks++; File.AppendAllText(Report, "PASS " + message + "\n");
    }
    static bool Inside(int x, int size, int width, int padding) => x >= -width / 2 + padding && x + size <= width - width / 2 - padding;
    // Copy of the original FindFloorSteps search; padding 0 reproduces the search before the border fix exactly.
    static Vector3Int? Reference(MapGenerator map, int depth, int size, System.Random random, int padding)
    {
        int width = map.GeneratedWidth, start = random.Next(Math.Max(1, width - size));
        for (int dy = 0; dy < 36; dy++)
        {
            int y = -depth - dy;
            if (y < 3 - map.GeneratedHeight) break;
            for (int n = 0; n < width - size - 2; n++)
            {
                int x = -width / 2 + 1 + (start + n) % Math.Max(1, width - size - 2);
                if (!Inside(x, size, width, padding)) continue;
                var cell = new Vector3Int(x, y, 0);
                if (!map.IsGeneratedCaveCell(cell) || !map.Terrain.HasTile(cell + Vector3Int.down)) continue;
                bool valid = true;
                for (int a = 0; a < size && valid; a++)
                    for (int b = 0; b < size; b++)
                    {
                        var target = cell + new Vector3Int(a, b, 0);
                        if (map.Terrain.HasTile(target) || map.IsCellProtected(target)) { valid = false; break; }
                    }
                if (valid) return cell;
            }
        }
        return null;
    }
    static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks = 0; Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");

        // Border rule, matching MapWorldBorders: walls at the left edges of cells xMin + padding and xMax - padding.
        var insideMethod = typeof(ExoticWorldContent).GetMethod("InsideBorders", BindingFlags.Static | BindingFlags.NonPublic);
        Check(insideMethod != null, "Randprüfung für Funde und Felsbrocken vorhanden");
        Func<int, int, int, int, bool> insideBorders = (x, size, width, padding) => (bool)insideMethod.Invoke(null, new object[] { x, size, width, padding });
        Check(!insideBorders(-19, 1, 60, 12) && insideBorders(-18, 1, 60, 12), "Linke Wand: Zelle dahinter gesperrt, erste Zelle innen erlaubt");
        Check(insideBorders(17, 1, 60, 12) && !insideBorders(18, 1, 60, 12), "Rechte Wand: letzte Zelle innen erlaubt, Zelle dahinter gesperrt");
        Check(insideBorders(16, 2, 60, 12) && !insideBorders(17, 2, 60, 12), "Felsbrocken (2 Zellen breit) ragen nicht über die rechte Wand");
        Check(insideBorders(-30, 1, 60, 0) && insideBorders(28, 2, 60, 0), "Ohne Seitenabstand bleibt die ganze Kartenbreite erlaubt");
        Check(insideBorders(0, 1, 3, 50) && !insideBorders(-1, 1, 3, 50), "Übergrosser Seitenabstand wird wie bei MapWorldBorders begrenzt");

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            const int width = 64, height = 200, seed = 4711, padding = 8;
            var grid = new GameObject("Exotics test grid", typeof(Grid)); grid.GetComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0);
            var go = new GameObject("Exotics test map", typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGenerator)); go.transform.SetParent(grid.transform, false);
            var map = go.GetComponent<MapGenerator>();
            go.AddComponent<MapWorldBorders>().sidePaddingCells = padding;
            // Small cave pockets, denser near the side edges so that the original search often ends behind a wall.
            var caves = new bool[width * height]; var tiles = new TileBase[width * height]; var random = new System.Random(17);
            for (int depth = 0; depth < height; depth++)
                for (int x = 0; x < width; x++)
                {
                    bool edge = x < padding + 2 || x >= width - padding - 2;
                    caves[depth * width + x] = depth >= 40 && depth < height - 2 && random.NextDouble() < (edge ? .3 : .03);
                }
            for (int row = 0; row < height; row++)
                for (int x = 0; x < width; x++)
                    if (!caves[(height - 1 - row) * width + x]) tiles[row * width + x] = tile;
            map.Terrain.SetTilesBlock(new BoundsInt(-width / 2, 1 - height, 0, width, height, 1), tiles);
            SetField(map, "isGenerated", true); SetField(map, "generatedSeed", seed);
            SetField(map, "generatedWidth", width); SetField(map, "generatedHeight", height);
            SetField(map, "generatedCaveMask", caves); SetField(map, "cachedCaveMaskWidth", width); SetField(map, "cachedCaveMaskHeight", height);
            Check(map.IsGenerated && map.GeneratedWidth == width && map.Terrain.HasTile(new Vector3Int(0, -10, 0)), "Prüfkarte aufgebaut");

            // Same random sequence and search order: inner finds stay identical, finds behind a wall move on to the
            // next valid candidate in the original order.
            var world = ExoticWorldContent.Ensure(map);
            var find = typeof(ExoticWorldContent).GetMethod("FindFloorSteps", Private);
            Check(find != null, "Bodensuche per Reflection erreichbar");
            int compared = 0, behind = 0, innerChanged = 0, mismatched = 0, outside = 0;
            for (int s = 0; s < 80; s++)
                for (int depth = 35; depth < height - 8; depth += 15)
                    for (int size = 1; size <= 2; size++)
                    {
                        Vector3Int? actual = null;
                        var steps = (IEnumerator)find.Invoke(world, new object[] { depth, size, new System.Random(s * 31 + depth), (Action<Vector3Int>)(cell => actual = cell) });
                        while (steps.MoveNext()) { }
                        var original = Reference(map, depth, size, new System.Random(s * 31 + depth), 0);
                        var expected = Reference(map, depth, size, new System.Random(s * 31 + depth), padding);
                        compared++;
                        if (original.HasValue && !Inside(original.Value.x, size, width, padding)) behind++;
                        else if (original != actual) innerChanged++;
                        if (actual != expected) mismatched++;
                        if (actual.HasValue && !Inside(actual.Value.x, size, width, padding)) outside++;
                    }
            Check(behind > 0, "Ursprüngliche Suche fand auf der Prüfkarte Zellen hinter den Wänden (" + behind + " von " + compared + ")");
            Check(outside == 0, "Keine Funde oder Felsbrocken mehr hinter den Weltgrenzen");
            Check(innerChanged == 0, "Funde im Innern bleiben bei gleichem Seed exakt gleich");
            Check(mismatched == 0, "Hinter den Wänden wird in der bisherigen Reihenfolge weitergesucht");

            // F1 "Map generieren": the old map's state is dropped and the new map gets its own seed-stable content.
            var stale = new Vector3Int(0, -5000, 0);
            SetField(world, "state", new ExoticWorldState { caches = new[] { new SavedBlueprintCache { cell = stale } }, boulders = Array.Empty<SavedBoulder>() });
            bool rejected = false;
            try { ExoticWorldContent.ValidateState(world.CaptureState(), width, height); } catch (InvalidDataException) { rejected = true; }
            Check(world.Initialized && rejected, "Veralteter Fund ausserhalb der neuen Karte würde das Speichern verhindern");
            world.Regenerate();
            var state = world.CaptureState();
            Check(state != null && state.caches.Length > 0 && state.boulders.Length > 0, "Neue Karte erhält eigene Bauplanfunde und Felsbrocken");
            ExoticWorldContent.ValidateState(state, width, height);
            Check(state.caches.All(cache => cache.cell != stale && !cache.claimed), "Alte Funde werden verworfen, neuer Zustand ist speicherbar");
            var placement = new System.Random(seed ^ 0x347A52); var caches = new List<Vector3Int>(); var rocks = new List<Vector3Int>();
            for (int depth = 35; depth < height - 8; depth += 45)
            {
                var cache = Reference(map, depth, 1, placement, padding); if (cache.HasValue) caches.Add(cache.Value);
                if (depth >= 80) { var rock = Reference(map, depth + 8, 2, placement, padding); if (rock.HasValue) rocks.Add(rock.Value); }
            }
            Check(state.caches.Select(cache => cache.cell).SequenceEqual(caches) && state.boulders.Select(rock => rock.cell).SequenceEqual(rocks),
                "Neu erzeugte Funde und Felsbrocken folgen der seedstabilen Platzierung");
            Check(state.boulders.All(rock => rock.health == MineBoulder.MaximumHealth), "Neue Felsbrocken sind unbeschädigt");

            string panel = File.ReadAllText("Assets/GameObjects/UI/GpsRuntimePanel.cs");
            int generate = panel.IndexOf("map.GenerateMap();", StringComparison.Ordinal);
            Check(generate >= 0 && panel.IndexOf("ExoticWorldContent.Ensure(map).Regenerate();", generate, StringComparison.Ordinal) > generate,
                "F1 'Map generieren' erneuert danach die exotischen Weltobjekte");
            string content = File.ReadAllText("Assets/GameObjects/Map/ExoticWorldContent.cs");
            int begin = content.IndexOf("public void Regenerate()", StringComparison.Ordinal);
            int end = begin < 0 ? -1 : content.IndexOf("Generate();", begin + 24, StringComparison.Ordinal);
            Check(begin >= 0 && end > begin && content.Substring(begin, end - begin).Contains("PlacedDynamite.Clear(map);"),
                "Neugenerierung entfernt brennendes Dynamit der alten Karte");
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            Object.DestroyImmediate(tile);
        }
        return "PASS: " + checks + " Prüfungen (Weltgrenzen, Seedstabilität, F1-Neugenerierung); Bericht in " + Report;
    }
}
