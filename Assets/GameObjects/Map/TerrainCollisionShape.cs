using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// CPU counterpart of MaskedTerrain. Only exposed cells need custom geometry.
public sealed class TerrainCollisionShape
{
    readonly Dictionary<int,Vector2[]> outlines=new();
    readonly float[][] profiles=new float[48][];
    readonly UniformStoneAppearance appearance;
    readonly Tilemap source;
    readonly Vector3 tuning;
    public bool Ready {get;}
    public TerrainCollisionShape(UniformStoneAppearance appearance,Tilemap source)
    {
        this.appearance=appearance;this.source=source;
        tuning=new Vector3(appearance.edgeDepth,appearance.edgeIrregularity,appearance.edgeRounding);
        var masks=Resources.Load<Texture2DArray>("TerrainEdgeMasks");
        if(!masks||!masks.isReadable||masks.depth<192)return;
        for(int v=0;v<12;v++)for(int side=0;side<4;side++)
        {
            var pixels=masks.GetPixels(v*16+(1<<side));var p=new float[128];
            for(int i=0;i<128;i++)
            {
                int index=side==0?i*128:side==1?i*128+127:side==2?i:127*128+i;
                p[i]=-pixels[index].r;
            }
            profiles[v*4+side]=p;
        }
        Ready=true;
    }
    public bool Matches=>appearance&&tuning==new Vector3(appearance.edgeDepth,appearance.edgeIrregularity,appearance.edgeRounding);
    public static int Variant(Vector3Int cell)
    {
        unchecked{uint h=(uint)cell.x*73856093u^(uint)cell.y*19349663u;h^=h>>16;return (int)(h%12);}
    }
    public int Neighbours(Vector3Int cell)
    {
        int bits=0;
        var offsets=new[]{Vector3Int.left,Vector3Int.right,Vector3Int.down,Vector3Int.up,
            new Vector3Int(-1,-1),new Vector3Int(-1,1),new Vector3Int(1,-1),new Vector3Int(1,1)};
        for(int i=0;i<8;i++)if(!source.HasTile(cell+offsets[i]))bits|=1<<i;
        if(cell.y==0)bits&=~(8|32|128);
        return bits;
    }
    public Vector2[] Get(Vector3Int cell,TileBase original)
    {
        if(!original||!Ready||tuning.x<=0)return null;
        int bits=Neighbours(cell);if(bits==0)return null;
        int variant=Variant(cell),key=bits|(variant<<8)|(cell.y==0?1<<12:0);
        if(!outlines.TryGetValue(key,out var points))outlines.Add(key,points=Outline(bits,variant,cell.y==0));
        return points;
    }
    public Vector2[] Outline(int bits,int variant,bool surface)
    {
        var points=new Vector2[96];
        for(int i=0;i<points.Length;i++)
        {
            int side=i/24;float t=(i%24)/24f;
            Vector2 end=side==0?new Vector2(t,0):side==1?new Vector2(1,t):side==2?new Vector2(1-t,1):new Vector2(0,1-t);
            float lo=0,hi=1;
            if(Distance(end,bits,variant,surface)>=0){points[i]=end;continue;}
            for(int step=0;step<13;step++)
            {
                float mid=(lo+hi)*.5f;
                if(Distance(Vector2.Lerp(Vector2.one*.5f,end,mid),bits,variant,surface)>=0)lo=mid;else hi=mid;
            }
            points[i]=Vector2.Lerp(Vector2.one*.5f,end,lo);
        }
        // Smooth only the physics outline. Keep shared cell edges pinned so adjacent
        // collision polygons still join exactly; cap movement to avoid visible hovering.
        var original=(Vector2[])points.Clone();
        for(int pass=0;pass<3;pass++)
        {
            var next=(Vector2[])points.Clone();
            for(int i=0;i<points.Length;i++)
            {
                var p=original[i];
                if(p.x<.0001f||p.x>.9999f||p.y<.0001f||p.y>.9999f)continue;
                var average=(points[(i+points.Length-1)%points.Length]+points[(i+1)%points.Length])*.5f;
                var candidate=Vector2.Lerp(points[i],average,.65f);
                next[i]=p+Vector2.ClampMagnitude(candidate-p,.015f);
            }
            points=next;
        }
        return points;
    }
    static Vector2 Corner(Vector2 f,int i)=>i==0?f:i==1?new Vector2(f.x,1-f.y):i==2?new Vector2(1-f.x,f.y):Vector2.one-f;
    static float Smooth(float a,float b,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
    static float Min(float a,float b,float r){float h=Mathf.Max(r-Mathf.Abs(a-b),0)/Mathf.Max(r,.00001f);return Mathf.Min(a,b)-h*h*r*.25f;}
    public float Distance(Vector2 f,int bits,int variant,bool surface)
    {
        float depth=Mathf.Clamp(tuning.x,0,2),round=Mathf.Clamp(tuning.z,0,2),inset=Mathf.Min(.20f,.12f*depth);
        var open=new Vector4((bits&1)!=0?1:0,(bits&2)!=0?1:0,(bits&4)!=0?1:0,(bits&8)!=0?1:0);
        var dist=new Vector4(f.x,1-f.x,f.y,1-f.y);
        var convex=new Vector4(open.x*open.z,open.x*open.w,open.y*open.z,open.y*open.w);
        
        var calm=Vector4.zero;for(int i=0;i<4;i++)calm[i]=convex[i]*(1-Smooth(.28f,.65f,Corner(f,i).magnitude))*Mathf.Clamp01(round);
        var influence=new Vector4(Mathf.Max(calm.x,calm.y),Mathf.Max(calm.z,calm.w),Mathf.Max(calm.x,calm.z),Mathf.Max(calm.y,calm.w));
        var margins=Vector4.one;
        for(int i=0;i<4;i++)
        {
            float index=Mathf.Clamp01(i<2?f.y:f.x)*127;int j=Mathf.Min(126,(int)index);
            var profile=profiles[variant*4+i];float value=Mathf.Lerp(profile[j],profile[j+1],index-j);
            float cut=Mathf.Min(.32f,Mathf.Max(.005f,.065f+(value-.065f)*Mathf.Clamp(tuning.y,0,2))*depth);
            cut=Mathf.Lerp(cut,inset,influence[i]);if(surface&&i<2)cut*=1-Smooth(.70f,1,f.y);
            margins[i]=open[i]>0?dist[i]-cut:1;
        }
        float radius=.24f*round*depth;
        float edge=Min(Min(margins.x,margins.y,radius),Min(margins.z,margins.w,radius),radius);
        float cr=Mathf.Min(.30f,.20f*depth*round),centre=inset+cr;
        for(int i=0;i<4;i++)
        {
            float cap=cr-new Vector2(Mathf.Max(centre-Corner(f,i).x,0),Mathf.Max(centre-Corner(f,i).y,0)).magnitude;
            float weight=convex[i]*(1-Smooth(.48f,.70f,Corner(f,i).magnitude))*Mathf.Clamp01(round);
            edge=Mathf.Min(edge,Mathf.Lerp(1,cap,weight));
            int a=i<2?0:1,b=i%2==0?2:3;
            if((bits&(16<<i))!=0&&open[a]==0&&open[b]==0&&!(surface&&i%2==1))
                edge=Mathf.Min(edge,Corner(f,i).magnitude-.065f*depth);
        }
        return edge;
    }
    public void Dispose()=>outlines.Clear();
}


