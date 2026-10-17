using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupSkySwitch
{
    public static object Verify()
    {
        var sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
        CheckCycle();
        CheckDifferentSprites();
        return Check(sky);
    }

    static void CheckCycle()
    {
        var root = new GameObject("Sky cycle test");
        try
        {
            var sky = root.AddComponent<SkyController>();
            sky.DayDuration = 10f; sky.NightDuration = 5f; sky.SkyFadeDuration = 2f;
            sky.AdvanceTime(10f);
            Assert(sky.IsNight && sky.NightBlend == 0f, "Day duration");
            sky.AdvanceTime(1f);
            Assert(sky.NightBlend == 0.5f, "Automatic fade midpoint");
            sky.AdvanceTime(1f);
            Assert(sky.NightBlend == 1f, "Automatic night endpoint");
            sky.AdvanceTime(5f);
            Assert(!sky.IsNight && sky.NightBlend == 1f, "Night duration excludes fade");
            sky.AdvanceTime(2f);
            Assert(sky.NightBlend == 0f, "Automatic day endpoint");
            sky.AdvanceTime(19f * 100f + 11f);
            Assert(sky.IsNight && sky.NightBlend == 0.5f, "Large time step");
            sky.SetNight(false); sky.AdvanceTime(1f);
            Assert(sky.NightBlend == 0f, "Manual reversal");
            sky.automaticCycle = false; sky.AdvanceTime(100f);
            Assert(!sky.IsNight && sky.NightBlend == 0f, "Disabled automatic cycle");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void CheckDifferentSprites()
    {
        var texture = new Texture2D(128, 128);
        var daySprite = Sprite.Create(texture, new Rect(0, 0, 120, 80), new Vector2(0.2f, 0.8f), 10f);
        var nightSprite = Sprite.Create(texture, new Rect(0, 0, 30, 25), new Vector2(0.7f, 0.1f), 35f);
        var root = new GameObject("Sky rectangle test");
        try
        {
            root.transform.position = new Vector3(-17f, 23f, 0f);
            root.transform.localScale = new Vector3(2f, 3f, 1f);
            var day = root.AddComponent<SpriteRenderer>(); day.sprite = daySprite;
            var child = new GameObject("Night"); child.transform.SetParent(root.transform, false);
            var night = child.AddComponent<SpriteRenderer>(); night.sprite = nightSprite;
            SkyController.MatchRectangle(day, night);
            Assert(Vector3.Distance(day.bounds.min, night.bounds.min) < 0.001f &&
                Vector3.Distance(day.bounds.max, night.bounds.max) < 0.001f,
                "Different resolution, PPU, pivot, aspect ratio and parent scale");
            Vector3 center = night.bounds.center;
            Vector3 size = night.bounds.size;
            SkyController.ScaleAroundCenter(night, 1.5f);
            Assert(Vector3.Distance(night.bounds.center, center) < 0.001f &&
                Vector3.Distance(night.bounds.size, Vector3.Scale(size, new Vector3(1.5f, 1.5f, 1f))) < 0.001f,
                "Independent night scale preserves center with arbitrary pivots");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(daySprite);
            UnityEngine.Object.DestroyImmediate(nightSprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before setup.");
        const string source = "Assets/ZZZ New Assets/Nachthimmel_Mond_4500x3000.png";
        const string destination = "Assets/AB Sprites/Parralax BG/Sky_Night.png";
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(destination))
        {
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new Exception(error);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(destination);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        var layer = UnityEngine.Object.FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None)
            .Single(x => x.name == "Sky");
        var sky = layer.GetComponent<SkyController>();
        if (!sky) sky = Undo.AddComponent<SkyController>(layer.gameObject);
        Undo.RecordObject(sky, "Configure sky switch");
        sky.nightSky = AssetDatabase.LoadAssetAtPath<Sprite>(destination);
        sky.Refresh();
        EditorUtility.SetDirty(sky);
        EditorSceneManager.MarkSceneDirty(layer.gameObject.scene);
        EditorSceneManager.SaveScene(layer.gameObject.scene);
        return new { day = layer.segments[0].rect.ToString(), dayPPU = layer.segments[0].pixelsPerUnit,
            night = sky.nightSky.rect.ToString(), nightPPU = sky.nightSky.pixelsPerUnit, checks = Check(sky) };
    }

    static string Check(SkyController sky)
    {
        var layer = sky.GetComponent<ParallaxLayer>();
        var camera = Camera.main;
        Vector3 position = camera.transform.position;
        bool state = sky.IsNight;
        try
        {
            sky.SetNight(false); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh();
            var ids = layer.Renderers.Where(x => x && x.enabled)
                .Select(x => x.transform.GetChild(0).GetInstanceID()).ToArray();
            sky.SetNight(true); sky.AdvanceFade(sky.SkyFadeDuration * 0.5f); sky.Refresh();
            Assert(Mathf.Abs(sky.NightBlend - 0.5f) < 0.0001f, "Midpoint");
            CheckBounds(layer);
            sky.SetNight(false); sky.AdvanceFade(sky.SkyFadeDuration * 0.25f); sky.Refresh();
            Assert(Mathf.Abs(sky.NightBlend - 0.25f) < 0.0001f, "Reversal");
            sky.SetNight(true); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh();
            Assert(sky.NightBlend == 1f, "Night endpoint");
            Assert(ids.SequenceEqual(layer.Renderers.Where(x => x && x.enabled)
                .Select(x => x.transform.GetChild(0).GetInstanceID())), "Persistent renderers");
            camera.transform.position += new Vector3(40f, -12f, 0f); sky.Refresh(); CheckBounds(layer);
            sky.SetNight(false); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh();
            Assert(sky.NightBlend == 0f, "Day endpoint");
            return "Passed: bounds, alpha, reversal, endpoints, movement, persistent renderers";
        }
        finally { camera.transform.position = position; sky.SetNight(state); sky.AdvanceFade(sky.SkyFadeDuration); sky.Refresh(); }
    }

    static void CheckBounds(ParallaxLayer layer)
    {
        int count = 0;
        foreach (var day in layer.Renderers.Where(x => x && x.enabled))
        {
            var night = day.transform.GetChild(0).GetComponent<SpriteRenderer>();
            Assert(Vector3.Distance(day.bounds.min, night.bounds.min) < 0.001f &&
                Vector3.Distance(day.bounds.max, night.bounds.max) < 0.001f, "World rectangle");
            var background = layer.GetComponentInParent<SurfaceBackgroundController>();
            float alpha = layer.tint.a * layer.opacity * background.GetOpacity(background.RenderCamera);
            float blend = layer.GetComponent<SkyController>().NightBlend;
            Assert(Mathf.Abs(day.color.a - alpha * (1f - blend)) < 0.0001f &&
                Mathf.Abs(night.color.a - alpha * blend) < 0.0001f, "Complementary alpha");
            count++;
        }
        Assert(count > 0, "Sky renderers exist");
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
