using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class SetupArtifactDiscovery
{
    public static string Run()
    {
        const string target = "Assets/Resources/ArtifactDiscovery/TitleFont.asset";
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target);
        if (!asset)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/ArtifactDiscovery/EBGaramond.ttf");
            asset = TMP_FontAsset.CreateFontAsset(font, 100, 12, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            if (!asset) throw new System.Exception("Unable to create the discovery font.");
            asset.name = "Artifact Discovery Garamond";
            AssetDatabase.CreateAsset(asset, target);
            asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789ÄÖÜäöüß -");
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
        return "Artifact discovery font ready; no scene changes required.";
    }
}
