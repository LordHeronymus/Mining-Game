using System.IO;
using UnityEditor;
using UnityEngine;

public static class ImportSelectionSprites
{
    public static object Main()
    {
        string source = "C:/Users/jlang/.codex/generated_images/01a0fdb7-d8f1-7163-bc2f-edb8eceb9711/";
        Import(source + "exec-2857b436-8163-4787-82c7-8818857407c0.png", "Assets/Resources/Homescreen/SelectionButtons-v2.png");
        Import(source + "exec-74350fd5-070b-4297-aef2-9d8022dd02fa.png", "Assets/Resources/Homescreen/SelectionCards-v2.png");
        Directory.CreateDirectory("Assets/Design/Tiefenhall/SelectionBevels");
        File.Copy(source + "exec-9430b45e-64f4-4ff0-bf9c-d2dd3f9c9489.png", "Assets/Design/Tiefenhall/SelectionBevels/Approved.png", true);
        AssetDatabase.ImportAsset("Assets/Design/Tiefenhall/SelectionBevels/Approved.png");
        return "Imported matching button and card state atlases";
    }
    static void Import(string source, string path)
    {
        File.Copy(source, path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
    }
}
