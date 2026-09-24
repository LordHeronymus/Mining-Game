using UnityEngine;

[ExecuteAlways,DisallowMultipleComponent]
public sealed class FixedUndergroundBackground : MonoBehaviour
{
    public MapGenerator map;
    public ParallaxLayer nearHills;
    public Material material;
    public float topY=1;
    public float yOffset;
    [Min(.1f)] public float textureHeight=11;
    [Range(0,1)] public float fadeStart=.3333333f;
    [Range(0,1)] public float fadeEnd=.6666667f;
    Mesh mesh;
    GameObject visual;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;
    readonly Vector3[] corners=new Vector3[4];
    void OnEnable()
    {
        // Generated children can survive an editor domain reload while private references do not.
        for(int i=transform.childCount-1;i>=0;i--)
        {
            var child=transform.GetChild(i).gameObject;
            if(child.name!="Fixed underground (generated)"||child==visual)continue;
            var filter=child.GetComponent<MeshFilter>();var stale=filter?filter.sharedMesh:null;
            child.SetActive(false);
            if(Application.isPlaying){if(stale)Destroy(stale);Destroy(child);}
            else{if(stale)DestroyImmediate(stale);DestroyImmediate(child);}
        }
    }

    public Vector2 FadeDepths
    {
        get
        {
            int end=300;
            if(map&&map.layers!=null)
            {
                end=map.GeneratedHeight;
                foreach(var layer in map.layers)if(layer!=null&&layer.startDepth>0)end=Mathf.Min(end,layer.startDepth);
            }
            float height=end*(map&&map.Terrain?map.Terrain.layoutGrid.cellSize.y:1.1f);
            return new Vector2(height*fadeStart,height*Mathf.Max(fadeStart+.001f,fadeEnd));
        }
    }
    void LateUpdate()=>Refresh();
    public void Refresh(Camera view=null)
    {
        var camera=view?view:Camera.main;
        if(!material||!camera||!camera.orthographic){if(visual)visual.SetActive(false);return;}
        if(!visual)
        {
            visual=new GameObject("Fixed underground (generated)",typeof(MeshFilter),typeof(MeshRenderer));
            visual.hideFlags=HideFlags.HideAndDontSave;visual.layer=gameObject.layer;
            visual.transform.SetParent(transform,false);
            mesh=new Mesh{name="Underground viewport",hideFlags=HideFlags.HideAndDontSave};
            visual.GetComponent<MeshFilter>().sharedMesh=mesh;
            meshRenderer=visual.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial=material;
        }
        visual.SetActive(true);
        meshRenderer.sortingLayerName=nearHills?nearHills.sortingLayerName:"Background";
        meshRenderer.sortingOrder=nearHills?nearHills.sortingOrder+1:31;
        // Only the coverage quad follows the viewport. The shader samples absolute
        // world coordinates, so the visible wall remains fixed with zero parallax.
        var p=camera.transform.position;float h=camera.orthographicSize+1,w=h*camera.aspect+1;
        corners[0]=transform.InverseTransformPoint(new Vector3(p.x-w,p.y-h,0));
        corners[1]=transform.InverseTransformPoint(new Vector3(p.x-w,p.y+h,0));
        corners[2]=transform.InverseTransformPoint(new Vector3(p.x+w,p.y+h,0));
        corners[3]=transform.InverseTransformPoint(new Vector3(p.x+w,p.y-h,0));
        mesh.vertices=corners;mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
        properties??=new MaterialPropertyBlock();
        properties.SetFloat("_TopY",topY+yOffset);
        properties.SetVector("_RepeatSize",new Vector4(textureHeight*1.5f,textureHeight,0,0));
        var fade=FadeDepths;properties.SetVector("_FadeDepth",new Vector4(fade.x,fade.y,0,0));
        meshRenderer.SetPropertyBlock(properties);
    }
    void OnDisable()
    {
        if(Application.isPlaying){if(visual)Destroy(visual);if(mesh)Destroy(mesh);}
        else{if(visual)DestroyImmediate(visual);if(mesh)DestroyImmediate(mesh);}
        visual=null;mesh=null;
    }
}
