#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// SessionState survives the domain reload when leaving Play mode.
[InitializeOnLoad]
public static class GameplayDebugDefaults
{
    const string PendingKey = "MiningGame.DebugDefaults.Pending";
    [Serializable]
    class Request
    {
        public string statsPath, lightingId, previousJson;
        public float speed;
        public LightingSettingsData lighting;
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
            var stats = UnityEngine.Object.FindFirstObjectByType<StatsManager>();
            var lights = UnityEngine.Object.FindObjectsByType<MapLighting>(FindObjectsSortMode.None);
            if (!stats || lights.Length != 1 || !GameplaySettings.LightingAvailable)
                throw new InvalidOperationException("Genau eine Map mit Licht und ein StatsManager müssen aktiv sein.");
            var asset = new SerializedObject(stats).FindProperty("baseStats").objectReferenceValue;
            string path = AssetDatabase.GetAssetPath(asset);
            var id = GlobalObjectId.GetGlobalObjectIdSlow(lights[0]);
            if (string.IsNullOrEmpty(path) || id.targetObjectId == 0 || string.IsNullOrEmpty(lights[0].gameObject.scene.path))
                throw new InvalidOperationException("Standardwerte benötigen ein gespeichertes Stats-Asset und eine gespeicherte Map-Szene.");
            var request = new Request {
                statsPath = path, lightingId = id.ToString(),
                speed = GameplaySettings.BaseDiggingSpeed, lighting = GameplaySettings.Lighting,
                previousJson = File.Exists(GameplaySettings.FilePath) ? File.ReadAllText(GameplaySettings.FilePath) : null
            };
            if (!GameplaySettingsStore.IsValidSpeed(request.speed) || !request.lighting.IsValid)
                throw new InvalidOperationException("Ungültige Einstellungen.");
            SessionState.SetString(PendingKey, JsonUtility.ToJson(request));
            message = "Als Standard vorgemerkt. Wird beim Beenden des Play-Modus gespeichert.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Standardwerte konnten nicht übernommen werden: " + ex.Message;
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
            var stats = AssetDatabase.LoadAssetAtPath<PlayerBaseStats>(request.statsPath);
            if (!GlobalObjectId.TryParse(request.lightingId, out var id))
                throw new InvalidOperationException("Ungültige Map-Referenz.");
            var light = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as MapLighting;
            if (!stats || !light) throw new InvalidOperationException("Ziel-Asset oder Map-Szene ist nicht verfügbar.");
            if (!GameplaySettingsStore.IsValidSpeed(request.speed) || request.lighting == null || !request.lighting.IsValid)
                throw new InvalidOperationException("Ungültige vorgemerkte Werte.");
            Undo.RecordObjects(new UnityEngine.Object[] { stats, light }, "Debugwerte als Standard festlegen");
            stats.miningSpeed = request.speed;
            var value = request.lighting;
            light.lightingEnabled = value.enabled;
            light.daylightStrength = value.daylight;
            light.ambientBrightness = value.ambient;
            light.downwardLoss = value.downLoss;
            light.sidewaysLoss = value.sideLoss;
            light.blockLoss = value.blockLoss;
            light.exponentialStrength = value.strength;
            EditorUtility.SetDirty(stats);
            EditorUtility.SetDirty(light);
            EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
            if (!EditorSceneManager.SaveScene(light.gameObject.scene))
                throw new IOException("Map-Szene konnte nicht gespeichert werden.");
            AssetDatabase.SaveAssetIfDirty(stats);
            // Remove the old debug override so future base edits are not shadowed.
            // Preserve a newer JSON saved after the user pressed this button.
            string path = GameplaySettings.FilePath;
            if (File.Exists(path) && File.ReadAllText(path) == request.previousJson)
                File.Move(path, path + ".defaults-" + Guid.NewGuid().ToString("N") + ".bak");
            SessionState.EraseString(PendingKey);
            Debug.Log("Abbau- und Lichtwerte als Standard in Asset und Szene gespeichert.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Standardwerte nicht vollständig gespeichert; Auftrag bleibt vorgemerkt: " + ex.Message);
            return false;
        }
    }
}
#endif
