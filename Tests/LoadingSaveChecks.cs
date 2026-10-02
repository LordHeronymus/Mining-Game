using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LoadingSaveChecks
{
    static string output, oldDirectory;
    static bool background, waiting;
    static double began;
    static MapGenerator original;
    static int terrain, ores, artifacts, occupancy, seed, money;
    static float scale;

    public static object Main()
    {
        original = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        if (!EditorApplication.isPlaying || !original || !GameSaveSystem.CanSave)
            return "Requires a completed run.";
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadingSaveChecks.txt"));
        oldDirectory = GameSaveSystem.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LoadingSave-" + DateTime.UtcNow.Ticks));
        background = Application.runInBackground; Application.runInBackground = true;
        scale = Time.timeScale; Time.timeScale = 0;
        terrain = Hash(original.Terrain); ores = Hash(original.OreOverlay); artifacts = Hash(original.ArtifactOverlay);
        occupancy = MaskHash(original); seed = original.ActiveSeed; money = StatsManager.Instance.Money;
        began = EditorApplication.timeSinceStartup; waiting = false;
        EditorApplication.update += Poll;
        original.StartCoroutine(GameSaveSystem.Save(1, original, (ok, error) => {
            if (!ok) { Finish("FAIL: Save: " + error); return; }
            if (!RunNavigation.LoadGame(1, out error)) { Finish("FAIL: Load: " + error); return; }
            waiting = true;
        }));
        return output;
    }

    static void Poll()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - began > 180)
        { Finish("FAIL: Interrupted or timed out"); return; }
        if (!waiting || LoadingProgress.Active || RunNavigation.IsTransitioning) return;
        try
        {
            var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            var camera = Camera.main;
            if (!map || map == original || map.ActiveSeed != seed || Hash(map.Terrain) != terrain ||
                Hash(map.OreOverlay) != ores || Hash(map.ArtifactOverlay) != artifacts)
                throw new Exception("Tile layers changed on reload");
            if (MaskHash(map) != occupancy) throw new Exception("Terrain material mask changed on reload");
            if (StatsManager.Instance.Money != money) throw new Exception("Run state changed");
            if (!player || !camera || !camera.GetComponent<CameraFollow>().enabled ||
                camera.GetComponent<CameraFollow>().target != player.transform ||
                !camera.GetComponent<CameraWorldBorderClamp>().enabled || !GameplayTestSettings.CameraFollowEnabled)
                throw new Exception("Camera follow/clamp");
            if (GameplayInputBlocker.IsBlocked || Time.timeScale != 1) throw new Exception("Gameplay was not released");
            var properties = new MaterialPropertyBlock(); map.GetComponent<TilemapRenderer>().GetPropertyBlock(properties);
            if (properties.GetVector("_UniformStone").x <= 0 || !properties.GetTexture("_TestOccupancy"))
                throw new Exception("Terrain material was not prepared");
            Finish("PASS: Actual save/reload, identical terrain/ore/artifact tiles, identical material mask, run state, gameplay release and player camera. Test save is isolated under Temp.");
        }
        catch (Exception error) { Finish("FAIL: " + error); }
    }
    static int Hash(Tilemap map)
    {
        unchecked { int hash = 17; foreach (var tile in map.GetTilesBlock(map.cellBounds)) hash = hash * 31 + (tile ? tile.GetInstanceID() : 0); return hash; }
    }
    static int MaskHash(MapGenerator map)
    {
        var field = typeof(UniformStoneAppearance).GetField("occupancy", BindingFlags.NonPublic | BindingFlags.Instance);
        var texture = (Texture2D)field.GetValue(map.GetComponent<UniformStoneAppearance>());
        unchecked { int hash = 17; foreach (var pixel in texture.GetPixels32()) hash = hash * 31 + pixel.GetHashCode(); return hash; }
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Poll; GameSaveSystem.TestDirectory = oldDirectory;
        Application.runInBackground = background; Time.timeScale = scale;
        File.WriteAllText(output, result);
    }
}
