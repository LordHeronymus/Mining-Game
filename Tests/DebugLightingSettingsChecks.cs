using System;
using System.IO;
using UnityEngine;

public static class DebugLightingSettingsChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static object Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "mining-light-settings-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"version\":1,\"baseDiggingSpeed\":7}");
            var legacy = GameplaySettingsStore.Load(path, 2, out string error);
            Check(error == null && legacy.baseDiggingSpeed == 7 && !legacy.hasLightingOverride,
                "Existing speed-only JSON must retain scene lighting.");
            var data = new GameplaySettingsData {
                baseDiggingSpeed = 8, hasLightingOverride = true,
                lighting = new LightingSettingsData { enabled = false, daylight = 0.8f, ambient = 0,
                    downLoss = 0.007f, sideLoss = 0.12f, blockLoss = 0.3f, strength = 2.5f }
            };
            Check(GameplaySettingsStore.Save(path, data, out error), error);
            var loaded = GameplaySettingsStore.Load(path, 2, out error);
            Check(error == null && JsonUtility.ToJson(data) == JsonUtility.ToJson(loaded), "Lighting JSON round trip failed.");
            string valid = File.ReadAllText(path);
            data.lighting.strength = float.NaN;
            Check(!GameplaySettingsStore.Save(path, data, out error) && File.ReadAllText(path) == valid,
                "Invalid values must not replace the saved settings.");
            File.WriteAllText(path, valid.Replace("2.5", "9.5"));
            var fallback = GameplaySettingsStore.Load(path, 2, out error);
            Check(error != null && !fallback.hasLightingOverride && fallback.baseDiggingSpeed == 2,
                "Invalid loaded lighting must fall back safely.");
            return new { passed = true, legacyCompatible = true, roundTrip = true, invalidValuesRejected = true };
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
