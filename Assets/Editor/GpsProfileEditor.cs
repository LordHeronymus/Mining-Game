using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GpsProfileEditor
{
    public const string Path = "Assets/Resources/Gameplay/GpsProfile.asset";
    static GpsProfileEditor()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Install(); };
        Undo.undoRedoPerformed += () => { if (Resources.Load<GpsProfile>(GpsSettings.ResourceName)) GpsSettings.Reload(); };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (!Resources.Load<GpsProfile>(GpsSettings.ResourceName) || GpsSettings.Document.records.Count==0) Install();
                GpsSettings.ApplyLoadedComponents();
                var source=Resources.Load<GpsProfile>(GpsSettings.ResourceName);
                if (source) SessionState.SetString("GPS.Committed",GpsSettings.CommittedJson);
            }
            if (state == PlayModeStateChange.EnteredEditMode) { GpsSettings.ReloadCommitted(); if (GpsSettings.Document.records.Count==0) Install(); }
        };
    }
    public static string Register(Object asset, GpsProfile profile)
    {
        if (!asset) return null;
        string existing = profile.Key(asset); if (existing != null) return existing;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
            throw new InvalidOperationException("GPS benötigt ein gespeichertes Asset: " + asset.name);
        string key = guid + ":" + localId;
        profile.assets.Add(new GpsAssetReference { key = key, asset = asset });
        EditorUtility.SetDirty(profile); return key;
    }
    [MenuItem("Mining Game/GPS-Profil aktualisieren", false, 201)]
    public static void Install()
    {
        if (EditorApplication.isPlaying) return;
        var profile = AssetDatabase.LoadAssetAtPath<GpsProfile>(Path);
        bool created = !profile;
        if (created)
        {
            Directory.CreateDirectory("Assets/Resources/Gameplay"); AssetDatabase.Refresh();
            profile = ScriptableObject.CreateInstance<GpsProfile>(); AssetDatabase.CreateAsset(profile, Path);
        }
        var data = created ? new GpsDocument() : GpsSettings.Document;
        if (created) GpsSettings.UseProfile(profile, data);
        bool wasUnsaved = GpsSettings.HasUnsavedChanges;
        string before = JsonUtility.ToJson(data);
        int assetCount = profile.assets.Count;
        Scene extra = default;
        if (!Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include))
            extra = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            var assetTypes = GpsSchema.Sections.Select(section => section.source).Distinct().Where(type => typeof(ScriptableObject).IsAssignableFrom(type)).ToArray();
            foreach (var type in assetTypes)
                foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("_SceneBackups") || path.Contains("/Archive/")) continue;
                    var asset = AssetDatabase.LoadAssetAtPath(path, type);
                    if (asset is CraftingRecipe recipe && !recipe.TryGetCosts(out _)) continue;
                    if (asset) Capture(asset, profile, data);
                }
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("_SceneBackups") && !path.Contains("/Archive/")) Register(AssetDatabase.LoadAssetAtPath<AudioClip>(path), profile);
            }
            var components = Resources.FindObjectsOfTypeAll<Component>().Where(component => component && component.gameObject.scene.IsValid()).ToArray();
            foreach (var type in GpsSchema.Sections.Select(section => section.source).Distinct().Where(type => typeof(Component).IsAssignableFrom(type)))
            {
                var candidates = components.Where(component => component.GetType() == type);
                if (type == typeof(Camera)) candidates = candidates.Where(component => component.GetComponent<CameraFollow>());
                if (type == typeof(Rigidbody2D)) candidates = candidates.Where(component => component.GetComponent<PlayerMovement>());
                foreach (var component in candidates.GroupBy(GpsSchema.ComponentKey).Select(group => group.First())) Capture(component, profile, data);
            }
            if (!data.legacyMigrated) Migrate(profile, data);
            var map=Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
            var oreRecord=data.records.Find(record=>record.type==typeof(MapGenerator).AssemblyQualifiedName);
            if (map && oreRecord!=null)
                foreach (var ore in oreRecord.fields.Find(field=>field.name=="oreSettings").children)
                {
                    var minimum=ore.children.Find(field=>field.name=="minimumVeinSize");
                    if (minimum.number<=0)
                    {
                        var id=(BlockType)(int)ore.children.Find(field=>field.name=="ore").number;
                        minimum.number=id==BlockType.UltroniumOre ? 1 : Mathf.Max(1,map.minimumOreVeinSize);
                    }
                }
            PopulateClips(profile, data);
            if (!GpsSettings.ValidateDocument(data, out string error)) throw new InvalidDataException(error);
            if (created || before != JsonUtility.ToJson(data) || assetCount != profile.assets.Count)
            {
                if (!wasUnsaved) { GpsSettings.Save(out error); if (error != null) throw new InvalidOperationException(error); }
                else { profile.documentJson = JsonUtility.ToJson(data, true); EditorUtility.SetDirty(profile); GpsSettings.NotifyConfigurationChanged(); }
            }
        }
        finally { if (extra.IsValid()) EditorSceneManager.CloseScene(extra, true); }
        if (created) Debug.Log("GPS-Profil erstellt.");
    }
    static void Capture(Object target, GpsProfile profile, GpsDocument data)
    {
        string assetKey = target is ScriptableObject ? Register(target, profile) : null;
        string key = assetKey == null ? GpsSchema.ComponentKey(target) : "asset:" + assetKey;
        var record = data.records.Find(entry => entry.key == key);
        if (record == null)
        {
            record = new GpsRecord { key = key, type = target.GetType().AssemblyQualifiedName, assetKey = assetKey,
                name = target is Component ? "" : GpsSchema.DisplayName(target) }; data.records.Add(record);
        }
        foreach (var spec in GpsSchema.Fields(target.GetType(), GpsSchema.Role(target)))
        {
            if (record.fields.Exists(field => field.name == spec.name)) continue;
            Type fieldType = GpsCodec.MemberType(target.GetType(), spec.name);
            if (fieldType == null) throw new InvalidOperationException(target.GetType().Name + "." + spec.name + " fehlt.");
            record.fields.Add(GpsCodec.Read(spec.name, fieldType, GpsCodec.Get(target, spec.name), asset => Register(asset, profile)));
        }
    }
    static void PopulateClips(GpsProfile profile, GpsDocument data)
    {
        var record = data.records.Find(entry => entry.type == typeof(AudioClipTuningSettingsAsset).AssemblyQualifiedName);
        if (record == null) return;
        var clips = record.fields.Find(field => field.name == "clips");
        foreach (var entry in profile.assets.Where(entry => entry.asset is AudioClip).ToArray())
            if (!clips.children.Any(child => child.children.Find(field => field.name == "clip")?.text == entry.key))
                clips.children.Add(GpsCodec.Read(clips.children.Count.ToString(), typeof(AudioClipTuningEntry), new AudioClipTuningEntry { clip = (AudioClip)entry.asset }, asset => Register(asset, profile)));
    }
    static void Migrate(GpsProfile profile, GpsDocument data) => GpsLegacyImport.Import(data,profile,Application.persistentDataPath);
}
