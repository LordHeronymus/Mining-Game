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
using Object = UnityEngine.Object;

// Checks for F1 → Artefakte → "Fundchance je Artefakt" (GpsRuntimePanel.Artifacts): every artifact has its own row
// "Chance <Name> (%)" with an input, an entry changes only its own artifact, labels are German, layer/money/points
// stay reachable below. Runs in Edit Mode on a temporary profile; the real profile, overrides and scene stay untouched.
// Report: Temp/ArtifactChanceF1Checks.txt.
public static class ArtifactChanceF1Checks
{
    const string Section = "Fundchance je Artefakt";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        Directory.CreateDirectory("Temp");
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        bool autoCommit = GpsSettings.AutoCommit; bool? overridesActive = GpsUserOverrides.TestActive;
        var scene = EditorSceneManager.NewPreviewScene(); GpsRuntimePanel panel = null;
        try
        {
            GpsSettings.AutoCommit = false; GpsUserOverrides.TestActive = false;
            GpsSettings.UseProfile(temporary, Copy(original));

            var sections = GpsSchema.Sections.Where(entry => entry.tab == "Artefakte").ToArray();
            var chances = sections.FirstOrDefault(entry => entry.title == Section);
            Check(chances != null && chances.source == typeof(MapGenerator), "Abschnitt '" + Section + "' im Reiter Artefakte");
            Check(chances.fields.Select(field => field.name).SequenceEqual(new[] { "artifactDropChanceMultiplier", "artifactSettings" }),
                "Abschnitt enthält Faktor für alle Artefakte und die Artefaktliste");
            Check(sections.Count(entry => entry.fields.Any(field => field.name == "artifactSettings" || field.name == "artifactDropChanceMultiplier")) == 1,
                "Chancen stehen nur in einem Abschnitt");
            var english = sections.SelectMany(entry => entry.fields).Where(field => field.label == field.name && field.name.StartsWith("artifact")).Select(field => field.name).ToArray();
            Check(english.Length == 0, "Alle Artefakt-Felder deutsch beschriftet" + (english.Length > 0 ? " (fehlt: " + string.Join(", ", english) + ")" : ""));

            var map = GpsSettings.Document.records.First(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
            var list = GpsSettings.GetValue(map.key, "artifactSettings");
            Check(list != null && list.children.Count >= 1, "Artefaktliste im Profil (" + list?.children.Count + " Artefakte)");
            var labels = list.children.Select(GpsRuntimePanel.ArtifactChanceLabel).ToArray();
            Check(list.children.All(entry => GpsSettings.Profile.Resolve(entry.children.Find(child => child.name == "tile")?.text) is ArtifactTile tile && !string.IsNullOrEmpty(tile.displayName)),
                "Jedes Artefakt hat einen Anzeigenamen (" + string.Join(", ", labels) + ")");
            Check(labels.Distinct().Count() == labels.Length, "Jede Zeile ist eindeutig beschriftet");

            var content = BuildPanel(scene, ref panel);
            var texts = content.GetComponentsInChildren<TextMeshProUGUI>(true).Select(text => text.text).ToArray();
            Check(FindInput(content, "Faktor für alle Artefakte (×)"), "Feld 'Faktor für alle Artefakte (×)' sichtbar");
            Check(texts.Contains("+ Layer, Geld und Punkte je Artefakt"), "Layer, Geld und Punkte bleiben aufklappbar erreichbar");
            for (int i = 0; i < list.children.Count; i++)
            {
                var input = FindInput(content, labels[i]);
                double value = list.children[i].children.Find(child => child.name == "chancePercent").number;
                Check(input && input.text == value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    "Zeile '" + labels[i] + "' mit Eingabefeld (" + input?.text + ")");
            }

            // An entry changes only its own artifact; values are limited to 0–100 %.
            double[] before = list.children.Select(entry => entry.children.Find(child => child.name == "chancePercent").number).ToArray();
            int target = list.children.Count - 1;
            var field = FindInput(content, labels[target]);
            field.SetTextWithoutNotify("0.5"); field.onEndEdit.Invoke("0.5");
            double[] after = GpsSettings.GetValue(map.key, "artifactSettings").children.Select(entry => entry.children.Find(child => child.name == "chancePercent").number).ToArray();
            Check(Math.Abs(after[target] - .5) < 1e-6, "Eingabe setzt die Chance von " + labels[target] + " auf 0,5 %");
            Check(Enumerable.Range(0, after.Length).Where(i => i != target).All(i => Math.Abs(after[i] - before[i]) < 1e-9), "Andere Artefakte bleiben unverändert");
            var written = (ArtifactDistributionSetting[])GpsCodec.Write(GpsSettings.GetValue(map.key, "artifactSettings"), GpsSettings.Profile.Resolve);
            Check(Mathf.Approximately(written[target].chancePercent, .5f) && written[target].tile, "Neuer Wert kommt in der Spiel-Einstellung an");
            field.SetTextWithoutNotify("250"); field.onEndEdit.Invoke("250");
            Check(Math.Abs(GpsSettings.GetValue(map.key, "artifactSettings").children[target].children.Find(child => child.name == "chancePercent").number - 100) < 1e-6,
                "Werte über 100 % werden auf 100 % begrenzt");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/ArtifactChanceF1Checks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/ArtifactChanceF1Checks.txt", "FAIL " + ex + "\n" + string.Join("\n", checks)); throw; }
        finally
        {
            if (panel) typeof(GpsRuntimePanel).GetMethod("OnDestroy", Hidden)?.Invoke(panel, null);
            EditorSceneManager.ClosePreviewScene(scene);
            GpsSettings.AutoCommit = autoCommit; GpsUserOverrides.TestActive = overridesActive;
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            Undo.ClearUndo(temporary); Object.DestroyImmediate(temporary);
        }
    }

    // Builds the F1 panel once (Open() needs Play Mode) with only the chance section expanded.
    static Transform BuildPanel(Scene scene, ref GpsRuntimePanel panel)
    {
        var root = new GameObject("ArtifactChanceF1Checks Panel", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        SceneManager.MoveGameObjectToScene(root, scene);
        panel = root.AddComponent<GpsRuntimePanel>();
        typeof(GpsRuntimePanel).GetMethod("Build", Hidden).Invoke(panel, null);
        typeof(GpsRuntimePanel).GetField("tab", Hidden).SetValue(panel, "Artefakte");
        ((HashSet<string>)typeof(GpsRuntimePanel).GetField("expanded", Hidden).GetValue(panel)).Add("Artefakte/" + Section);
        typeof(GpsRuntimePanel).GetMethod("BuildPage", Hidden).Invoke(panel, null);
        var content = root.transform.Find("Card/Viewport/Content");
        Check(content, "F1-Panel im Edit Mode aufgebaut (Artefakte)");
        return content;
    }
    // The input is the next element after its label.
    static TMP_InputField FindInput(Transform content, string label)
    {
        for (int i = 0; i + 1 < content.childCount; i++)
            if (content.GetChild(i).TryGetComponent(out TextMeshProUGUI text) && text.text == label &&
                content.GetChild(i + 1).TryGetComponent(out TMP_InputField input)) return input;
        return null;
    }
}
