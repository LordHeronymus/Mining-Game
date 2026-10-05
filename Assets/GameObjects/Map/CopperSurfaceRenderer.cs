using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// One camera-bounded surface lets a deposit cross cell boundaries. Actual terrain
// occupancy still cuts out every mined cell in the shader.
public sealed class CopperSurfaceRenderer : IDisposable
{
    public MeshRenderer Renderer {get;private set;}
    Mesh mesh;
    BoundsInt previousBounds;Matrix4x4 previousTransform;bool geometryReady;
    public void Prepare(MapGenerator map,TilemapRenderer source,OreVeinField field,MaterialPropertyBlock properties)
    {
        if(!Renderer)
        {
            var root=new GameObject("Copper deposit surface",typeof(MeshFilter),typeof(MeshRenderer));
            root.hideFlags=HideFlags.HideAndDontSave;root.transform.SetParent(map.transform,false);
            Renderer=root.GetComponent<MeshRenderer>();
            mesh=new Mesh{name="Copper camera surface",hideFlags=HideFlags.HideAndDontSave};
            root.GetComponent<MeshFilter>().sharedMesh=mesh;
            Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;Renderer.receiveShadows=false;
        }
        var bounds=field.Bounds;var a=map.Terrain.CellToWorld(bounds.min);var b=map.Terrain.CellToWorld(bounds.max);
        var t=Renderer.transform;
        var transform=map.Terrain.transform.localToWorldMatrix;
        if(!geometryReady || previousBounds!=bounds || previousTransform!=transform)
        {
        mesh.vertices=new[]{t.InverseTransformPoint(new Vector3(a.x,a.y,0)),t.InverseTransformPoint(new Vector3(b.x,a.y,0)),t.InverseTransformPoint(new Vector3(b.x,b.y,0)),t.InverseTransformPoint(new Vector3(a.x,b.y,0))};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
        mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
        previousBounds=bounds;previousTransform=transform;geometryReady=true;
        }
        Renderer.gameObject.layer=source.gameObject.layer;Renderer.sortingLayerID=source.sortingLayerID;Renderer.sortingOrder=source.sortingOrder+1;
        Renderer.sharedMaterial=source.sharedMaterial;
        properties.SetFloat("_CopperSurfacePass",1);Renderer.SetPropertyBlock(properties);properties.SetFloat("_CopperSurfacePass",0);
        Renderer.enabled=true;
    }
    public void Hide(){if(Renderer)Renderer.enabled=false;}
    public void Dispose(){if(Renderer)Destroy(Renderer.gameObject);if(mesh)Destroy(mesh);Renderer=null;mesh=null;geometryReady=false;}
    static void Destroy(UnityEngine.Object obj){if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
}
