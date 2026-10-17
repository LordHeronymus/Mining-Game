using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupCopperL1Sprite
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        const string path="Assets/Resources/OreVeins/CopperL1Sprite/Copper.png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;
        importer.spritePixelsPerUnit=1254;
        importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=true;importer.sRGBTexture=true;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
        importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;
        importer.wrapMode=TextureWrapMode.Clamp;importer.isReadable=false;
        importer.SaveAndReimport();
        var t=new Texture2D(2,2);
        try {
            t.LoadImage(File.ReadAllBytes(path));
            int transparent=0,opaque=0,edge=0;
            var pixels=t.GetPixels32();
            for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++){
                var c=pixels[y*t.width+x];if(c.a==0)transparent++;if(c.a>=250)opaque++;
                if((x==0||y==0||x==t.width-1||y==t.height-1)&&c.a!=0)edge++;
            }
            if(transparent<t.width*t.height*.3f||opaque<t.width*t.height*.25f||edge!=0)throw new Exception($"Invalid sprite alpha/margins: {t.width}x{t.height}, transparent={transparent}, near-opaque={opaque}, edge={edge}");
            string report=$"Approved V1 cutout imported: {t.width}x{t.height}, true alpha; transparent={transparent}, near-opaque={opaque}, nontransparent border={edge}. Uncompressed sRGB, trilinear mipmaps, clamp.\n";
            File.WriteAllText("Temp/CopperL1SpriteImport.txt",report);
            return report;
        } finally {UnityEngine.Object.DestroyImmediate(t);}
    }
}

