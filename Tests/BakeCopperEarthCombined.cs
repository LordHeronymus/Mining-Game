using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes existing copper pigment into the fissures of the exact live LightDirt image.
public static class BakeCopperEarthCombined
{
    public const int Size=418, Padding=16, Pitch=Size+Padding*2;
    public const string Output="Assets/Resources/OreVeins/CopperEarthTiles/Combined.png";
    static readonly string[] Sheets={"Small-1","Small-2","Medium-1","Medium-2","Large-1","Large-2"};
    public const string Design="Assets/Design/OreVeins/CopperL1OriginalDirt";
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        var ore=new Texture2D(2,2);
        var dirt=new Texture2D(2,2);
        var metalSource=new Texture2D(2,2);
        var approved=new Texture2D(2,2);
        var result=new Texture2D(Pitch*9,Pitch*6,TextureFormat.RGBA32,false);
        var paletteReport=new System.Collections.Generic.List<string>();
        try
        {
            dirt.LoadImage(File.ReadAllBytes("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png"));
            if(dirt.width!=Size*3 || dirt.height!=Size*3)throw new Exception("Unexpected reference dirt size");
            var reference=dirt.GetPixels();
            var fissures=Fissures(reference,dirt.width);
            metalSource.LoadImage(File.ReadAllBytes("Assets/Design/OreVeins/CopperL1Branches/Sources/Large-1.png"));
            approved.LoadImage(File.ReadAllBytes(Design+"/V3/Approved-prototype.png"));
            var fill=MetalPatch(approved.GetPixels(),approved.width,paletteReport);
            var routes=MetalRoutes(metalSource.GetPixels(),metalSource.width);
            Directory.CreateDirectory(Design+"/Sources");
            var pixels=new Color32[result.width*result.height];
            for(int motif=0;motif<6;motif++)
            {
                ore.LoadImage(File.ReadAllBytes("Assets/Design/OreVeins/CopperL1Branches/Sources/"+Sheets[motif]+".png"));
                if(ore.width!=Size*3 || ore.height!=Size*3)throw new Exception("Authored sheet must contain nine 418px earth cells: "+Sheets[motif]);
                var painting=ore.GetPixels();
                EmbedCopper(painting,reference,fissures[motif/2],routes,fill,ore.width,Sheets[motif],paletteReport);
                ore.SetPixels(painting);ore.Apply();
                File.WriteAllBytes(Design+"/Sources/"+Sheets[motif]+".png",ore.EncodeToPNG());
                for(int n=0;n<painting.Length;n++)painting[n]=painting[n].linear;
                for(int phase=0;phase<9;phase++)for(int y=0;y<Pitch;y++)for(int x=0;x<Pitch;x++)
                {
                    float localU=(x-Padding+.5f)/Size,localV=(y-Padding+.5f)/Size;
                    float u=(phase%3+localU)/3,v=(phase/3+localV)/3;
                    Color authored=Sample(painting,ore.width,ore.height,u,v,true);
                    // Pack the full authored cell without replacing copper at its edges.
                    Color combined=authored.gamma;
                    combined.a=1;
                    pixels[(motif*Pitch+y)*result.width+phase*Pitch+x]=combined;
                }
            }
            result.SetPixels32(pixels);result.Apply();File.WriteAllBytes(Output,result.EncodeToPNG());
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Output);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=true;importer.maxTextureSize=4096;importer.npotScale=TextureImporterNPOTScale.None;
            importer.filterMode=FilterMode.Trilinear;importer.wrapMode=TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit=Size;importer.isReadable=false;
            var sprites=new SpriteMetaData[54];
            for(int m=0;m<6;m++)for(int p=0;p<9;p++)sprites[m*9+p]=new SpriteMetaData {
                name=$"Copper-{(m<2?"Small":m<4?"Medium":"Large")}-{m%2+1}-Earth-{p%3}-{p/3}",
                rect=new Rect(p*Pitch+Padding,m*Pitch+Padding,Size,Size),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f)};
#pragma warning disable CS0618
            importer.spritesheet=sprites;
