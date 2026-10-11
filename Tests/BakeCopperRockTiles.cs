using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Deterministic packing of complete jointly painted rock/copper artwork.
public static class BakeCopperRockTiles
{
    const int Size=208, Padding=8, Pitch=224, Width=2016, Height=1344;
    const string Folder="Assets/Design/OreVeins/CopperAllLayers";
    const string Output="Assets/Resources/OreVeins/CopperRockTiles/Combined.asset";
    static readonly string[] Hosts={"Clay","Gray","Warm","Deep"};
    static readonly string[] Sheets={"Small-1","Small-2","Medium-1","Medium-2","Large-1","Large-2"};
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));Directory.CreateDirectory(Folder+"/Atlases");
        var report=new List<string>();
        var art=new Texture2D(2,2);
        var preview=new Texture2D(Width,Height,TextureFormat.RGBA32,true);
        var array=new Texture2DArray(Width,Height,4,TextureFormat.BC7,true,false)
            {name="Copper rock complete tiles",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=1};
        try
        {
            for(int h=0;h<4;h++)
            {
                var pixels=new Color32[Width*Height];
                var averages=new float[3];
                for(int m=0;m<6;m++)
                {
                    art.LoadImage(File.ReadAllBytes(Folder+"/Sources/"+Hosts[h]+"/"+Sheets[m]+".png"));
                    if(art.width!=1254 || art.height!=1254)throw new Exception("Unexpected authored size: "+Hosts[h]+Sheets[m]);
                    var painting=Linear(art);
                    for(int phase=0;phase<9;phase++)for(int y=0;y<Pitch;y++)for(int x=0;x<Pitch;x++)
                    {
                        float u=(x-Padding+.5f)/Size,v=(y-Padding+.5f)/Size;
                        float worldU=(phase%3+u)/3,worldV=(phase/3+v)/3;
                        Color authored=Sample(painting,art.width,art.height,worldU,worldV,false);
                        // Keep the jointly painted rock and copper through the entire
                        // cell. Padding samples the adjoining source image, not host rock.
                        var c=authored.gamma;c.a=1;
                        pixels[(m*Pitch+y)*Width+phase*Pitch+x]=c;
                    }
                    // Validate the final packed image at normal gameplay scale.
                    for(int phase=0;phase<9;phase++)
                    {
                        int count=0,minX=80,minY=80,maxX=-1,maxY=-1;
                        for(int y=0;y<80;y++)for(int x=0;x<80;x++)
                        {
                            var c=pixels[(m*Pitch+Padding+(int)((y+.5f)*Size/80))*Width+phase*Pitch+Padding+(int)((x+.5f)*Size/80)];
                            if(c.a!=255)throw new Exception("Incomplete rock tile");
                            if(c.r-c.b>40 && (c.r-c.g)/(float)(c.r-c.b)>.65f)
                            {count++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
                        }
                        if(count<80 || maxX-minX<24 || maxY-minY<24)
                            throw new Exception("Copper visibility "+Hosts[h]+" "+Sheets[m]+" phase "+phase+": count="+count+" span="+(maxX-minX)+"/"+(maxY-minY));
                        averages[m/2]+=count/(80f*80*18);
                        report.Add("PASS "+Hosts[h]+" "+Sheets[m]+" phase "+phase+": copper="+count+", span="+(maxX-minX)+"/"+(maxY-minY));
                    }
                }
                if(averages[1]<=averages[0]*1.15f || averages[2]<=averages[1]*1.15f)
                    throw new Exception("Richness ordering "+Hosts[h]+": "+string.Join(",",averages));
                report.Add("PASS "+Hosts[h]+" richness: "+string.Join(",",averages));
                preview.Reinitialize(Width,Height,TextureFormat.RGBA32,true);
                preview.SetPixels32(pixels);preview.Apply(true,false);
                File.WriteAllBytes(Folder+"/Atlases/"+Hosts[h]+".png",preview.EncodeToPNG());
                EditorUtility.CompressTexture(preview,TextureFormat.BC7,TextureCompressionQuality.Normal);
                for(int mip=0;mip<array.mipmapCount;mip++)
                    array.SetPixelData(preview.GetPixelData<byte>(mip),mip,h);
            }
            array.Apply(false,true);
            // Save this large texture as binary without changing the project's
            // global serialization mode (which would trigger a full reimport).
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[]{array},Output,false);
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceSynchronousImport);
            report.Add("ALL PASSED: 216 opaque tiles, 4 rock families, 6 motifs, 9 host phases; sRGB BC7 array with mips; 208px tiles and 8px gutters.");
            File.WriteAllText("Temp/CopperRockBake.txt",string.Join("\n",report));
            return report[report.Count-1];
        }
        catch(Exception e){File.WriteAllText("Temp/CopperRockBake.txt",string.Join("\n",report)+"\nFAIL "+e);throw;}
        finally
        {
            UnityEngine.Object.DestroyImmediate(art);UnityEngine.Object.DestroyImmediate(preview);
            UnityEngine.Object.DestroyImmediate(array);
        }
    }
    static Color[] Linear(Texture2D tex){var p=tex.GetPixels();for(int n=0;n<p.Length;n++)p[n]=p[n].linear;return p;}
    static Color Sample(Color[] data,int width,int height,float u,float v,bool repeat)
    {
        float x=u*width-.5f,y=v*height-.5f;int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);
        int X(int n)=>repeat?(n%width+width)%width:Mathf.Clamp(n,0,width-1);
        int Y(int n)=>repeat?(n%height+height)%height:Mathf.Clamp(n,0,height-1);
        return Color.Lerp(Color.Lerp(data[Y(iy)*width+X(ix)],data[Y(iy)*width+X(ix+1)],x-ix),
            Color.Lerp(data[Y(iy+1)*width+X(ix)],data[Y(iy+1)*width+X(ix+1)],x-ix),y-iy);
    }
}
