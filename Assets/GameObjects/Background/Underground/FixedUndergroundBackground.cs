using UnityEngine;

[ExecuteAlways,DisallowMultipleComponent,DefaultExecutionOrder(1450)]
public sealed class FixedUndergroundBackground : MonoBehaviour
{
    public MapGenerator map;
    public ParallaxLayer nearHills;
    public Material material;
    public float topY=1;
    public float yOffset;
    [Min(.1f)] public float textureHeight=11;
    [Min(.01f)] public float fadeDepthBlocks=10;
    [Range(0,1)] public float fadeStart=.3333333f;
    [Range(0,1)] public float fadeEnd=.6666667f;
    [Range(0f,3f)] public float nightBrightnessMultiplier=1.2f;
    Mesh mesh;
    GameObject visual;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;
    MapLighting mapLighting;
    MoonlightController moonlight;
    SkyController sky;
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
                int skipped=0;
                bool surfaceLayer=map.layers.Length>0 && map.layers[0]!=null && map.layers[0].stone &&
                    map.layers[0].stone.id==BlockType.Dirt;
                foreach(var layer in map.layers)
                    if(layer!=null&&layer.startDepth>0)
                    {
                        if(surfaceLayer && skipped++==0)continue;
                        end=Mathf.Min(end,layer.startDepth);
                    }
            }
            float height=end*(map&&map.Terrain?map.Terrain.layoutGrid.cellSize.y:1.1f);
            return new Vector2(height*fadeStart,height*Mathf.Max(fadeStart+.001f,fadeEnd));
        }
    }
    void LateUpdate()=>Refresh();
    public void Refresh(Camera view=null)
    {
        var camera=view?view:Camera.main;
        if(!material||!camera||!camera.orthographic||!map||map.layers==null||map.layers.Length==0)
        {if(visual)visual.SetActive(false);return;}
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
        float cellHeight=map.Terrain?map.Terrain.transform.TransformVector(
            Vector3.up*map.Terrain.layoutGrid.cellSize.y).magnitude:1.1f;
        float surfaceY=map.Terrain?map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y:topY;
        properties.SetFloat("_SurfaceY",surfaceY);
        properties.SetFloat("_LayerFadeWorld",Mathf.Max(.01f,fadeDepthBlocks)*cellHeight);
        var starts=new Vector4(1e9f,1e9f,1e9f,1e9f);
        for(int i=0;i<Mathf.Min(4,map.layers.Length);i++)
        {
            var layer=map.layers[i];
            if(layer==null||!layer.backgroundSprite)continue;
            properties.SetTexture("_Layer"+i+"Tex",layer.backgroundSprite.texture);
            if(i==1)starts.x=layer.startDepth*cellHeight;
            if(i==2)starts.y=layer.startDepth*cellHeight;
            if(i==3)starts.z=layer.startDepth*cellHeight;
        }
        properties.SetVector("_LayerStarts",starts);
        if (!mapLighting) mapLighting=map.GetComponent<MapLighting>();
        if (!moonlight) moonlight=FindFirstObjectByType<MoonlightController>();
        if (!sky) sky=moonlight?moonlight.GetComponent<SkyController>():FindFirstObjectByType<SkyController>();
        float globalLight=moonlight && moonlight.daylight ? moonlight.daylight.intensity : 1f;
        Color globalColor=moonlight && moonlight.daylight ? moonlight.daylight.color : Color.white;
        bool fullGlobalLighting=GameplayTestSettings.GlobalLighting;
        if (fullGlobalLighting) { globalLight=1f; globalColor=Color.white; }
        properties.SetFloat("_GlobalLight",globalLight);
        properties.SetColor("_GlobalLightColor",globalColor);
        float nightBlend=!fullGlobalLighting&&sky&&sky.isActiveAndEnabled?sky.NightBlend:0f;
        properties.SetFloat("_NightBrightnessMultiplier",
            Mathf.Lerp(1f,Mathf.Clamp(nightBrightnessMultiplier,0f,3f),nightBlend));
        if (mapLighting) mapLighting.ApplyBackgroundLighting(properties);
        else properties.SetFloat("_UseMapLighting",0f);
        meshRenderer.SetPropertyBlock(properties);
    }
    void OnDisable()
    {
        if(Application.isPlaying){if(visual)Destroy(visual);if(mesh)Destroy(mesh);}
        else{if(visual)DestroyImmediate(visual);if(mesh)DestroyImmediate(mesh);}
        visual=null;mesh=null;
    }
}
