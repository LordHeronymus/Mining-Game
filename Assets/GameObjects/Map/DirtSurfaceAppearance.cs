using UnityEngine;
using UnityEngine.Tilemaps;
[ExecuteAlways, DisallowMultipleComponent]
public sealed class DirtSurfaceAppearance : MonoBehaviour
{
    [SerializeField] Material terrainMaterial;
    MapGenerator map;
    MaterialPropertyBlock properties;
    Texture2D mask;
    int appliedSeed=int.MinValue;
    public Material TerrainMaterial {get=>terrainMaterial;set{terrainMaterial=value;Apply();}}
    void OnEnable()=>Apply();
    void OnValidate()=>appliedSeed=int.MinValue;
    void Update()
    {
        if(!map)map=GetComponent<MapGenerator>();
        if(map && map.ActiveSeed!=appliedSeed)Apply();
    }
    public void Apply()
    {
        if(!map)map=GetComponent<MapGenerator>();
        var renderer=GetComponent<TilemapRenderer>();
        if(!map || !map.registry || !renderer || !terrainMaterial || !map.Terrain.layoutGrid)return;
        int width=map.GeneratedWidth,height=Mathf.Min(map.GeneratedHeight,MapGenerationSampler.DirtEndDepth+2);
        if(width<=0 || height<=0)return;
        appliedSeed=map.ActiveSeed;
        var raw=new float[width*height];
        MapGenerationSampler fallback=null;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var block=map.registry.FromTile(map.Terrain.GetTile(new Vector3Int(x-width/2,-y,0)));
            if(block)raw[y*width+x]=block.id==BlockType.Dirt?1f:0f;
            else
            {
                // A mined hole remains empty; only its former material contributes to the border field.
                fallback??=new MapGenerationSampler(map.registry,appliedSeed,map.GeneratedHeight,map.layers,map.oreDensityByDepth);
                raw[y*width+x]=fallback.IsDirtAt(x,y)?1f:0f;
            }
        }
        var pixels=new Color32[raw.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            float total=0;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                total+=raw[Mathf.Clamp(y+dy,0,height-1)*width+Mathf.Clamp(x+dx,0,width-1)]*(dx==0?2:1)*(dy==0?2:1);
            byte value=(byte)Mathf.RoundToInt(total*255f/16f);
            pixels[y*width+x]=new Color32(value,value,value,255);
        }
        if(!mask || mask.width!=width || mask.height!=height)
        {
            ReleaseMask();
            mask=new Texture2D(width,height,TextureFormat.RGBA32,false,true){name="Dirt Boundary Mask",hideFlags=HideFlags.HideAndDontSave,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        }
        mask.SetPixels32(pixels);mask.Apply(false,false);
        renderer.sharedMaterial=terrainMaterial;
        properties??=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
        properties.SetTexture("_DirtMask",mask);
        properties.SetVector("_DirtMaskBounds",new Vector4(-width/2,0,1f/width,1f/height));
        properties.SetVector("_DirtSurface",new Vector4(map.Terrain.layoutGrid.cellSize.y,
            MapGenerationSampler.SurfaceDirtRows,MapGenerationSampler.DirtTransitionRows,
            (OreVeins.Hash(appliedSeed,0,0,0xD17u)%10000u)*.01f));
        renderer.SetPropertyBlock(properties);
    }
    void OnDisable()
    {
        var renderer=GetComponent<TilemapRenderer>();
        if(renderer)
        {
            properties??=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
            properties.SetVector("_DirtSurface",Vector4.zero);renderer.SetPropertyBlock(properties);
        }
        ReleaseMask();appliedSeed=int.MinValue;
    }
    void ReleaseMask()
    {
        if(!mask)return;
        if(Application.isPlaying)Destroy(mask);else DestroyImmediate(mask);
        mask=null;
    }
}