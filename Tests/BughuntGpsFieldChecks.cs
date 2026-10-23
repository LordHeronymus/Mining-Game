using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Checks for the F1/GPS field fixes: integer overflow and focus loss in a number field
// that must not store the rounded display value.
// Runs in Edit Mode on a temporary profile; the real profile, overrides and scene stay untouched.
public static class BughuntGpsFieldChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static GpsRecord Record(Type type) => GpsSettings.Document.records.First(record => record.type == type.AssemblyQualifiedName);
    static string Json(GpsValue node) => JsonUtility.ToJson(node);
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        bool autoCommit = GpsSettings.AutoCommit; bool? overridesActive = GpsUserOverrides.TestActive;
        var scene = EditorSceneManager.NewPreviewScene(); var panels = new List<GpsRuntimePanel>();
        try
        {
            GpsSettings.AutoCommit = false; GpsUserOverrides.TestActive = false;
            GpsSettings.UseProfile(temporary, Copy(original));

            // A-gps-1: integers beyond the target type are refused instead of overflowing later in GpsCodec.Write.
            var map = Record(typeof(MapGenerator));
            var depth = GpsSettings.GetValue(map.key, "artifactMinimumDepth"); string depthBefore = Json(depth);
            Check(depth.kind == GpsValueKind.Integer, "Mindesttiefe ist ein Ganzzahlfeld");
            depth.number = 9999999999;
            Check(!GpsSettings.SetValue(map.key, depth, out string error) && error != null, "Ganzzahl über int.MaxValue wird abgelehnt: " + error);
            Check(Json(GpsSettings.GetValue(map.key, "artifactMinimumDepth")) == depthBefore, "Abgelehnter Wert bleibt nicht im Dokument");
            depth.number = 2147483648d;
            Check(!GpsSettings.SetValue(map.key, depth, out _), "2^31 (float-Grenze int.MaxValue) wird abgelehnt");
            var broken = Copy(original); broken.records.First(record => record.key == map.key).fields.Find(field => field.name == "artifactMinimumDepth").number = 9999999999;
            Check(!GpsSettings.ValidateDocument(broken, out _), "Dokument mit übergrossem Wert ist ungültig (Laden/Speichern)");
            Check(!GpsSettings.IsValidOverride(map, depth), "Übergrosser eigener Wert wird beim Laden verworfen");
            var shop = GpsSettings.Document.records.First(record => record.type == typeof(ItemSO).AssemblyQualifiedName &&
                record.fields.Any(field => field.name == "worth" && field.kind == GpsValueKind.Integer));
            var worth = GpsSettings.GetValue(shop.key, "worth"); worth.number = 1e12;
            Check(!GpsSettings.SetValue(shop.key, worth, out _), "Übergrosser Verkaufswert eines Items wird abgelehnt");
            depth.number = int.MaxValue;
            Check(GpsSettings.SetValue(map.key, depth, out error), "int.MaxValue selbst bleibt erlaubt: " + error);
            Check(GpsCodec.FitsInteger(int.MaxValue, typeof(int)) && !GpsCodec.FitsInteger(int.MaxValue + 1d, typeof(int)) && GpsCodec.ClampInteger(1e10, typeof(int)) == int.MaxValue,
                "Bereichsprüfung des Zieltyps");
            var restoreDepth = JsonUtility.FromJson<GpsValue>(depthBefore);
            Check(GpsSettings.SetValue(map.key, restoreDepth, out error), "Mindesttiefe zurückgesetzt: " + error);

            // A-gps-6: focus loss without typing keeps the exact value; real input still applies.
            var stone = Record(typeof(UniformStoneAppearance));
            var rubble = GpsSettings.GetValue(stone.key, "rubbleMinSize"); rubble.number = 0.12345678;
            Check(GpsSettings.SetValue(stone.key, rubble, out error), "Testwert mit mehr als 3 Nachkommastellen gesetzt: " + error);
            var input = PanelField(scene, panels, typeof(UniformStoneAppearance), "rubbleMinSize");
            Check(input.text == "0.123", "Feld zeigt den gerundeten Wert (" + input.text + ")");
            string assetJson = temporary.documentJson;
            input.onEndEdit.Invoke(input.text);
            Check(GpsSettings.GetValue(stone.key, "rubbleMinSize").number == 0.12345678 && temporary.documentJson == assetJson,
                "Fokusverlust ohne Eingabe speichert keinen gerundeten Wert (" + GpsSettings.GetValue(stone.key, "rubbleMinSize").number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")");
            input.SetTextWithoutNotify("0.2"); input.onEndEdit.Invoke("0.2");
            Check(Math.Abs(GpsSettings.GetValue(stone.key, "rubbleMinSize").number - .2) < 1e-6, "Echte Eingabe wird weiterhin übernommen");
            input.SetTextWithoutNotify("0,25"); input.onEndEdit.Invoke("0,25");
            Check(Math.Abs(GpsSettings.GetValue(stone.key, "rubbleMinSize").number - .25) < 1e-6, "Komma als Dezimaltrennzeichen bleibt möglich");

            // A-gps-1 in the panel: a ten-digit entry is clamped to the int range instead of throwing.
            var depthInput = PanelField(scene, panels, typeof(MapGenerator), "artifactMinimumDepth");
            depthInput.SetTextWithoutNotify("9999999999"); depthInput.onEndEdit.Invoke("9999999999");
            Check(GpsSettings.GetValue(map.key, "artifactMinimumDepth").number == int.MaxValue, "F1-Eingabe wird auf int.MaxValue begrenzt");
            Check((int)GpsCodec.Write(GpsSettings.GetValue(map.key, "artifactMinimumDepth"), GpsSettings.Profile.Resolve) == int.MaxValue, "Begrenzter Wert lässt sich schreiben");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntGpsFieldChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntGpsFieldChecks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            foreach (var panel in panels) if (panel) typeof(GpsRuntimePanel).GetMethod("OnDestroy", Hidden)?.Invoke(panel, null);
            EditorSceneManager.ClosePreviewScene(scene);
            GpsSettings.AutoCommit = autoCommit; GpsUserOverrides.TestActive = overridesActive;
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Undo.ClearUndo(temporary); Object.DestroyImmediate(temporary);
        }
    }

    // Builds the F1 panel once (Open() needs Play Mode) with only the field's section expanded and returns its input.
    static TMP_InputField PanelField(Scene scene, List<GpsRuntimePanel> panels, Type source, string field)
    {
        var section = GpsSchema.Sections.First(entry => entry.source == source && entry.fields.Any(spec => spec.name == field));
        string label = section.fields.First(spec => spec.name == field).label;
        var root = new GameObject("BughuntGpsFieldChecks Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(root, scene);
        var panel = root.AddComponent<GpsRuntimePanel>(); panels.Add(panel);
        typeof(GpsRuntimePanel).GetMethod("Build", Hidden).Invoke(panel, null);
        typeof(GpsRuntimePanel).GetField("tab", Hidden).SetValue(panel, section.tab);
        ((HashSet<string>)typeof(GpsRuntimePanel).GetField("expanded", Hidden).GetValue(panel)).Add(section.tab + "/" + section.title);
        typeof(GpsRuntimePanel).GetMethod("BuildPage", Hidden).Invoke(panel, null);
        var content = root.transform.Find("Card/Viewport/Content");
        Check(content, "F1-Panel im Edit Mode aufgebaut (" + section.tab + ")");
        var input = FindInput(content, label); if (input) return input;
        throw new Exception("Feld '" + label + "' im F1-Panel nicht gefunden");
    }
    // The input is the next element after its label.
    static TMP_InputField FindInput(Transform content, string label)
    {
        for (int i = 0; i + 1 < content.childCount; i++)
            // TryGetComponent: GetComponent returns a fake-null object in the editor, which `is` would accept.
            if (content.GetChild(i).TryGetComponent(out TextMeshProUGUI text) && text.text == label &&
                content.GetChild(i + 1).TryGetComponent(out TMP_InputField input)) return input;
        return null;
    }
}
