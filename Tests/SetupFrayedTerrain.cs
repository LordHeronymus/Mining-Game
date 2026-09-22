using UnityEditor;
using UnityEngine;

public static class SetupFrayedTerrain
{
    public static object Main()
    {
        const string texturePath="Assets/GameObjects/Map/StoneTest/Frayed/FrayedDirtEdge.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=true;importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;
        importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;
        importer.filterMode=FilterMode.Trilinear;importer.isReadable=true;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var shader=Shader.Find("Mining Game/Frayed Terrain Edge Lit");
        if(!shader||ShaderUtil.ShaderHasError(shader))throw new System.Exception("Frayed shader failed");
        const string materialPath="Assets/Resources/FrayedTerrainEdges.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
        material.SetTexture("_FrayedTex",texture);EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
        int clear=0,opaque=0;foreach(var p in texture.GetPixels32()){if(p.a<8)clear++;if(p.a>247)opaque++;}
        if(clear<texture.width*texture.height/5||opaque<texture.width*texture.height/5)throw new System.Exception("Texture lacks a usable alpha silhouette");
        return new{texture.width,texture.height,clear,opaque,materialPath};
    }
}
