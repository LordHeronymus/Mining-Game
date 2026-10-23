using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

// Dynamic TMP fonts (TitleFont, LiberationSans fallback) add every glyph shown in Play to their versioned atlas and mark
// it dirty; the next SaveAssets (AutoSave, Ctrl+S, quitting) would write a session-dependent atlas. After Play the
// glyphs stay in memory, but fonts that were clean before Play are clean again. Builds clear dynamic data anyway.
[InitializeOnLoad]
public static class DynamicFontPlayGuard
{
    const string DirtyBeforePlayKey = "DynamicFontPlayGuard.DirtyBeforePlay";

    static DynamicFontPlayGuard() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // Unsaved edits made before Play are kept; SessionState survives the domain reload of Play.
        if (state == PlayModeStateChange.ExitingEditMode)
            SessionState.SetString(DirtyBeforePlayKey, string.Join("\n", DynamicFontPaths().Where(IsDirty)));
        if (state != PlayModeStateChange.EnteredEditMode) return;
        var keep = new HashSet<string>(SessionState.GetString(DirtyBeforePlayKey, "").Split('\n'));
        SessionState.EraseString(DirtyBeforePlayKey);
        foreach (string path in DynamicFontPaths().Where(path => !keep.Contains(path)))
            if (ForgetChanges(path)) Debug.Log("[DynamicFontPlayGuard] Im Play ergänzte Zeichen werden nicht gespeichert: " + path);
    }

    // Loaded persistent fonts only: a font that was never loaded cannot have picked up glyphs.
    internal static IEnumerable<string> DynamicFontPaths() => Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
        .Where(font => font && font.atlasPopulationMode != AtlasPopulationMode.Static && EditorUtility.IsPersistent(font))
        .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct();

    // Atlas textures and the material are sub-assets of the font file; any dirty one would write the whole file.
    internal static bool IsDirty(string path) => AssetDatabase.LoadAllAssetsAtPath(path).Any(item => item && EditorUtility.IsDirty(item));

    internal static bool ForgetChanges(string path)
    {
        bool dirty = false;
        foreach (var item in AssetDatabase.LoadAllAssetsAtPath(path))
            if (item && EditorUtility.IsDirty(item)) { EditorUtility.ClearDirty(item); dirty = true; }
        return dirty;
    }
}
