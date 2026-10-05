using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

// Copper's visual skeleton is independent of gameplay tiles. Each complete deposit is
// traversed so camera scrolling cannot change its branch layout. No persistent save data.
internal static class CopperVeinTopology
{
    static readonly Vector3Int[] Steps = { Vector3Int.right, Vector3Int.up, Vector3Int.left, Vector3Int.down };
    const int ComponentLimit = 4096;
    const byte Copper = (byte)((int)BlockType.CopperOre + 1);
    struct Edge { public int a,b,side; public uint weight; }

    public static void Fill(Tilemap ores, Tilemap terrain, BoundsInt bounds, NativeArray<Color32> pixels, int seed)
    {
        var visited = new HashSet<Vector3Int>();
        var cells = new List<Vector3Int>();
        var indices = new Dictionary<Vector3Int,int>();
        var edges = new List<Edge>();
        bool HasCopper(Vector3Int p)
        {
            if(bounds.Contains(p))return pixels[(p.y-bounds.yMin)*bounds.size.x+p.x-bounds.xMin].r==Copper;
            var ore=ores.GetTile<OreTile>(p);
            return ore && ore.block && ore.block.id==BlockType.CopperOre && terrain.HasTile(p);
        }
        for(int y=bounds.yMin;y<bounds.yMax;y++)for(int x=bounds.xMin;x<bounds.xMax;x++)
        {
            var start=new Vector3Int(x,y,0);
            if(!HasCopper(start) || !visited.Add(start))continue;
            cells.Clear();indices.Clear();edges.Clear();cells.Add(start);indices.Add(start,0);
            bool capped=false;
            for(int n=0;n<cells.Count;n++)
            {
                for(int side=0;side<4;side++)
                {
                    var other=cells[n]+Steps[side];
                    if(indices.ContainsKey(other) || !HasCopper(other))continue;
                    if(cells.Count==ComponentLimit){capped=true;continue;}
                    visited.Add(other);indices.Add(other,cells.Count);cells.Add(other);
                }
            }
            if(capped)
            {
                // Bound pathological user-configured deposits without introducing loops.
                foreach(var p in cells)
                {
                    int mask=0;
                    for(int side=0;side<4;side++)
                    {
                        var q=p+Steps[side];
                        if(!HasCopper(q))continue;
                        var child=side<2?q:p;
                        var parent=side<2?p:q;
                        bool left=HasCopper(child+Vector3Int.left),down=HasCopper(child+Vector3Int.down);
                        var chosen=child+(left&&(!down||(Hash(child,seed,0)&1)==0)?Vector3Int.left:Vector3Int.down);
                        if(chosen==parent)mask|=1<<side;
                    }
                    Write(p,mask);
                }
                continue;
            }
            for(int n=0;n<cells.Count;n++)for(int side=0;side<2;side++)
            {
                if(indices.TryGetValue(cells[n]+Steps[side],out int other))
                    edges.Add(new Edge {a=n,b=other,side=side,weight=Hash(cells[n],seed,side)});
            }
            edges.Sort((a,b)=>{int v=a.weight.CompareTo(b.weight);if(v!=0)return v;
                v=cells[a.a].y.CompareTo(cells[b.a].y);if(v!=0)return v;
                v=cells[a.a].x.CompareTo(cells[b.a].x);return v!=0?v:a.side.CompareTo(b.side);});
            var parents=new int[cells.Count];var masks=new byte[cells.Count];
            for(int n=0;n<parents.Length;n++)parents[n]=n;
            int Root(int n){while(parents[n]!=n){parents[n]=parents[parents[n]];n=parents[n];}return n;}
            foreach(var edge in edges)
            {
                int a=Root(edge.a),b=Root(edge.b);if(a==b)continue;
                parents[b]=a;masks[edge.a]|=(byte)(1<<edge.side);masks[edge.b]|=(byte)(1<<(edge.side+2));
            }
            for(int n=0;n<cells.Count;n++)Write(cells[n],masks[n]);
        }
        void Write(Vector3Int cell,int links)
        {
            if(!bounds.Contains(cell))return;
            int n=(cell.y-bounds.yMin)*bounds.size.x+cell.x-bounds.xMin;
            var pixel=pixels[n];pixel.b=(byte)links;pixels[n]=pixel;
        }
    }

    static uint Hash(Vector3Int p,int seed,int side)
    {
        unchecked {uint h=(uint)p.x*0x9E3779B9u^(uint)p.y*0x85EBCA6Bu^(uint)seed*0xC2B2AE35u^(uint)side*0x27D4EB2Fu;
            h^=h>>16;h*=0x7FEB352Du;h^=h>>15;h*=0x846CA68Bu;return h^(h>>16);}
    }
}