#pragma warning restore CS0618
            importer.SaveAndReimport();
            Directory.CreateDirectory("Temp");
            paletteReport.Add("ALL PASSED: 54 cells use exact original LightDirt outside embedded copper; all copper spans exceed 30% per axis.");
            string report=$"54 opaque earth/copper sprites; {Size}px each; {Padding}px gutters; atlas {result.width}x{result.height}; two variants per richness; existing copper pigment restricted to original dirt fissures. Original soil pixels, shapes and lighting retained; periodic whole-sheet padding.\n";
            File.WriteAllText("Temp/CopperEarthCombinedBake.txt",report);return report;
        }
        finally {
            File.WriteAllText("Temp/CopperL1OriginalDirtBake.txt",string.Join("\n",paletteReport));
            UnityEngine.Object.DestroyImmediate(dirt);UnityEngine.Object.DestroyImmediate(ore);UnityEngine.Object.DestroyImmediate(metalSource);UnityEngine.Object.DestroyImmediate(approved);UnityEngine.Object.DestroyImmediate(result);
        }
    }
    static float Smooth(float lo,float hi,float value){float t=Mathf.Clamp01((value-lo)/(hi-lo));return t*t*(3-2*t);}
    static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
    static float[] Blur(float[] data,int width,int radius)
    {
        int span=radius*2+1;int Wrap(int i)=>(i%width+width)%width;
        var horizontal=new float[data.Length];var result=new float[data.Length];
        for(int y=0;y<width;y++){
            double sum=0;for(int k=-radius;k<=radius;k++)sum+=data[y*width+Wrap(k)];
            for(int x=0;x<width;x++){
                horizontal[y*width+x]=(float)(sum/span);
                sum+=data[y*width+Wrap(x+radius+1)]-data[y*width+Wrap(x-radius)];
            }
        }
        for(int x=0;x<width;x++){
            double sum=0;for(int k=-radius;k<=radius;k++)sum+=horizontal[Wrap(k)*width+x];
            for(int y=0;y<width;y++){
                result[y*width+x]=(float)(sum/span);
                sum+=horizontal[Wrap(y+radius+1)*width+x]-horizontal[Wrap(y-radius)*width+x];
            }
        }
        return result;
    }
    // A valley is darker than its neighbourhood. Work across the whole repeating
    // 3x3 image, never reset the filter at cell boundaries. Bright stone tops
    // stay original; the soft transition retains native shadows at rift edges.
    static float[][] Fissures(Color[] dirt,int width)
    {
        var values=new float[dirt.Length];for(int i=0;i<values.Length;i++)values[i]=Luma(dirt[i]);
        var means=Blur(values,width,48);var valleys=Blur(values,width,8);var mask=new float[dirt.Length];
        // Suppress painted surface grain before finding valleys: grain must not
        // turn a continuous copper seam into hundreds of isolated flecks.
        for(int y=0;y<width;y++)for(int x=0;x<width;x++){
            int i=y*width+x;float l=valleys[i];
            mask[i]=(1-Smooth(.26f,.38f,l))*(1-Smooth(.98f,1.06f,l/Mathf.Max(.001f,means[i])));
        }
        // Widen actual native valleys, instead of classifying every shaded
        // stone face as ore. Preserve the topology of the existing crack graph.
        var nearest=new int[mask.Length];for(int i=0;i<mask.Length;i++)nearest[i]=mask[i]>.65f?i:-1;
        void Consider(int i,int j){
            int candidate=nearest[j];if(candidate<0)return;
            int x=i%width,y=i/width,dx=candidate%width-x,dy=candidate/width-y;
            int d=dx*dx+dy*dy;if(d>12*12)return;
            int old=nearest[i];if(old<0){nearest[i]=candidate;return;}
            dx=old%width-x;dy=old/width-y;if(d<dx*dx+dy*dy)nearest[i]=candidate;
        }
        for(int y=0;y<width;y++)for(int x=0;x<width;x++){
            int i=y*width+x;if(x>0)Consider(i,i-1);
            if(y>0){Consider(i,i-width);if(x>0)Consider(i,i-width-1);if(x<width-1)Consider(i,i-width+1);}
        }
        for(int y=width-1;y>=0;y--)for(int x=width-1;x>=0;x--){
            int i=y*width+x;if(x<width-1)Consider(i,i+1);
            if(y<width-1){Consider(i,i+width);if(x>0)Consider(i,i+width-1);if(x<width-1)Consider(i,i+width+1);}
        }
        var fields=new[]{(float[])mask.Clone(),(float[])mask.Clone(),(float[])mask.Clone()};
        for(int i=0;i<mask.Length;i++)if(nearest[i]>=0){
            int k=nearest[i];float d=Vector2.Distance(new Vector2(i%width,i/width),new Vector2(k%width,k/width));
            for(int richness=0;richness<3;richness++){
                float r=new[]{4f,8f,12f}[richness];
                fields[richness][i]=Mathf.Max(mask[i],(1-Smooth(r*.5f,r,d))*(1-Smooth(.38f,.48f,Luma(dirt[i]))));
            }
        }
        return fields;
    }
    static Color[] MetalPatch(Color[] source,int width,System.Collections.Generic.List<string> report)
    {
        const int size=32;int bestX=0,bestY=0,bestCount=0;float best=float.MinValue;
        for(int y=0;y<source.Length/width-size;y+=4)for(int x=0;x<width-size;x+=4){
            int copper=0;float sum=0,square=0;
            for(int yy=0;yy<size;yy++)for(int xx=0;xx<size;xx++){
                var c=source[(y+yy)*width+x+xx];float chroma=Mathf.Max(.001f,c.r-c.b);
                if(chroma>.14f && (c.r-c.g)/chroma>.66f && c.r>.28f && Luma(c)>.15f)copper++;
                float l=Luma(c);sum+=l;square+=l*l;
            }
            float mean=sum/(size*size),variance=square/(size*size)-mean*mean;
            float score=copper/(float)(size*size)+Mathf.Min(.015f,variance)*2;
            if(score>best){best=score;bestX=x;bestY=y;bestCount=copper;}
        }
        if(bestCount<size*size*.80f)throw new Exception("No predominantly metallic approved copper patch found: "+bestCount);
        var patch=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)patch[y*size+x]=source[(bestY+y)*width+bestX+x];
        // Remove the few native dirt specks from the reference crop before
        // using it as metal pigment. Keep every actual metallic flake intact.
        var original=(Color[])patch.Clone();
        bool IsMetal(Color c){float chroma=Mathf.Max(.001f,c.r-c.b);return chroma>.14f && (c.r-c.g)/chroma>.66f && c.r>.28f && Luma(c)>.15f;}
        for(int i=0;i<patch.Length;i++)if(!IsMetal(original[i])){
            int bestPixel=-1,nearest=int.MaxValue;
            for(int j=0;j<patch.Length;j++)if(IsMetal(original[j])){
                int dx=i%size-j%size,dy=i/size-j/size,d=dx*dx+dy*dy;
                if(d<nearest){nearest=d;bestPixel=j;}
            }
            patch[i]=original[bestPixel];
        }
        report.Add("PASS existing textured copper fill patch: "+bestX+","+bestY+", 32px, metal="+bestCount+"/1024");
        return patch;
    }
    static float[] MetalRoutes(Color[] painting,int width)
    {
        const float reach=40;
        var nearest=new int[painting.Length];
        for(int i=0;i<painting.Length;i++){
            var c=painting[i];float chroma=Mathf.Max(.001f,c.r-c.b);
            // Do not extrapolate the dark contact shadow around authored metal:
            // only actual lit copper can seed a wider ribbon.
            nearest[i]=chroma>.18f && (c.r-c.g)/chroma>.68f && c.r>.48f && Luma(c)>.23f?i:-1;
        }
        void Consider(int i,int j){
            int candidate=nearest[j];if(candidate<0)return;
            int x=i%width,y=i/width,cx=candidate%width,cy=candidate/width;
            int dx=cx-x,dy=cy-y,distance=dx*dx+dy*dy;
            if(distance>reach*reach)return;
            int current=nearest[i];
            if(current<0){nearest[i]=candidate;return;}
            dx=current%width-x;dy=current/width-y;
            if(distance<dx*dx+dy*dy)nearest[i]=candidate;
        }
        // Project the existing pigment onto nearby original rifts, rather than
        // leaving flecks where the generated rocks and original rocks differ.
        for(int y=0;y<width;y++)for(int x=0;x<width;x++){
            int i=y*width+x;if(x>0)Consider(i,i-1);
            if(y>0){Consider(i,i-width);if(x>0)Consider(i,i-width-1);if(x<width-1)Consider(i,i-width+1);}
        }
        for(int y=width-1;y>=0;y--)for(int x=width-1;x>=0;x--){
            int i=y*width+x;if(x<width-1)Consider(i,i+1);
            if(y<width-1){Consider(i,i+width);if(x>0)Consider(i,i+width-1);if(x<width-1)Consider(i,i+width+1);}
        }
        var result=new float[painting.Length];
        for(int i=0;i<result.Length;i++){
            int source=nearest[i];float d=source<0?reach:Vector2.Distance(new Vector2(i%width,i/width),new Vector2(source%width,source/width));
            result[i]=1-Smooth(20,reach,d);
        }
        return result;
    }
    static void EmbedCopper(Color[] painting,Color[] reference,float[] fissures,float[] routes,Color[] fill,int width,string name,System.Collections.Generic.List<string> report)
    {
        var coverage=new float[painting.Length];
        var pigment=(Color[])painting.Clone();
        for(int i=0;i<painting.Length;i++){
            // Extend ribbons with a real painted metal patch, never stretched
            // nearest-pixel spokes. World-space pigment cadence crosses cells.
            float variant=name.EndsWith("2")?16:0;
            var c=Sample(fill,32,32,(i%width+variant+.5f)/74f,(i/width+variant+.5f)/74f,true);
            var original=pigment[i];float chroma=Mathf.Max(.001f,original.r-original.b);
            float authoredMetal=Smooth(.63f,.70f,(original.r-original.g)/chroma)*Smooth(.12f,.20f,chroma)*Smooth(.28f,.45f,original.r);
            c=Color.Lerp(c,original,authoredMetal*.25f);
            // Restore the reference's readable flake highlights after pigment
            // blending and mip filtering, without brightening the dirt.
            float metalLight=Luma(c);
            c*=Mathf.Max(.02f,.35f+(metalLight-.35f)*1.30f)/Mathf.Max(.001f,metalLight);
            // Every ore cell follows the SAME native valley graph. Richness
            // controls width; artwork contributes the texture of its metal.
            // This prevents unrelated motif masks from breaking adjoining seams.
            float alpha=Smooth(.02f,.48f,fissures[i])*routes[i];coverage[i]=alpha;
            // Keep the approved copper saturation, applied to extracted pigment
            // only. No runtime colour classifier needs to touch native dirt.
            float lum=Luma(c);c=new Color(Mathf.Clamp01(lum+(c.r-lum)*1.15f),Mathf.Clamp01(lum+(c.g-lum)*1.15f),Mathf.Clamp01(lum+(c.b-lum)*1.15f),1);
            // Pixels outside metal stay exactly equal to LightDirt; keep the
            // original shadow below a soft metal edge. This is baked once.
            painting[i]=Color.Lerp(reference[i].linear,c.linear,alpha).gamma;
            if(alpha==0)painting[i]=reference[i];
            painting[i].a=1;
        }
        for(int phase=0;phase<9;phase++){
            int ox=phase%3*Size,oy=phase/3*Size;
            int exactSoil=0,copper=0,minX=Size,maxX=-1,minY=Size,maxY=-1;
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++){
                int i=(oy+y)*width+ox+x;var c=painting[i];
                if(coverage[i]==0){if(c!=reference[i])throw new Exception("Original soil changed");exactSoil++;}
                if(c.r-c.b>.16f && (c.r-c.g)/Mathf.Max(.001f,c.r-c.b)>.65f){
                    copper++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                }
            }
            if(copper<Size*Size*.015f || maxX-minX<Size*.30f || maxY-minY<Size*.30f)throw new Exception("Insufficient copper visibility "+name+" phase "+phase+": "+copper);
            report.Add("PASS "+name+" phase "+phase+": exact original soil="+exactSoil+", visible copper="+copper+", spans="+(maxX-minX)+"x"+(maxY-minY));
        }
    }
    static Color Sample(Color[] data,int width,int height,float u,float v,bool repeat)
    {
        float x=u*width-.5f,y=v*height-.5f;int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);
        int X(int n)=>repeat?(n%width+width)%width:Mathf.Clamp(n,0,width-1);
        int Y(int n)=>repeat?(n%height+height)%height:Mathf.Clamp(n,0,height-1);
        return Color.Lerp(Color.Lerp(data[Y(iy)*width+X(ix)],data[Y(iy)*width+X(ix+1)],x-ix),
            Color.Lerp(data[Y(iy+1)*width+X(ix)],data[Y(iy+1)*width+X(ix+1)],x-ix),y-iy);
    }
}
