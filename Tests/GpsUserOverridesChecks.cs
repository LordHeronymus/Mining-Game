using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Eigene GPS-Werte als Überschreibungs-Schicht (GpsUserOverrides, Docs/GPS-Eigene-Werte.md).
// Edit Mode: Profiltext lesen, Unterschiede/Anwenden/Vereinigen, Speichern über GpsSettings ohne Profiländerung,
//            Zurücksetzen, Übernahme aus einem geänderten Profiltext, Vorschlags-Profil (gleicher Text wie Unity).
// Play Mode (frischer Start ab MainMenu): F1-Panel zeigt eigene Werte, "×" setzt zurück, Profil bleibt unverändert.
// Die Schicht wird nur für den Check eingeschaltet (TestActive) und schreibt in Temp/; danach ist alles wie vorher.
public static class GpsUserOverridesChecks
{
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks.Add(label); }
    const string ProfilePath = "Assets/Resources/Gameplay/GpsProfile.asset";

    public static async Task<object> Main()
    {
        checks.Clear();
        string temp = Path.GetFullPath("Temp/GpsUserOverridesChecks-" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(temp);
        string committed = SessionState.GetString("GPS.Committed", "");
        string profileJson = GpsSettings.Profile.documentJson;
        var fileTime = File.GetLastWriteTimeUtc(ProfilePath);
        GpsSettings.CommitPendingNow();
        try
        {
            ParseChecks();
            DiffChecks();
            GpsUserOverrides.TestPath = Path.Combine(temp, "GpsOverrides.json");
            GpsUserOverrides.TestActive = true;
            GpsUserOverrides.Reload();
            GpsSettings.ReloadFromProfile();
            if (Application.isPlaying) await PanelChecks();
            else { LayerChecks(); TakeOverChecks(temp); ProposalChecks(); }
            Check(GpsSettings.Profile.documentJson == profileJson, "Profil im Speicher unverändert");
            Check(File.GetLastWriteTimeUtc(ProfilePath) == fileTime, "GpsProfile.asset auf der Platte unverändert");
        }
        finally
        {
            GpsUserOverrides.TestActive = null; GpsUserOverrides.TestPath = null; GpsUserOverrides.Hold = false;
            GpsUserOverrides.Reload();
            if (string.IsNullOrEmpty(committed)) SessionState.EraseString("GPS.Committed"); else SessionState.SetString("GPS.Committed", committed);
            GpsSettings.ReloadFromProfile();
            if (string.IsNullOrEmpty(committed)) SessionState.EraseString("GPS.Committed"); else SessionState.SetString("GPS.Committed", committed);
            try { Directory.Delete(temp, true); } catch { }
        }
        return new { mode = Application.isPlaying ? "play" : "edit", count = checks.Count, checks, proposalNoiseLines = noiseLines };
    }

    static GpsDocument Shipped() => GpsSettings.ShippedDocument();
    static GpsRecord MapRecord(GpsDocument doc) => doc.records.Find(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);

    static void ParseChecks()
    {
        string yaml = File.ReadAllText(ProfilePath);
        var parsed = GpsUserOverrides.ParseProfileYaml(yaml);
        // Vergleich mit Unitys eigenem Lesen derselben Datei (frische Kopie; das geladene Profil kann im Speicher abweichen).
        var fresh = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(ProfilePath).OfType<GpsProfile>().First();
        var loaded = JsonUtility.FromJson<GpsDocument>(fresh.documentJson);
        UnityEngine.Object.DestroyImmediate(fresh);
        Check(parsed != null && parsed.records.Count == loaded.records.Count && GpsUserOverrides.Diff(parsed, loaded, GpsSettings.Profile).Count == 0 &&
            GpsUserOverrides.Diff(loaded, parsed, GpsSettings.Profile).Count == 0, "Profiltext aus der Datei ergibt dasselbe Dokument wie Unity");
        var noiseA = new GpsValue { name = "n", kind = GpsValueKind.Number, number = JsonUtility.FromJson<GpsValue>("{\"number\":0.45176199078559878}").number };
        var noiseB = new GpsValue { name = "n", kind = GpsValueKind.Number, number = JsonUtility.FromJson<GpsValue>("{\"number\":0.4517619907855988}").number };
        var real = new GpsValue { name = "n", kind = GpsValueKind.Number, number = .4518 };
        Check(GpsUserOverrides.Same(noiseA, noiseB) && !GpsUserOverrides.Same(noiseA, real), "Rundung in der letzten Stelle ist kein eigener Wert, echte Änderung schon");
        Check(GpsUserOverrides.ExtractScalar("x\n  key: \"a b\n    c\\\n    d\n\n    e\\ \n    f\"\n", "key") == "a b cd\ne  f", "YAML-Faltung, maskierter Umbruch, Leerzeile");
        Check(GpsUserOverrides.ExtractScalar("x\n  key: \"\\u00e4\\\"\\\\\"\n", "key") == "ä\"\\", "YAML-Escapes");
        Check(GpsUserOverrides.ParseProfileYaml("kein Profil") == null, "Fremder Text ergibt kein Dokument");
    }

    static void DiffChecks()
    {
        var profile = GpsSettings.Profile;
        var baseDoc = Shipped(); var changed = Shipped();
        Check(GpsUserOverrides.Diff(changed, baseDoc, profile).Count == 0, "Gleiche Dokumente: keine Unterschiede");
        changed.preferences.hotbarHoldDuration = baseDoc.preferences.hotbarHoldDuration + .25f;
        changed.preferences.masterVolume = .1f;   // persönlich, gehört nie in die Überschreibungen
        changed.tests.godMode = !baseDoc.tests.godMode;
        var ores = MapRecord(changed).fields.Find(field => field.name == "oreSettings");
        var leaf = ores.children[0].children.First(child => child.kind == GpsValueKind.Number || child.kind == GpsValueKind.Integer);
        leaf.number += 1;
        var diff = GpsUserOverrides.Diff(changed, baseDoc, profile);
        Check(diff.Count == 3, "Drei Unterschiede (Haltezeit, God Mode, ein Erzfeld), Lautstärke ausgenommen: " + diff.Count);
        var oreEntry = diff.Find(entry => entry.path.StartsWith("oreSettings/", StringComparison.Ordinal));
        Check(oreEntry != null && oreEntry.path == "oreSettings/" + ores.children[0].name + "/" + leaf.name && oreEntry.arrays.SequenceEqual(new[] { ores.children.Count }),
            "Listenfeld wird einzeln gespeichert, nicht die ganze Liste");
        var applied = Shipped();
        Check(GpsUserOverrides.Apply(applied, profile, diff) == 3, "Alle Einträge angewendet");
        Check(GpsUserOverrides.Diff(applied, changed, profile).Count == 0 && applied.preferences.masterVolume == baseDoc.preferences.masterVolume,
            "Angewendet = geändert (ohne Lautstärke)");
        // Neuer Standardwert an anderer Stelle derselben Liste kommt trotz eigener Überschreibung an.
        var upstream = Shipped();
        var other = MapRecord(upstream).fields.Find(field => field.name == "oreSettings").children.Last().children.First(child => child.kind == GpsValueKind.Number || child.kind == GpsValueKind.Integer);
        other.number += 2;
        GpsUserOverrides.Apply(upstream, profile, diff);
        var otherAfter = MapRecord(upstream).fields.Find(field => field.name == "oreSettings").children.Last().children.Find(child => child.name == other.name);
        Check(ores.children.Count == 1 || otherAfter.number == other.number, "Neuer Standard im selben Erz-Array bleibt erhalten");
        // Umgebaute Liste: Eintrag passt nicht mehr und wird gemeldet statt falsch angewendet.
        var rebuilt = Shipped();
        var rebuiltOres = MapRecord(rebuilt).fields.Find(field => field.name == "oreSettings");
        rebuiltOres.children.Add(rebuiltOres.children[0].Copy()); rebuiltOres.children.Last().name = (rebuiltOres.children.Count - 1).ToString();
        var stale = new List<GpsUserOverrides.Entry>();
        GpsUserOverrides.Apply(rebuilt, profile, diff, stale);
        Check(stale.Count == 1 && stale[0] == oreEntry, "Eintrag in umgebauter Liste gilt als veraltet");
        var merged = GpsUserOverrides.Merge(new[] { diff[0] }, diff.Select(entry => new GpsUserOverrides.Entry { record = entry.record, path = entry.path, arrays = entry.arrays, value = entry.value.Copy() }));
        Check(merged.Count == 3 && merged[0] == diff[0], "Vereinigen: gleiche Stelle nur einmal, bevorzugter Eintrag gewinnt");
    }

    static void LayerChecks()
    {
        var shipped = Shipped();
        Check(GpsUserOverrides.Count == 0 && !GpsSettings.HasUnsavedChanges, "Start: keine eigenen Werte, nichts ungespeichert");
        float target = Mathf.Approximately(shipped.preferences.hotbarHoldDuration, .3f) ? .4f : .3f;
        var node = GpsSettings.GetValue("preferences", "hotbarHoldDuration"); node.number = target;
        Check(GpsSettings.SetValueAndCommit("preferences", node, out string error), "Wert setzen: " + error);
        var map = MapRecord(GpsSettings.Document);
        var mapField = map.fields.First(field => field.kind == GpsValueKind.Boolean);
        var mapNode = mapField.Copy(); mapNode.flag = !mapNode.flag;
        Check(GpsSettings.SetValueAndCommit(map.key, mapNode, out error), "Komponentenwert setzen: " + error);
        GpsSettings.CommitPendingNow();
        Check(File.Exists(GpsUserOverrides.FilePath) && GpsUserOverrides.Count == 2, "Zwei eigene Werte in der Datei: " + GpsUserOverrides.Count);
        Check(GpsUserOverrides.IsOverridden("preferences", "hotbarHoldDuration") && GpsUserOverrides.IsOverridden(map.key, mapField.name), "Felder als eigen markiert");
        Check(!GpsSettings.HasUnsavedChanges, "Gespeichert, nichts offen");
        GpsSettings.ReloadFromProfile();
        Check(Mathf.Approximately(GpsSettings.Preferences.hotbarHoldDuration, target) && GpsSettings.GetValue(map.key, mapField.name).flag == mapNode.flag,
            "Nach Neuladen gelten die eigenen Werte");
        // Während einer Aktualisierung (Hold) darf Speichern bestehende Einträge nicht verlieren.
        var held = new GpsUserOverrides.Entry { record = "preferences", path = "dragPopDuration", value = GpsSettings.GetValue("preferences", "dragPopDuration") };
        held.value.number = .5;
        GpsUserOverrides.Write(GpsUserOverrides.Entries.Append(held).ToList());
        GpsUserOverrides.Hold = true;
        Check(GpsSettings.Save(out error) && GpsUserOverrides.Count == 3, "Hold: Speichern behält übernommene Einträge");
        GpsUserOverrides.Hold = false;
        Check(GpsSettings.Save(out error) && GpsUserOverrides.Count == 2, "Ohne Hold: Speichern schreibt genau die Abweichungen");
        Check(GpsSettings.ResetToDefaults(new[] { ("preferences", "hotbarHoldDuration") }, out error) && GpsUserOverrides.Count == 1 &&
            Mathf.Approximately(GpsSettings.Preferences.hotbarHoldDuration, shipped.preferences.hotbarHoldDuration), "Einzelnes Feld zurückgesetzt");
        Check(GpsSettings.ResetToDefaults(GpsUserOverrides.OverriddenRoots().ToList(), out error) && GpsUserOverrides.Count == 0, "Alle zurückgesetzt");
        Check(File.Exists(GpsUserOverrides.FilePath + ".bak"), "Vorherige Datei als .bak erhalten");
        // Einträge, die dem Standard entsprechen (z. B. nach gemergtem Vorschlag), fallen beim Laden weg.
        var same = new GpsUserOverrides.Entry { record = "preferences", path = "hotbarHoldDuration", value = GpsSettings.GetValue("preferences", "hotbarHoldDuration") };
        GpsUserOverrides.Write(new List<GpsUserOverrides.Entry> { same });
        GpsSettings.ReloadFromProfile();
        Check(GpsUserOverrides.Count == 0, "Eintrag gleich Standard wird beim Laden entfernt");
    }

    // Wie MainAutoUpdate: lokal geändertes Profil → Unterschiede gegenüber HEAD in die Schicht übernehmen.
    static void TakeOverChecks(string temp)
    {
        var profile = GpsSettings.Profile;
        var changed = Shipped();
        float target = changed.preferences.hotbarHoldDuration + .05f;
        changed.preferences.hotbarHoldDuration = target;
        string file = GpsDefaultsProposal.WriteProfile(Path.GetFullPath("."), profile, Shipped());
        var baseline = GpsUserOverrides.ParseProfileYaml(File.ReadAllText(file));
        file = GpsDefaultsProposal.WriteProfile(Path.GetFullPath("."), profile, changed);
        var local = GpsUserOverrides.ParseProfileYaml(File.ReadAllText(file));
        var found = GpsUserOverrides.Diff(local, baseline, profile);
        Check(found.Count == 1 && found[0].record == "preferences" && found[0].path == "hotbarHoldDuration", "Übernahme findet genau den geänderten Wert");
        GpsUserOverrides.Write(GpsUserOverrides.Merge(GpsUserOverrides.Entries, found));
        GpsSettings.ReloadFromProfile();
        Check(Mathf.Approximately(GpsSettings.Preferences.hotbarHoldDuration, target), "Übernommener Wert gilt");
        GpsSettings.ResetToDefaults(GpsUserOverrides.OverriddenRoots().ToList(), out _);
        File.Delete(file);
    }

    static void ProposalChecks()
    {
        string file = GpsDefaultsProposal.WriteProfile(Path.GetFullPath("."), GpsSettings.Profile, Shipped());
        string written = File.ReadAllText(file).Replace("\r\n", "\n"), original = File.ReadAllText(ProfilePath).Replace("\r\n", "\n");
        const string Marker = "\n  documentJson: ";
        Check(written.Substring(0, written.IndexOf(Marker)) == original.Substring(0, original.IndexOf(Marker)), "Vorschlag: Kopf (fileID, Skript) unverändert");
        Check(written.Substring(written.IndexOf("\n  assets:")) == original.Substring(original.IndexOf("\n  assets:")), "Vorschlag: Asset-Liste unverändert");
        var a = GpsUserOverrides.ParseProfileYaml(written); var b = Shipped();
        Check(GpsUserOverrides.Diff(a, b, GpsSettings.Profile).Count == 0 && GpsUserOverrides.Diff(b, a, GpsSettings.Profile).Count == 0,
            "Vorschlag schreibt genau das übergebene Profil");
        var lines = written.Split('\n'); var old = original.Split('\n');
        noiseLines = lines.Length == old.Length ? lines.Where((line, i) => line != old[i]).Count() : -1;
        File.Delete(file);
    }
    static int noiseLines;
    static string Snip(string text, int at) => at >= text.Length ? "<Ende>" : text.Substring(at, Math.Min(60, text.Length - at)).Replace("\n", "⏎");

    static async Task PanelChecks()
    {
        if (!MainMenuController.IsVisible) throw new Exception("Requires fresh Play from MainMenu.");
        string before = File.ReadAllText(ProfilePath);
        GpsRuntimePanel.Open();
        try
        {
            var panel = UnityEngine.Object.FindFirstObjectByType<GpsRuntimePanel>();
            var card = panel.transform.Find("Card");
            Check(card.Find("Zurücksetzen") && card.Find("Als Standard vorschlagen"), "Knöpfe für eigene Werte im F1-Panel");
            panel.SelectTab("UI"); await Frames(2);
            var content = card.Find("Viewport/Content");
            Click(content, "+ GPS-Panel"); await Frames(2);
            var input = Next(content, "Hintergrund-Alpha").GetComponent<TMP_InputField>();
            string value = GpsSettings.Preferences.panelBackdropAlpha > .5f ? "0.25" : "0.75";
            input.SetTextWithoutNotify(value); input.onEndEdit.Invoke(value); await Frames(2);
            GpsSettings.CommitPendingNow(); await Frames(2);
            Check(GpsUserOverrides.IsOverridden("preferences", "panelBackdropAlpha"), "Panel-Änderung als eigener Wert gespeichert");
            Check(Labels(content).Contains("Hintergrund-Alpha (eigen)"), "Feld im Panel als eigen markiert");
            Check(File.ReadAllText(ProfilePath) == before, "Profil-Datei bleibt unverändert");
            var reset = LabelRow(content, "Hintergrund-Alpha (eigen)", -1);
            Check(reset && reset.name == "×", "Zurücksetzen-Knopf vor dem Feld");
            reset.GetComponent<Button>().onClick.Invoke(); await Frames(2);
            Check(!GpsUserOverrides.IsOverridden("preferences", "panelBackdropAlpha") && GpsUserOverrides.Count == 0, "× setzt auf Standard zurück");
            Check(Labels(content).Contains("Hintergrund-Alpha"), "Markierung nach Zurücksetzen weg");
        }
        finally { GpsRuntimePanel.Close(); }
    }
    static IEnumerable<string> Labels(Transform content) => content.Cast<Transform>().Select(child => child.GetComponent<TextMeshProUGUI>()?.text).Where(text => text != null);
    static Transform LabelRow(Transform content, string label, int offset)
    {
        for (int i = 0; i < content.childCount; i++)
            if (content.GetChild(i).GetComponent<TextMeshProUGUI>()?.text == label) return i + offset >= 0 && i + offset < content.childCount ? content.GetChild(i + offset) : null;
        return null;
    }
    static Transform Next(Transform content, string label) => LabelRow(content, label, 1) ?? throw new Exception("Panel row missing: " + label);
    static void Click(Transform content, string name)
    {
        var button = content.Cast<Transform>().FirstOrDefault(child => child.name == name)?.GetComponent<Button>();
        Check(button, "Panel-Knopf vorhanden: " + name); button.onClick.Invoke();
    }
    static Task Frames(int count)
    {
        var source = new TaskCompletionSource<bool>();
        var waiter = new GameObject("Overrides check waiter").AddComponent<FrameWaiter>();
        waiter.frames = count; waiter.source = source;
        return source.Task;
    }
    sealed class FrameWaiter : MonoBehaviour
    {
        public int frames;
        public TaskCompletionSource<bool> source;
        void Update() { if (--frames > 0) return; source.TrySetResult(true); Destroy(gameObject); }
    }
}
