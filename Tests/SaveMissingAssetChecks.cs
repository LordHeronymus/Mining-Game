using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// A save keeps the map-generation settings of its run, including asset references such as the layer backgrounds.
// When such an asset is replaced in the profile (PR #82: new Layer 2 background), the save must still load:
// the unknown reference takes the profile's current asset instead of rejecting the whole save.
// Optional: Temp/SaveMissingAssetSample.thsave (a copy of a real save) is loaded from a temporary directory.
// Runs in Edit Mode; the real profile, saves and scene stay untouched.
public static class SaveMissingAssetChecks
{
    const string Report = "Temp/SaveMissingAssetChecks.txt", Sample = "Temp/SaveMissingAssetSample.thsave";
    const string UnknownKey = "00000000000000000000000000000000:21300000";
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static GpsValue Field(GpsValue layer, string name) => layer.children.Find(field => field.name == name);

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        if (GameSaveSystem.IsBusy || GameSaveSystem.HasPendingLoad || GameSaveSystem.IsAutoSaving) throw new InvalidOperationException("Spielstandsystem ist belegt.");
        checks.Clear();
        var save = typeof(GameSaveSystem);
        string previousDirectory = GameSaveSystem.TestDirectory, previousRunName = GameSaveSystem.ActiveRunName;
        int previousSlot = GameSaveSystem.ActiveSlot;
        object previousActiveDirectory = save.GetField("activeDirectory", Hidden).GetValue(null);
        string directory = Path.GetFullPath("Temp/SaveMissingAsset-" + DateTime.UtcNow.Ticks);
        GameObject holder = null;
        try
        {
            var map = GpsSettings.Document.records.First(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
            var fields = GpsSchema.Fields(typeof(MapGenerator)).Where(field => field.applyTime == GpsApplyTime.NewMap)
                .Select(field => GpsSettings.GetValue(map.key, field.name)).Where(field => field != null).ToArray();
            int width = (int)GpsSettings.GetValue(map.key, "mapWidth").number, height = (int)GpsSettings.GetValue(map.key, "mapHeight").number;
            var current = GpsSettings.GetValue(map.key, "layers");
            Check(current != null && current.children.Count >= 2, "Profil hat mindestens zwei Layer");
            string currentBackground = Field(current.children[1], "backgroundSprite").text, currentStone = Field(current.children[1], "stone").text;
            Check(!string.IsNullOrEmpty(currentBackground) && GpsSettings.Profile.Resolve(currentBackground), "Layer 2 hat ein bekanntes Hintergrundbild");
            Check(!GpsSettings.Profile.Resolve(UnknownKey), "Testschlüssel ist im Profil unbekannt");
            Check(GpsSettings.ValidateGeneration(Clone(fields), width, height, out string error), "Unveränderter Kartenstand bleibt gültig: " + error);

            // The case of "The One": the save still names the old Layer 2 background.
            var saved = Clone(fields); var layers = saved.First(field => field.name == "layers");
            Field(layers.children[1], "backgroundSprite").text = UnknownKey;
            string savedJson = JsonUtility.ToJson(layers);
            Check(Quiet(() => GpsSettings.ValidateGeneration(saved, width, height, out error)), "Spielstand mit fehlendem Hintergrundbild bleibt ladbar: " + error);
            Check(JsonUtility.ToJson(layers) == savedJson, "Prüfung verändert die Spielstanddaten nicht");

            holder = new GameObject("SaveMissingAssetChecks Map") { hideFlags = HideFlags.HideAndDontSave }; holder.SetActive(false);
            var generator = holder.AddComponent<MapGenerator>();
            Quiet(() => { GpsSettings.RestoreGeneration(generator, saved); return true; });
            var expected = (Sprite)GpsSettings.Profile.Resolve(currentBackground);
            Check(generator.layers != null && generator.layers.Length == current.children.Count, "Layer werden aus dem Spielstand übernommen");
            Check(generator.layers[1].backgroundSprite == expected, "Fehlendes Hintergrundbild wird durch das aktuelle ersetzt (" + expected.name + ")");
            Check(generator.layers[1].stone == (Block)GpsSettings.Profile.Resolve(currentStone), "Übrige Werte des Layers bleiben aus dem Spielstand");

            // Any unknown reference falls back the same way; known but invalid values stay refused.
            var stone = Clone(fields); Field(stone.First(field => field.name == "layers").children[1], "stone").text = UnknownKey;
            Check(Quiet(() => GpsSettings.ValidateGeneration(stone, width, height, out error)), "Fehlende Gesteinsart wird ersetzt statt abgelehnt: " + error);
            string oreKey = GpsSettings.Profile.assets.FirstOrDefault(entry => entry.asset is Block block && !block.IsStone && block.id != BlockType.Dirt)?.key;
            var ore = Clone(fields); Field(ore.First(field => field.name == "layers").children[1], "stone").text = oreKey;
            Check(oreKey != null && !GpsSettings.ValidateGeneration(ore, width, height, out _), "Erz als Gesteinsart wird weiterhin abgelehnt");

            // Optional: a copy of a real save from before the asset swap loads from a temporary directory.
            if (File.Exists(Sample))
            {
                Directory.CreateDirectory(directory); GameSaveSystem.TestDirectory = directory;
                File.Copy(Sample, GameSaveSystem.SlotPath(1));
                bool loaded = Quiet(() => GameSaveSystem.PrepareLoad(1, out error));
                GameSaveSystem.CancelPendingLoad();
                Check(loaded, "Kopie des echten Spielstands lädt: " + error);
            }
            else checks.Add("SKIP Kopie eines echten Spielstands (" + Sample + " fehlt)");

            string report = "PASS (" + checks.Count(line => line.StartsWith("PASS")) + ")\n" + string.Join("\n", checks);
            File.WriteAllText(Report, report); return report;
        }
        catch (Exception ex) { File.WriteAllText(Report, "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            if (holder) Object.DestroyImmediate(holder);
            GameSaveSystem.CancelPendingLoad();
            GameSaveSystem.TestDirectory = previousDirectory;
            save.GetProperty("ActiveSlot").GetSetMethod(true).Invoke(null, new object[] { previousSlot });
            save.GetProperty("ActiveRunName").GetSetMethod(true).Invoke(null, new object[] { previousRunName });
            save.GetField("activeDirectory", Hidden).SetValue(null, previousActiveDirectory);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (Exception) { }
        }
    }

    static GpsValue[] Clone(GpsValue[] fields) => fields.Select(field => field.Copy()).ToArray();
    // The fallback logs a warning; keep it out of the check's error count.
    static bool Quiet(Func<bool> action)
    {
        bool previous = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = false;
        try { return action(); } finally { Debug.unityLogger.logEnabled = previous; }
    }
}
