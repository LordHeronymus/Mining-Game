using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks for R2-gps-bounds-1: a layer's stone ("Gesteinsart") must be a stone block (dirt only in the top layer).
// An ore, dirt further down or "—" is refused when set, dropped from saved own values and save games,
// never applied to the map and not offered in the F1 dropdown; otherwise a new run hangs in the loading screen.
// Runs in Edit Mode on a temporary profile; the real profile, overrides and scene stay untouched.
public static class BughuntLayerStoneChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static string Json(GpsValue node) => JsonUtility.ToJson(node);
    static GpsValue Stone(GpsValue layers, int index) => layers.children[index].children.Find(field => field.name == "stone");

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        bool autoCommit = GpsSettings.AutoCommit; bool? overridesActive = GpsUserOverrides.TestActive;
        GameObject holder = null;
        try
        {
            GpsSettings.AutoCommit = false; GpsUserOverrides.TestActive = false;
            GpsSettings.UseProfile(temporary, Copy(original));
            var map = GpsSettings.Document.records.First(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
            var layers = GpsSettings.GetValue(map.key, "layers"); string layersBefore = Json(layers);
            Check(layers != null && layers.children.Count >= 3, "Profil hat mindestens drei Layer (" + (layers?.children.Count ?? 0) + ")");
            var blocks = temporary.assets.Where(entry => entry.asset is Block).ToList();
            string oreKey = blocks.FirstOrDefault(entry => !((Block)entry.asset).IsStone && ((Block)entry.asset).id != BlockType.Dirt)?.key;
            string dirtKey = blocks.FirstOrDefault(entry => ((Block)entry.asset).id == BlockType.Dirt)?.key;
            Check(oreKey != null && dirtKey != null, "Profil enthält Erz- und Erdblock");

            // Valid profile stays valid (existing profiles, own values and saves keep loading).
            Check(GpsSettings.ValidateDocument(Copy(original), out string error), "Mitgeliefertes Profil bleibt gültig: " + error);
            Check(GpsSettings.IsValidOverride(map, layers), "Gültige Layer als eigener Wert erlaubt");
            Check(ValidGeneration(map, null, out error), "Spielstand mit gültigen Layern bleibt ladbar: " + error);

            foreach (var (key, name) in new[] { (oreKey, "Erz"), (dirtKey, "Erde in Layer 3"), ((string)null, "—") })
            {
                var broken = JsonUtility.FromJson<GpsValue>(layersBefore); Stone(broken, 2).text = key;
                Check(!GpsSettings.SetValue(map.key, broken, out error) && error != null, name + " als Gesteinsart wird beim Setzen abgelehnt: " + error);
                Check(Json(GpsSettings.GetValue(map.key, "layers")) == layersBefore, name + ": abgelehnter Wert bleibt nicht im Dokument");
                Check(!GpsSettings.IsValidOverride(map, broken), name + ": eigener Wert wird beim Laden verworfen");
                var document = Copy(original); document.records.First(record => record.key == map.key).fields[map.fields.FindIndex(field => field.name == "layers")] = broken;
                Check(!GpsSettings.ValidateDocument(document, out _), name + ": Dokument ungültig (Speichern, Map generieren)");
                Check(!ValidGeneration(map, broken, out _), name + ": Spielstand mit diesen Layern wird abgewiesen statt zu hängen");
            }
            var dirtTop = JsonUtility.FromJson<GpsValue>(layersBefore); Stone(dirtTop, 0).text = dirtKey;
            Check(GpsSettings.IsValidOverride(map, dirtTop), "Erde im obersten Layer bleibt erlaubt");

            // Saved own values (UserSettings/GpsOverrides.json): an invalid stone is dropped, the profile value stays.
            var brokenDoc = Copy(original); Stone(brokenDoc.records.First(record => record.key == map.key).fields.Find(field => field.name == "layers"), 2).text = oreKey;
            var entries = GpsUserOverrides.Diff(brokenDoc, original, temporary);
            Check(entries.Count > 0, "Eigener Wert für die Gesteinsart erkannt (" + entries.Count + ")");
            var target = Copy(original); var stale = new List<GpsUserOverrides.Entry>();
            GpsUserOverrides.Apply(target, temporary, entries, stale);
            Check(stale.Count > 0 && Json(target.records.First(record => record.key == map.key).fields.Find(field => field.name == "layers")) == layersBefore,
                "Ungültige eigene Gesteinsart wird beim Laden verworfen statt angewendet");

            // Defensive apply: invalid layers never reach a MapGenerator (inactive object, no lifecycle in Edit Mode).
            holder = new GameObject("BughuntLayerStoneChecks Map") { hideFlags = HideFlags.HideAndDontSave }; holder.SetActive(false);
            var generator = holder.AddComponent<MapGenerator>();
            var apply = typeof(GpsSettings).GetMethod("ApplyField", BindingFlags.Static | BindingFlags.NonPublic);
            Check(apply != null, "GpsSettings.ApplyField gefunden");
            apply.Invoke(null, new object[] { generator, JsonUtility.FromJson<GpsValue>(layersBefore), false });
            Check(generator.layers != null && generator.layers.Length == layers.children.Count && generator.layers.All(layer => layer.stone), "Gültige Layer werden übernommen");
            var applied = generator.layers.Select(layer => layer.stone).ToArray();
            var invalid = JsonUtility.FromJson<GpsValue>(layersBefore); Stone(invalid, 2).text = oreKey;
            Quiet(() => apply.Invoke(null, new object[] { generator, invalid, false }));
            Check(generator.layers.Select(layer => layer.stone).SequenceEqual(applied), "Ungültige Layer werden nicht auf die Karte angewendet");

            // F1 dropdown: only stone blocks, dirt only for the top layer, no empty entry.
            var choices = typeof(GpsSchema).GetMethod("Choices", new[] { typeof(Type), typeof(GpsValue), typeof(string) });
            var isLayerStone = typeof(GpsSchema).GetMethod("IsLayerStone", BindingFlags.Static | BindingFlags.Public);
            Check(choices != null && isLayerStone != null, "Gefilterte Auswahl für die Gesteinsart vorhanden");
            var lower = ((List<GpsAssetReference>)choices.Invoke(null, new object[] { typeof(Block), layers.children[2], "stone" })).Select(entry => (Block)entry.asset).ToList();
            var top = ((List<GpsAssetReference>)choices.Invoke(null, new object[] { typeof(Block), layers.children[0], "stone" })).Select(entry => (Block)entry.asset).ToList();
            Check(lower.Count >= 4 && lower.All(block => block.IsStone), "Dropdown Layer 3 bietet nur Steine an (" + lower.Count + ")");
            Check(top.Any(block => block.id == BlockType.Dirt) && top.All(block => block.IsStone || block.id == BlockType.Dirt), "Dropdown Layer 1 bietet Steine und Erde an");
            Check((bool)isLayerStone.Invoke(null, new object[] { layers.children[2], "stone" }) &&
                !(bool)isLayerStone.Invoke(null, new object[] { layers.children[2], "backgroundSprite" }), "Nur die Gesteinsart verzichtet auf den leeren Eintrag");
            int all = GpsSchema.Choices(typeof(Block)).Count;
            Check(((List<GpsAssetReference>)choices.Invoke(null, new object[] { typeof(Block), null, "stone" })).Count == all, "Andere Block-Auswahlen bleiben ungefiltert");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntLayerStoneChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntLayerStoneChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            if (holder) Object.DestroyImmediate(holder);
            GpsSettings.AutoCommit = autoCommit; GpsUserOverrides.TestActive = overridesActive;
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Undo.ClearUndo(temporary); Object.DestroyImmediate(temporary);
        }
    }

    // Same input as a save game: the map-generation fields, optionally with replaced layers.
    static bool ValidGeneration(GpsRecord map, GpsValue layers, out string error)
    {
        var fields = GpsSchema.Fields(typeof(MapGenerator)).Where(field => field.applyTime == GpsApplyTime.NewMap)
            .Select(field => field.name == "layers" && layers != null ? layers.Copy() : GpsSettings.GetValue(map.key, field.name)).Where(field => field != null).ToArray();
        int width = (int)GpsSettings.GetValue(map.key, "mapWidth").number, height = (int)GpsSettings.GetValue(map.key, "mapHeight").number;
        return GpsSettings.ValidateGeneration(fields, width, height, out error);
    }
    // The defensive path logs a warning; keep it out of the check's error count.
    static void Quiet(Action action)
    {
        bool previous = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = false;
        try { action(); } finally { Debug.unityLogger.logEnabled = previous; }
    }
}
