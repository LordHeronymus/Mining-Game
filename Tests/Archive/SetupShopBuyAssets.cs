var paths = new[] { "Assets/Resources/Shop/BuyBackground.png", "Assets/Resources/Shop/BuyFrames.png" };
for (int i=0;i<2;i++) {
    UnityEditor.AssetDatabase.ImportAsset(paths[i], UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(paths[i]);
    importer.textureType=i==0?UnityEditor.TextureImporterType.Sprite:UnityEditor.TextureImporterType.Default;
    importer.spriteImportMode=UnityEditor.SpriteImportMode.Single;
    importer.textureCompression=UnityEditor.TextureImporterCompression.Uncompressed;
    importer.mipmapEnabled=false;
    importer.alphaIsTransparency=true;
    importer.npotScale=UnityEditor.TextureImporterNPOTScale.None;
    importer.maxTextureSize=4096;
    importer.SaveAndReimport();
}
return "Imported buy background and frame sheet.";
