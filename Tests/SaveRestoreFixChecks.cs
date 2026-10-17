using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Play Mode (fresh start from MainMenu). Starts an isolated run, buys a pickaxe blueprint, digs a shaft,
// saves, reloads and checks that the blueprint survives and that only generated caves count as caves.
// The isolated save directories stay set until Play Mode ends, so saving on exit cannot touch real slots;
// reset GameSaveSystem.TestDirectory and MetaProgression.TestDirectory afterwards in Edit Mode.
public static class SaveRestoreFixChecks
{
    const string Report = "Temp/SaveRestoreFixChecks.txt";
    static readonly List<string> passed = new();
    static int step, settle;
    static double deadline;
    static bool saved, saveDone;
    static string saveError;
    static MapGenerator map;
    static CraftingRecipe pickaxe;
    static List<Vector3Int> shaft;
    static bool[] before;

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode ab MainMenu erforderlich.");
        passed.Clear(); File.WriteAllText(Report, "");
        string directory = Path.GetFullPath("Temp/SaveRestoreFix-" + DateTime.UtcNow.Ticks);
        GameSaveSystem.TestDirectory = Path.Combine(directory, "saves");
        MetaProgression.TestDirectory = Path.Combine(directory, "meta");
        Application.runInBackground = true;
        step = 0; settle = 0; saved = saveDone = false; saveError = null;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        return "Gestartet; Bericht in " + Report;
    }

    static void Check(bool value, string label) { if (!value) throw new Exception(label); passed.Add(label); File.WriteAllLines(Report, passed); }
    static void Finish(string result) { EditorApplication.update -= Tick; File.AppendAllText(Report, "\n" + result); }

    static bool RunReady()
    {
        var current = Object.FindFirstObjectByType<MapGenerator>();
        return !RunNavigation.IsTransitioning && !LoadingProgress.Active && GameSaveSystem.CanSave && current && current.IsGenerated;
    }

    static bool[] CaveCells(MapGenerator target)
    {
        int width = target.GeneratedWidth, height = target.GeneratedHeight;
        var result = new bool[width * height];
        for (int depth = 0; depth < height; depth++)
            for (int x = 0; x < width; x++)
                result[depth * width + x] = target.IsGeneratedCaveCell(new Vector3Int(x - width / 2, -depth, 0));
        return result;
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) { Finish("FAIL: Play Mode beendet"); return; }
            if (step > 0 && EditorApplication.timeSinceStartup > deadline) throw new Exception("Zeitüberschreitung in Schritt " + step);
            switch (step)
            {
                case 0:
                    RunNavigation.NewGame("Pruefung");
                    deadline = EditorApplication.timeSinceStartup + 300; step = 1; break;
                case 1:
                    if (!RunReady()) { settle = 0; return; }
                    if (++settle < 30) return;
                    BeforeSave(); step = 2; break;
                case 2:
                    if (!saveDone) return;
                    Check(saved, "Spielstand gespeichert: " + saveError);
                    Check(RunNavigation.LoadGame(1, out string loadError), "Laden gestartet: " + loadError);
                    deadline = EditorApplication.timeSinceStartup + 300; settle = 0; step = 3; break;
                case 3:
                    if (!RunNavigation.IsTransitioning && !LoadingProgress.Active && settle == 0) return; // wait until loading begins
                    settle = 1; step = 4; break;
                case 4:
                    if (!RunReady()) { settle = 1; return; }
                    if (++settle < 30) return;
                    AfterLoad(); Finish("PASS (" + passed.Count + ")"); break;
            }
        }
        catch (Exception ex) { Finish("FAIL: " + ex); }
    }

    static void BeforeSave()
    {
        map = Object.FindFirstObjectByType<MapGenerator>();
        var camera = Camera.main;
        Check(camera && camera.GetComponent<CameraFollow>() && camera.GetComponent<CameraFollow>().enabled, "Kamera folgt dem Spieler");
        pickaxe = ExoticCatalog.AllRecipes.First(recipe => recipe && recipe.output && recipe.output.item == Item.IronPickaxe);
        Check(!RecipeUnlocks.IsUnlocked(pickaxe) && RecipeUnlocks.Unlock(pickaxe), "Spitzhacken-Bauplan im Run gekauft");

        int width = map.GeneratedWidth, minimum = Mathf.Max(1, map.caveGeneration.minimumDepth);
        shaft = new List<Vector3Int>();
        for (int x = -width / 2 + 10; x < width / 2 - 10 && shaft.Count == 0; x += 7)
        {
            var column = Enumerable.Range(minimum + 5, 12).Select(depth => new Vector3Int(x, -depth, 0)).ToList();
            if (column.All(cell => map.Terrain.HasTile(cell) && !map.IsCellProtected(cell))) shaft = column;
        }
        Check(shaft.Count > 0, "Schacht-Position gefunden");
        foreach (var cell in shaft) map.Terrain.SetTile(cell, null);
        Check(shaft.All(cell => !map.IsGeneratedCaveCell(cell)), "Gegrabener Schacht ist vor dem Speichern keine Höhle");
        before = CaveCells(map);
        Check(before.Count(value => value) > 100, "Karte enthält generierte Höhlen (" + before.Count(value => value) + " Zellen)");
        map.StartCoroutine(GameSaveSystem.Save(1, map, (ok, error) => { saved = ok; saveError = error; saveDone = true; }));
    }

    static void AfterLoad()
    {
        map = Object.FindFirstObjectByType<MapGenerator>();
        Check(shaft.All(cell => !map.Terrain.HasTile(cell)), "Schacht ist nach dem Laden offen");
        Check(shaft.All(cell => !map.IsGeneratedCaveCell(cell)), "Gegrabener Schacht gilt nach dem Laden nicht als Höhle");
        var after = CaveCells(map);
        int mismatches = 0; for (int i = 0; i < Math.Min(before.Length, after.Length); i++) if (before[i] != after[i]) mismatches++;
        Check(after.Length == before.Length && mismatches == 0, "Höhlenmaske nach dem Laden identisch (" + mismatches + " Abweichungen)");
        pickaxe = ExoticCatalog.AllRecipes.First(recipe => recipe && recipe.output && recipe.output.item == Item.IronPickaxe);
        Check(RecipeUnlocks.IsUnlocked(pickaxe), "Spitzhacken-Bauplan nach dem Laden vorhanden");
        var camera = Camera.main;
        Check(camera && camera.GetComponent<CameraFollow>() && camera.GetComponent<CameraFollow>().enabled, "Kamera folgt nach dem Laden");
    }
}
