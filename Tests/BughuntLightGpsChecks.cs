using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Checks for the F1 lighting fix from 23.10.2026: GpsSettings writes the MapLighting fields before
// it notifies, so MapLighting must compare against the values its daylight field was built with and
// rebuild once on a real change (daylight, losses, strength, ambient), but not for the on/off switch,
// Ultronium colours or unrelated notifications.
public static class BughuntLightGpsChecks
{
    const string Report = "Temp/BughuntLightGpsChecks.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        File.AppendAllText(Report, "PASS " + label + "\n");
    }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "");
        var root = new GameObject("Bughunt light GPS grid") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            root.AddComponent<Grid>().cellSize = new Vector3(.5f, .5f, 0f);
            var mapObject = new GameObject("Bughunt light GPS map") { hideFlags = HideFlags.HideAndDontSave };
            mapObject.transform.SetParent(root.transform, false);
            mapObject.AddComponent<Tilemap>();
            mapObject.AddComponent<MapGenerator>();
            var lighting = mapObject.AddComponent<MapLighting>();
            Run(lighting);
            File.AppendAllText(Report, "PASS all checks\n");
            return new { passed = true };
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error.Message + "\n");
            throw;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void Run(MapLighting lighting)
    {
        var type = typeof(MapLighting);
        var rebuild = type.GetField("rebuild", Private);
        var built = type.GetField("builtSettings", Private);
        var shader = type.GetField("darknessShader", Private);
        var map = type.GetField("map", Private);
        var apply = type.GetMethod("ApplyDebugSettings", Private);
        var steps = type.GetMethod("InitializeSteps", Private);
        Check(rebuild != null && built != null && shader != null && map != null && apply != null && steps != null,
            "MapLighting remembers the settings its daylight field was built with");
        var record = GpsSettings.Document.records.Find(item => item.type == type.AssemblyQualifiedName);
        Check(record != null, "GPS document has a lighting record");

        // Only the in-memory lighting nodes are swapped; the profile asset and scene stay untouched.
        var original = record.fields.ToArray();
        try
        {
            var start = GameplaySettings.Lighting;
            Check(start != null && start.IsValid, "GPS document provides valid lighting values");
            Assign(lighting, start);

            // Building the field records the values it uses (the first step runs before any work is spread out).
            map.SetValue(lighting, lighting.GetComponent<MapGenerator>());
            shader.SetValue(lighting, Shader.Find("Hidden/InternalErrorShader"));
            var initialization = (System.Collections.IEnumerator)steps.Invoke(lighting, new object[] { 1, 1, new TileBase[1], null });
            initialization.MoveNext();
            var recorded = built.GetValue(lighting) as LightingSettingsData;
            Check(recorded != null && JsonUtility.ToJson(recorded) == JsonUtility.ToJson(start),
                "Initialize records the lighting values of the built field");

            rebuild.SetValue(lighting, false);
            apply.Invoke(lighting, null);
            Check(!(bool)rebuild.GetValue(lighting), "A notification without a lighting change does not rebuild");

            foreach (var name in new[] { "daylight", "ambient", "downLoss", "sideLoss", "blockLoss", "strength" })
            {
                var changed = start.Copy();
                var member = typeof(LightingSettingsData).GetField(name);
                float value = (float)member.GetValue(changed);
                member.SetValue(changed, value > .5f ? value * .5f : value + .05f);
                // GpsSettings.ApplyField order: document and component hold the new value, then NotifyChanged.
                SetDocument(record, changed);
                Assign(lighting, changed);
                rebuild.SetValue(lighting, false);
                apply.Invoke(lighting, null);
                Check((bool)rebuild.GetValue(lighting), "F1 change of " + name + " rebuilds the light field");
                built.SetValue(lighting, changed.Copy());
                rebuild.SetValue(lighting, false);
                apply.Invoke(lighting, null);
                Check(!(bool)rebuild.GetValue(lighting), "After the rebuild, " + name + " does not rebuild again");
                SetDocument(record, start);
                Assign(lighting, start);
                built.SetValue(lighting, start.Copy());
            }

            // A document change the component has not seen yet (GPS reload) is copied over and rebuilds.
            var reloaded = start.Copy();
            reloaded.daylight = start.daylight > .5f ? start.daylight * .5f : start.daylight + .05f;
            SetDocument(record, reloaded);
            rebuild.SetValue(lighting, false);
            apply.Invoke(lighting, null);
            Check(lighting.daylightStrength == reloaded.daylight && (bool)rebuild.GetValue(lighting),
                "A changed GPS document is applied to the component and rebuilds");
            SetDocument(record, start);
            Assign(lighting, start);
            built.SetValue(lighting, start.Copy());

            var toggled = start.Copy();
            toggled.enabled = !start.enabled;
            SetDocument(record, toggled);
            Assign(lighting, toggled);
            rebuild.SetValue(lighting, false);
            apply.Invoke(lighting, null);
            Check(!(bool)rebuild.GetValue(lighting), "Switching lighting on or off keeps the light field");
            SetDocument(record, start);
            Assign(lighting, start);

            lighting.ultroniumLightColor = Color.green;
            lighting.ultroniumLightIntensity = .5f;
            lighting.ultroniumPulsesPerMinute = 90;
            rebuild.SetValue(lighting, false);
            apply.Invoke(lighting, null);
            Check(!(bool)rebuild.GetValue(lighting), "Ultronium colour, intensity and pulses keep the light field");
        }
        finally
        {
            for (int i = 0; i < original.Length && i < record.fields.Count; i++) record.fields[i] = original[i];
        }
        bool restored = record.fields.Count == original.Length;
        for (int i = 0; restored && i < original.Length; i++) restored = ReferenceEquals(record.fields[i], original[i]);
        Check(restored, "GPS lighting record restored");
    }

    static void SetDocument(GpsRecord record, LightingSettingsData value)
    {
        for (int i = 0; i < record.fields.Count; i++)
        {
            var node = record.fields[i].Copy();
            switch (node.name)
            {
                case "lightingEnabled": node.flag = value.enabled; break;
                case "daylightStrength": node.number = value.daylight; break;
                case "ambientBrightness": node.number = value.ambient; break;
                case "downwardLoss": node.number = value.downLoss; break;
                case "sidewaysLoss": node.number = value.sideLoss; break;
                case "blockLoss": node.number = value.blockLoss; break;
                case "exponentialStrength": node.number = value.strength; break;
                default: continue;
            }
            record.fields[i] = node;
        }
    }

    static void Assign(MapLighting lighting, LightingSettingsData value)
    {
        lighting.lightingEnabled = value.enabled;
        lighting.daylightStrength = value.daylight;
        lighting.ambientBrightness = value.ambient;
        lighting.downwardLoss = value.downLoss;
        lighting.sidewaysLoss = value.sideLoss;
        lighting.blockLoss = value.blockLoss;
        lighting.exponentialStrength = value.strength;
    }
}
