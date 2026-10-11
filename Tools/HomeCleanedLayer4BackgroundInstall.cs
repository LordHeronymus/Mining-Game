using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class HomeCleanedLayer4BackgroundInstall
{
    static string Hash(string path) {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    public static object Main()
    {
        const string source = "Design/Tiefenhall/HomeBackgroundCleanup/Layer4-approved-x4.png";
        const string asset = "Assets/Resources/Homescreen/Layer4.png";
        const string backup = "Design/Tiefenhall/HomeBackgroundCleanup/Layer4-before-install.png";
        if (!File.Exists(source)) throw new Exception("Missing approved master");
        string guid = AssetDatabase.AssetPathToGUID(asset);
        if (!File.Exists(backup)) {
            File.Copy(asset, backup);
            File.Copy(asset + ".meta", backup + ".meta.txt");
        }
        File.Copy(source, asset, true);
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
        importer.maxTextureSize = 8192;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.mipmapEnabled = false;
        importer.streamingMipmaps = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.sRGBTexture = true;
        var defaults = importer.GetDefaultPlatformTextureSettings();
        defaults.maxTextureSize = 8192;
        defaults.textureCompression = TextureImporterCompression.Uncompressed;
        defaults.crunchedCompression = false;
        importer.SetPlatformTextureSettings(defaults);
        foreach (string platform in new[] {"Standalone", "WebGL", "Android", "iPhone", "tvOS"}) {
            var settings = importer.GetPlatformTextureSettings(platform);
            if (!settings.overridden) continue;
            settings.maxTextureSize = 8192;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            settings.format = TextureImporterFormat.RGB24;
            importer.SetPlatformTextureSettings(settings);
        }
        importer.SaveAndReimport();
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
        var resource = Resources.Load<Texture2D>("Homescreen/Layer4");
        if (width != 6688 || height != 3764 || texture.width != width || texture.height != height)
            throw new Exception("Imported resolution differs from approved master");
        if (resource != texture || resource.width != width || resource.height != height)
            throw new Exception("Resources loaded a different or reduced texture");
        if (texture.format != TextureFormat.RGB24 || texture.mipmapCount != 1)
            throw new Exception("Unexpected compression or mipmaps");
        if (guid != AssetDatabase.AssetPathToGUID(asset) || Hash(source) != Hash(asset))
            throw new Exception("Asset identity or file mismatch");
        var result = new { source, asset, guid, width, height, importedWidth = texture.width,
            importedHeight = texture.height, format = texture.format.ToString(), texture.mipmapCount,
            importer.maxTextureSize, compression = importer.textureCompression.ToString(), sha256 = Hash(asset) };
        File.WriteAllText("Design/Tiefenhall/HomeBackgroundCleanup/Layer4-approved-import.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}


