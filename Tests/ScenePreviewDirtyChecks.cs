using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// The editor map preview (LastPlayedMap) must not mark a clean SampleScene as modified, otherwise every Play start
// via MainMenu asks "Scene(s) Have Been Modified". Two steps:
// 1. Fresh Play start on the free MainMenu: starts a world in an isolated profile and returns to the menu.
//    Then stop Play Mode; LastPlayedMap restores the world as preview.
// 2. Edit Mode afterwards: checks the restored preview left the scene clean, then strips/restores it again with a
//    clean and with a really modified scene. Discards all changes by reopening the scene from disk.
// Report: Temp/ScenePreviewDirtyChecks.txt
public static class ScenePreviewDirtyChecks
{
    const string Report = "Temp/ScenePreviewDirtyChecks.txt";
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;

    public static object Main()
    {
        if (Application.isPlaying)
        {
            if (!MainMenuController.IsVisible || GameplayInputBlocker.IsBlocked) return "Requires fresh Play start on the free MainMenu.";
            File.WriteAllText(Report, "");
            var go = new GameObject("Scene Preview Dirty Probe");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ScenePreviewDirtyProbe>();
            return "Started world, see " + Report + "; stop Play Mode after WORLD READY, then run again in Edit Mode.";
        }
        checks = 0;
        try { return EditModeChecks(); }
        catch (Exception e) { File.AppendAllText(Report, "FAILED " + e.Message + "\n"); return "FAILED: " + e.Message; }
    }

    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + (++checks) + " " + label + "\n");
    }

    static string EditModeChecks()
    {
        var map = Object.FindFirstObjectByType<MapGenerator>();
        Check(map, "MapGenerator in the open scene");
        var scene = map.gameObject.scene;
        var terrain = map.GetComponent<Tilemap>();
        int restored = terrain.GetUsedTilesCount();
        File.AppendAllText(Report, "Edit Mode after Play: dirty=" + scene.isDirty + ", terrain tiles=" + restored + "\n");
        Check(restored > 0, "Last played world restored as preview (" + restored + " tiles)");
        Check(!scene.isDirty, "Restoring the preview after Play leaves the scene clean");

        var strip = typeof(LastPlayedMap).GetMethod("StripPreviewForPlay", Private);
        try
        {
            strip.Invoke(null, null);
            string preview = SessionState.GetString("MiningGame.PreviewBeforePlay", "");
            Check(!string.IsNullOrEmpty(preview) && terrain.GetUsedTilesCount() == 0, "Strip before Play removes the preview");
            Check(!scene.isDirty, "Strip on a clean scene keeps it clean");

            SessionState.SetString("MiningGame.LastPlayedMaps", preview);
            LastPlayedMap.Restore();
            Check(terrain.GetUsedTilesCount() == restored, "Restore brings the preview back");
            Check(!scene.isDirty, "Restore on a clean scene keeps it clean");

            // A real change made before must stay marked through strip and restore.
            EditorSceneManager.MarkSceneDirty(scene);
            strip.Invoke(null, null);
            Check(scene.isDirty, "Strip keeps a really modified scene marked");
            SessionState.SetString("MiningGame.LastPlayedMaps", SessionState.GetString("MiningGame.PreviewBeforePlay", ""));
            LastPlayedMap.Restore();
            Check(scene.isDirty && terrain.GetUsedTilesCount() == restored, "Restore keeps a really modified scene marked");
        }
        finally
        {
            SessionState.EraseString("MiningGame.PreviewBeforePlay");
            SessionState.EraseString("MiningGame.LastPlayedMaps");
            EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
        }
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n");
        return "PASS: " + checks + " checks, see " + Report;
    }
}

public sealed class ScenePreviewDirtyProbe : MonoBehaviour
{
    const string Report = "Temp/ScenePreviewDirtyChecks.txt";
    string save, meta;

    IEnumerator Start()
    {
        save = GameSaveSystem.TestDirectory; meta = MetaProgression.TestDirectory;
        GameSaveSystem.TestDirectory = Path.GetFullPath("Temp/ScenePreviewDirty-" + DateTime.UtcNow.Ticks);
        MetaProgression.TestDirectory = Path.Combine(GameSaveSystem.TestDirectory, "Profile");
        RunNavigation.NewGame("Vorschau-Test");
        float end = Time.realtimeSinceStartup + 120;
        while (LoadingProgress.Active || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy)
        {
            if (Time.realtimeSinceStartup > end) { File.AppendAllText(Report, "FAILED Loading timeout\n"); break; }
            yield return null;
        }
        yield return null; yield return null;
        var map = Object.FindFirstObjectByType<MapGenerator>();
        File.AppendAllText(Report, "WORLD READY generated=" + (map && map.IsGenerated) + "\n");
    }

    // The world stays open until Play stops (LastPlayedMap captures it then); saves on exit still go to the test profile.
    void OnDestroy()
    {
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = meta;
        GameSaveSystem.TestDirectory = save;
    }
}
