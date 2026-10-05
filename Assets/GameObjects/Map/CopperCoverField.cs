using System;
using System.Collections.Generic;
using UnityEngine;

// Low-resolution, world-aligned distance fields. The immutable deposit footprint
// survives mining; live OreVeinField occupancy alone clips mined cells away.
public sealed class CopperCoverField : IDisposable
{
    public Texture2D Texture { get; private set; }

    public void Build(MapGenerator map, BoundsInt bounds)
    {
        int density=Mathf.Min(8,1024/Mathf.Max(bounds.size.x,bounds.size.y));
        int width=bounds.size.x*density,height=bounds.size.y*density;
        if(!Texture || Texture.width!=width || Texture.height!=height)
        {
            Dispose();
            Texture=new Texture2D(width,height,TextureFormat.RGFloat,false,true)
            {name="Copper cover distances",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,
                hideFlags=HideFlags.HideAndDontSave};
        }
        var pixels=Texture.GetPixelData<Vector2>(0);
        for(int n=0;n<pixels.Length;n++)pixels[n]=new Vector2(0,-2);
        var plans=map.GetComponent<OreOverlayAppearance>().CopperPlans;
        var visible=new HashSet<CopperDepositState>();
        var ores=map.OreOverlay.GetTilesBlock(bounds);
        for(int n=0;n<ores.Length;n++)
        {
            if(ores[n] is not OreTile ore || !ore.block || ore.block.id!=BlockType.CopperOre)continue;
            var cell=new Vector3Int(bounds.xMin+n%bounds.size.x,bounds.yMin+n/bounds.size.x,0);
            if(map.Terrain.HasTile(cell))visible.Add(plans.Get(map,cell));
        }
        foreach(var plan in visible)
        {
            var footprint=new HashSet<Vector3Int>(plan.cells);
            foreach(var cell in plan.cells)
            {
                if(!bounds.Contains(cell))continue;
                var outside=new List<Vector2>();
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                {var next=cell+new Vector3Int(x,y,0);if(!footprint.Contains(next))outside.Add(new Vector2(next.x+.5f,next.y+.5f));}
                for(int y=0;y<density;y++)for(int x=0;x<density;x++)
                {
                    var p=new Vector2(cell.x+(x+.5f)/density,cell.y+(y+.5f)/density);
                    float edge=1.5f;
                    foreach(var hole in outside)
                    {
                        var d=p-hole;
                        edge=Mathf.Min(edge,new Vector2(Mathf.Max(0,Mathf.Abs(d.x)-.5f),Mathf.Max(0,Mathf.Abs(d.y)-.5f)).magnitude);
                    }
                    float opening=-2;
                    for(int n=0;n<plan.segments.Length;n++)
                    {
                        var s=plan.segments[n];var a=new Vector2(s.x,s.y);var b=new Vector2(s.z,s.w);
                        var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.00001f,d.sqrMagnitude));
                        float radius=plan.widths[n].z>.5f?.08f:.12f;
                        opening=Mathf.Max(opening,radius-Vector2.Distance(p,a+d*t));
                    }
                    // A few guaranteed exposures keep even tiny deposits readable.
                    foreach(var b in plan.bodies)
                        if(b.z>.7f)opening=Mathf.Max(opening,Mathf.Min(.68f,b.z*.53f)-Vector2.Distance(p,new Vector2(b.x,b.y)));
                    int index=((cell.y-bounds.yMin)*density+y)*width+(cell.x-bounds.xMin)*density+x;
                    pixels[index]=new Vector2(edge,opening);
                }
            }
        }
        Texture.Apply(false,false);
    }

    public void Dispose()
    {
        if(!Texture)return;
        if(Application.isPlaying)UnityEngine.Object.Destroy(Texture);else UnityEngine.Object.DestroyImmediate(Texture);
        Texture=null;
    }
}
