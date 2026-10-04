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
    static string savedJson;
    static bool applying, dirty;
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
    { profile = null; document = null; savedJson = null; Warning = null; Changed = null; Saved = null; applying = false; dirty=false; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartRuntime()
    {
        EnsureLoaded();
        ApplyAssets();
        ApplyLoadedComponents();
        if (!Object.FindFirstObjectByType<GpsRuntimeHost>(FindObjectsInactive.Include))
        { var host = new GameObject("Ingame GPS"); Object.DontDestroyOnLoad(host); host.AddComponent<GpsRuntimeHost>(); }
    }
    public static void EnsureLoaded()
    {
        if (document != null) return;
        profile = Resources.Load<GpsProfile>(ResourceName);
        document = profile && !string.IsNullOrWhiteSpace(profile.documentJson)
            ? JsonUtility.FromJson<GpsDocument>(profile.documentJson) : new GpsDocument();
        document ??= new GpsDocument();
#if !UNITY_EDITOR
        bool existing=File.Exists(FilePath) || File.Exists(FilePath+".bak");
        document=GpsFileStore.Load(FilePath,document,out var warning); Warning=warning;
        if(!existing && profile)
        {
            GpsLegacyImport.Import(document,profile,Application.persistentDataPath);
            try { GpsFileStore.Save(FilePath,document); } catch(Exception ex) { Warning="GPS speichern fehlgeschlagen: "+ex.Message; }
        }
#endif
        savedJson = JsonUtility.ToJson(document);
#if UNITY_EDITOR
        var committed = UnityEditor.SessionState.GetString("GPS.Committed", "");
        if (!string.IsNullOrEmpty(committed)) savedJson = JsonUtility.ToJson(JsonUtility.FromJson<GpsDocument>(committed));
#endif
        dirty = savedJson != JsonUtility.ToJson(document);
    }
    public static void UseProfile(GpsProfile source, GpsDocument data)
    {
        profile = source; document = data;
        savedJson = JsonUtility.ToJson(data); Warning = null; dirty=false;
    }
    public static void ReloadCommitted()
    {
#if UNITY_EDITOR
        profile = Resources.Load<GpsProfile>(ResourceName);
        string json = UnityEditor.SessionState.GetString("GPS.Committed", profile ? profile.documentJson : "");
        if (profile && !string.IsNullOrEmpty(json)) profile.documentJson = json;
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
    public static bool SetValue(string key, GpsValue node, out string error)
    {
        EnsureLoaded();
        if (!GpsCodec.Validate(node, out error)) return false;
        var record = Document.records.Find(item => item.key == key);
        Type type = key == "preferences" ? typeof(GpsPreferences) : key == "tests" ? typeof(GameplayTestSettingsData) : GpsCodec.ResolveType(record?.type);
        if (type == null) { error = "GPS-Bereich fehlt."; return false; }
        var spec = GpsSchema.Fields(type, key.Split(':').Last()).FirstOrDefault(field => field.name == node.name);
        if (spec == null) { error = "Unbekannte GPS-Einstellung."; return false; }
        if (!GpsCodec.ValidateShape(node,GpsCodec.MemberType(type,node.name),out error)) return false;
        if (!ValidateBounds(node, spec, out error) || !ValidateReferences(node,out error)) return false;
        var previous=GetValue(key,node.name);
        if (previous!=null && JsonUtility.ToJson(previous)==JsonUtility.ToJson(node)) return true;
#if UNITY_EDITOR
        if (!Application.isPlaying && profile) UnityEditor.Undo.RecordObject(profile, "GPS ändern");
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
        if (!Application.isPlaying && profile)
        { profile.documentJson = JsonUtility.ToJson(document, true); UnityEditor.EditorUtility.SetDirty(profile); }
#endif
        dirty=true;
        Changed?.Invoke();
        if (type==typeof(PlayerBaseStats)) GameplaySettings.NotifyChanged();
        return true;
    }
    public static void NotifyConfigurationChanged() { Changed?.Invoke(); }
    static bool ValidateBounds(GpsValue node, GpsFieldSpec spec, out string error)
    {
        error = null;
        if ((node.kind == GpsValueKind.Number || node.kind == GpsValueKind.Integer) &&
            (node.number < spec.min || node.number > spec.max)) { error = spec.label + ": Wert außerhalb des Bereichs."; return false; }
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
    public static bool Save(out string error)
    {
        if (!ValidateDocument(Document, out error)) return false;
        if (!Persist(document,out error)) return false;
        savedJson = JsonUtility.ToJson(document); Warning = null; dirty=false;
        Saved?.Invoke(); Changed?.Invoke(); return true;
    }
    public static bool SavePreference(string field, out string error)
    {
        if (!GpsSchema.Fields(typeof(GpsPreferences)).Any(spec=>spec.name==field)) { error="Unbekannte GPS-Einstellung."; return false; }
        var committed=JsonUtility.FromJson<GpsDocument>(CommittedJson);
        var node=GpsCodec.Read(field,GpsCodec.MemberType(typeof(GpsPreferences),field),GpsCodec.Get(Preferences,field),Profile.Key);
        GpsCodec.Set(committed.preferences,field,GpsCodec.Write(node,Profile.Resolve));
        if (!ValidateDocument(committed,out error) || !Persist(committed,out error)) return false;
        savedJson=JsonUtility.ToJson(committed);Warning=null;dirty=savedJson!=JsonUtility.ToJson(document);
        Saved?.Invoke();Changed?.Invoke();return true;
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
            UnityEditor.Undo.RecordObject(profile, "GPS speichern");
            profile.documentJson = json;
            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(profile);
            UnityEditor.SessionState.SetString("GPS.Committed", json);
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
            !GameplayTestSettings.IsValidSmartCursorStrokeWidth(data.tests.smartCursorStrokeWidth) || !Enum.IsDefined(typeof(GameplayDayNightMode), data.tests.dayNightMode))
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
            }
        }
        if (data.preferences.startingResources == null || data.preferences.startingResources.money < 0 || data.preferences.startingResources.items == null ||
            data.preferences.startingResources.items.Any(entry => entry.amount < 1 || !StartingResourcesSettings.Resolve(entry.itemId)))
        { error = "Ungültige Startressourcen."; return false; }
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
        foreach (var field in fields) ApplyField(map, field, false);
    }
    public static bool ValidateGeneration(GpsValue[] fields, int width, int height, out string error)
    {
        error = "Ungültige Karteneinstellungen im Spielstand.";
        var source = Document.records.Find(record => record.type == typeof(MapGenerator).AssemblyQualifiedName);
        if (source == null || fields == null || fields.Any(field => field == null) || fields.Select(field => field.name).Distinct().Count() != fields.Length) return false;
        var allowed = GpsSchema.Fields(typeof(MapGenerator)).Where(field => field.applyTime == GpsApplyTime.NewMap).ToArray();
        if (fields.Length == 0 || fields.Any(field => !allowed.Any(spec => spec.name == field.name)) ||
            !fields.Any(field => field.name == "mapWidth") || !fields.Any(field => field.name == "mapHeight")) return false;
        var record = JsonUtility.FromJson<GpsRecord>(JsonUtility.ToJson(source));
        foreach (var field in fields) record.fields[record.fields.FindIndex(node => node.name == field.name)] = field.Copy();
        if (record.fields.Find(field => field.name == "mapWidth").number != width || record.fields.Find(field => field.name == "mapHeight").number != height) return false;
        return ValidateDocument(new GpsDocument { records = new List<GpsRecord> { record }, preferences = Preferences, tests = Tests }, out error);
    }
    static void ApplyField(Object target, GpsValue node, bool effects = true)
    {
        if (!target || GpsCodec.MemberType(target.GetType(), node.name) == null) return;
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
            if (target is MinerPlayerVisual visual && node.name=="height") visual.Refresh();
            if (target is UniformStoneAppearance appearance) appearance.RefreshAppearance();
            if (target is MapWorldBorders borders) borders.RefreshBounds();
            if (target is AudioClipTuningSettingsAsset || target is LayerMiningAudioSettingsAsset)
                target.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(target, null);
            if (target is MapLighting) GameplaySettings.NotifyChanged();
            if (target is ItemSO || target is UpgradeSettings) { InventoryManager.Instance?.NotifyWeightChanged(); StatsManager.Instance?.RefreshEnergyCapacity(); }
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
