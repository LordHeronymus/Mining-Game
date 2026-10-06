using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupCopperEarthTiles
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        const string path = "Assets/Resources/OreVeins/CopperEarthTiles/Atlas.png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 4096;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        var raw = new Texture2D(2,2);
        try
        {
            raw.LoadImage(File.ReadAllBytes(path));
            int transparent=0, opaque=0;
            foreach(var pixel in raw.GetPixels32())
            { if(pixel.a==0)transparent++; if(pixel.a>=250)opaque++; }
            if(transparent < raw.width*raw.height*.4 || opaque < raw.width*raw.height*.08)
                throw new Exception("Atlas is missing its true alpha or opaque copper");
            var cores=new Color[16];
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
                cores[y*4+x]=Measure(raw,x,y);
            const string layoutPath="Assets/Resources/OreVeins/CopperEarthTiles/Layout.asset";
            var layout=AssetDatabase.LoadAssetAtPath<Texture2D>(layoutPath);
            if(!layout)
            {
                layout=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
                AssetDatabase.CreateAsset(layout,layoutPath);
            }
            layout.name="Measured copper tile cores";
            layout.filterMode=FilterMode.Point;layout.wrapMode=TextureWrapMode.Clamp;
            layout.SetPixels(cores);layout.Apply(false,false);
            EditorUtility.SetDirty(layout);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Temp");
            string report=$"Atlas {raw.width}x{raw.height}; transparent={transparent}; opaque={opaque}; uncompressed, sRGB, alpha, bilinear, no mipmaps\n";
            File.WriteAllText("Temp/CopperEarthTilesImport.txt",report);
            return report;
        }
        finally { UnityEngine.Object.DestroyImmediate(raw); }
    }
    static Color Measure(Texture2D texture,int column,int row)
    {
        int left=column*texture.width/4, right=(column+1)*texture.width/4;
        int bottom=row*texture.height/4,top=(row+1)*texture.height/4;
        int width=right-left,height=top-bottom;
        var pixels=texture.GetPixels32();var distance=new float[width*height];
        int xmin=width,ymin=height,xmax=0,ymax=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var c=pixels[(bottom+y)*texture.width+left+x];
            bool copper=c.a>=248;
            distance[y*width+x]=copper && x>0 && y>0 && x<width-1 && y<height-1 ? width+height : 0;
            if(c.a>=96){xmin=Math.Min(xmin,x);xmax=Math.Max(xmax,x);ymin=Math.Min(ymin,y);ymax=Math.Max(ymax,y);}
        }
        const float diagonal=1.414214f;
        for(int y=1;y<height;y++)for(int x=1;x<width-1;x++)
        {
            int n=y*width+x;
            distance[n]=Math.Min(distance[n],Math.Min(distance[n-1]+1,Math.Min(distance[n-width]+1,
                Math.Min(distance[n-width-1]+diagonal,distance[n-width+1]+diagonal))));
        }
        for(int y=height-2;y>=0;y--)for(int x=width-2;x>=1;x--)
        {
            int n=y*width+x;
            distance[n]=Math.Min(distance[n],Math.Min(distance[n+1]+1,Math.Min(distance[n+width]+1,
                Math.Min(distance[n+width-1]+diagonal,distance[n+width+1]+diagonal))));
        }
        float best=0,radius=0,cap=0;int cx=0,cy=0;
        for(int y=1;y<height-1;y++)for(int x=1;x<width-1;x++)
        {
            var centre=pixels[(bottom+y)*texture.width+left+x];
            if(centre.r<centre.g*1.25f || centre.g<centre.b*1.1f)continue;
            float r=Math.Max(0,(distance[y*width+x]*.9f-2)/Math.Max(width,height));
            float reach=Math.Max(Math.Max(x-xmin,xmax-x)/(float)width,Math.Max(y-ymin,ymax-y)/(float)height);
            float limit=Math.Min(1.12f,.445f/Math.Max(.001f,reach));
            float score=r*limit;
            if(score>best){best=score;radius=r;cap=limit;cx=x;cy=y;}
        }
        if(best<.156f)throw new Exception($"Copper variant {column},{row} cannot fit a 30% core: {best}");
        return new Color((cx+.5f)/width,(cy+.5f)/height,radius,cap);
    }
}
