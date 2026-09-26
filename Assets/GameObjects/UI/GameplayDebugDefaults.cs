#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// SessionState survives the domain reload when leaving Play mode.
[InitializeOnLoad]
public static class GameplayDebugDefaults
{
    const string PendingKey = "MiningGame.DebugDefaults.Pending";

    [Serializable]
    sealed class Request
    {
        public string statsPath, lightingId, previousJson;
        public float speed;
        public bool hasGameplayValues, hasSpeed, hasLighting;
        public LightingSettingsData lighting;
        public ComponentPatch[] componentPatches = Array.Empty<ComponentPatch>();
    }

    [Serializable]
    sealed class ComponentPatch
    {
        public string objectId, propertyPath, valueType, scenePath, componentType;
        public string[] hierarchyNames = Array.Empty<string>();
        public int componentIndex;
        public float floatValue;
        public int intValue;
        public bool boolValue;
        public float[] floatArray = Array.Empty<float>();
    }

    static GameplayDebugDefaults()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode) ApplyPending();
        };
    }

    public static bool QueueCurrent(out string message)
    {
        try
        {
            var request = LoadPending();
            var stats = UnityEngine.Object.FindFirstObjectByType<StatsManager>();
            var lights = UnityEngine.Object.FindObjectsByType<MapLighting>(FindObjectsSortMode.None);
            if (stats)
            {
                var asset = new SerializedObject(stats).FindProperty("baseStats")?.objectReferenceValue;
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException("Das Spieler-Basiswerte-Asset ist nicht gespeichert.");
                request.statsPath = path;
                request.speed = GameplaySettings.BaseDiggingSpeed;
                request.previousJson = File.Exists(GameplaySettings.FilePath)
                    ? File.ReadAllText(GameplaySettings.FilePath) : null;
                request.hasGameplayValues = true;
                request.hasSpeed = true;
            }

            if (GameplaySettings.LightingAvailable)
            {
                if (lights.Length != 1 || string.IsNullOrEmpty(lights[0].gameObject.scene.path))
                    throw new InvalidOperationException("Genau eine gespeicherte Map mit Beleuchtung muss aktiv sein.");
                var id = GlobalObjectId.GetGlobalObjectIdSlow(lights[0]);
                if (id.targetObjectId == 0)
                    throw new InvalidOperationException("Die Map-Beleuchtung hat keine gespeicherte Szenenreferenz.");
                request.lightingId = id.ToString();
                request.lighting = GameplaySettings.Lighting;
                request.hasGameplayValues = true;
                request.hasLighting = true;
            }

            if (!request.hasGameplayValues && request.componentPatches.Length == 0)
                throw new InvalidOperationException("Es wurden keine GPS-Standardwerte gefunden.");
            if ((request.hasSpeed && !GameplaySettingsStore.IsValidSpeed(request.speed)) ||
                (request.hasLighting && (request.lighting == null || !request.lighting.IsValid)))
                throw new InvalidOperationException("Ungültige Gameplay-Einstellungen.");

            Store(request);
            message = "Als GPS-Standard vorgemerkt. Wird beim Beenden des Play-Modus gespeichert.";
            return true;
        }
        catch (Exception ex)
        {
            message = "GPS-Standardwerte konnten nicht vorgemerkt werden: " + ex.Message;
            return false;
        }
    }

    public static bool QueueComponentValue(Component component, string propertyPath, out string error)
    {
        error = null;
        if (!component || string.IsNullOrWhiteSpace(propertyPath))
        {
            error = "Komponente oder Feld fehlt.";
            return false;
        }

        try
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(component);
            var sourceScene = component.gameObject.scene;
            if (!sourceScene.IsValid() || string.IsNullOrEmpty(sourceScene.path) ||
                sourceScene.name == "DontDestroyOnLoad" || sourceScene.path == "DontDestroyOnLoad")
                sourceScene = SceneManager.GetActiveScene();
            if (!sourceScene.IsValid() || string.IsNullOrEmpty(sourceScene.path))
                throw new InvalidOperationException("Die Einstellung gehört zu keiner gespeicherten Szene.");

            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(propertyPath);
            if (property == null)
                throw new InvalidOperationException("Feld nicht gefunden: " + propertyPath);

            var patch = new ComponentPatch
            {
                objectId = id.ToString(),
                propertyPath = propertyPath,
                scenePath = sourceScene.path,
                hierarchyNames = GetHierarchyNames(component.transform),
                componentType = component.GetType().AssemblyQualifiedName,
                componentIndex = Array.IndexOf(component.GetComponents<Component>()
                    .Where(candidate => candidate && candidate.GetType() == component.GetType()).ToArray(), component)
            };
            if (property.propertyType == SerializedPropertyType.Float)
            {
                patch.valueType = "float";
                patch.floatValue = property.floatValue;
            }
            else if (property.propertyType == SerializedPropertyType.Boolean)
            {
                patch.valueType = "bool";
                patch.boolValue = property.boolValue;
            }
            else if (property.propertyType == SerializedPropertyType.Integer ||
                     property.propertyType == SerializedPropertyType.Enum)
            {
                patch.valueType = property.propertyType == SerializedPropertyType.Enum ? "enum" : "int";
                patch.intValue = property.intValue;
            }
            else if (property.isArray && property.propertyType == SerializedPropertyType.Generic &&
                     property.arrayElementType == "float")
            {
                patch.valueType = "floatArray";
                patch.floatArray = new float[property.arraySize];
                for (int i = 0; i < property.arraySize; i++)
                    patch.floatArray[i] = property.GetArrayElementAtIndex(i).floatValue;
            }
            else throw new InvalidOperationException("Nicht unterstützter GPS-Feldtyp: " + property.propertyType);

            var request = LoadPending();
            var patches = new List<ComponentPatch>(request.componentPatches ?? Array.Empty<ComponentPatch>());
            int existing = patches.FindIndex(item => item.objectId == patch.objectId && item.propertyPath == patch.propertyPath);
            if (existing >= 0) patches[existing] = patch;
            else patches.Add(patch);
            request.componentPatches = patches.ToArray();
            Store(request);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ApplyPending()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        string json = SessionState.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var request = JsonUtility.FromJson<Request>(json);
            if (request == null) throw new InvalidOperationException("Ungültiger Speicherauftrag.");

            var dirtyScenes = new HashSet<Scene>();
            var dirtyAssets = new HashSet<UnityEngine.Object>();
            if (request.hasGameplayValues)
                ApplyGameplayValues(request, dirtyScenes, dirtyAssets);
            foreach (var patch in request.componentPatches ?? Array.Empty<ComponentPatch>())
                ApplyComponentPatch(patch, dirtyScenes, dirtyAssets);

            foreach (var scene in dirtyScenes)
            {
                if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || !EditorSceneManager.SaveScene(scene))
                    throw new IOException("Eine Map-Szene konnte nicht gespeichert werden.");
            }
            foreach (var asset in dirtyAssets)
                if (asset) AssetDatabase.SaveAssetIfDirty(asset);

            // Remove the old debug override only when its GPS equivalents were saved
            // and the file has not changed since the request was queued.
            if (request.hasSpeed && request.hasLighting && File.Exists(GameplaySettings.FilePath) &&
                File.ReadAllText(GameplaySettings.FilePath) == request.previousJson)
                File.Move(GameplaySettings.FilePath,
                    GameplaySettings.FilePath + ".defaults-" + Guid.NewGuid().ToString("N") + ".bak");

            SessionState.EraseString(PendingKey);
            Debug.Log("Debugwerte wurden als GPS-Standard in Szene und Assets gespeichert.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Debugwerte nicht vollständig als GPS-Standard gespeichert; Auftrag bleibt vorgemerkt: " + ex.Message);
            return false;
        }
    }

    static Request LoadPending()
    {
        string json = SessionState.GetString(PendingKey, "");
        var request = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Request>(json);
        if (request == null) request = new Request();
        if (request.componentPatches == null) request.componentPatches = Array.Empty<ComponentPatch>();
        return request;
    }

    static void Store(Request request) => SessionState.SetString(PendingKey, JsonUtility.ToJson(request));

    static void ApplyGameplayValues(Request request, HashSet<Scene> dirtyScenes,
        HashSet<UnityEngine.Object> dirtyAssets)
    {
        if (request.hasSpeed)
        {
            if (!GameplaySettingsStore.IsValidSpeed(request.speed))
                throw new InvalidOperationException("Ungültige vorgemerkte Abbaugeschwindigkeit.");
            var stats = AssetDatabase.LoadAssetAtPath<PlayerBaseStats>(request.statsPath);
            if (!stats) throw new InvalidOperationException("Spieler-Basiswerte-Asset ist nicht verfügbar.");
            Undo.RecordObject(stats, "Debugwert als GPS-Standard festlegen");
            stats.miningSpeed = request.speed;
            EditorUtility.SetDirty(stats);
            dirtyAssets.Add(stats);
        }
        if (request.hasLighting)
        {
            if (!GlobalObjectId.TryParse(request.lightingId, out var id) ||
                request.lighting == null || !request.lighting.IsValid)
                throw new InvalidOperationException("Ungültige vorgemerkte Map-Beleuchtung.");
            var light = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as MapLighting;
            if (!light) throw new InvalidOperationException("Map-Beleuchtung ist nicht verfügbar.");
            Undo.RecordObject(light, "Debugwert als GPS-Standard festlegen");
            var value = request.lighting;
            light.lightingEnabled = value.enabled;
            light.daylightStrength = value.daylight;
            light.ambientBrightness = value.ambient;
            light.downwardLoss = value.downLoss;
            light.sidewaysLoss = value.sideLoss;
            light.blockLoss = value.blockLoss;
            light.exponentialStrength = value.strength;
            EditorUtility.SetDirty(light);
            dirtyScenes.Add(light.gameObject.scene);
        }
    }

    static void ApplyComponentPatch(ComponentPatch patch, HashSet<Scene> dirtyScenes,
        HashSet<UnityEngine.Object> dirtyAssets)
    {
        if (patch == null)
            throw new InvalidOperationException("Ungültige Komponentenreferenz.");
        Component component = null;
        if (GlobalObjectId.TryParse(patch.objectId, out var id))
            component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Component;
        if (!component) component = ResolveSceneComponent(patch);
        if (!component) throw new InvalidOperationException("Komponente nicht verfügbar: " + patch.propertyPath);

        var serialized = new SerializedObject(component);
        var property = serialized.FindProperty(patch.propertyPath);
        if (property == null) throw new InvalidOperationException("GPS-Feld nicht verfügbar: " + patch.propertyPath);
        Undo.RecordObject(component, "Debugwert als GPS-Standard festlegen");
        switch (patch.valueType)
        {
            case "float": property.floatValue = patch.floatValue; break;
            case "bool": property.boolValue = patch.boolValue; break;
            case "int":
            case "enum": property.intValue = patch.intValue; break;
            case "floatArray":
                var values = patch.floatArray ?? Array.Empty<float>();
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).floatValue = values[i];
                break;
            default: throw new InvalidOperationException("Unbekannter GPS-Feldtyp: " + patch.valueType);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorUtility.SetDirty(component);
        if (component.gameObject.scene.IsValid()) dirtyScenes.Add(component.gameObject.scene);
        else dirtyAssets.Add(component);
    }

    static string[] GetHierarchyNames(Transform transform)
    {
        var names = new Stack<string>();
        while (transform)
        {
            names.Push(transform.name);
            transform = transform.parent;
        }
        return names.ToArray();
    }

    static Component ResolveSceneComponent(ComponentPatch patch)
    {
        var scene = SceneManager.GetSceneByPath(patch.scenePath);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        var path = patch.hierarchyNames ?? Array.Empty<string>();
        if (path.Length == 0) return null;

        IEnumerable<Transform> candidates = scene.GetRootGameObjects()
            .Where(root => root.name == path[0]).Select(root => root.transform);
        for (int i = 1; i < path.Length; i++)
        {
            string name = path[i];
            candidates = candidates.SelectMany(parent => Enumerable.Range(0, parent.childCount)
                .Select(parent.GetChild).Where(child => child.name == name));
        }

        var componentType = Type.GetType(patch.componentType, false);
        foreach (var candidate in candidates)
        {
            var matching = candidate.GetComponents<Component>().Where(component => component &&
                (componentType != null ? component.GetType() == componentType :
                    component.GetType().FullName == patch.componentType)).ToArray();
            if (patch.componentIndex >= 0 && patch.componentIndex < matching.Length)
                return matching[patch.componentIndex];
            if (matching.Length > 0) return matching[0];
        }
        return null;
    }
}
#endif
