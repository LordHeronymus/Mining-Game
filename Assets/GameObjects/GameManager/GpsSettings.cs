using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GpsSettings
{
    public const string ResourceName = "Gameplay/GpsProfile";
    static GpsProfile profile;
    static GpsDocument document;
    static string savedJson, shippedJson;
    static bool applying, dirty;
    // Debug-menu edits are committed field by field shortly after the last change, so a slider drag writes once.
    static readonly List<(string key, string field)> pendingCommits = new();
    static double commitAt;
    const double CommitDelaySeconds = .35;
    // Checks switch this off (or use a save test directory) so driving the menu never writes the real profile.
    public static bool AutoCommit = true;
#if UNITY_EDITOR
    static bool CanAutoCommit => AutoCommit && string.IsNullOrEmpty(GameSaveSystem.TestDirectory) && string.IsNullOrEmpty(MetaProgression.TestDirectory);
#else
    static bool CanAutoCommit => AutoCommit && string.IsNullOrEmpty(MetaProgression.TestDirectory); // No save test directory in builds.
#endif
    public static event Action Changed;
    public static event Action Saved;
    public static string Warning { get; private set; }
    public static string FilePath => Path.Combine(Application.persistentDataPath, "gps-settings.json");
    public static GpsProfile Profile { get { EnsureLoaded(); return profile; } }
    public static GpsDocument Document { get { EnsureLoaded(); return document; } }
    public static bool HasUnsavedChanges { get { EnsureLoaded(); return dirty; } }
    public static string CommittedJson { get { EnsureLoaded(); return savedJson; } }
    public static bool IsApplying => applying;
    public static GpsPreferences Preferences => Document.preferences;
    public static GameplayTestSettingsData Tests => Document.tests;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    { profile = null; document = null; savedJson = null; shippedJson = null; Warning = null; Changed = null; Saved = null; applying = false; dirty=false; pendingCommits.Clear(); AutoCommit = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartRuntime()
    {
        EnsureLoaded();
        ApplyAssets();
        ApplyLoadedComponents();
        Application.quitting -= CommitPendingNow; Application.quitting += CommitPendingNow;
        if (!Object.FindFirstObjectByType<GpsRuntimeHost>(FindObjectsInactive.Include))
        { var host = new GameObject("Ingame GPS"); Object.DontDestroyOnLoad(host); host.AddComponent<GpsRuntimeHost>(); }
    }
    public static void EnsureLoaded()
    {
        if (document != null) return;
        profile = Resources.Load<GpsProfile>(ResourceName);
        shippedJson = profile ? profile.documentJson : null;
        document = profile && !string.IsNullOrWhiteSpace(profile.documentJson)
            ? JsonUtility.FromJson<GpsDocument>(profile.documentJson) : new GpsDocument();
        document ??= new GpsDocument();
#if !UNITY_EDITOR
        bool existing=File.Exists(FilePath) || File.Exists(FilePath+".bak");
        document=GpsFileStore.Load(FilePath,document,out var warning,out bool legacyFormat); Warning=warning;
        if(!existing && profile) GpsLegacyImport.Import(document,profile,Application.persistentDataPath);
        // The first start stores the import; an old full copy is converted to overrides.
        bool rewrite=(!existing && profile) || (legacyFormat && warning==null);
#if !DEVELOPMENT_BUILD
        // Release players can only change personal preferences; every other value in the file (an old full copy,
        // F1 values of a development build on the same PC, imported debug files) must not hide the current profile.
        var personal=KeepPersonalPreferences(document,ShippedDocument());
        rewrite|=profile && warning==null && JsonUtility.ToJson(personal)!=JsonUtility.ToJson(document);
        document=personal;
#endif
        if(rewrite)
            try { GpsFileStore.Save(FilePath,document); } catch(Exception ex) { Warning="GPS speichern fehlgeschlagen: "+ex.Message; }
#else
        LoadPersonalPreferences(document);
#endif
        ItemIconLayout.Migrate(document, profile);
#if UNITY_EDITOR
        if (GpsUserOverrides.Active) ApplyUserOverrides();
#endif
        savedJson = JsonUtility.ToJson(document);
#if UNITY_EDITOR
        profileText = shippedJson;
        // Mit eigener Überschreibungs-Schicht ist Profil + Datei immer der gespeicherte Stand.
        var committed = GpsUserOverrides.Active ? "" : CommittedSession();
        if (!string.IsNullOrEmpty(committed)) savedJson = JsonUtility.ToJson(JsonUtility.FromJson<GpsDocument>(committed));
#endif
        dirty = savedJson != JsonUtility.ToJson(document);
    }
    // Volume is chosen per player. In the editor it lives in EditorPrefs so it never ships in the profile asset.
    static readonly string[] PersonalPreferences = { nameof(GpsPreferences.masterVolume), nameof(GpsPreferences.musicVolume) };
    // Takes only the personal values of a loaded player document over into the shipped one.
    static GpsDocument KeepPersonalPreferences(GpsDocument loaded, GpsDocument shipped)
    {
        foreach (string field in PersonalPreferences) GpsCodec.Set(shipped.preferences, field, GpsCodec.Get(loaded.preferences, field));
        return shipped;
    }
    // The profile as loaded, before player overrides. Edit-mode changes write through to the asset, so the
    // live documentJson is not a stable baseline there.
    public static GpsDocument ShippedDocument()
    {
        var source = profile ? profile : Resources.Load<GpsProfile>(ResourceName);
        string json = shippedJson ?? (source ? source.documentJson : null);
        var shipped = !string.IsNullOrWhiteSpace(json) ? JsonUtility.FromJson<GpsDocument>(json) : null;
        shipped ??= new GpsDocument();
        ItemIconLayout.Migrate(shipped, source);
        return shipped;
    }
    // Swaps the personal values out in place instead of copying: a JSON round trip would shift doubles in the last digit.
    public static string AssetJson(GpsDocument data)
    {
        var personal = PersonalPreferences.Select(field => GpsCodec.Get(data.preferences, field)).ToArray();
        var defaults = new GpsPreferences();
        try
        {
            foreach (string field in PersonalPreferences) GpsCodec.Set(data.preferences, field, GpsCodec.Get(defaults, field));
            return JsonUtility.ToJson(data, true);
        }
        finally { for (int i = 0; i < PersonalPreferences.Length; i++) GpsCodec.Set(data.preferences, PersonalPreferences[i], personal[i]); }
    }
#if UNITY_EDITOR
    // EditorPrefs are shared by every editor of the user, so the key names the project: the test bench and work folders
    // never change the volume of the user's own editor.
    static string personalPrefix;
    public static string PersonalPreferenceKey(string field) => (personalPrefix ??= "MiningGame.GPS." +
        Fnv(Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/')).ToString("x16") + ".") + field;
    static void LoadPersonalPreferences(GpsDocument data)
    {
        foreach (string field in PersonalPreferences)
        {
            string key = PersonalPreferenceKey(field), shared = "MiningGame.GPS." + field;
            // First run with the project key: take over the former shared value once, else keep the value from the profile.
            if (!UnityEditor.EditorPrefs.HasKey(key)) UnityEditor.EditorPrefs.SetFloat(key,
                UnityEditor.EditorPrefs.HasKey(shared) ? UnityEditor.EditorPrefs.GetFloat(shared) : (float)GpsCodec.Get(data.preferences, field));
            GpsCodec.Set(data.preferences, field, Mathf.Clamp01(UnityEditor.EditorPrefs.GetFloat(key)));
        }
    }
    static void SavePersonalPreferences(GpsDocument data)
    {
        foreach (string field in PersonalPreferences)
            UnityEditor.EditorPrefs.SetFloat(PersonalPreferenceKey(field), (float)GpsCodec.Get(data.preferences, field));
    }
    static ulong Fnv(string text)
    {
        ulong hash = 14695981039346656037;
        unchecked { foreach (char c in text) { hash ^= c; hash *= 1099511628211; } }
        return hash;
    }
    // The committed state outlives domain reloads in SessionState, tied to the profile text it belongs to. A branch switch
    // without editor restart (Tools/editor-bench checkout) replaces the profile; the old state then no longer applies.
    static string profileText; // documentJson as last read or written here; another instance means the profile was replaced
    static string ProfileStamp(string text) => (text ?? "").Length + ":" + Fnv(text ?? "").ToString("x16");
    static string CommittedSession()
    {
        string json = UnityEditor.SessionState.GetString("GPS.Committed", "");
        if (json.Length == 0 || profile && UnityEditor.SessionState.GetString("GPS.CommittedBase", "") == ProfileStamp(profile.documentJson)) return json;
        UnityEditor.SessionState.EraseString("GPS.Committed"); UnityEditor.SessionState.EraseString("GPS.CommittedBase");
        return "";
    }
    static void RememberCommitted(string json)
    {
        UnityEditor.SessionState.SetString("GPS.Committed", json);
        UnityEditor.SessionState.SetString("GPS.CommittedBase", ProfileStamp(profile.documentJson));
    }
    // Play start: keep the committed state for the return to edit mode, unless the profile was replaced since it was loaded.
    public static void RememberCommitted()
    {
        EnsureLoaded();
        if (!profile) return;
        if (ReferenceEquals(profile.documentJson, profileText)) RememberCommitted(savedJson);
        else { UnityEditor.SessionState.EraseString("GPS.Committed"); UnityEditor.SessionState.EraseString("GPS.CommittedBase"); }
    }
    // Eigene Werte (UserSettings/GpsOverrides.json) über das geladene Profil legen. Was inzwischen dem Standard entspricht
    // oder dessen Stelle es nicht mehr gibt, fällt aus der Datei; Letzteres steht mit Wert in der Konsole.
    static void ApplyUserOverrides()
    {
        var list = GpsUserOverrides.Entries.ToList();
        if (list.Count == 0) return;
        var stale = new List<GpsUserOverrides.Entry>();
        GpsUserOverrides.Apply(document, profile, list, stale);
        foreach (var entry in stale)
            Debug.LogWarning("Eigener GPS-Wert passt nicht mehr zum Profil und wurde entfernt: " + entry.record + " / " + entry.path +
                " = " + GpsUserOverrides.Describe(entry.value) + " (alte Datei: " + GpsUserOverrides.FilePath + ".bak)");
        var effective = GpsUserOverrides.Diff(document, ShippedDocument(), profile);
        if (stale.Count > 0 || effective.Count != list.Count)
        {
            int same = list.Count - stale.Count - effective.Count;
            if (same > 0) Debug.Log(same + " eigene GPS-Werte entsprechen jetzt dem Standard und wurden aus der Überschreibung entfernt.");
            try { GpsUserOverrides.Write(effective); } catch (Exception ex) { Warning = "Eigene GPS-Werte speichern fehlgeschlagen: " + ex.Message; }
        }
    }
    // Nach einer Aktualisierung von main: Profil neu einlesen, eigene Werte wieder darüberlegen.
    public static void ReloadFromProfile()
    {
        CommitPendingNow();
        pendingCommits.Clear(); document = null; savedJson = null; shippedJson = null;
        EnsureLoaded(); ApplyAssets();
        if (!Application.isPlaying) ApplyLoadedComponents();
        Changed?.Invoke();
    }
    // Setzt Felder auf die Standardwerte des Profils zurück und speichert nur diese Felder.
    public static bool ResetToDefaults(IEnumerable<(string key, string field)> fields, out string error)
    {
        error = null;
        var shipped = ShippedDocument(); var list = fields.ToList();
        foreach (var (key, field) in list)
        {
            GpsValue node;
            if (key == "preferences" || key == "tests")
            {
                object target = key == "preferences" ? shipped.preferences : shipped.tests;
                node = GpsCodec.Read(field, GpsCodec.MemberType(target.GetType(), field), GpsCodec.Get(target, field), Profile.Key);
            }
            else node = shipped.records.Find(record => record.key == key)?.fields.Find(value => value.name == field)?.Copy();
            if (node != null && !SetValue(key, node, out error)) return false;
        }
        pendingCommits.RemoveAll(list.Contains);
        if (!CommitFields(list, out error)) return false;
        Changed?.Invoke(); return true;
    }
#endif
    public static bool IsValidOverride(GpsRecord record, GpsValue node)
    {
        if (record == null || node == null) return false;
        var spec = GpsSchema.Spec(record, node.name);
        var type = GpsCodec.MemberType(GpsCodec.ResolveType(record.type) ?? typeof(object), node.name);
        return spec != null && type != null && GpsCodec.Validate(node, out _) && GpsCodec.ValidateShape(node, type, out _) &&
            ValidateBounds(node, spec, out _) && ValidateReferences(node, out _) && ValidateLayerStones(GpsCodec.ResolveType(record.type), node, out _);
    }
    public static void UseProfile(GpsProfile source, GpsDocument data)
    {
        profile = source; document = data; shippedJson = source ? source.documentJson : null;
#if UNITY_EDITOR
        profileText = shippedJson;
#endif
        savedJson = JsonUtility.ToJson(data); Warning = null; dirty=false; pendingCommits.Clear();
    }
    public static void ReloadCommitted()
    {
#if UNITY_EDITOR
        profile = Resources.Load<GpsProfile>(ResourceName);
        // Mit Überschreibungs-Schicht bleibt das Profil unverändert; der gespeicherte Stand kommt aus Profil + Datei.
        if (GpsUserOverrides.Active) { Reload(); return; }
        string json = CommittedSession(); bool remembered = json.Length > 0;
        if (!remembered) json = profile ? profile.documentJson : "";
        if (profile && !string.IsNullOrEmpty(json))
        {
            profile.documentJson = AssetJson(JsonUtility.FromJson<GpsDocument>(json)); profileText = profile.documentJson;
            if (remembered) RememberCommitted(json);
        }
#endif
        Reload();
    }
    public static void Reload()
    {
        string committed = savedJson;
        document = null; EnsureLoaded(); ApplyAssets();
        if (!string.IsNullOrEmpty(committed)) savedJson = committed;
        dirty = savedJson != JsonUtility.ToJson(document);
        if (!Application.isPlaying) ApplyLoadedComponents();
        Changed?.Invoke();
    }
    public static IEnumerable<GpsRecord> Records(GpsSectionSpec section)
    {
        if (section.source == typeof(GpsPreferences)) return new[] { VirtualRecord("preferences", Preferences) };
        if (section.source == typeof(GameplayTestSettingsData)) return new[] { VirtualRecord("tests", Tests) };
        return Document.records.Where(section.Matches);
    }
    static GpsRecord VirtualRecord(string key, object value) => new() { key = key, type = value.GetType().AssemblyQualifiedName,
        name = "", fields = GpsSchema.Fields(value.GetType()).Select(field => GpsCodec.Read(field.name,
            GpsCodec.MemberType(value.GetType(), field.name), GpsCodec.Get(value, field.name), Profile.Key)).ToList() };
    public static GpsValue GetValue(string key, string field)
    {
        if (key == "preferences" || key == "tests")
        {
            object target = key == "preferences" ? Preferences : Tests;
            return GpsCodec.Read(field, GpsCodec.MemberType(target.GetType(), field), GpsCodec.Get(target, field), Profile.Key);
        }
        return Document.records.Find(record => record.key == key)?.fields.Find(node => node.name == field)?.Copy();
    }
    public static bool SetValue(string key, GpsValue node, out string error) => SetValue(key, node, out error, out _);
    static bool SetValue(string key, GpsValue node, out string error, out bool changed)
    {
        EnsureLoaded();
        changed = false;
        if (!GpsCodec.Validate(node, out error)) return false;
        // List entries are named by position; removing and adding in the menus would otherwise leave duplicate names
        // that make saved overrides hit the wrong entry.
        Renumber(node);
        var record = Document.records.Find(item => item.key == key);
        Type type = key == "preferences" ? typeof(GpsPreferences) : key == "tests" ? typeof(GameplayTestSettingsData) : GpsCodec.ResolveType(record?.type);
        if (type == null) { error = "GPS-Bereich fehlt."; return false; }
        var spec = GpsSchema.Fields(type, key.Split(':').Last()).FirstOrDefault(field => field.name == node.name);
        if (spec == null) { error = "Unbekannte GPS-Einstellung."; return false; }
        if (!GpsCodec.ValidateShape(node,GpsCodec.MemberType(type,node.name),out error)) return false;
        if (!ValidateBounds(node, spec, out error) || !ValidateReferences(node,out error) || !ValidateLayerStones(type, node, out error)) return false;
        // A larger world could be generated but never saved; checked here so stored override files stay loadable.
        if (type == typeof(MapGenerator) && (node.name == "mapWidth" || node.name == "mapHeight") && node.number *
            (record.fields.Find(field => field.name == (node.name == "mapWidth" ? "mapHeight" : "mapWidth"))?.number ?? 1) > GameSaveSystem.MaxCells)
        { error = "Karte zu groß: Breite × Tiefe höchstens " + GameSaveSystem.MaxCells + " Zellen."; return false; }
        var previous=GetValue(key,node.name);
        if (previous!=null && JsonUtility.ToJson(previous)==JsonUtility.ToJson(node)) return true;
        changed = true;
#if UNITY_EDITOR
        bool writeThrough = !Application.isPlaying && profile && !GpsUserOverrides.Active;
        if (writeThrough) UnityEditor.Undo.RecordObject(profile, "GPS ändern");
#endif
        if (key == "preferences" || key == "tests") GpsCodec.Set(key == "preferences" ? Preferences : (object)Tests, node.name, GpsCodec.Write(node, Profile.Resolve));
        else
        {
            int index = record.fields.FindIndex(field => field.name == node.name);
            if (index < 0) { error = "GPS-Feld fehlt."; return false; }
            record.fields[index] = node.Copy();
            if (spec.applyTime == GpsApplyTime.Immediately || !Application.isPlaying)
                foreach (var target in Targets(record)) ApplyField(target, node);
        }
        if (key == "preferences")
        {
            PlayerSettings.ApplyAudio();
            Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
        }
        if (key == "tests") ApplyTestConfiguration();
#if UNITY_EDITOR
        if (writeThrough)
        { profile.documentJson = AssetJson(document); profileText = profile.documentJson; UnityEditor.EditorUtility.SetDirty(profile); }
#endif
        dirty=true;
        Changed?.Invoke();
        if (type==typeof(PlayerBaseStats)) GameplaySettings.NotifyChanged();
        return true;
    }
    static void Renumber(GpsValue node)
    {
        for (int i = 0; i < node.children.Count; i++)
        {
            if (node.kind == GpsValueKind.Array) node.children[i].name = i.ToString();
            Renumber(node.children[i]);
        }
    }
    public static void NotifyConfigurationChanged() { Changed?.Invoke(); }
    static bool ValidateBounds(GpsValue node, GpsFieldSpec spec, out string error)
    {
        error = null;
        if ((node.kind == GpsValueKind.Number || node.kind == GpsValueKind.Integer) &&
            (node.number < spec.min || node.number > spec.max)) { error = spec.label + ": Wert außerhalb des Bereichs."; return false; }
        if (node.kind == GpsValueKind.Integer && !GpsCodec.FitsInteger(node.number, GpsCodec.ResolveType(node.type)))
        { error = spec.label + ": Wert außerhalb des Bereichs."; return false; }
        if (node.kind==GpsValueKind.Vector)
            for (int i=0;i<GpsSchema.VectorDimension(node);i++)
                if (node.vector[i]<spec.min || node.vector[i]>spec.max) { error=spec.label+": Wert außerhalb des Bereichs."; return false; }
        Type type=GpsCodec.ResolveType(node.type);
        for (int i=0;i<node.children.Count;i++)
        {
            var child=node.children[i];
            var childSpec=node.kind==GpsValueKind.Array ? GpsSchema.ArraySpec(child,spec,i) : GpsSchema.ChildSpec(child,type);
            if (!ValidateBounds(child,childSpec,out error)) return false;
        }
        return true;
    }
    // A bound raised in a later version would otherwise refuse stored values (own value dropped, whole player file
    // rejected); such values move into the range instead. Mirrors ValidateBounds; true when something changed.
    public static bool ClampBounds(GpsValue node, GpsFieldSpec spec)
    {
        if (node?.children == null || spec == null) return false;
        bool changed = false;
        // Non-finite values stay as they are; GpsCodec.Validate still refuses them.
        if ((node.kind == GpsValueKind.Number || node.kind == GpsValueKind.Integer) && !double.IsInfinity(node.number) && (node.number < spec.min || node.number > spec.max))
        { node.number = Math.Clamp(node.number, spec.min, spec.max); changed = true; }
        if (node.kind==GpsValueKind.Vector)
            for (int i=0;i<GpsSchema.VectorDimension(node);i++)
                if (!float.IsInfinity(node.vector[i]) && (node.vector[i]<spec.min || node.vector[i]>spec.max)) { node.vector[i]=Mathf.Clamp(node.vector[i],spec.min,spec.max); changed=true; }
        Type type=GpsCodec.ResolveType(node.type);
        for (int i=0;i<node.children.Count;i++)
        {
            var child=node.children[i];
            changed|=ClampBounds(child,node.kind==GpsValueKind.Array ? GpsSchema.ArraySpec(child,spec,i) : GpsSchema.ChildSpec(child,type));
        }
        return changed;
    }
    public static void ClampBounds(GpsDocument data)
    {
        if (data?.records == null) return;
        foreach (var record in data.records)
            if (record?.fields != null) foreach (var field in record.fields) ClampBounds(field, GpsSchema.Spec(record, field.name));
        if (data.preferences == null) return;
        foreach (var pref in GpsSchema.Fields(typeof(GpsPreferences)))
        {
            var node=GpsCodec.Read(pref.name,GpsCodec.MemberType(typeof(GpsPreferences),pref.name),GpsCodec.Get(data.preferences,pref.name),Profile.Key);
            if (ClampBounds(node,pref)) GpsCodec.Set(data.preferences,pref.name,GpsCodec.Write(node,Profile.Resolve));
        }
    }
    public static bool Save(out string error)
    {
        pendingCommits.Clear();
        if (!Commit(Document, out error)) return false;
        Changed?.Invoke(); return true;
    }
    public static bool SavePreference(string field, out string error)
    {
        if (!GpsSchema.Fields(typeof(GpsPreferences)).Any(spec=>spec.name==field)) { error="Unbekannte GPS-Einstellung."; return false; }
        if (!CommitFields(new[] { ("preferences", field) }, out error)) return false;
        Changed?.Invoke(); return true;
    }
    // Debug-menu edit: applies like SetValue and is stored for good a moment later, so it survives play-mode and editor restarts.
    public static bool SetValueAndCommit(string key, GpsValue node, out string error)
    {
        if (!SetValue(key, node, out error, out bool changed)) return false;
        // An unchanged value (e.g. a foldout click inside a change check) has nothing to store, unless earlier
        // uncommitted edits such as a slider preview wait for exactly this commit.
        if (changed || dirty) QueueCommit(key, node.name);
        return true;
    }
    public static void QueueCommit(string key, string field)
    {
        if (!CanAutoCommit || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(field)) return;
        if (!pendingCommits.Contains((key, field))) pendingCommits.Add((key, field));
        commitAt = Time.realtimeSinceStartupAsDouble + CommitDelaySeconds;
    }
    public static bool HasPendingCommits => pendingCommits.Count > 0;
    public static void CommitPending() { if (pendingCommits.Count > 0 && Time.realtimeSinceStartupAsDouble >= commitAt) CommitPendingNow(); }
    public static void CommitPendingNow()
    {
        if (pendingCommits.Count == 0) return;
        var fields = pendingCommits.ToArray(); pendingCommits.Clear();
        if (document == null || !CanAutoCommit) return;
        // Edit mode writes every change through to the asset already, so the whole document is the user's state there.
        // In play mode only the edited fields are stored: runtime-only changes stay temporary as before.
        bool ok = Application.isPlaying ? CommitFields(fields, out string error) : Commit(document, out error);
        if (!ok) { Warning = error; Debug.LogWarning(error); }
    }
    // Copies the current values of the given fields onto the committed state and persists only that.
    public static bool CommitFields(IEnumerable<(string key, string field)> fields, out string error)
    {
        var committed = JsonUtility.FromJson<GpsDocument>(CommittedJson);
        foreach (var (key, field) in fields)
        {
            var node = GetValue(key, field); if (node == null) continue;
            if (key == "preferences" || key == "tests")
                GpsCodec.Set(key == "preferences" ? committed.preferences : (object)committed.tests, field, GpsCodec.Write(node, Profile.Resolve));
            else
            {
                int index = committed.records.Find(record => record.key == key)?.fields.FindIndex(value => value.name == field) ?? -1;
                if (index >= 0) committed.records.Find(record => record.key == key).fields[index] = node;
            }
        }
        return Commit(committed, out error);
    }
    static bool Commit(GpsDocument data, out string error)
    {
        if (!ValidateDocument(data, out error) || !Persist(data, out error)) return false;
        savedJson = JsonUtility.ToJson(data); Warning = null; dirty = savedJson != JsonUtility.ToJson(document);
        Saved?.Invoke(); return true;
    }
    public static void PreviewMasterVolume(float value)
    {
        float volume = Mathf.Clamp01(value);
        if (Mathf.Approximately(Preferences.masterVolume, volume)) return;
        Preferences.masterVolume = volume;
        AudioListener.volume = volume;
        dirty = true;
    }
    public static void PreviewMusicVolume(float value)
    {
        float volume = Mathf.Clamp01(value);
        if (Mathf.Approximately(Preferences.musicVolume, volume)) return;
        Preferences.musicVolume = volume;
        dirty = true;
    }
    static bool Persist(GpsDocument data, out string error)
    {
        error=null;
        string json = JsonUtility.ToJson(data, true);
        try
        {
#if UNITY_EDITOR
            if (!profile) throw new InvalidOperationException("GPS-Profil fehlt.");
            if (GpsUserOverrides.Active)
            {
                // Benutzerordner: nur die Abweichungen vom Profil in die nicht versionierte Datei, das Profil bleibt unberührt.
                var entries = GpsUserOverrides.Diff(data, ShippedDocument(), profile);
                GpsUserOverrides.Write(GpsUserOverrides.Hold ? GpsUserOverrides.Merge(entries, GpsUserOverrides.Entries) : entries);
                SavePersonalPreferences(data);
                UnityEditor.SessionState.SetString("GPS.Committed", json);
                return true;
            }
            UnityEditor.Undo.RecordObject(profile, "GPS speichern");
            profile.documentJson = AssetJson(data); profileText = profile.documentJson;
            SavePersonalPreferences(data);
            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(profile);
            RememberCommitted(json);
#else
            GpsFileStore.Save(FilePath,data);
#endif
            return true;
        }
        catch (Exception ex) { error = "GPS speichern fehlgeschlagen: " + ex.Message; return false; }
    }
    public static bool ValidateDocument(GpsDocument data, out string error)
    {
        error = null;
        if (data == null || data.version != 1 || data.records == null || data.records.Count==0 || data.preferences == null || data.tests == null)
        { error = "Ungültiges GPS-Profil."; return false; }
        if (data.records.GroupBy(record => record.key).Any(group => group.Count() != 1)) { error = "Doppelte GPS-Bereiche."; return false; }
        foreach (var record in data.records)
        {
            if (record.fields == null || record.fields.GroupBy(field => field.name).Any(group => group.Count() != 1)) { error = "Doppelte GPS-Felder."; return false; }
            foreach (var field in record.fields)
            {
                if (!GpsCodec.Validate(field, out error)) return false;
                var spec = GpsSchema.Spec(record, field.name);
                if (!GpsCodec.ValidateShape(field,GpsCodec.MemberType(GpsCodec.ResolveType(record.type),field.name),out error)) return false;
                if (spec == null || !ValidateBounds(field, spec, out error)) { error ??= "Unbekanntes GPS-Feld: " + field.name; return false; }
                if (!ValidateReferences(field, out error)) return false;
            }
        }
        foreach (var pref in GpsSchema.Fields(typeof(GpsPreferences)))
        {
            var node=GpsCodec.Read(pref.name,GpsCodec.MemberType(typeof(GpsPreferences),pref.name),GpsCodec.Get(data.preferences,pref.name),Profile.Key);
            if (!GpsCodec.Validate(node,out error) || !ValidateBounds(node,pref,out error)) return false;
        }
        if (!GameplaySettingsStore.IsValidSpeed((float)(data.records.FirstOrDefault(record => record.type == typeof(PlayerBaseStats).AssemblyQualifiedName)?.fields.Find(field => field.name == "miningSpeed")?.number ?? 1.5)))
        { error = "Ungültige Abbaugeschwindigkeit."; return false; }
        if (!GameplayTestSettings.IsValid(data.tests.diggingMultiplier) || !GameplayTestSettings.IsValidMovementMultiplier(data.tests.movementMultiplier) ||
            !GameplayTestSettings.IsValidSmartCursorStrokeWidth(data.tests.smartCursorStrokeWidth) || !Enum.IsDefined(typeof(GameplayDayNightMode), data.tests.dayNightMode) ||
            !Enum.IsDefined(typeof(HomescreenBackgroundMode), data.tests.homescreenBackground))
        { error = "Ungültige Testeinstellungen."; return false; }
        return ValidateRelationships(data, out error);
    }
    static bool ValidateReferences(GpsValue node, out string error)
    {
        error = null;
        if (node.kind == GpsValueKind.Reference && !string.IsNullOrEmpty(node.text) && (!profile || !profile.Resolve(node.text)))
        { error = "GPS-Asset fehlt: " + node.text; return false; }
        if (node.kind==GpsValueKind.Reference && !string.IsNullOrEmpty(node.text) && !GpsCodec.ResolveType(node.type).IsInstanceOfType(profile.Resolve(node.text)))
        { error="GPS-Assettyp stimmt nicht überein."; return false; }
        foreach (var child in node.children) if (!ValidateReferences(child, out error)) return false;
        return true;
    }
    static bool ValidateRelationships(GpsDocument data, out string error)
    {
        error = null;
        foreach (var record in data.records)
        {
            if (record.type == typeof(CraftingRecipe).AssemblyQualifiedName)
            {
                var list = record.fields.Find(field => field.name == "ingredients");
                var recipe = profile.Resolve(record.assetKey) as CraftingRecipe;
                if (list == null || list.children.Count == 0 || list.children.Any(entry => string.IsNullOrEmpty(entry.children.Find(field => field.name == "item")?.text) ||
                    (entry.children.Find(field => field.name == "amount")?.number ?? 0) < 1 || profile.Resolve(entry.children.Find(field => field.name == "item").text) == recipe?.output))
                { error = "Ungültige Zutaten: " + record.name; return false; }
            }
            if (record.type == typeof(MapGenerator).AssemblyQualifiedName)
            {
                var layers = record.fields.Find(field => field.name == "layers");
                int height = (int)(record.fields.Find(field => field.name == "mapHeight")?.number ?? 1);
                var starts = layers?.children.Select(layer => (int)(layer.children.Find(field => field.name == "startDepth")?.number ?? 0)).ToArray();
                if (starts == null || starts.Length == 0 || starts[0] != 0 || starts.Distinct().Count() != starts.Length || starts.Any(start => start < 0 || start >= height) || !starts.SequenceEqual(starts.OrderBy(start => start)))
                { error = "Layerstarts müssen bei 0 beginnen und innerhalb der Karte aufsteigend sein."; return false; }
                if (!ValidateLayerStones(typeof(MapGenerator), layers, out error)) return false;
            }
        }
        if (data.preferences.startingResources == null || data.preferences.startingResources.money < 0 || data.preferences.startingResources.items == null ||
            data.preferences.startingResources.items.Any(entry => entry.amount < 1 || !StartingResourcesSettings.Resolve(entry.itemId)))
        { error = "Ungültige Startressourcen."; return false; }
        return true;
    }
    // Same rule as MapGenerationSampler: every layer needs a stone block, dirt only in the top layer. The sampler
    // throws otherwise, so a new run would never leave the loading screen.
    static bool ValidateLayerStones(Type owner, GpsValue node, out string error)
    {
        error = null;
        if (owner != typeof(MapGenerator) || node == null || node.name != "layers") return true;
        var ordered = node.children.OrderBy(layer => layer.children.Find(field => field.name == "startDepth")?.number ?? 0).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            string key = ordered[i].children.Find(field => field.name == "stone")?.text;
            var stone = profile && !string.IsNullOrEmpty(key) ? profile.Resolve(key) as Block : null;
            if (!stone || (!stone.IsStone && !(i == 0 && stone.id == BlockType.Dirt)))
            { error = "Layer " + (i + 1) + ": Gesteinsart muss ein Stein sein (Erde nur im obersten Layer)."; return false; }
        }
        return true;
    }
    public static IEnumerable<Object> Targets(GpsRecord record)
    {
        if (!string.IsNullOrEmpty(record.assetKey)) { var target = Profile.Resolve(record.assetKey); return target ? new[] { target } : Array.Empty<Object>(); }
        Type type = GpsCodec.ResolveType(record.type);
        return type == null ? Array.Empty<Object>() : Resources.FindObjectsOfTypeAll(type).Where(target => target is Component component &&
            component.gameObject.scene.IsValid() && GpsSchema.ComponentKey(component) == record.key &&
            (type!=typeof(Camera) || component.GetComponent<CameraFollow>()) &&
            (type!=typeof(Rigidbody2D) || component.GetComponent<PlayerMovement>()));
    }
    public static void ApplyComponent(Object target)
    {
        if (!target || applying) return;
        EnsureLoaded();
        var record = Document.records.Find(item => item.key == GpsSchema.ComponentKey(target));
        if (record == null) return;
        foreach (var field in record.fields) ApplyField(target, field, false);
        if (target is CameraFollow follow) { ApplyComponent(follow.GetComponent<Camera>()); ApplyComponent(follow.GetComponent<CameraWorldBorderClamp>()); }
        if (target is PlayerMovement movement) ApplyComponent(movement.GetComponent<Rigidbody2D>());
    }
    public static void ApplyAssets()
    {
        foreach (var record in Document.records.Where(record => !string.IsNullOrEmpty(record.assetKey)))
            foreach (var target in Targets(record)) foreach (var field in record.fields) ApplyField(target, field, false);
    }
    public static void ApplyLoadedComponents()
    {
        foreach (var record in Document.records.Where(record => string.IsNullOrEmpty(record.assetKey)))
            foreach (var target in Targets(record)) foreach (var field in record.fields) ApplyField(target, field, false);
    }
    public static void ApplyGeneration(MapGenerator map)
    {
        var record = Document.records.Find(record => record.key == GpsSchema.ComponentKey(map));
        if (record != null) foreach (var field in record.fields) ApplyField(map, field, false);
    }
    public static GpsValue[] CaptureGeneration(MapGenerator map) => GpsSchema.Fields(typeof(MapGenerator))
        .Where(field => field.applyTime == GpsApplyTime.NewMap)
        .Select(field => GpsCodec.Read(field.name, GpsCodec.MemberType(typeof(MapGenerator), field.name), GpsCodec.Get(map, field.name), Profile.Key)).ToArray();
    public static void RestoreGeneration(MapGenerator map, GpsValue[] fields)
    {
        if (fields == null) return; // Existing saves predate the optional generation snapshot.
        foreach (var field in NormalizeGeneration(fields)) ApplyField(map, field, false);
    }
    // Saves outlive schema changes: keep only fields that are still map-generation settings and bring
    // each to the current layout, so adding a field to MapLayer or the ore settings keeps old saves loadable.
    static GpsValue[] NormalizeGeneration(GpsValue[] fields)
    {
        var allowed = GpsSchema.Fields(typeof(MapGenerator)).Where(field => field.applyTime == GpsApplyTime.NewMap).Select(field => field.name).ToHashSet();
        return fields.Where(field => field != null && allowed.Contains(field.name))
            .Select(field => GpsCodec.Upgrade(field, GpsCodec.MemberType(typeof(MapGenerator), field.name), Profile.Key)).ToArray();
    }
    public static bool ValidateGeneration(GpsValue[] fields, int width, int height, out string error)
    {
        error = "Ungültige Karteneinstellungen im Spielstand.";
        var source = Document.records.Find(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
        if (source == null || fields == null || fields.Any(field => field == null) || fields.Select(field => field.name).Distinct().Count() != fields.Length) return false;
        fields = NormalizeGeneration(fields);
        if (fields.Length == 0 || !fields.Any(field => field.name == "mapWidth") || !fields.Any(field => field.name == "mapHeight")) return false;
        // Validation only reads the nodes; a separate field list keeps the profile's record unchanged.
        var record = new GpsRecord { key = source.key, type = source.type, assetKey = source.assetKey, name = source.name, fields = new List<GpsValue>(source.fields) };
        foreach (var field in fields) record.fields[record.fields.FindIndex(node => node.name == field.name)] = field;
        if (record.fields.Find(field => field.name == "mapWidth").number != width || record.fields.Find(field => field.name == "mapHeight").number != height) return false;
        return ValidateDocument(new GpsDocument { records = new List<GpsRecord> { record }, preferences = Preferences, tests = Tests }, out error);
    }
    static void ApplyField(Object target, GpsValue node, bool effects = true)
    {
        if (!target || GpsCodec.MemberType(target.GetType(), node.name) == null) return;
        // Defensive: invalid layers (e.g. a hand-edited profile) keep the map's current ones instead of breaking generation.
        if (target is MapGenerator && !ValidateLayerStones(typeof(MapGenerator), node, out string layerError)) { Debug.LogWarning(layerError); return; }
        applying = true;
        try
        {
            if (effects && Application.isPlaying && target is SkyController liveSky && node.name == "isNight") liveSky.SetNight(node.flag);
            else GpsCodec.Set(target, node.name, GpsCodec.Write(node, Profile.Resolve));
            if (!effects || !Application.isPlaying) return;
            if (target is MapGenerator map)
            {
                if (node.name=="shopSize" || node.name=="workshopSize" || node.name=="altarSize" || node.name=="energyMonolythSize") map.ApplyMapObjectSizes();
                if (node.name.StartsWith("torch", StringComparison.Ordinal)) PlacedTorch.ApplySettings(map);
                if (node.name == "artifactEmbeddingStrength" && map.ArtifactOverlay)
                    ArtifactOverlayAppearance.ApplyTo(map.ArtifactOverlay.GetComponent<UnityEngine.Tilemaps.TilemapRenderer>(), map.artifactEmbeddingStrength);
            }
            if (target is SurfaceTallGrass grass && (node.name=="grassSizeMultiplier" || node.name=="healingHerbSizeMultiplier")) grass.ApplyPatchSizes();
            if (target is SurfaceTrees forest) forest.ApplySettings();
            if (target is MinerPlayerVisual visual && node.name=="height") visual.Refresh();
            if (target is UniformStoneAppearance appearance) appearance.RefreshAppearance();
            if (target is MapWorldBorders borders) borders.RefreshBounds();
            if (target is AudioClipTuningSettingsAsset || target is LayerMiningAudioSettingsAsset)
                target.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(target, null);
            if (target is MapLighting) GameplaySettings.NotifyChanged();
            if (target is ItemSO || target is UpgradeSettings) { InventoryManager.Instance?.NotifyWeightChanged(); StatsManager.Instance?.RefreshEnergyCapacity(); }
            if (target is ItemSO shopItem && (node.name.StartsWith("icon", StringComparison.Ordinal) || node.name.StartsWith("shopIcon", StringComparison.Ordinal)))
            {
                foreach (var slot in Object.FindObjectsByType<ShopSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (slot.Item == shopItem) slot.RefreshIconLayout();
                foreach (var page in Object.FindObjectsByType<SellPage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    page.RefreshIconLayouts();
                foreach (var inventory in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    inventory.RefreshIconLayouts();
                Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
                Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include)?.RefreshRecipeIcons();
                Object.FindFirstObjectByType<BuyPage>(FindObjectsInactive.Include)?.RefreshIconLayouts();
            }
            if (target is CraftingRecipe recipe)
            {
                var workbench=Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);
                if (workbench && workbench.IsOpen) { workbench.RefreshRecipeSettings(recipe); workbench.RefreshRecipeIcons(); }
                Object.FindFirstObjectByType<CompactHud>(FindObjectsInactive.Include)?.RefreshHotbarIconLayouts();
                Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include)?.RefreshIconLayouts();
            }
        }
        finally { applying = false; }
    }
    static void ApplyTestConfiguration()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        foreach (var follow in Object.FindObjectsByType<CameraFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            follow.enabled = Tests.cameraFollow;
#endif
    }
    public static void Capture(Object target)
    {
        if (!target || applying) return;
        var record = Document.records.Find(record => string.IsNullOrEmpty(record.assetKey)
            ? record.key == GpsSchema.ComponentKey(target) : Profile.Resolve(record.assetKey) == target);
        if (record == null) return;
        foreach (var field in record.fields.ToArray())
        {
            var value = GpsCodec.Read(field.name, GpsCodec.MemberType(target.GetType(), field.name), GpsCodec.Get(target, field.name), Profile.Key);
            SetValue(record.key, value, out _);
        }
    }
}
