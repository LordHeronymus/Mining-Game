using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// GPS persistence in the editor (A-gps-2/3/4/7/8), Edit Mode:
// - user folder: a local, unversioned clip does not turn the clip list into one override; a whole saved list loses only
//   entries whose asset is gone instead of all own clip values
// - SessionState "GPS.Committed" is tied to the profile text and ignored after a branch switch without editor restart
// - unchanged saves keep GpsOverrides.json and its .bak, an unchanged value queues no commit
// - personal volume uses a per-project EditorPrefs key and takes over the former shared value once
// - list entries are renumbered on SetValue, so overrides never address duplicate names
// - R3-gps-files-0: a clip added to the catalog on main keeps all own clip values (matched by clip, older files by position)
// - R3-gps-files-3: resetting a narrow deep own map works although the default width alone is too large next to the own depth
// Runs on a temporary profile and temporary override file; profile text, SessionState and EditorPrefs are restored exactly.
public static class BughuntGpsPersistenceChecks
{
    static readonly List<string> lines = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); lines.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static GpsRecord Record(GpsDocument doc, Type type) => doc.records.First(record => record.type == type.AssemblyQualifiedName);
    static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static GpsProfile catalog; // temporary profile without one clip, destroyed in Main

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode erwartet.");
        if (!string.IsNullOrEmpty(GameSaveSystem.TestDirectory) || !string.IsNullOrEmpty(MetaProgression.TestDirectory))
            throw new InvalidOperationException("Save-/Meta-TestDirectory muss null sein.");
        lines.Clear();
        GpsSettings.CommitPendingNow();
        var profile = GpsSettings.Profile; var original = Copy(GpsSettings.Document); string originalJson = profile.documentJson;
        var temporary = ScriptableObject.CreateInstance<GpsProfile>(); temporary.hideFlags = HideFlags.HideAndDontSave;
        temporary.assets = profile.assets.ToList(); temporary.documentJson = originalJson;
        catalog = null;
        string directory = Path.GetFullPath("Temp/BughuntGpsPersistenceChecks-" + DateTime.UtcNow.Ticks); Directory.CreateDirectory(directory);
        string committed = SessionState.GetString("GPS.Committed", ""), committedBase = SessionState.GetString("GPS.CommittedBase", "");
        var prefKeys = new[] { "masterVolume", "musicVolume" }.SelectMany(field => new[] { GpsSettings.PersonalPreferenceKey(field), "MiningGame.GPS." + field }).ToArray();
        var prefs = prefKeys.ToDictionary(key => key, key => EditorPrefs.HasKey(key) ? (float?)EditorPrefs.GetFloat(key) : null);
        bool autoCommit = GpsSettings.AutoCommit; bool? testActive = GpsUserOverrides.TestActive; string testPath = GpsUserOverrides.TestPath;
        try
        {
            GpsUserOverrides.TestActive = false; GpsSettings.AutoCommit = false;
            GpsUserOverrides.TestPath = Path.Combine(directory, "GpsOverrides.json"); GpsUserOverrides.Reload();
            OverrideFileChecks();
            CommitChecks(temporary, original);
            ClipCatalogChecks(temporary, original);
            CatalogGrowthChecks(temporary, original);
            ResetChecks(temporary, original);
            SessionChecks(profile, original, originalJson);
            VolumeChecks();
            File.WriteAllLines("Temp/BughuntGpsPersistenceChecks.txt", lines.Append("PASS (" + lines.Count + ")"));
            return "PASS (" + lines.Count + ")";
        }
        catch (Exception ex) { File.WriteAllLines("Temp/BughuntGpsPersistenceChecks.txt", lines.Append("FAIL " + ex)); throw; }
        finally
        {
            GpsUserOverrides.TestActive = testActive; GpsUserOverrides.TestPath = testPath; GpsUserOverrides.Reload();
            GpsSettings.AutoCommit = false; GpsSettings.CommitPendingNow(); GpsSettings.AutoCommit = autoCommit;
            profile.documentJson = originalJson; GpsSettings.UseProfile(profile, original); GpsSettings.ApplyAssets(); GpsSettings.ApplyLoadedComponents();
            if (committed.Length == 0) SessionState.EraseString("GPS.Committed"); else SessionState.SetString("GPS.Committed", committed);
            if (committedBase.Length == 0) SessionState.EraseString("GPS.CommittedBase"); else SessionState.SetString("GPS.CommittedBase", committedBase);
            foreach (var pref in prefs) if (pref.Value.HasValue) EditorPrefs.SetFloat(pref.Key, pref.Value.Value); else EditorPrefs.DeleteKey(pref.Key);
            Undo.ClearUndo(temporary); Object.DestroyImmediate(temporary);
            if (catalog) Object.DestroyImmediate(catalog);
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    // A-gps-4: saving the same entries again keeps the file and its backup.
    static void OverrideFileChecks()
    {
        string path = GpsUserOverrides.FilePath;
        List<GpsUserOverrides.Entry> Entries(double value) => new() { new GpsUserOverrides.Entry { record = "asset:check", path = "speed",
            value = new GpsValue { name = "speed", type = typeof(double).AssemblyQualifiedName, kind = GpsValueKind.Number, number = value } } };
        GpsUserOverrides.Write(Entries(1)); string first = File.ReadAllText(path);
        GpsUserOverrides.Write(Entries(2)); string second = File.ReadAllText(path);
        Check(File.ReadAllText(path + ".bak") == first, "Geänderte eigene Werte schieben die vorherige Datei nach .bak");
        GpsUserOverrides.Write(Entries(2));
        Check(File.ReadAllText(path) == second && File.ReadAllText(path + ".bak") == first, "Speichern ohne Änderung lässt Datei und .bak unverändert");
        GpsUserOverrides.Write(Entries(3));
        Check(File.ReadAllText(path + ".bak") == second, "Echte Änderung danach sichert weiterhin den letzten Stand");
    }

    // A-gps-4 and A-gps-8 on the temporary profile: no commit for unchanged values, list entries named by position.
    static void CommitChecks(GpsProfile temporary, GpsDocument original)
    {
        GpsSettings.UseProfile(temporary, Copy(original));
        void Clear() { GpsSettings.AutoCommit = false; GpsSettings.CommitPendingNow(); GpsSettings.AutoCommit = true; }
        // Only queued: nothing runs the delayed commit during this synchronous check, and Clear drops it.
        GpsSettings.AutoCommit = true;
        var speedRecord = Record(GpsSettings.Document, typeof(PlayerBaseStats));
        var speed = GpsSettings.GetValue(speedRecord.key, "miningSpeed");
        Check(GpsSettings.SetValueAndCommit(speedRecord.key, speed, out _) && !GpsSettings.HasPendingCommits, "Unveränderter Wert (z. B. Aufklappen) löst kein Speichern aus");
        speed.number += .25;
        Check(GpsSettings.SetValueAndCommit(speedRecord.key, speed, out string error) && GpsSettings.HasPendingCommits, "Geänderter Wert wird gespeichert: " + error);
        Clear();
        Check(GpsSettings.SetValueAndCommit(speedRecord.key, speed, out _) && GpsSettings.HasPendingCommits, "Gleicher Wert nach ungespeicherter Änderung wird weiterhin gespeichert");
        Clear(); GpsSettings.AutoCommit = false;

        var map = Record(GpsSettings.Document, typeof(MapGenerator));
        var ores = GpsSettings.GetValue(map.key, "oreSettings");
        int oreIndex = ores.children.FindIndex(ore => ore.children.Find(child => child.name == "layerIndices")?.children.Count >= 2);
        Check(oreIndex >= 0, "Erz mit mehreren Layern vorhanden");
        var layers = ores.children[oreIndex].children.Find(child => child.name == "layerIndices");
        // Like the menus: remove an entry, then add one named after the list length.
        var moved = layers.children[0]; layers.children.RemoveAt(0); moved.name = layers.children.Count.ToString(); layers.children.Add(moved);
        Check(layers.children.Select(child => child.name).Distinct().Count() < layers.children.Count, "Menüfolge erzeugt doppelte Namen");
        Check(GpsSettings.SetValue(map.key, ores, out error), "Layerliste setzbar: " + error);
        var stored = GpsSettings.GetValue(map.key, "oreSettings").children[oreIndex].children.Find(child => child.name == "layerIndices");
        Check(stored.children.Select(child => child.name).SequenceEqual(Enumerable.Range(0, stored.children.Count).Select(i => i.ToString())), "Listeneinträge tragen nach dem Setzen ihre Position als Namen");
        var target = GpsSettings.ShippedDocument();
        GpsUserOverrides.Apply(target, temporary, GpsUserOverrides.Diff(GpsSettings.Document, GpsSettings.ShippedDocument(), temporary));
        var applied = Record(target, typeof(MapGenerator)).fields.Find(field => field.name == "oreSettings").children[oreIndex].children.Find(child => child.name == "layerIndices");
        Check(applied.children.Select(child => child.number).SequenceEqual(stored.children.Select(child => child.number)), "Eigene Layerliste trifft beim Laden dieselben Einträge");
    }

    // A-gps-2: local clips in the user folder, whole saved clip lists with a missing clip.
    static void ClipCatalogChecks(GpsProfile temporary, GpsDocument original)
    {
        var doc = Copy(original);
        var audio = Record(doc, typeof(AudioClipTuningSettingsAsset));
        var clips = audio.fields.Find(field => field.name == "clips");
        string ClipKey(GpsValue entry) => entry.children.Find(child => child.name == "clip")?.text;
        int index = clips.children.FindIndex(entry => temporary.Resolve(ClipKey(entry)) is AudioClip clip && AssetDatabase.GetAssetPath(clip).StartsWith("Assets/", StringComparison.Ordinal));
        Check(index >= 0, "Clipliste enthält einen Clip unter Assets");
        string local = ClipKey(clips.children[index]);
        int shippedCount = clips.children.Count;
        // Profile without this clip: in it the clip counts as a new, local one.
        clips.children.RemoveAt(index); for (int i = 0; i < clips.children.Count; i++) clips.children[i].name = i.ToString();
        catalog = ScriptableObject.CreateInstance<GpsProfile>(); catalog.hideFlags = HideFlags.HideAndDontSave;
        catalog.assets = temporary.assets.ToList(); catalog.documentJson = JsonUtility.ToJson(doc);
        GpsSettings.UseProfile(catalog, Copy(doc));
        GpsValue Clips(GpsDocument data) => Record(data, typeof(AudioClipTuningSettingsAsset)).fields.Find(field => field.name == "clips");

        GpsUserOverrides.TestActive = true;
        var user = Copy(doc); GpsAudioCatalogEditor.Populate(catalog, user);
        Check(Clips(user).children.Count == clips.children.Count && Clips(user).children.All(entry => ClipKey(entry) != local), "Benutzerordner: lokaler Clip kommt nicht in die Clipliste");
        Check(GpsUserOverrides.Diff(user, GpsSettings.ShippedDocument(), catalog).All(entry => !(entry.record == audio.key && entry.path == "clips")), "Benutzerordner: Clipliste bleibt einzeln vergleichbar");
        GpsUserOverrides.TestActive = false;
        var work = Copy(doc); GpsAudioCatalogEditor.Populate(catalog, work);
        Check(Clips(work).children.Any(entry => ClipKey(entry) == local), "Arbeitsordner nehmen neue Clips weiterhin auf");

        // A whole saved list holding a clip that no longer resolves keeps all other own clip values.
        GpsSettings.UseProfile(temporary, Copy(original));
        var own = Clips(Copy(original)).Copy();
        var volume = own.children[0].children.Find(child => child.name == "volume");
        double tuned = volume.number > .5 ? volume.number - .25 : volume.number + .25; volume.number = tuned;
        var ghost = own.children[0].Copy(); ghost.children.Find(child => child.name == "clip").text = "00000000000000000000000000000000:1";
        ghost.name = own.children.Count.ToString(); own.children.Add(ghost);
        var whole = new GpsUserOverrides.Entry { record = audio.key, path = "clips", value = own };
        var loaded = Copy(original); var stale = new List<GpsUserOverrides.Entry>();
        int count = GpsUserOverrides.Apply(loaded, temporary, new[] { whole }, stale);
        var result = Clips(loaded);
        Check(count == 1 && stale.Count == 0 && result.children.Count == shippedCount, "Gespeicherte Clipliste verliert nur den Eintrag ohne Asset");
        Check(Near(result.children[0].children.Find(child => child.name == "volume").number, tuned), "Übrige eigene Clipwerte bleiben erhalten");
        var effective = GpsUserOverrides.Diff(loaded, GpsSettings.ShippedDocument(), temporary);
        Check(effective.Any(item => item.record == audio.key && item.path == "clips/0/volume") && effective.All(item => item.path != "clips"), "Danach gespeichert als einzelner Clipwert");
    }

    // R3-gps-files-0: main appends a clip to the catalog (Populate in every work folder); own clip values stay on their clip.
    static void CatalogGrowthChecks(GpsProfile temporary, GpsDocument original)
    {
        GpsSettings.UseProfile(temporary, Copy(original));
        string audioKey = Record(original, typeof(AudioClipTuningSettingsAsset)).key;
        GpsValue Clips(GpsDocument data) => Record(data, typeof(AudioClipTuningSettingsAsset)).fields.Find(field => field.name == "clips");
        string ClipKey(GpsValue entry) => entry.children.Find(child => child.name == "clip")?.text;
        double Volume(GpsDocument data, string clip) => Clips(data).children.First(entry => ClipKey(entry) == clip).children.Find(child => child.name == "volume").number;
        void Renumber(GpsValue list) { for (int i = 0; i < list.children.Count; i++) list.children[i].name = i.ToString(); }
        double Tune(GpsValue entry) { var volume = entry.children.Find(child => child.name == "volume"); volume.number = volume.number > .5 ? volume.number - .25 : volume.number + .25; return volume.number; }

        // Older main: the catalog has one clip less. Own values on the first and the last clip of that catalog.
        var before = Copy(original); var shorter = Clips(before);
        Check(shorter.children.Count >= 3 && shorter.children.Select(ClipKey).Distinct().Count() == shorter.children.Count, "Clipkatalog mit eindeutigen Clips");
        shorter.children.RemoveAt(shorter.children.Count - 1);
        var own = Copy(before); var ownClips = Clips(own);
        string first = ClipKey(ownClips.children[0]), last = ClipKey(ownClips.children[ownClips.children.Count - 1]);
        double firstVolume = Tune(ownClips.children[0]), lastVolume = Tune(ownClips.children[ownClips.children.Count - 1]);
        var entries = GpsUserOverrides.Diff(own, before, temporary).Where(entry => entry.record == audioKey).ToList();
        Check(entries.Count == 2 && entries.All(entry => entry.path.StartsWith("clips/", StringComparison.Ordinal) && entry.arrays.Count == 1 && entry.keys.Count == 1) &&
            entries.Select(entry => entry.keys[0]).SequenceEqual(new[] { first, last }), "Eigene Clipwerte werden mit ihrem Clip gespeichert");

        // Newer main: one clip more at the end.
        var grown = Copy(original); var stale = new List<GpsUserOverrides.Entry>();
        int applied = GpsUserOverrides.Apply(grown, temporary, entries, stale);
        Check(applied == 2 && stale.Count == 0 && Near(Volume(grown, first), firstVolume) && Near(Volume(grown, last), lastVolume),
            "Neuer Clip von main verwirft eigene Clipwerte nicht (angewendet " + applied + ", veraltet " + stale.Count + ")");
        // A clip moved inside the catalog: the own value follows its clip, not the position.
        var moved = Copy(original); var movedClips = Clips(moved);
        var head = movedClips.children[0]; movedClips.children.RemoveAt(0); movedClips.children.Add(head); Renumber(movedClips);
        stale.Clear(); applied = GpsUserOverrides.Apply(moved, temporary, entries, stale);
        Check(applied == 2 && stale.Count == 0 && Near(Volume(moved, first), firstVolume) && Near(Volume(moved, last), lastVolume), "Verschobener Clip behält seinen eigenen Wert");
        // A clip removed from the catalog: only its own value is stale, nothing lands on the clip now at its position.
        var removed = Copy(original); var removedClips = Clips(removed);
        string next = ClipKey(removedClips.children[1]); double nextVolume = Volume(removed, next);
        removedClips.children.RemoveAt(0); Renumber(removedClips);
        stale.Clear(); applied = GpsUserOverrides.Apply(removed, temporary, entries, stale);
        Check(applied == 1 && stale.Count == 1 && stale[0].keys[0] == first && Near(Volume(removed, next), nextVolume) && Near(Volume(removed, last), lastVolume),
            "Eigener Wert eines entfernten Clips gilt als veraltet, ohne den nächsten Clip zu treffen");

        // Older override file without clip keys: in the catalog that only grew, the position still applies.
        var keyless = entries.Select(entry => new GpsUserOverrides.Entry { record = entry.record, path = entry.path, arrays = entry.arrays.ToList(), value = entry.value.Copy() }).ToList();
        var legacy = Copy(original); stale.Clear();
        applied = GpsUserOverrides.Apply(legacy, temporary, keyless, stale);
        Check(applied == 2 && stale.Count == 0 && Near(Volume(legacy, first), firstVolume) && Near(Volume(legacy, last), lastVolume), "Ältere Datei ohne Clipschlüssel übersteht einen neuen Clip");
        // Loading such a file stores the clip keys right away.
        var applyUser = typeof(GpsSettings).GetMethod("ApplyUserOverrides", Hidden);
        Check(applyUser != null, "GpsSettings.ApplyUserOverrides vorhanden");
        GpsUserOverrides.TestActive = true; GpsUserOverrides.Write(keyless);
        GpsSettings.UseProfile(temporary, Copy(original));
        applyUser.Invoke(null, null);
        var stored = GpsUserOverrides.Entries.Where(entry => entry.record == audioKey).ToList();
        Check(stored.Count == 2 && stored.All(entry => entry.keys.Count == entry.arrays.Count) &&
            Near(Volume(GpsSettings.Document, first), firstVolume) && Near(Volume(GpsSettings.Document, last), lastVolume), "Laden ergänzt den Clipschlüssel älterer Einträge");
        GpsUserOverrides.Write(new List<GpsUserOverrides.Entry>()); GpsUserOverrides.TestActive = false;
        GpsSettings.UseProfile(temporary, Copy(original));
    }

    // R3-gps-files-3: "Zurücksetzen" sets width before depth; the default width next to an own depth may exceed MaxCells alone.
    static void ResetChecks(GpsProfile temporary, GpsDocument original)
    {
        GpsUserOverrides.TestActive = true; GpsUserOverrides.Write(new List<GpsUserOverrides.Entry>());
        GpsSettings.UseProfile(temporary, Copy(original));
        var map = Record(GpsSettings.Document, typeof(MapGenerator));
        var width = GpsSettings.GetValue(map.key, "mapWidth"); var height = GpsSettings.GetValue(map.key, "mapHeight");
        var shipped = Record(GpsSettings.ShippedDocument(), typeof(MapGenerator)).fields;
        double defaultWidth = shipped.Find(field => field.name == "mapWidth").number, defaultHeight = shipped.Find(field => field.name == "mapHeight").number;
        double deep = Math.Floor(2000000 / defaultWidth) + 1, narrow = Math.Floor(2000000 / deep);
        Check(deep <= 10000 && narrow >= 1 && narrow < defaultWidth, "Testkarte " + narrow + " × " + deep + " liegt im Einzelbereich");
        width.number = narrow; height.number = deep;
        Check(GpsSettings.SetValue(map.key, width, out string error) && GpsSettings.SetValue(map.key, height, out error), "Schmale tiefe Karte ist setzbar: " + error);
        var fields = new[] { (map.key, "mapWidth"), (map.key, "mapHeight") };
        Check(GpsSettings.CommitFields(fields, out error) && GpsUserOverrides.IsOverridden(map.key, "mapWidth") && GpsUserOverrides.IsOverridden(map.key, "mapHeight"),
            "Eigene Kartengrösse gespeichert: " + error);
        Check(GpsSettings.ResetToDefaults(fields, out error), "Zurücksetzen (Breite vor Tiefe) gelingt: " + error);
        Check(GpsSettings.GetValue(map.key, "mapWidth").number == defaultWidth && GpsSettings.GetValue(map.key, "mapHeight").number == defaultHeight &&
            !GpsUserOverrides.IsOverridden(map.key, "mapWidth") && !GpsUserOverrides.IsOverridden(map.key, "mapHeight"), "Standardgrösse wiederhergestellt und gespeichert");
        var wide = GpsSettings.GetValue(map.key, "mapWidth"); wide.number = Math.Floor(2000000 / defaultHeight) + 1;
        Check(wide.number > 10000 || !GpsSettings.SetValue(map.key, wide, out error), "Zu grosse Karte wird weiterhin abgelehnt");
        GpsUserOverrides.Write(new List<GpsUserOverrides.Entry>()); GpsUserOverrides.TestActive = false;
        GpsSettings.UseProfile(temporary, Copy(original));
    }

    // A-gps-3: the remembered committed state belongs to one profile text.
    static void SessionChecks(GpsProfile profile, GpsDocument original, string originalJson)
    {
        var key = Record(original, typeof(PlayerBaseStats)).key;
        double Speed(string json) => JsonUtility.FromJson<GpsDocument>(json).records.First(record => record.key == key).fields.Find(field => field.name == "miningSpeed").number;
        double speed = Speed(JsonUtility.ToJson(original));
        var previous = Copy(original); previous.records.First(record => record.key == key).fields.Find(field => field.name == "miningSpeed").number = speed + .25;

        // Branch A was tested: the play start remembers its committed state.
        profile.documentJson = GpsSettings.AssetJson(previous); GpsSettings.UseProfile(profile, Copy(previous));
        GpsSettings.RememberCommitted();
        Check(Near(Speed(SessionState.GetString("GPS.Committed", "")), speed + .25), "Play-Start merkt sich den gespeicherten Stand");
        // editor-bench checkout then switches to branch B without restarting the editor.
        profile.documentJson = originalJson;
        GpsSettings.ReloadFromProfile();
        Check(Near(Speed(GpsSettings.CommittedJson), speed) && !GpsSettings.HasUnsavedChanges, "Nach Branchwechsel gilt der Stand des neuen Profils");
        GpsSettings.ReloadCommitted();
        Check(Near(Speed(profile.documentJson), speed) && Near(Speed(GpsSettings.CommittedJson), speed), "Play-Stopp setzt nicht auf den vorherigen Branch zurück");

        // Branch switch while GPS still holds branch A in memory (no domain reload).
        profile.documentJson = GpsSettings.AssetJson(previous); GpsSettings.UseProfile(profile, Copy(previous));
        profile.documentJson = originalJson;
        GpsSettings.RememberCommitted();
        Check(SessionState.GetString("GPS.Committed", "") == "", "Play-Start nach Branchwechsel ohne Domain-Reload merkt sich keinen veralteten Stand");
        GpsSettings.ReloadFromProfile();
        Check(Near(Speed(GpsSettings.CommittedJson), speed), "Geladen wird der Stand des neuen Profils");

        // Same profile: the remembered state still applies as before (e.g. edit-mode changes not yet committed).
        profile.documentJson = originalJson; GpsSettings.UseProfile(profile, Copy(previous));
        GpsSettings.RememberCommitted();
        GpsSettings.ReloadFromProfile();
        Check(Near(Speed(GpsSettings.CommittedJson), speed + .25) && GpsSettings.HasUnsavedChanges, "Ohne Branchwechsel bleibt der gemerkte Stand gültig");
        GpsSettings.ReloadCommitted();
        Check(Near(Speed(profile.documentJson), speed + .25), "Play-Stopp stellt den gemerkten Stand wie bisher her");
    }

    // A-gps-7: personal volume per project, the former shared value is taken over once.
    static void VolumeChecks()
    {
        string key = GpsSettings.PersonalPreferenceKey("masterVolume"), shared = "MiningGame.GPS.masterVolume";
        Check(key != shared && key.StartsWith("MiningGame.GPS.", StringComparison.Ordinal) && key.EndsWith(".masterVolume", StringComparison.Ordinal), "Lautstärke-Schlüssel gilt nur für dieses Projekt");
        var load = typeof(GpsSettings).GetMethod("LoadPersonalPreferences", Hidden); var save = typeof(GpsSettings).GetMethod("SavePersonalPreferences", Hidden);
        EditorPrefs.DeleteKey(key); EditorPrefs.SetFloat(shared, .37f);
        var data = new GpsDocument(); load.Invoke(null, new object[] { data });
        Check(Mathf.Approximately(data.preferences.masterVolume, .37f) && Mathf.Approximately(EditorPrefs.GetFloat(key), .37f), "Bisherige gemeinsame Lautstärke wird einmalig übernommen");
        data.preferences.masterVolume = .61f; save.Invoke(null, new object[] { data });
        Check(Mathf.Approximately(EditorPrefs.GetFloat(shared), .37f) && Mathf.Approximately(EditorPrefs.GetFloat(key), .61f), "Speichern ändert die Lautstärke anderer Editoren nicht");
        EditorPrefs.SetFloat(shared, .2f); data = new GpsDocument(); load.Invoke(null, new object[] { data });
        Check(Mathf.Approximately(data.preferences.masterVolume, .61f), "Spätere Änderungen anderer Editoren wirken hier nicht");
    }
}
