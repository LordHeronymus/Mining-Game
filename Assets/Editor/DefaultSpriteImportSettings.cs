using UnityEditor;

public sealed class DefaultSpriteImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        // Apply once so later manual choices (including sprite sheets) survive reimports.
        if (!assetImporter.importSettingsMissing) return;
        ((TextureImporter)assetImporter).spriteImportMode = SpriteImportMode.Single;
    }
}
