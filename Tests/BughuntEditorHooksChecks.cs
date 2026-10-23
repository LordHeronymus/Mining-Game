using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Editor hooks in the user's folder: the moonlight must not adopt its own moon colour as day colour, the star preview
// must not force the Edit Mode player loop by day, and glyphs a dynamic TMP font picks up in Play must not be saved.
// Uses temporary hidden objects only; the font's dirty state and the guard's session key are restored.
public static class BughuntEditorHooksChecks
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Report = "Temp/BughuntEditorHooksChecks.txt";
    const string TitleFontPath = "Assets/Resources/ArtifactDiscovery/TitleFont.asset";
    const string GuardKey = "DynamicFontPlayGuard.DirtyBeforePlay";
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }
    static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f;

    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear(); Directory.CreateDirectory("Temp");
        try
        {
            CheckMoonlight();
            CheckStarPreview();
            CheckFontGuard();
            File.WriteAllText(Report, string.Join("\n", checks) + "\n");
            return checks.Count + " checks passed";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
            File.WriteAllText(Report, string.Join("\n", checks) + "\nFAIL " + inner.Message + "\n");
            throw;
        }
    }

    static void CheckMoonlight()
    {
        GameObject root = null;
        try
        {
            root = new GameObject("BughuntEditorHooksMoon") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var light = new GameObject("Daylight") { hideFlags = HideFlags.HideAndDontSave };
            light.transform.SetParent(root.transform, false);
            var daylight = light.AddComponent<Light2D>();
            var moon = root.AddComponent<MoonlightController>();
            moon.daylight = daylight;
            var dayColor = typeof(MoonlightController).GetField("dayColor");
            Check(dayColor != null && dayColor.FieldType == typeof(Color), "MoonlightController führt die Tagfarbe als eigenes Feld");
            Check(Same((Color)dayColor.GetValue(moon), Color.white), "Tagfarbe ist standardmässig weiss wie das Tageslicht der SampleScene");
            // State after a night preview in Edit Mode, saved or carried into Play: the light holds the moon colour.
            daylight.color = moon.moonColor;
            root.SetActive(true);
            moon.enabled = false;
            Check(Same(daylight.color, Color.white), "Mondfarbe im Licht wird beim Aktivieren nicht zur Tagfarbe");
            daylight.color = moon.moonColor;
            moon.enabled = true; moon.enabled = false;
            Check(Same(daylight.color, Color.white), "Auch nach erneutem Aktivieren kehrt das Licht zu Weiss zurück");
            dayColor.SetValue(moon, new Color(1f, .9f, .8f));
            moon.enabled = true; moon.enabled = false;
            Check(Same(daylight.color, new Color(1f, .9f, .8f)), "Eingestellte Tagfarbe wird verwendet");
        }
        finally { if (root) Object.DestroyImmediate(root); }
    }

    static void CheckStarPreview()
    {
        GameObject root = null; Material material = null;
        try
        {
            root = new GameObject("BughuntEditorHooksStars") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var stars = root.AddComponent<NightSkyStars>();
            var sky = root.GetComponent<SkyController>();
            Check(sky && root.GetComponent<ParallaxLayer>(), "Sternenobjekt mit Himmel und Ebene angelegt");
            material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            stars.material = material;
            root.SetActive(true);
            var animates = typeof(NightSkyStars).GetProperty("PreviewAnimates", Any);
            Check(animates != null, "NightSkyStars prüft, ob die Vorschau laufen muss");
            var update = typeof(SkyController).GetMethod("Update", Any);
            var preview = typeof(NightSkyStars).GetMethod("UpdatePreview", Any);
            var next = typeof(NightSkyStars).GetField("nextPreviewFrame", Any);
            Func<bool> Animates = () => (bool)animates.GetValue(stars);

            sky.SetNight(false); update.Invoke(sky, null);
            Check(sky.NightBlend == 0f && !Animates(), "Tagsüber kein erzwungener Edit-Mode-Player-Loop");
            next.SetValue(stars, 0d); preview.Invoke(stars, null);
            Check((double)next.GetValue(stars) == 0d, "Vorschau-Takt bleibt am Tag stehen");
            sky.SetNight(true);
            Check(Animates(), "Nacht startet die Vorschau sofort");
            preview.Invoke(stars, null);
            Check((double)next.GetValue(stars) > 0d, "Vorschau-Takt läuft bei Nacht");
            update.Invoke(sky, null);
            sky.SetNight(false);
            Check(sky.NightBlend > 0f && Animates(), "Vorschau läuft weiter, bis die Nachtkopie ausgeblendet ist");
            update.Invoke(sky, null);
            Check(!Animates(), "Danach hält die Vorschau wieder an");
            sky.SetNight(true); stars.material = null;
            Check(!Animates(), "Ohne Material keine Vorschau");
            stars.material = material; stars.enabled = false;
            Check(!Animates(), "Deaktivierte Sterne erzwingen keine Vorschau");
        }
        finally
        {
            if (root) Object.DestroyImmediate(root);
            if (material) Object.DestroyImmediate(material);
        }
    }

    static void CheckFontGuard()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
        Check(font && font.atlasPopulationMode != AtlasPopulationMode.Static, "TitleFont ist eine dynamische TMP-Schrift");
        var guard = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("DynamicFontPlayGuard")).FirstOrDefault(type => type != null);
        Check(guard != null, "Editor-Hook für dynamische Schriften vorhanden");
        var handler = guard.GetMethod("OnPlayModeChanged", Any);
        Check(handler != null, "Hook reagiert auf Play-Wechsel");
        Action<PlayModeStateChange> Raise = state => handler.Invoke(null, new object[] { state });
        Func<bool> Dirty = () => AssetDatabase.LoadAllAssetsAtPath(TitleFontPath).Any(item => item && EditorUtility.IsDirty(item));
        var dirtyBefore = AssetDatabase.LoadAllAssetsAtPath(TitleFontPath).Where(item => item && EditorUtility.IsDirty(item)).ToList();
        string savedKey = SessionState.GetString(GuardKey, null);
        try
        {
            foreach (var item in dirtyBefore) EditorUtility.ClearDirty(item);
            Check(((IEnumerable<string>)guard.GetMethod("DynamicFontPaths", Any).Invoke(null, null)).Contains(TitleFontPath),
                "Geladene TitleFont wird überwacht");
            // TMP marks the font dirty when Play adds glyphs; afterwards nothing may be left for SaveAssets to write.
            Raise(PlayModeStateChange.ExitingEditMode);
            EditorUtility.SetDirty(font);
            if (font.atlasTexture) EditorUtility.SetDirty(font.atlasTexture);
            Raise(PlayModeStateChange.EnteredEditMode);
            Check(!Dirty(), "Im Play ergänzte Atlasdaten werden nicht gespeichert");
            // Unsaved edits from before Play are not discarded.
            EditorUtility.SetDirty(font);
            Raise(PlayModeStateChange.ExitingEditMode);
            Raise(PlayModeStateChange.EnteredEditMode);
            Check(EditorUtility.IsDirty(font), "Ungespeicherte Änderungen von vor dem Play bleiben erhalten");
        }
        finally
        {
            foreach (var item in AssetDatabase.LoadAllAssetsAtPath(TitleFontPath)) if (item) EditorUtility.ClearDirty(item);
            foreach (var item in dirtyBefore) if (item) EditorUtility.SetDirty(item);
            if (savedKey == null) SessionState.EraseString(GuardKey); else SessionState.SetString(GuardKey, savedKey);
        }
    }
}
