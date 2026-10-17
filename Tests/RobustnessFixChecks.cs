using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks for the fixes from 17.10.2026: blueprint saves, sell-all filter, GPS override files,
// save compatibility after schema changes, meta-profile bounds and personal volume storage.
public static class RobustnessFixChecks
{
    [Serializable] sealed class OverrideFile { public string format; public List<GpsRecord> records = new(); }
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static GpsValue Field(GpsDocument doc, Type type, string name) =>
        doc.records.First(record => record.type == type.AssemblyQualifiedName).fields.Find(field => field.name == name);

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        string directory = Path.GetFullPath("Temp/RobustnessFixChecks-" + DateTime.UtcNow.Ticks); Directory.CreateDirectory(directory);
        int[] recipeState = RecipeUnlocks.CaptureRunState();
        try
        {
            // Blueprints bought in the shop survive save/load, including pickaxes outside Resources.
            var pickaxe = ExoticCatalog.AllRecipes.First(recipe => recipe && recipe.output && recipe.output.item == Item.IronPickaxe);
            Check(!Resources.LoadAll<CraftingRecipe>("WorkbenchRecipes").Contains(pickaxe), "Spitzhacken-Rezept liegt ausserhalb von Resources/WorkbenchRecipes");
            RecipeUnlocks.ResetRun();
            Check(RecipeUnlocks.Unlock(pickaxe), "Spitzhacken-Bauplan freischaltbar");
            var saved = RecipeUnlocks.CaptureRunState();
            Check(saved.Contains((int)Item.IronPickaxe), "Spitzhacken-Bauplan wird gespeichert");
            RecipeUnlocks.ResetRun();
            Check(!RecipeUnlocks.IsUnlocked(pickaxe), "Neuer Run setzt Bauplan zurück");
            RecipeUnlocks.RestoreRunState(saved);
            Check(RecipeUnlocks.IsUnlocked(pickaxe), "Spitzhacken-Bauplan nach dem Laden freigeschaltet");
            var medkit = ExoticCatalog.AllRecipes.First(recipe => recipe && recipe.output && recipe.output.item == Item.Medkit);
            Check(!RecipeUnlocks.IsUnlocked(medkit), "Nicht gekaufte Baupläne bleiben gesperrt");

            // Sell-all only sells what the sell page lists.
            var dynamite = StartingResourcesSettings.Resolve((int)Item.Dynamite);
            var copper = StartingResourcesSettings.Resolve((int)Item.Copper);
            Check(dynamite && dynamite.worth > 0 && dynamite.category != ItemCategory.Ore, "Dynamit hat einen Wert, ist aber kein Erz");
            Check(!ShopManager.IsSellable(dynamite) && ShopManager.IsSellable(copper), "Nur Erze sind verkaufbar");

            // Player GPS files hold overrides only, so new shipped values reach existing players.
            GpsSettings.UseProfile(temporary, Copy(original));
            var speedRecord = GpsSettings.Document.records.First(record => record.type == typeof(PlayerBaseStats).AssemblyQualifiedName);
            var speed = GpsSettings.GetValue(speedRecord.key, "miningSpeed"); double before = speed.number; speed.number = before + .25;
            Check(GpsSettings.SetValue(speedRecord.key, speed, out string error), "Eigener GPS-Wert setzbar: " + error);
            GpsSettings.Preferences.musicVolume = .4f;
            string path = Path.Combine(directory, "gps-settings.json");
            GpsFileStore.Save(path, GpsSettings.Document);
            string json = File.ReadAllText(path);
            Check(json.Contains("gps-overrides") && json.Contains("miningSpeed") && json.Contains("musicVolume"), "Spielerdatei enthält die Abweichungen");
            var overrides = JsonUtility.FromJson<OverrideFile>(json);
            int fieldCount = overrides.records.Sum(record => record.fields.Count), profileFields = original.records.Sum(record => record.fields.Count);
            Check(fieldCount < 10 && fieldCount * 20 < profileFields, "Spielerdatei enthält nur einzelne Felder (" + fieldCount + " von " + profileFields + ")");
            var rebalanced = Copy(original);
            var ambient = Field(rebalanced, typeof(MapLighting), "ambientBrightness");
            double shippedAmbient = Math.Abs(ambient.number - .12) < .001 ? .13 : .12;
            ambient.number = shippedAmbient;
            Check(GpsSettings.ValidateDocument(rebalanced, out error), "Simuliertes neues Profil gültig: " + error);
            var loaded = GpsFileStore.Load(path, rebalanced, out string warning, out bool legacy);
            Check(warning == null && !legacy, "Abweichungsdatei geladen: " + warning);
            Check(Math.Abs(Field(loaded, typeof(PlayerBaseStats), "miningSpeed").number - (before + .25)) < 1e-9, "Eigene Änderung bleibt erhalten");
            Check(Math.Abs(Field(loaded, typeof(MapLighting), "ambientBrightness").number - shippedAmbient) < 1e-9, "Neuer ausgelieferter Wert kommt beim Spieler an");
            Check(Mathf.Approximately(loaded.preferences.musicVolume, .4f), "Persönliche Lautstärke bleibt erhalten");
            string legacyPath = Path.Combine(directory, "legacy-gps-settings.json");
            File.WriteAllText(legacyPath, JsonUtility.ToJson(GpsSettings.Document));
            var old = GpsFileStore.Load(legacyPath, Copy(original), out warning, out legacy);
            Check(legacy && warning == null && Math.Abs(Field(old, typeof(PlayerBaseStats), "miningSpeed").number - (before + .25)) < 1e-9, "Altes Vollformat wird weiterhin gelesen");

            // A save from before a schema change stays loadable.
            var map = Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
            Check(map, "MapGenerator in der Szene gefunden");
            var generation = GpsSettings.CaptureGeneration(map);
            int width = (int)generation.First(field => field.name == "mapWidth").number, height = (int)generation.First(field => field.name == "mapHeight").number;
            Check(GpsSettings.ValidateGeneration(generation, width, height, out error), "Aktueller Kartenschnappschuss gültig: " + error);
            var oldSave = generation.Select(field => field.Copy()).ToArray();
            var layers = oldSave.First(field => field.name == "layers");
            foreach (var layer in layers.children) layer.children.RemoveAt(layer.children.Count - 1);
            layers.children[0].children.Add(new GpsValue { name = "removedSetting", type = typeof(float).AssemblyQualifiedName, kind = GpsValueKind.Number, number = 3 });
            var leaf = layers.children[0].children.First(child => child.kind == GpsValueKind.Integer || child.kind == GpsValueKind.Number);
            leaf.type = leaf.type.Replace("Version=4.0.0.0", "Version=2.0.0.0");
            Check(!oldSave.All(field => GpsCodec.ValidateShape(field, GpsCodec.MemberType(typeof(MapGenerator), field.name), out _)), "Testdaten entsprechen einem älteren Layer-Format");
            Check(GpsSettings.ValidateGeneration(oldSave, width, height, out error), "Spielstand mit älterem Layer-Format bleibt ladbar: " + error);
            var malformed = generation.Select(field => field.Copy()).ToArray();
            malformed.First(field => field.name == "layers").children[0].children.Find(child => child.name == "startDepth").kind = GpsValueKind.Text;
            Check(!GpsSettings.ValidateGeneration(malformed, width, height, out _), "Beschädigter Wert (falscher Typ) wird weiterhin abgelehnt");
            var upgraded = GpsCodec.Upgrade(layers, GpsCodec.MemberType(typeof(MapGenerator), "layers"), GpsSettings.Profile.Key);
            Check(upgraded.children.Count == layers.children.Count && upgraded.children.All(layer => layer.children.All(child => child.name != "removedSetting")), "Fehlende Felder ergänzt, entfernte verworfen");

            // Meta-profile limits come from the catalog and match the previous constants.
            var meta = typeof(MetaProgression);
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
            var stored = meta.GetMethod("IsStoredPeriodLength", hidden);
            int pool = ((string[])meta.GetField("PeriodMetrics", hidden).GetValue(null)).Length;
            bool Stored(int length, int first) => (bool)stored.Invoke(null, new object[] { length, first });
            Check(Stored(6, 6) && Stored(22, 6) && Stored(pool, 6) && !Stored(pool + 1, 6) && !Stored(5, 6) && !Stored(19, 20), "Ältere Periodenlängen werden ohne Liste erkannt");
            var max = meta.GetMethod("MaxUpgradeEffect", hidden); var effect = meta.GetMethod("UpgradeEffect", hidden);
            float Max(string id) => (float)max.Invoke(null, new object[] { id });
            Check(Mathf.Approximately(Max("health"), .5f) && Mathf.Approximately(Max("energy"), .5f) && Mathf.Approximately(Max("movement"), .1f) &&
                Mathf.Approximately(Max("jump"), .08f) && Mathf.Approximately(Max("mining"), .2f) && Mathf.Approximately(Max("reach"), .3f) &&
                Mathf.Approximately(Max("capacity"), 10f) && Mathf.Approximately(Max("money"), 200f) && Mathf.Approximately(Max("torches"), 10f), "Grenzen entsprechen den bisherigen Konstanten");
            Check((float)effect.Invoke(null, new object[] { "jump", 7 }) == 7 * .008f && (float)effect.Invoke(null, new object[] { "health", 13 }) == 13 * .025f, "Upgrade-Effekte rechnen identisch");

            // Personal volume stays out of the shipped profile.
            Check(EditorPrefs.HasKey("MiningGame.GPS.masterVolume") && EditorPrefs.HasKey("MiningGame.GPS.musicVolume"), "Persönliche Lautstärke liegt in den EditorPrefs");
            var assetDocument = JsonUtility.FromJson<GpsDocument>(GpsSettings.AssetJson(GpsSettings.Document));
            Check(assetDocument.preferences.masterVolume == 1f && assetDocument.preferences.musicVolume == 1f, "Profil-Asset erhält Standardlautstärke");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/RobustnessFixChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/RobustnessFixChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            RecipeUnlocks.RestoreRunState(recipeState);
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Object.DestroyImmediate(temporary);
        }
    }
}
