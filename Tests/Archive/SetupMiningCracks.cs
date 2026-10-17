using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupMiningCracks
{
    public static string Main()
    {
        const string folder = "Assets/Resources/Mining";
        Directory.CreateDirectory(folder);
        string path = folder + "/MiningCracksFiveStages.png";
        File.Copy("Design/MiningCracks/MiningCracks-V1-FiveStages-Concept.png", path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        string matPath = folder + "/MiningCracksLit.mat";
        AssetDatabase.ImportAsset("Assets/GameObjects/Player/MiningCrackGrowthLit.shader", ImportAssetOptions.ForceSynchronousImport);
        var shader = Shader.Find("Mining/CrackGrowthLit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!material) AssetDatabase.CreateAsset(new Material(shader), matPath);
        else { material.shader = shader; EditorUtility.SetDirty(material); }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Imported five-frame crack sheet and URP lit material. Playing=" + Application.isPlaying;
    }
}
