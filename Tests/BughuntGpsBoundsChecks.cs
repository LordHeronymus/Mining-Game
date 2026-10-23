using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Checks for the F1/GPS bound fixes: grass cluster size below 2 or with max < min, element alpha 0,
// camera depth offset in front of the near plane and powerups in the starting resources with an amount above 1.
// Runs in Edit Mode on a temporary profile; the real profile, overrides, scene and player files stay untouched.
public static class BughuntGpsBoundsChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static GpsRecord Record(GpsDocument doc, Type type) => doc.records.First(record => record.type == type.AssemblyQualifiedName);
    static GpsValue Field(GpsDocument doc, Type type, string name) => Record(doc, type).fields.Find(field => field.name == name);
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        bool autoCommit = GpsSettings.AutoCommit; bool? overridesActive = GpsUserOverrides.TestActive;
        string directory = Path.GetFullPath("Temp/BughuntGpsBoundsChecks-" + DateTime.UtcNow.Ticks); Directory.CreateDirectory(directory);
        var scene = EditorSceneManager.NewPreviewScene(); GpsRuntimePanel panel = null;
        var temporaryObjects = new List<Object>();
        try
        {
            GpsSettings.AutoCommit = false; GpsUserOverrides.TestActive = false;
            GpsSettings.UseProfile(temporary, Copy(original));

            // R2-gps-bounds-0: the cluster split terminates for every GPS value and keeps valid sizes seed-stable.
            var split = typeof(SurfaceTallGrass).GetMethod("SplitClusters", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Check(split != null, "SurfaceTallGrass.SplitClusters vorhanden");
            int Split(List<int> sizes, int count, Vector2Int range, int seed) => (int)split.Invoke(null, new object[] { sizes, count, range, seed });
            foreach (var range in new[] { new Vector2Int(0, 7), new Vector2Int(-3, -5), new Vector2Int(1, 0), new Vector2Int(2, 1), new Vector2Int(8, 7), new Vector2Int(0, 0) })
            {
                var sizes = new List<int>();
                int remaining = Split(sizes, 160, range, 12345);
                int minimum = Mathf.Max(2, range.x), maximum = Mathf.Max(minimum, range.y);
                Check(sizes.Count <= 80 && sizes.All(size => size >= minimum && size <= maximum) && sizes.Sum() + remaining == 160 && remaining < minimum,
                    "Clustergrösse " + range + " endet ohne Ausnahme (" + sizes.Count + " Gruppen, Rest " + remaining + ")");
            }
            foreach (int seed in new[] { 0, 1, 777, -42, int.MaxValue })
                foreach (var range in new[] { new Vector2Int(2, 7), new Vector2Int(3, 7), new Vector2Int(2, 2), new Vector2Int(4, 12) })
                    foreach (int count in new[] { 0, 1, 2, 9, 160 })
                    {
                        var expected = new List<int>(); int expectedRemaining = ReferenceSplit(expected, count, range, seed);
                        var actual = new List<int>(); int actualRemaining = Split(actual, count, range, seed);
                        if (!expected.SequenceEqual(actual) || expectedRemaining != actualRemaining)
                            Check(false, "Gültige Clustergrösse " + range + " verteilt wie bisher (Seed " + seed + ", Anzahl " + count + ")");
                    }
            Check(true, "Gültige Clustergrössen verteilen seedstabil wie bisher");
            var clusterSpec = GpsSchema.Fields(typeof(SurfaceTallGrass)).First(spec => spec.name == "clusterSize");
            Check(clusterSpec.min == 2, "GPS-Untergrenze der Clustergrösse wie OnValidate (" + clusterSpec.min + ")");

            // R2-gps-bounds-2: element alpha has a lower bound and a stored 0 does not hide the F1 card.
            var alphaSpec = GpsSchema.Fields(typeof(GpsPreferences)).First(spec => spec.name == "panelElementAlpha");
            Check(alphaSpec.min > 0f && alphaSpec.min == GpsPreferences.MinPanelElementAlpha, "Elemente-Alpha hat eine Untergrenze > 0 (" + alphaSpec.min + ")");
            var zeroAlpha = GpsSettings.GetValue("preferences", "panelElementAlpha"); zeroAlpha.number = 0;
            Check(!GpsSettings.SetValue("preferences", zeroAlpha, out _), "Elemente-Alpha 0 wird nicht mehr übernommen");
            GpsSettings.Preferences.panelElementAlpha = 0f;
            var root = new GameObject("BughuntGpsBoundsChecks Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(root, scene);
            panel = root.AddComponent<GpsRuntimePanel>();
            typeof(GpsRuntimePanel).GetMethod("Build", Hidden).Invoke(panel, null);
            typeof(GpsRuntimePanel).GetMethod("Refresh", Hidden).Invoke(panel, null);
            var card = root.transform.Find("Card");
            Check(card && card.TryGetComponent(out CanvasGroup group) && group.alpha >= GpsPreferences.MinPanelElementAlpha,
                "Gespeichertes Elemente-Alpha 0 lässt das F1-Fenster sichtbar");
            GpsSettings.Preferences.panelElementAlpha = original.preferences.panelElementAlpha;

            // Stored own values below the new bounds still load, clamped instead of dropped.
            var grassRecord = Record(original, typeof(SurfaceTallGrass));
            var tinyCluster = Field(original, typeof(SurfaceTallGrass), "clusterSize").Copy(); tinyCluster.vector = new Vector4(0, 9, 0, 0);
            var tinyAlpha = GpsCodec.Read("panelElementAlpha", typeof(float), 0f, temporary.Key);
            var entries = new List<GpsUserOverrides.Entry>
            {
                new() { record = grassRecord.key, path = "clusterSize", value = tinyCluster },
                new() { record = "preferences", path = "panelElementAlpha", value = tinyAlpha },
            };
            var withOverrides = Copy(original); var stale = new List<GpsUserOverrides.Entry>();
            int applied = GpsUserOverrides.Apply(withOverrides, temporary, entries, stale);
            var clamped = Field(withOverrides, typeof(SurfaceTallGrass), "clusterSize").vector;
            Check(applied == 2 && stale.Count == 0, "Eigene Werte unter der Grenze bleiben erhalten (angewendet " + applied + ", verworfen " + stale.Count + ")");
            Check(clamped.x == 2 && clamped.y == 9, "Eigene Clustergrösse wird auf die Grenze geklemmt (" + clamped + ")");
            Check(withOverrides.preferences.panelElementAlpha == GpsPreferences.MinPanelElementAlpha, "Eigenes Elemente-Alpha wird geklemmt (" + withOverrides.preferences.panelElementAlpha + ")");

            // The player file (build) with such values still loads instead of falling back to the shipped profile.
            var legacy = Copy(original);
            Field(legacy, typeof(SurfaceTallGrass), "clusterSize").vector = new Vector4(1, 6, 0, 0);
            legacy.preferences.panelElementAlpha = 0f; legacy.preferences.panelBackdropAlpha = .37f;
            string path = Path.Combine(directory, "gps-settings.json");
            File.WriteAllText(path, JsonUtility.ToJson(legacy));
            var loaded = GpsFileStore.Load(path, Copy(original), out string warning);
            Check(warning == null, "Spielerdatei mit Werten unter der Grenze lädt ohne Warnung (" + warning + ")");
            Check(Math.Abs(loaded.preferences.panelBackdropAlpha - .37f) < 1e-6f, "Übrige Werte der Spielerdatei bleiben erhalten");
            Check(loaded.preferences.panelElementAlpha == GpsPreferences.MinPanelElementAlpha && Field(loaded, typeof(SurfaceTallGrass), "clusterSize").vector.x == 2,
                "Spielerdatei: Werte unter der Grenze werden geklemmt");
            Check(GpsSettings.ValidateDocument(loaded, out string error), "Geladenes Dokument ist gültig: " + error);

            // R2-gps-bounds-3: the camera depth stays between near and far plane; X/Y and the default -10 stay untouched.
            var cameraObject = new GameObject("BughuntGpsBoundsChecks Camera") { hideFlags = HideFlags.HideAndDontSave }; temporaryObjects.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.nearClipPlane = .3f; camera.farClipPlane = 1000f;
            var follow = cameraObject.AddComponent<CameraFollow>();
            follow.offset = new Vector3(0, 0, -10);
            Check(follow.EffectiveOffset == new Vector3(0, 0, -10), "Standardversatz (0, 0, -10) bleibt unverändert");
            follow.offset = new Vector3(1.5f, -2f, 0f);
            var effective = follow.EffectiveOffset;
            Check(effective.x == 1.5f && effective.y == -2f && effective.z < -camera.nearClipPlane, "Z = 0 liegt hinter der Near Plane, X/Y bleiben (" + effective + ")");
            follow.offset = new Vector3(0, 0, 5f);
            Check(follow.EffectiveOffset.z < -camera.nearClipPlane, "Positives Z wird vor die Kamera geklemmt");
            follow.offset = new Vector3(0, 0, -5000f);
            Check(follow.EffectiveOffset.z > -camera.farClipPlane, "Z hinter der Far Plane wird geklemmt");
            var target = new GameObject("BughuntGpsBoundsChecks Target") { hideFlags = HideFlags.HideAndDontSave }; temporaryObjects.Add(target);
            follow.target = target.transform; follow.offset = Vector3.zero;
            if (GameplayTestSettings.CameraFollowEnabled)
            {
                typeof(CameraFollow).GetMethod("LateUpdate", Hidden).Invoke(follow, null);
                Check(cameraObject.transform.position.z < -camera.nearClipPlane, "Nachführung setzt die geklemmte Kameratiefe");
            }
            else checks.Add("PASS Nachführung übersprungen (Kamera folgen im Testprofil aus)");

            // R2-gps-bounds-4: a powerup in the starting resources is unlocked whatever amount the entry carries.
            var inventoryObject = new GameObject("BughuntGpsBoundsChecks Inventory") { hideFlags = HideFlags.HideAndDontSave }; temporaryObjects.Add(inventoryObject);
            var inventory = inventoryObject.AddComponent<InventoryManager>();
            ItemSO Powerup(Item id)
            {
                var item = ScriptableObject.CreateInstance<ItemSO>(); item.hideFlags = HideFlags.HideAndDontSave; temporaryObjects.Add(item);
                item.item = id; item.category = ItemCategory.Powerup; return item;
            }
            var axe = Powerup(Item.Axe); var scythe = Powerup(Item.Scythe); var backpack = Powerup(Item.Backpack);
            Check(inventory.AddStartingItem(axe, 10) && inventory.IsPowerupUnlocked(axe), "Startressource Axt mit Anzahl 10 wird freigeschaltet");
            Check(inventory.AddStartingItem(scythe, 1) && inventory.IsPowerupUnlocked(scythe), "Startressource Sense mit Anzahl 1 wird freigeschaltet");
            Check(!inventory.AddStartingItem(backpack, 0) && !inventory.IsPowerupUnlocked(backpack), "Anzahl 0 schaltet kein Powerup frei");
            Check(!inventory.Add(backpack, 10) && !inventory.IsPowerupUnlocked(backpack), "Andere Aufrufer verlangen weiterhin Anzahl 1");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntGpsBoundsChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntGpsBoundsChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            if (panel) typeof(GpsRuntimePanel).GetMethod("OnDestroy", Hidden)?.Invoke(panel, null);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in temporaryObjects) if (item) Object.DestroyImmediate(item);
            GpsSettings.AutoCommit = autoCommit; GpsUserOverrides.TestActive = overridesActive;
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Undo.ClearUndo(temporary); Object.DestroyImmediate(temporary);
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    // The split as it was before the fix; valid cluster sizes must give the same groups.
    static int ReferenceSplit(List<int> groupSizes, int clusteredCount, Vector2Int clusterSize, int seed)
    {
        int remainingClustered = clusteredCount, groupIndex = 0;
        while (remainingClustered >= clusterSize.x)
        {
            int maximum = Mathf.Min(clusterSize.y, remainingClustered);
            int size = clusterSize.x + (int)(OreVeins.Hash(seed, groupIndex++, 0, 0xC1A57u) % (uint)(maximum - clusterSize.x + 1));
            if (remainingClustered - size > 0 && remainingClustered - size < clusterSize.x)
            {
                if (remainingClustered <= clusterSize.y) size = remainingClustered;
                else if (remainingClustered - clusterSize.x >= clusterSize.x) size = remainingClustered - clusterSize.x;
            }
            groupSizes.Add(size);
            remainingClustered -= size;
        }
        return remainingClustered;
    }
}
