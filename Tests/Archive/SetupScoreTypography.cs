using System;
using TMPro;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public static class SetupScoreTypography
{
    public static string Main()
    {
        if (Application.isPlaying) throw new Exception("Use Edit Mode");
        const string folder = "Assets/GameObjects/UI/Numbers/Fonts/";
        const string assetPath = folder + "BreeSerif Score SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(folder + "BreeSerif-Regular.ttf");
            font = TMP_FontAsset.CreateFontAsset(source, 96, 12, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic);
            font.name = "Bree Serif Score SDF";
            if (!font.TryAddCharacters("+0123456789", out string missing))
                throw new Exception("Missing glyphs: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, assetPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        }
        const string prefabPath = "Assets/GameObjects/UI/Numbers/Number.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var text = root.GetComponentInChildren<TMP_Text>();
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontStyle = FontStyles.Normal;
            text.extraPadding = true;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(180, 40);
            var canvas = root.GetComponentInChildren<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerName = "UI";
            canvas.sortingOrder = 10;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var spawner = UnityEngine.Object.FindFirstObjectByType<NumnberSpawner>();
        var config = new SerializedObject(spawner);
        config.FindProperty("fontSize").floatValue = 10.5f;
        config.FindProperty("duration").floatValue = 2f;
        config.FindProperty("accumulationRadius").floatValue = 2.5f;
        config.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        EditorSceneManager.SaveScene(spawner.gameObject.scene);
        return "Bree Serif imported with static score glyphs; prefab and animation settings saved.";
    }
}
