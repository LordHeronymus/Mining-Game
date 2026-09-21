using UnityEngine;
using UnityEngine.Tilemaps;
[ExecuteAlways, DisallowMultipleComponent]
public sealed class DirtSurfaceAppearance : MonoBehaviour
{
    [SerializeField] Material terrainMaterial;
    MapGenerator map;
    MaterialPropertyBlock properties;
    Texture2D mask, deepMask;
    Texture2DArray dirtVariants, stoneVariants, deepVariants;
    Block cachedDirt, cachedStone, cachedDeep;
    int dirtSignature, stoneSignature, deepSignature;
    int appliedSeed=int.MinValue;
    public Material TerrainMaterial {get=>terrainMaterial;set{terrainMaterial=value;Apply();}}
    void OnEnable()=>Apply();
    void OnValidate()=>appliedSeed=int.MinValue;
    void Update()
    {
        if(!map)map=GetComponent<MapGenerator>();
        if(map && map.ActiveSeed!=appliedSeed)Apply();
    }
    public void Apply(bool includeDeepBoundary=true)
    {
        if(!map)map=GetComponent<MapGenerator>();
        var renderer=GetComponent<TilemapRenderer>();
        if(!map || !map.registry || !renderer || !terrainMaterial || !map.Terrain.layoutGrid)return;
        int thickness=Mathf.Clamp(map.transitionThickness,1,100);
        int width=map.GeneratedWidth,height=Mathf.Min(map.GeneratedHeight,MapGenerationSampler.SurfaceDirtRows+thickness+2);
        if(width<=0 || height<=0)return;
        appliedSeed=map.ActiveSeed;
        int boundary=-1;
        Block upper=null,lower=null;
        if(map.layers!=null)
        {
            foreach(var layer in map.layers)
            {
                if(layer==null || !layer.stone)continue;
                if(layer.startDepth==0)upper=layer.stone;
                else if(layer.startDepth>0 && (boundary<0 || layer.startDepth<boundary))
                {boundary=layer.startDepth;lower=layer.stone;}
            }
        }
        upper ??= map.registry.GetById(BlockType.Stone);
        var dirt=map.registry.GetById(BlockType.Dirt);
        SyncVariantArray(dirt,ref cachedDirt,ref dirtSignature,ref dirtVariants);
        SyncVariantArray(upper,ref cachedStone,ref stoneSignature,ref stoneVariants);
        SyncVariantArray(lower,ref cachedDeep,ref deepSignature,ref deepVariants);
        var dirtIndices=BuildIndices(appliedSeed,width,height,dirt);
        var stoneIndices=BuildIndices(appliedSeed,width,height,upper);
        var raw=new float[width*height];
        var placedTiles=new TileBase[raw.Length];
        MapGenerationSampler fallback=null;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var tile=map.Terrain.GetTile(new Vector3Int(x-width/2,-y,0));
            placedTiles[y*width+x]=tile;
            var block=map.registry.FromTile(tile);
            if(block)raw[y*width+x]=block.id==BlockType.Dirt?1f:0f;
            else
            {
                // A mined hole remains empty; only its former material contributes to the border field.
                fallback??=new MapGenerationSampler(map.registry,appliedSeed,map.GeneratedHeight,map.layers,map.oreDensityCurve,map.oreDensityMultiplierPercent,thickness);
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
            int index=y*width+x;
            pixels[index]=new Color32(value,
                (byte)PlacedIndex(dirt,placedTiles[index],dirtIndices[index]),
                (byte)PlacedIndex(upper,placedTiles[index],stoneIndices[index]),255);
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
        if(dirtVariants)properties.SetTexture("_DirtVariants",dirtVariants);
        if(stoneVariants)properties.SetTexture("_StoneVariants",stoneVariants);
        if(deepVariants)properties.SetTexture("_DeepVariants",deepVariants);
        properties.SetVector("_VariantCounts",new Vector4(dirtVariants ? dirtVariants.depth : 0,
            stoneVariants ? stoneVariants.depth : 0,deepVariants ? deepVariants.depth : 0,0));
        properties.SetVector("_DirtMaskBounds",new Vector4(-width/2,0,1f/width,1f/height));
        properties.SetVector("_DirtSurface",new Vector4(map.Terrain.layoutGrid.cellSize.y,
            MapGenerationSampler.SurfaceDirtRows,thickness,
            (OreVeins.Hash(appliedSeed,0,0,0xD17u)%10000u)*.01f));
        int firstRow=Mathf.Max(0,boundary-2);
        int deepHeight=Mathf.Min(map.GeneratedHeight,boundary+thickness+2)-firstRow;
        if(includeDeepBoundary && boundary>0 && upper && lower && upper!=lower && deepHeight>0)
        {
            var upperIndices=BuildIndices(appliedSeed,width,deepHeight,upper,firstRow);
            var lowerIndices=BuildIndices(appliedSeed,width,deepHeight,lower,firstRow);
            var deepRaw=new float[width*deepHeight];
            var deepTiles=new TileBase[deepRaw.Length];
            for(int row=0;row<deepHeight;row++)for(int x=0;x<width;x++)
            {
                int y=firstRow+row;
                var tile=map.Terrain.GetTile(new Vector3Int(x-width/2,-y,0));
                deepTiles[row*width+x]=tile;
                var block=map.registry.FromTile(tile);
                if(!block)
                {
                    fallback??=new MapGenerationSampler(map.registry,appliedSeed,map.GeneratedHeight,map.layers,map.oreDensityCurve,map.oreDensityMultiplierPercent,thickness);
                    block=fallback.GetBaseBlock(x,y);
                }
                deepRaw[row*width+x]=block==upper?1f:0f;
            }
            var deepPixels=new Color32[deepRaw.Length];
            for(int row=0;row<deepHeight;row++)for(int x=0;x<width;x++)
            {
                float total=0;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    total+=deepRaw[Mathf.Clamp(row+dy,0,deepHeight-1)*width+Mathf.Clamp(x+dx,0,width-1)]*
                        (dx==0?2:1)*(dy==0?2:1);
                byte value=(byte)Mathf.RoundToInt(total*255f/16f);
                int index=row*width+x;
                deepPixels[index]=new Color32(value,
                    (byte)PlacedIndex(upper,deepTiles[index],upperIndices[index]),
                    (byte)PlacedIndex(lower,deepTiles[index],lowerIndices[index]),255);
            }
            if(!deepMask || deepMask.width!=width || deepMask.height!=deepHeight)
            {
                ReleaseDeepMask();
                deepMask=new Texture2D(width,deepHeight,TextureFormat.RGBA32,false,true){name="Layer 2 Boundary Mask",
                    hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            }
            deepMask.SetPixels32(deepPixels);deepMask.Apply(false,false);
            properties.SetTexture("_DeepMask",deepMask);
            properties.SetVector("_DeepMaskBounds",new Vector4(-width/2,firstRow,1f/width,1f/deepHeight));
            properties.SetVector("_DeepSurface",new Vector4(map.Terrain.layoutGrid.cellSize.y,boundary,
                thickness,0));
        }
        else
        {
            ReleaseDeepMask();
            properties.SetTexture("_DeepMask",Texture2D.whiteTexture);
            properties.SetVector("_DeepMaskBounds",Vector4.zero);
            properties.SetVector("_DeepSurface",Vector4.zero);
        }
        renderer.SetPropertyBlock(properties);
    }
    void OnDisable()
    {
        var renderer=GetComponent<TilemapRenderer>();
        if(renderer)
        {
            properties??=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
            properties.SetVector("_DirtSurface",Vector4.zero);
            properties.SetVector("_DeepSurface",Vector4.zero);renderer.SetPropertyBlock(properties);
        }
        ReleaseMask();ReleaseDeepMask();
        ReleaseVariantArray(ref dirtVariants);ReleaseVariantArray(ref stoneVariants);ReleaseVariantArray(ref deepVariants);
        cachedDirt=null;cachedStone=null;cachedDeep=null;
        dirtSignature=stoneSignature=deepSignature=0;appliedSeed=int.MinValue;
    }
    void ReleaseMask()
    {
        if(!mask)return;
        if(Application.isPlaying)Destroy(mask);else DestroyImmediate(mask);
        mask=null;
    }
    void ReleaseDeepMask()
    {
        if(!deepMask)return;
        if(Application.isPlaying)Destroy(deepMask);else DestroyImmediate(deepMask);
        deepMask=null;
    }
    static int[] BuildIndices(int seed,int width,int height,Block block,int firstDepth=0)
    {
        var indices=new int[width*height];
        int[] groups=block && block.variants!=null && block.variants.Length>0
            ? TerrainVariantSelector.VisualGroups(block.variants):new[]{0};
        for(int row=0;row<height;row++)for(int x=0;x<width;x++)
        {
            int index=row*width+x;
            indices[index]=TerrainVariantSelector.Choose(seed,x,firstDepth+row,groups,
                x>0?indices[index-1]:-1,row>0?indices[index-width]:-1);
        }
        return indices;
    }
    static int PlacedIndex(Block block,TileBase tile,int fallback)
    {
        if(!block || !tile || block.variants==null)return fallback;
        int index=System.Array.IndexOf(block.variants,tile);
        return index>=0?index:fallback;
    }
    static void ReleaseVariantArray(ref Texture2DArray array)
    {
        if(!array)return;
        if(Application.isPlaying)Destroy(array);else DestroyImmediate(array);
        array=null;
    }
    static void SyncVariantArray(Block block,ref Block cached,ref int cachedSignature,ref Texture2DArray array)
    {
        int signature=17;
        if(block && block.variants!=null)
            foreach(var variant in block.variants)
            {
                var sprite=VariantSprite(variant);
                unchecked{signature=signature*31+(sprite?sprite.GetInstanceID():0);}
            }
        if(cached==block && array && cachedSignature==signature)return;
        ReleaseVariantArray(ref array);cached=block;cachedSignature=signature;
        if(!block || block.variants==null || block.variants.Length==0 || !SystemInfo.supports2DArrayTextures)return;
        var first=VariantTexture(block.variants[0]);
        if(!first)return;
        foreach(var variant in block.variants)
        {
            var source=VariantTexture(variant);
            if(!source || source.width!=first.width || source.height!=first.height ||
                source.format!=first.format || source.mipmapCount!=first.mipmapCount)return;
        }
        array=new Texture2DArray(first.width,first.height,block.variants.Length,first.format,
            first.mipmapCount>1,false){name=block.name+" Boundary Variants",
            hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,
            ignoreMipmapLimit=false};
        for(int i=0;i<block.variants.Length;i++)
        {
            var source=VariantTexture(block.variants[i]);
            for(int mip=0;mip<first.mipmapCount-first.activeMipmapLimit;mip++)
                Graphics.CopyTexture(source,0,mip,array,i,mip);
        }
    }
    static Texture2D VariantTexture(TileBase variant)
    {
        var sprite=VariantSprite(variant);
        return sprite ? sprite.texture : null;
    }
    static Sprite VariantSprite(TileBase variant)
    {
        if(!variant)return null;
        var data=new TileData();
        variant.GetTileData(Vector3Int.zero,null,ref data);
        return data.sprite;
    }
}
