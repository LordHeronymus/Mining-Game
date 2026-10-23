using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// AutoSave must not strip the map kept in the editor (Play stop, "Map im Editor generieren"), and must not save in the
// first Edit Mode frame after a long Play session. EditorQueueActivity must refresh every script "-Install" copies to
// EditorQueue/bin (including the focus guard). Uses a temporary additive scene and temporary directories only.
public static class BughuntEditorToolsChecks
{
    const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Report = "Temp/BughuntEditorToolsChecks.txt";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear(); Directory.CreateDirectory("Temp");
        try
        {
            CheckAutoSave();
            CheckScriptRefresh();
            File.WriteAllText(Report, string.Join("\n", checks) + "\n");
            return checks.Count + " checks passed";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
            File.WriteAllText(Report, string.Join("\n", checks) + "\nFAIL " + inner.Message + "\n");
            throw;
        }
    }

    static void CheckAutoSave()
    {
        var holds = typeof(AutoSave).GetMethod("HoldsMapPreview", StaticAny);
        var hasTiles = typeof(AutoSave).GetMethod("HasPreviewTiles", StaticAny);
        var tick = typeof(AutoSave).GetMethod("Tick", StaticAny);
        var playMode = typeof(AutoSave).GetMethod("OnPlayModeChanged", StaticAny);
        var nextField = typeof(AutoSave).GetField("next", StaticAny);
        Check(holds != null && hasTiles != null, "AutoSave erkennt eine im Editor behaltene Map");
        var toSave = typeof(AutoSave).GetMethod("ScenesToSave", StaticAny);
        Check(toSave != null && Calls(tick, toSave) && Calls(toSave, holds) && Calls(holds, hasTiles), "AutoSave prüft vor dem Speichern, ob die Gameplay-Szene eine Map hält");
        // R4-editor-tests-0: a held map pauses only the gameplay scene; other scenes and changed assets are still saved.
        var saveScenes = typeof(EditorSceneManager).GetMethod("SaveScenes", new[] { typeof(Scene[]) });
        var saveOpen = typeof(EditorSceneManager).GetMethod("SaveOpenScenes", Type.EmptyTypes);
        var saveAssets = typeof(AssetDatabase).GetMethod("SaveAssets", Type.EmptyTypes);
        Check(saveScenes != null && Calls(tick, saveScenes) && saveOpen != null && !Calls(tick, saveOpen) && saveAssets != null && Calls(tick, saveAssets),
            "AutoSave speichert die übrigen Szenen einzeln und die Assets auch bei pausierter SampleScene");
        Check(typeof(LoadingSceneData).GetField("GameplayScene", StaticAny)?.GetRawConstantValue() as string == "Assets/Scenes/SampleScene.unity",
            "Übersprungen wird dieselbe Szene, die LoadingSceneData beim Speichern leert");

        Check(playMode != null && nextField != null, "AutoSave reagiert auf Play-Mode-Wechsel");
        double savedNext = (double)nextField.GetValue(null);
        try
        {
            nextField.SetValue(null, 0d);
            playMode.Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
            Check((double)nextField.GetValue(null) == 0d, "Play-Start verschiebt das Intervall nicht");
            playMode.Invoke(null, new object[] { PlayModeStateChange.EnteredEditMode });
            Check((double)nextField.GetValue(null) >= EditorApplication.timeSinceStartup + 299,
                "Nach dem Play-Stopp beginnt ein neues 5-Minuten-Intervall (kein Speichern im ersten Edit-Frame)");
        }
        finally { nextField.SetValue(null, savedNext); }

        var previousActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            // New objects land in the temporary scene, never in the open SampleScene.
            SceneManager.SetActiveScene(scene);
            // Inactive objects: no OnEnable of the ExecuteAlways appearance components in Edit Mode.
            var grid = new GameObject("BughuntEditorToolsGrid", typeof(Grid));
            grid.SetActive(false);
            var mapObject = new GameObject("BughuntEditorToolsMap", typeof(Tilemap));
            mapObject.transform.SetParent(grid.transform, false);
            var map = mapObject.AddComponent<MapGenerator>();
            var terrain = mapObject.GetComponent<Tilemap>();
            var ore = NewLayer(grid, "Ore");
            var artifacts = NewLayer(grid, "Artifacts");
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            Check(grid.scene == scene && mapObject.scene == scene, "Testobjekte liegen nur in der temporären Szene");
            typeof(MapGenerator).GetField("oreOverlay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, ore);
            typeof(MapGenerator).GetField("artifactOverlay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, artifacts);

            bool Holds() => (bool)holds.Invoke(null, new object[] { scene });
            Check(!Holds(), "Leere Map (gespeicherte SampleScene) blockiert AutoSave nicht");
            terrain.SetTile(new Vector3Int(3, -2, 0), tile);
            Check(Holds(), "Behaltene Terrain-Kacheln halten AutoSave zurück");
            terrain.ClearAllTiles();
            ore.SetTile(new Vector3Int(1, -1, 0), tile);
            Check(Holds(), "Erz-Kacheln allein halten AutoSave zurück");
            ore.ClearAllTiles();
            artifacts.SetTile(new Vector3Int(2, -5, 0), tile);
            Check(Holds(), "Artefakt-Kacheln allein halten AutoSave zurück");
            artifacts.ClearAllTiles();
            Check(!Holds(), "Nach dem Leeren (manuelles Speichern) speichert AutoSave wieder");
            EditorSceneManager.MarkSceneDirty(scene);
            var pickArgs = new object[] { false };
            var picked = (Scene[])toSave.Invoke(null, pickArgs);
            Check(!picked.Contains(scene) && picked.All(s => s.isDirty && !string.IsNullOrEmpty(s.path)),
                "AutoSave wählt nur geänderte Szenen mit Pfad (temporäre Szene ohne Pfad bleibt aussen vor)");
            var gameplayScene = typeof(LoadingSceneData).GetField("GameplayScene", StaticAny)?.GetRawConstantValue() as string;
            bool gameplayHeld = false;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isLoaded && open.isDirty && open.path == gameplayScene && (bool)holds.Invoke(null, new object[] { open })) gameplayHeld = true;
            }
            Check((bool)pickArgs[0] == gameplayHeld && !(gameplayHeld && picked.Any(s => s.path == gameplayScene)),
                "Eine SampleScene mit Map wird übersprungen und als pausiert gemeldet");
        }
        finally
        {
            Object.DestroyImmediate(tile);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Tilemap NewLayer(GameObject grid, string name)
    {
        var layer = new GameObject("BughuntEditorTools" + name, typeof(Tilemap));
        layer.transform.SetParent(grid.transform, false);
        return layer.GetComponent<Tilemap>();
    }

    static void CheckScriptRefresh()
    {
        var listField = typeof(EditorQueueActivity).GetField("InstalledScripts", StaticAny);
        var refresh = typeof(EditorQueueActivity).GetMethod("RefreshScripts", StaticAny);
        Check(listField != null && refresh != null, "EditorQueueActivity frischt die Skriptkopien über eine gemeinsame Liste auf");
        var listed = (string[])listField.GetValue(null);
        Check(listed.Contains("editor-focus.ps1"), "Fokus-Wächter editor-focus.ps1 wird mit aufgefrischt");

        var install = Regex.Match(File.ReadAllText("Tools/editor-queue-server.ps1"), @"foreach \(\$name in ([^)]*)\)\s*\{\s*Copy-Item");
        Check(install.Success, "-Install-Liste in Tools/editor-queue-server.ps1 gefunden");
        var installed = Regex.Matches(install.Groups[1].Value, @"'([^']+)'").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        Check(installed.Length > 0 && new HashSet<string>(installed).SetEquals(listed),
            "Aufgefrischt werden genau die Skripte, die -Install kopiert (" + string.Join(", ", installed) + ")");

        string root = Path.GetFullPath("Temp/BughuntEditorToolsChecks-" + Guid.NewGuid().ToString("N"));
        string tools = Path.Combine(root, "Tools"), bin = Path.Combine(root, "EditorQueue", "bin");
        try
        {
            Directory.CreateDirectory(tools);
            Check((int)refresh.Invoke(null, new object[] { tools, bin }) == 0 && !Directory.Exists(bin),
                "Ohne -Install (kein bin-Ordner) wird nichts angelegt");
            Directory.CreateDirectory(bin);
            foreach (string name in listed) File.WriteAllText(Path.Combine(tools, name), "# neu " + name + "\n");
            string missing = listed.First(name => name != "editor-focus.ps1");
            foreach (string name in listed.Where(name => name != missing)) File.WriteAllText(Path.Combine(bin, name), "# alt\n");

            int updated = (int)refresh.Invoke(null, new object[] { tools, bin });
            Check(updated == listed.Length - 1, "Alle veralteten Kopien werden ersetzt (" + updated + ")");
            foreach (string name in listed.Where(name => name != missing))
                Check(File.ReadAllText(Path.Combine(bin, name)) == "# neu " + name + "\n", name + " in bin entspricht Tools/");
            Check(!File.Exists(Path.Combine(bin, missing)), "Nicht installierte Kopie (" + missing + ") wird nicht neu angelegt");
            Check(Directory.GetFiles(bin).Length == listed.Length - 1, "Keine Zwischendateien bleiben in bin liegen");
            Check((int)refresh.Invoke(null, new object[] { tools, bin }) == 0, "Gleiche Kopien werden nicht erneut geschrieben");
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    // Looks for a direct call of target in the IL of method (opcode call = 0x28 followed by a metadata token).
    static bool Calls(MethodInfo method, MethodInfo target)
    {
        byte[] il = method?.GetMethodBody()?.GetILAsByteArray();
        if (il == null || target == null) return false;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            try { if (method.Module.ResolveMethod(token) == target) return true; }
            catch (Exception) { }
        }
        return false;
    }
}
