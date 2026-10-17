using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bake only routing masks and metal pigment. The renderer samples the real terrain
// for soil, so neither atlas padding nor a colour roundtrip can alter it.
public static class BakeCopperFissureRoutes
{
    const int Size=418, Pad=16, Pitch=450, Grid=70;
    const string Folder="Assets/Resources/OreVeins/CopperFissures";
    static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
    static int Wrap(int p,int size)=>(p%size+size)%size;
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        Directory.CreateDirectory(Folder);
        var soil=new Texture2D(2,2);var reference=new Texture2D(2,2);
        var atlas=new Texture2D(Pitch*9,Pitch*2,TextureFormat.RGBA32,false,true);
        var pigment=new Texture2D(32,32,TextureFormat.RGB24,false);
        try {
            soil.LoadImage(File.ReadAllBytes("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png"));
            if(soil.width!=Size*3 || soil.height!=Size*3)throw new Exception("Original dirt dimensions changed");
            var colors=soil.GetPixels();int width=soil.width;
            // Average grain, retaining the larger native channels between clods.
            float Light(float x,float y){float sum=0;for(int j=-6;j<=6;j+=3)for(int i=-6;i<=6;i+=3)
                sum+=Luma(colors[Wrap(Mathf.RoundToInt(y)+j,width)*width+Wrap(Mathf.RoundToInt(x)+i,width)]);return sum/25;}
            Vector2 Point(int n)=>new Vector2(24+(n%Grid)*(Size-48f)/(Grid-1),24+(n/Grid)*(Size-48f)/(Grid-1));
            int Node(Vector2 p)=>Mathf.Clamp(Mathf.RoundToInt((p.y-24)/(Size-48f)*(Grid-1)),0,Grid-1)*Grid+
                Mathf.Clamp(Mathf.RoundToInt((p.x-24)/(Size-48f)*(Grid-1)),0,Grid-1);
            var packed=new Color32[atlas.width*atlas.height];var report=new List<string>();
            for(int phase=0;phase<9;phase++) {
                var origin=new Vector2(phase%3*Size,phase/3*Size);
                var cost=new float[Grid*Grid];int anchor=0;float best=float.MaxValue;
                for(int n=0;n<cost.Length;n++){
                    var p=Point(n);float l=Light(origin.x+p.x,origin.y+p.y);
                    cost[n]=1+Mathf.Pow(l/.30f,5)*12;
                    float d=(p-new Vector2(Size*.5f,Size*.5f)).magnitude;
                    float score=l+d*.0012f;
                    if(d<Size*.22f && score<best){best=score;anchor=n;}
                }
                // One shortest-path tree, shared by every arm. Its subsets cannot
                // introduce a closed ring around an original earth clod.
                var distances=new float[cost.Length];var parents=new int[cost.Length];
                for(int n=0;n<cost.Length;n++){distances[n]=float.MaxValue;parents[n]=-1;}
                var queue=new SortedSet<(float distance,int node)>();distances[anchor]=0;queue.Add((0,anchor));
                while(queue.Count>0){var item=queue.Min;queue.Remove(item);int n=item.node;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                        int x=n%Grid+dx,y=n/Grid+dy;if((dx==0&&dy==0)||x<0||y<0||x>=Grid||y>=Grid)continue;
                        int next=y*Grid+x;float distance=item.distance+(cost[n]+cost[next])*(dx!=0&&dy!=0?1.414214f:1);
                        if(distance>=distances[next])continue;
                        queue.Remove((distances[next],next));distances[next]=distance;parents[next]=n;queue.Add((distance,next));
                    }
                }
                var masks=new float[4][];List<Vector2> longest=null;
                for(int side=0;side<4;side++){
                    // Canonical shared portal: both cells inspect exactly the same
                    // original pixels, including the periodic 3x3 outer boundary.
                    bool horizontal=side%2==0;float edge=side<2?Size:0,at=0;best=float.MaxValue;
                    for(int t=90;t<=Size-90;t+=2){
                        float x=origin.x+(horizontal?edge:t),y=origin.y+(horizontal?t:edge);
                        float score=Light(x,y)+Light(x+(horizontal?18:0),y+(horizontal?0:18))*.4f+
                            Light(x-(horizontal?18:0),y-(horizontal?0:18))*.4f;
                        if(score<best){best=score;at=t;}
                    }
                    var portal=horizontal?new Vector2(edge,at):new Vector2(at,edge);
                    var inner=horizontal?new Vector2(side==0?Size-24:24,at):new Vector2(at,side==1?Size-24:24);
                    var points=new List<Vector2>{portal,inner};int node=Node(inner);
                    for(int n=node;n>=0;n=parents[n])points.Add(Point(n));
                    if(longest==null || points.Count>longest.Count)longest=points;
                    var mask=masks[side]=new float[Size*Size];
                    for(int segment=0;segment<points.Count-1;segment++){
                        var a=points[segment];var b=points[segment+1];var delta=b-a;
                        for(int y=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-29));y<=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+29));y++)
                        for(int x=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-29));x<=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+29));x++){
                            var p=new Vector2(x+.5f,y+.5f);float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                            float distance=Vector2.Distance(p,a+delta*t);
                            float l=Light(origin.x+x,origin.y+y);
                            float radius=Mathf.Lerp(29,21,Mathf.InverseLerp(.22f,.45f,l));
                            float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(radius-4,radius,distance));
                            mask[y*Size+x]=Mathf.Max(mask[y*Size+x],alpha);
                        }
                    }
                    report.Add("phase="+phase+" side="+side+" portal="+portal+" path nodes="+points.Count);
                }
                // Isolated ore needs one connected dead-end, not a rectangular
                // crop of four arms (cropping a winding path can split it).
                var core=new float[Size*Size];longest.Reverse();float travelled=0;
                for(int segment=0;segment<longest.Count-1 && travelled<180;segment++){
                    var a=longest[segment];var b=longest[segment+1];var delta=b-a;float length=delta.magnitude;
                    if(Mathf.Min(b.x,b.y,Size-b.x,Size-b.y)<48)break;
                    for(int y=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y)-29));y<=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y)+29));y++)
                    for(int x=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x)-29));x<=Mathf.Min(Size-1,Mathf.CeilToInt(Mathf.Max(a.x,b.x)+29));x++){
                        var p=new Vector2(x+.5f,y+.5f);float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                        float distance=Vector2.Distance(p,a+delta*t),along=travelled+t*length;
                        float radius=Mathf.Lerp(29,21,Mathf.InverseLerp(.22f,.45f,Light(origin.x+x,origin.y+y)));
                        radius*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(140,180,along));
                        float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,radius-4),Mathf.Max(.001f,radius),distance));
                        core[y*Size+x]=Mathf.Max(core[y*Size+x],alpha);
                    }
                    travelled+=length;
                }
                for(int y=0;y<Pitch;y++)for(int x=0;x<Pitch;x++){
                    int i=Mathf.Clamp(y-Pad,0,Size-1)*Size+Mathf.Clamp(x-Pad,0,Size-1);
                    packed[y*atlas.width+phase*Pitch+x]=new Color(masks[0][i],masks[1][i],masks[2][i],masks[3][i]);
                    packed[(y+Pitch)*atlas.width+phase*Pitch+x]=new Color(core[i],0,0,0);
                }
            }
            atlas.SetPixels32(packed);atlas.Apply();File.WriteAllBytes(Folder+"/Routes.png",atlas.EncodeToPNG());
            reference.LoadImage(File.ReadAllBytes("Design/OreVeins/ConnectedCopper/Approved-preview.png"));
            var source=reference.GetPixels();int bx=0,by=0,count=-1;
            bool Metal(Color c)=>c.r-c.b>.20f && ((c.r-c.g)/Mathf.Max(.001f,c.r-c.b)>.62f && c.r>.55f || c.r>.88f && c.g>.60f);
            for(int y=0;y<reference.height-32;y+=4)for(int x=0;x<reference.width-32;x+=4){int score=0;
                for(int j=0;j<32;j++)for(int i=0;i<32;i++)if(Metal(source[(y+j)*reference.width+x+i]))score++;
                if(score>count){count=score;bx=x;by=y;}}
            var patch=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++)patch[y*32+x]=source[(by+y)*reference.width+bx+x];
            var original=(Color[])patch.Clone();
            for(int i=0;i<patch.Length;i++)if(!Metal(patch[i])){int nearest=-1;float distance=float.MaxValue;
                for(int j=0;j<patch.Length;j++)if(Metal(original[j])){float d=Mathf.Pow(i%32-j%32,2)+Mathf.Pow(i/32-j/32,2);if(d<distance){distance=d;nearest=j;}}
                if(nearest<0)throw new Exception("Missing copper pigment");patch[i]=original[nearest];}
            pigment.SetPixels(patch);pigment.Apply();File.WriteAllBytes(Folder+"/Metal.png",pigment.EncodeToPNG());
            foreach(var name in new[]{"Routes","Metal"}){
                string path=Folder+"/"+name+".png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=name=="Metal";importer.alphaIsTransparency=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
                importer.maxTextureSize=4096;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;
                importer.wrapMode=name=="Metal"?TextureWrapMode.Repeat:TextureWrapMode.Clamp;importer.isReadable=false;importer.SaveAndReimport();
            }
            File.WriteAllText("Temp/CopperFissureBake.txt",string.Join("\n",report));
            return "Baked 9 original-earth phases with 4 connected fissure arms each; no richness variants or soil RGB written.";
        } finally {UnityEngine.Object.DestroyImmediate(soil);UnityEngine.Object.DestroyImmediate(reference);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(pigment);}
    }
}
