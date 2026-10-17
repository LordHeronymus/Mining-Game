using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class InstallGoldCursors
{
    public static string Main()
    {
        string source = @"C:\Users\jlang\.codex\generated_images\01a0dd99-7fec-7401-93bd-ea562ca98a79\";
        string[] files = { "exec-1c1e2fec-240d-4715-962f-a356398a4d4b.png", "exec-f291ed29-cbde-4b30-b44a-3cfd3dab4a68.png" };
        string[] names = { "NormalCursorFrame", "SmartCursorFrame" };
        Directory.CreateDirectory("Design/Cursor/BeforeGoldSelection");
        for (int i = 0; i < names.Length; i++)
        {
            string path = "Assets/Resources/Cursor/" + names[i] + ".png";
            string backup = "Design/Cursor/BeforeGoldSelection/" + names[i] + ".png";
            if (!File.Exists(backup)) File.Copy(path, backup);
            string guid = AssetDatabase.AssetPathToGUID(path);
            File.Copy(source + files[i], path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            if (guid != AssetDatabase.AssetPathToGUID(path)) throw new Exception("Cursor GUID changed");
        }
        AssetDatabase.Refresh();
        return "Both gold cursor textures imported; original GUIDs preserved. Previous PNGs archived in Design/Cursor/BeforeGoldSelection.";
    }
}
