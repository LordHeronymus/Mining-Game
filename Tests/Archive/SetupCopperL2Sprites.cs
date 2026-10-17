using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupCopperL2Sprites
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        string report="";
        foreach(string size in new[]{"Small","Medium","Large"})
        {
            string path="Assets/Resources/OreVeins/CopperL2Sprite/Copper"+size+".png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=1254;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=true;importer.sRGBTexture=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
            importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;
            importer.wrapMode=TextureWrapMode.Clamp;importer.isReadable=false;importer.SaveAndReimport();
            var t=new Texture2D(2,2);
            try {
                t.LoadImage(File.ReadAllBytes(path));int transparent=0,opaque=0,edge=0;
                var pixels=t.GetPixels32();
                for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++){
                    var c=pixels[y*t.width+x];if(c.a==0)transparent++;if(c.a>=250)opaque++;
                    if((x==0||y==0||x==t.width-1||y==t.height-1)&&c.a>8)edge++;
                }
                report+=$"{size}: {t.width}x{t.height}, transparent={transparent}, opaque={opaque}, border={edge}\n";
                File.WriteAllText("Temp/CopperL2Import.txt",report);
                if(transparent<t.width*t.height*.25f||opaque<t.width*t.height*.10f||edge!=0)
                    throw new Exception("Invalid sprite transparency or border: "+report);
            }finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        return report;
    }
}
