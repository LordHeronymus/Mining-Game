using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class TerrainFrayedEdges : MonoBehaviour
{
    MapGenerator map;
    GameObject visual;
    Mesh mesh;
    MeshRenderer edgeRenderer;
    Material material;
    bool dirty=true;
    BoundsInt lastBounds;
    readonly List<Vector3> vertices=new();
    readonly List<Vector2> uvs=new();
    readonly List<Color> colors=new();
    readonly List<int> triangles=new();
    static readonly Vector3Int[] directions={Vector3Int.left,Vector3Int.right,Vector3Int.down,Vector3Int.up};
    void OnEnable(){map=GetComponent<MapGenerator>();dirty=true;Tilemap.tilemapTileChanged+=Changed;}
    void Changed(Tilemap tiles,Tilemap.SyncTile[] changes){if(map&&tiles==map.Terrain)dirty=true;}
    public void Apply(MaterialPropertyBlock properties,Camera view=null)
    {
        var camera=view?view:Camera.main;
        if(!enabled||!map||!camera)return;
        if(!material)material=Resources.Load<Material>("FrayedTerrainEdges");
        if(!material)return;
        if(!visual)
        {
            visual=new GameObject("Frayed terrain edges",typeof(MeshFilter),typeof(MeshRenderer));
            visual.hideFlags=HideFlags.HideAndDontSave;visual.layer=gameObject.layer;visual.transform.SetParent(transform,false);
            mesh=new Mesh{name="Frayed terrain sections",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            visual.GetComponent<MeshFilter>().sharedMesh=mesh;edgeRenderer=visual.GetComponent<MeshRenderer>();dirty=true;
        }
        var terrain=map.Terrain.GetComponent<TilemapRenderer>();
        edgeRenderer.sharedMaterial=material;edgeRenderer.sortingLayerID=terrain.sortingLayerID;edgeRenderer.sortingOrder=terrain.sortingOrder+1;
        edgeRenderer.SetPropertyBlock(properties);
        float h=camera.orthographicSize+2,w=h*camera.aspect+2,size=map.Terrain.layoutGrid.cellSize.x;
        var lo=map.Terrain.WorldToCell(camera.transform.position-new Vector3(w,h,0));
        var hi=map.Terrain.WorldToCell(camera.transform.position+new Vector3(w,h,0));
        var bounds=new BoundsInt(lo.x,lo.y,0,hi.x-lo.x+1,hi.y-lo.y+1,1);
        if(!dirty&&bounds==lastBounds)return;
        dirty=false;lastBounds=bounds;vertices.Clear();uvs.Clear();colors.Clear();triangles.Clear();
        foreach(var cell in bounds.allPositionsWithin)
        {
            if(!map.Terrain.HasTile(cell))continue;
            for(int side=0;side<4;side++)
            {
                var n=directions[side];
                if(map.Terrain.HasTile(cell+n)||cell.y==0&&side==3)continue;
                var t=new Vector3Int(-n.y,n.x,0);
                bool joinStart=map.Terrain.HasTile(cell-t)&&!map.Terrain.HasTile(cell-t+n);
                bool joinEnd=map.Terrain.HasTile(cell+t)&&!map.Terrain.HasTile(cell+t+n);
                float edgeIndex=side==0?cell.x:side==1?cell.x+1:side==2?cell.y:cell.y+1;
                float phase=(OreVeins.Hash(map.ActiveSeed,(int)edgeIndex,side,15731)&65535)/65535f;
                var center=map.Terrain.GetCellCenterWorld(cell)+(Vector3)n*size*.5f;
                var gridOrigin=map.Terrain.CellToWorld(Vector3Int.zero);
                float along=Vector3.Dot((center-gridOrigin)/size,(Vector3)t);
                int first=vertices.Count;
                for(int i=0;i<=8;i++)
                {
                    float s=Mathf.Lerp(joinStart?-.5f:-.60f,joinEnd?.5f:.60f,i/8f);
                    float alpha=(joinStart?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.60f,-.35f,s)))*
                        (joinEnd?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(.60f,.35f,s)));
                    for(int j=0;j<2;j++)
                    {
                        var position=center+(Vector3)t*(s*size)+(Vector3)n*((j==0?-.40f:.45f)*size);
                        vertices.Add(transform.InverseTransformPoint(position));uvs.Add(new Vector2((along+s)/3f+phase,j));
                        colors.Add(new Color(alpha,1,1,.25f));
                    }
                    if(i==0)continue;
                    int a=first+(i-1)*2;
                    triangles.Add(a);triangles.Add(a+1);triangles.Add(a+2);
                    triangles.Add(a+1);triangles.Add(a+3);triangles.Add(a+2);
                }
            }
        }
        mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
    }
    void OnDisable()
    {
        Tilemap.tilemapTileChanged-=Changed;
        if(Application.isPlaying){if(visual)Destroy(visual);if(mesh)Destroy(mesh);}
        else{if(visual)DestroyImmediate(visual);if(mesh)DestroyImmediate(mesh);}
        visual=null;mesh=null;
    }
}
