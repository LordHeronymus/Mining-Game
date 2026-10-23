using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Play Mode (fresh start from MainMenu). The save tile shows the deepest point of the run, not the position at saving:
// starts an isolated run, digs a shaft, stands at its bottom, returns to the surface, saves and reloads.
// Also checks older saves: a header without the value keeps its old depth, a payload without it takes the deepest row.
// The isolated save directories stay set until Play Mode ends, so saving on exit cannot touch real slots;
// reset GameSaveSystem.TestDirectory and MetaProgression.TestDirectory afterwards in Edit Mode.
public static class SaveMaxDepthChecks
{
    const string Report = "Temp/SaveMaxDepthChecks.txt";
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<string> passed = new();
    static int step, settle, deepest;
    static double deadline;
    static bool saved, saveDone;
    static string saveError;
    static Vector3 start;
    static List<Vector3Int> shaft;

    public static object Main()
    {
        if (!Application.isPlaying) throw new Exception("Play Mode ab MainMenu erforderlich.");
        passed.Clear(); File.WriteAllText(Report, "");
        string directory = Path.GetFullPath("Temp/SaveMaxDepth-" + DateTime.UtcNow.Ticks);
        GameSaveSystem.TestDirectory = Path.Combine(directory, "saves");
        MetaProgression.TestDirectory = Path.Combine(directory, "meta");
        Application.runInBackground = true;
        step = 0; settle = 0; deepest = 0; saved = saveDone = false; saveError = null;
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
    static PlayerMovement Player => Object.FindFirstObjectByType<PlayerMovement>();
    static CompactHud Hud => Object.FindFirstObjectByType<CompactHud>();
    static void Place(Vector3 position)
    {
        var player = Player; var body = player.GetComponent<Rigidbody2D>();
        player.transform.position = position; body.position = position; body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }
    static int Shown(GameSaveSystem.Summary summary) =>
        (int)typeof(SaveSlotPanel).GetMethod("ShownDepth", Hidden).Invoke(null, new object[] { summary });

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) { Finish("FAIL: Play Mode beendet"); return; }
            if (step > 0 && EditorApplication.timeSinceStartup > deadline) throw new Exception("Zeitüberschreitung in Schritt " + step);
            switch (step)
            {
                case 0:
                    RunNavigation.NewGame("Pruefung Tiefe");
                    deadline = EditorApplication.timeSinceStartup + 300; step = 1; break;
                case 1:
                    if (!RunReady()) { settle = 0; return; }
                    if (++settle < 30) return;
                    GoDown(); settle = 0; step = 2; break;
                case 2:
                    if (++settle < 20) return;
                    deepest = Hud.DepthMeters;
                    Check(deepest >= 5, "Spieler steht unten im Schacht (" + deepest + " m)");
                    Check(GameSaveSystem.MaxDepthMeters == deepest, "Maximale Tiefe folgt dem Spieler (" + GameSaveSystem.MaxDepthMeters + " m)");
                    Place(start); settle = 0; step = 3; break;
                case 3:
                    if (++settle < 20) return;
                    Check(Hud.DepthMeters < deepest, "Spieler wieder oben (" + Hud.DepthMeters + " m)");
                    Check(GameSaveSystem.MaxDepthMeters == deepest, "Maximale Tiefe bleibt nach dem Aufstieg");
                    var map = Object.FindFirstObjectByType<MapGenerator>();
                    map.StartCoroutine(GameSaveSystem.Save(1, map, (ok, error) => { saved = ok; saveError = error; saveDone = true; }));
                    step = 4; break;
                case 4:
                    if (!saveDone) return;
                    Check(saved, "Spielstand gespeichert: " + saveError);
                    var summary = GameSaveSystem.GetSummary(1);
                    Check(summary != null && summary.hasMaxDepth && summary.maxDepth == deepest, "Kachel-Daten enthalten die maximale Tiefe (" + summary?.maxDepth + " m)");
                    Check(summary.depth < deepest, "Position beim Speichern bleibt separat (" + summary.depth + " m)");
                    Check(Shown(summary) == deepest, "Kachel zeigt die maximale Tiefe");
                    LegacyChecks();
                    Check(RunNavigation.LoadGame(1, out string loadError), "Laden gestartet: " + loadError);
                    deadline = EditorApplication.timeSinceStartup + 300; settle = 0; step = 5; break;
                case 5:
                    if (!RunNavigation.IsTransitioning && !LoadingProgress.Active && settle == 0) return; // wait until loading begins
                    settle = 1; step = 6; break;
                case 6:
                    if (!RunReady()) { settle = 1; return; }
                    if (++settle < 30) return;
                    Check(GameSaveSystem.MaxDepthMeters == deepest, "Maximale Tiefe nach dem Laden erhalten (" + GameSaveSystem.MaxDepthMeters + " m)");
                    var camera = Camera.main;
                    Check(camera && camera.GetComponent<CameraFollow>() && camera.GetComponent<CameraFollow>().enabled, "Kamera folgt nach dem Laden");
                    Finish("PASS (" + passed.Count + ")"); break;
            }
        }
        catch (Exception ex) { Finish("FAIL: " + ex); }
    }

    static void GoDown()
    {
        var map = Object.FindFirstObjectByType<MapGenerator>();
        Check(GameSaveSystem.MaxDepthMeters <= Hud.DepthMeters + 1, "Neuer Run beginnt ohne alte Tiefe (" + GameSaveSystem.MaxDepthMeters + " m)");
        start = Player.transform.position;
        int width = map.GeneratedWidth, minimum = Mathf.Max(1, map.caveGeneration.minimumDepth);
        shaft = new List<Vector3Int>();
        for (int x = -width / 2 + 10; x < width / 2 - 10 && shaft.Count == 0; x += 7)
        {
            var column = Enumerable.Range(minimum + 5, 8).Select(depth => new Vector3Int(x, -depth, 0)).ToList();
            if (column.All(cell => map.Terrain.HasTile(cell) && !map.IsCellProtected(cell)) && map.Terrain.HasTile(column[^1] + Vector3Int.down))
                shaft = column;
        }
        Check(shaft.Count > 0, "Schacht-Position gefunden");
        foreach (var cell in shaft) map.Terrain.SetTile(cell, null);
        Place(map.Terrain.GetCellCenterWorld(shaft[^1]));
    }

    static void LegacyChecks()
    {
        // Header of an older save: no maximum stored, the tile keeps the depth at saving.
        var old = JsonUtility.FromJson<GameSaveSystem.Summary>("{\"slot\":1,\"depth\":12,\"money\":5,\"name\":\"Alt\"}");
        Check(!old.hasMaxDepth && Shown(old) == 12, "Alter Spielstand ohne Wert zeigt weiter seine Tiefe");
        var copy = JsonUtility.FromJson<GameSaveSystem.Summary>(JsonUtility.ToJson(new GameSaveSystem.Summary { depth = 3, maxDepth = 87, hasMaxDepth = true }));
        Check(copy.hasMaxDepth && Shown(copy) == 87, "Neuer Wert übersteht Speichern und Lesen");

        // Payload of an older save: the deepest row from the progression (cells) and the position count.
        var map = Object.FindFirstObjectByType<MapGenerator>();
        var restore = typeof(GameSaveSystem).GetMethod("RestoredMaxDepth", Hidden);
        float surface = map.Terrain.CellToWorld(Vector3Int.up).y;
        var state = new RunSaveState { progression = new MetaRunState { maxDepth = 40 }, playerPosition = new Vector3(0, surface - 2.2f, 0) };
        int rows = (int)restore.Invoke(null, new object[] { map, state });
        int expected = Mathf.FloorToInt(surface - map.Terrain.CellToWorld(new Vector3Int(0, -39, 0)).y);
        Check(rows == expected && rows > 2, "Alter Spielstand übernimmt die tiefste Zeile (" + rows + " m)");
        state.maxDepthMeters = expected + 50;
        Check((int)restore.Invoke(null, new object[] { map, state }) == expected + 50, "Gespeicherter Wert hat Vorrang, wenn grösser");
        state.maxDepthMeters = -7; state.progression = null;
        Check((int)restore.Invoke(null, new object[] { map, state }) == 2, "Ungültiger Wert fällt auf die Position zurück");
    }
}
