using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class CopperDepositState
{
    public Vector3Int[] cells;
    public Vector4[] segments, widths, bodies, details, covers;
    public float phase;
}

// Plan the full deposit once. Mining cuts its surface, never recomposes the surviving vein.
public sealed class CopperDepositPlans
{
    public const int MaxSegments=48, MaxBodies=12, TextureWidth=128;
    readonly Dictionary<Vector3Int,CopperDepositState> byCell=new();
    readonly List<CopperDepositState> deposits=new();
    public void Clear(){byCell.Clear();deposits.Clear();}
    public CopperDepositState[] Capture()=>deposits.ToArray();
    public void Restore(CopperDepositState[] saved)
    {
        Clear();
        foreach(var plan in saved??Array.Empty<CopperDepositState>())
        {
            if(plan?.cells==null || plan.cells.Length>4096 || plan.segments==null || plan.widths==null ||
                plan.segments.Length>MaxSegments || plan.widths.Length!=plan.segments.Length ||
                plan.bodies==null || plan.details==null || plan.bodies.Length>MaxBodies || plan.details.Length!=plan.bodies.Length)continue;
            plan.covers??=Array.Empty<Vector4>();
            if(plan.covers.Length>7)continue;
            deposits.Add(plan);foreach(var p in plan.cells)byCell[p]=plan;
        }
    }
    public CopperDepositState Get(MapGenerator map,Vector3Int cell)
    {
        if(byCell.TryGetValue(cell,out var known))return known;
        var cells=new List<Vector3Int>{cell};var seen=new HashSet<Vector3Int>{cell};
        var steps=new[]{Vector3Int.right,Vector3Int.up,Vector3Int.left,Vector3Int.down};
        for(int n=0;n<cells.Count && cells.Count<4096;n++)foreach(var step in steps)
        {
            if(cells.Count>=4096)break;
            var p=cells[n]+step;if(!seen.Add(p))continue;
            var ore=map.OreOverlay.GetTile<OreTile>(p);
            if(ore && ore.block && ore.block.id==BlockType.CopperOre && map.Terrain.HasTile(p))cells.Add(p);
        }
        cells.Sort((a,b)=>a.y!=b.y?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
        var result=Build(cells,map.ActiveSeed);
        deposits.Add(result);foreach(var p in cells)if(!byCell.ContainsKey(p))byCell[p]=result;
        return result;
    }
    static CopperDepositState Build(List<Vector3Int> cells,int seed)
    {
        var rng=new System.Random(unchecked(seed ^ cells[0].x*73856093 ^ cells[0].y*19349663));
        float Rand()=> (float)rng.NextDouble();
        var points=cells.Select(p=>new Vector2(p.x+.5f,p.y+.5f)).ToArray();
        Vector2 mean=Vector2.zero;foreach(var p in points)mean+=p;mean/=points.Length;
        float xx=0,xy=0,yy=0;foreach(var p in points){var d=p-mean;xx+=d.x*d.x;xy+=d.x*d.y;yy+=d.y*d.y;}
        float angle=.5f*Mathf.Atan2(2*xy,xx-yy);
        var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));if(axis.y<0)axis=-axis;
        var normal=new Vector2(-axis.y,axis.x);
        float lo=float.MaxValue,hi=float.MinValue;
        foreach(var p in points){float t=Vector2.Dot(p-mean,axis);lo=Mathf.Min(lo,t);hi=Mathf.Max(hi,t);}
        float phase=Rand()*6.28f;
        int knots=Mathf.Clamp(Mathf.CeilToInt((hi-lo)/1.8f)+2,2,9);
        var spine=new List<Vector2>();
        for(int n=0;n<knots;n++)
        {
            float t=Mathf.Lerp(lo-.25f,hi+.25f,n/(float)(knots-1));float sum=0,total=0;
            foreach(var p in points){float dt=Vector2.Dot(p-mean,axis)-t;float w=Mathf.Exp(-dt*dt*.65f);sum+=Vector2.Dot(p-mean,normal)*w;total+=w;}
            float across=sum/Mathf.Max(.001f,total)+Mathf.Sin(t*1.16f+phase)*.28f;
            spine.Add(mean+axis*t+normal*across);
        }
        var segments=new List<Vector4>();var widths=new List<Vector4>();
        void Add(Vector2 a,Vector2 b,float wa,float wb){if(segments.Count>=MaxSegments)return;segments.Add(new Vector4(a.x,a.y,b.x,b.y));widths.Add(new Vector4(wa,wb,0,0));}
        // Smooth the common trunk independently of cell centres and cardinal adjacency.
        var smooth=new List<Vector2>();
        for(int n=0;n<spine.Count-1;n++)for(int k=0;k<3;k++)
        {
            float t=k/3f;var a=spine[Mathf.Max(0,n-1)];var b=spine[n];var c=spine[n+1];var d=spine[Mathf.Min(spine.Count-1,n+2)];
            smooth.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
        }
        smooth.Add(spine[spine.Count-1]);
        for(int n=0;n<smooth.Count-1;n++)
        {
            float u=n/(float)(smooth.Count-1),v=(n+1f)/(smooth.Count-1);
            Add(smooth[n],smooth[n+1],.025f+.105f*Mathf.Pow(Mathf.Sin(u*Mathf.PI),.65f),.025f+.105f*Mathf.Pow(Mathf.Sin(v*Mathf.PI),.65f));
        }
        Vector2 Closest(Vector2 p)
        {
            float best=float.MaxValue;Vector2 result=spine[0];
            for(int n=0;n<smooth.Count-1;n++){var d=smooth[n+1]-smooth[n];var q=smooth[n]+d*Mathf.Clamp01(Vector2.Dot(p-smooth[n],d)/Mathf.Max(.001f,d.sqrMagnitude));float dist=(q-p).sqrMagnitude;if(dist<best){best=dist;result=q;}}
            return result;
        }
        var tips=new List<Vector2>();
        foreach(var p in points.OrderByDescending(p=>(p-Closest(p)).sqrMagnitude))
        {
            var q=Closest(p);if((p-q).magnitude<.7f || tips.Any(t=>(p-t).magnitude<1.55f) || tips.Count>=7)continue;
            var tip=p+new Vector2(Rand()-.5f,Rand()-.5f)*.45f;
            var attach=Closest(p-axis*.6f);var bend=Vector2.Lerp(attach,tip,.53f)+axis*.16f;
            Add(attach,bend,.065f,.045f);widths[widths.Count-1]=new Vector4(.065f,.045f,1,0);
            Add(bend,tip,.045f,.009f);widths[widths.Count-1]=new Vector4(.045f,.009f,1,0);tips.Add(tip);
        }
        var bodies=new List<Vector4>();var details=new List<Vector4>();
        void Body(Vector2 p,float size,int variant)
        {if(bodies.Count>=MaxBodies)return;bodies.Add(new Vector4(p.x,p.y,size,variant));details.Add(new Vector4((Rand()-.5f)*.9f,0,0,0));}
        int large=Mathf.Clamp(Mathf.RoundToInt(cells.Count/7f),1,4);
        for(int n=0;n<large;n++)
        {
            float t=Mathf.Lerp(lo,hi,(n+.55f)/(large+.1f));
            Vector2 ideal=mean+axis*t;
            var p=points.OrderBy(p=>Mathf.Abs(Vector2.Dot(p-ideal,axis))+(p-Closest(p)).magnitude*.65f).First();
            p+=normal*(Rand()-.5f)*.48f;
            Body(p,.86f+Rand()*.30f,n%3);
        }
        for(int n=0;n<tips.Count;n++)if(n%3!=1 && !bodies.Any(b=>Vector2.Distance(new Vector2(b.x,b.y),tips[n])<.85f))Body(tips[n],.24f+Rand()*.20f,3+rng.Next(3));
        for(int n=0;n<smooth.Count && bodies.Count<MaxBodies;n+=3)
            if(!bodies.Any(b=>Vector2.Distance(new Vector2(b.x,b.y),smooth[n])<.9f))Body(smooth[n],.16f+Rand()*.21f,3+rng.Next(3));
        var covers=new List<Vector4>();
        for(int n=0;n<large;n++)
        {var b=bodies[n];covers.Add(new Vector4(b.x+(n%2==0?-.32f:.32f),b.y+.03f,b.z*1.25f,n%6));}
        for(int n=3;n<smooth.Count-2 && covers.Count<7;n+=5)
        {var p=smooth[n];if(bodies.Any(b=>b.z>.7f && Vector2.Distance(p,new Vector2(b.x,b.y))<.8f))continue;covers.Add(new Vector4(p.x+(Rand()-.5f)*.35f,p.y,.72f+Rand()*.32f,covers.Count%6));}
        return new CopperDepositState{cells=cells.ToArray(),segments=segments.ToArray(),widths=widths.ToArray(),bodies=bodies.ToArray(),details=details.ToArray(),covers=covers.ToArray(),phase=phase};
    }
}
