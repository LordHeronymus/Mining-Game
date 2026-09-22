using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MapGenerator))]
public sealed class UniformStoneAppearance : MonoBehaviour
{
    public Texture2D texture;
    public Texture2D dirtTexture;
    public Texture2D layerOneTexture;
    public Texture2D layerThreeTexture;
    public bool useFrayedEdges;
    public bool useTerrainMasks;
    [Range(0f,2f)] public float edgeDepth=1f;
    [Range(0f,2f)] public float edgeIrregularity=1f;
    [Range(0f,2f)] public float edgeRounding=1f;
    [Range(0f,8f)] public float rubbleAmount=2f;
    [Range(.02f,.6f)] public float rubbleMinSize=.08f;
    [Range(.02f,.6f)] public float rubbleMaxSize=.24f;
    [Range(0f,100f)] public float rubbleProtrusionPercent=50f;
    Texture2DArray edgeMasks;
    MapGenerator map;
    Texture2D occupancy;
    MaterialPropertyBlock properties;
    bool rebuild = true, dirty;
    int left, bottom;
    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall-=RefreshAppearance;
        UnityEditor.EditorApplication.delayCall+=RefreshAppearance;
#endif
    }
    public void RefreshAppearance()
    {
        if(!this||!isActiveAndEnabled)return;
        LateUpdate();
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }
    void OnEnable()
    {
        map = GetComponent<MapGenerator>();
        map.Generated += Rebuild;
        Tilemap.tilemapTileChanged += Changed;
        rebuild = true;
        var rubble=GetComponent<TerrainEdgeRubble>();if(rubble)rubble.enabled=true;
    }
    void Rebuild() => rebuild = true;
    void Changed(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (!map || source != map.Terrain || !occupancy || rebuild) return;
        foreach (var change in changes)
        {
            int x = change.position.x - left, y = change.position.y - bottom;
            if (x < 0 || y < 0 || x >= occupancy.width || y >= occupancy.height) continue;
            var tile = source.GetTile(change.position);
            var color = TileColor(tile);
            if (!tile)
            {
                var previous = occupancy.GetPixel(x,y);
                color.g = (byte)Mathf.RoundToInt(previous.g * 255);
                color.b = (byte)Mathf.RoundToInt(previous.b * 255);
                color.a = (byte)Mathf.RoundToInt(previous.a * 255);
            }
            occupancy.SetPixel(x, y, color);
            dirty = true;
        }
    }
    void LateUpdate()
    {
        if (!map || !map.uniformTestStone || !texture) return;
        if (rebuild)
        {
            Release();
            int width = map.GeneratedWidth, height = map.GeneratedHeight;
            left = -width / 2; bottom = 1 - height;
            // The occupancy mask also drives the visual chipped edges around mined openings.
            occupancy = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { name = "Test stone occupancy", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave };
            var tiles = map.Terrain.GetTilesBlock(new BoundsInt(left, bottom, 0, width, height, 1));
            var colors = new Color32[tiles.Length];
            var sampler = new MapGenerationSampler(map.registry,map.ActiveSeed,height,map.layers,
                map.oreDensityCurve,map.oreDensityMultiplierPercent,map.transitionThickness);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = TileColor(tiles[i]);
                if (!tiles[i])
                {
                    var block = sampler.GetBaseBlock(i % width, -(bottom + i / width));
                    if(map.surfaceDirtTile && block.id == BlockType.Dirt) colors[i].g = 255;
                    if(map.layerOneTile && block == map.layerOneTile.block) colors[i].b = 255;
                    if(map.layerThreeTile && block == map.layerThreeTile.block) colors[i].a = 255;
                }
            }
            occupancy.SetPixels32(colors);
            rebuild = false; dirty = true;
        }
        if (dirty) { occupancy.Apply(false, false); dirty = false; }
        var renderer = GetComponent<TilemapRenderer>();
        properties ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetTexture("_TestStoneTex", texture);
        properties.SetTexture("_SurfaceDirtTex", dirtTexture ? dirtTexture : texture);
        properties.SetTexture("_LayerOneTex", layerOneTexture ? layerOneTexture : texture);
        properties.SetTexture("_LayerThreeTex", layerThreeTexture ? layerThreeTexture : texture);
        properties.SetTexture("_TestOccupancy", occupancy);
        var origin = map.Terrain.CellToWorld(Vector3Int.zero);
        properties.SetVector("_UniformStone", new Vector4(map.Terrain.layoutGrid.cellSize.x, 3f, origin.x, origin.y));
        properties.SetVector("_TestBounds", new Vector4(left, bottom, occupancy.width, occupancy.height));
        if(useTerrainMasks&&!edgeMasks)edgeMasks=Resources.Load<Texture2DArray>("TerrainEdgeMasks");
        bool masked=useTerrainMasks&&edgeMasks;
        properties.SetFloat("_UseTerrainMasks",masked?1:0);
        properties.SetVector("_TerrainEdgeTuning",new Vector4(Mathf.Clamp(edgeDepth,0,2),
            Mathf.Clamp(edgeIrregularity,0,2),Mathf.Clamp(edgeRounding,0,2),0));
        if(edgeMasks)properties.SetTexture("_TerrainEdgeMasks",edgeMasks);
        properties.SetFloat("_FrayedInset",useFrayedEdges&&!masked?.18f:0);
        renderer.SetPropertyBlock(properties);
        GetComponent<OreOverlayAppearance>()?.ApplyTerrain(properties);
        var rubble=GetComponent<TerrainEdgeRubble>();
        if(!rubble)rubble=gameObject.AddComponent<TerrainEdgeRubble>();
        rubble.enabled=rubbleAmount>0;
        if(rubble.enabled)rubble.Apply(properties);
        var frayed=GetComponent<TerrainFrayedEdges>();
        if(useFrayedEdges&&!masked&&!frayed)frayed=gameObject.AddComponent<TerrainFrayedEdges>();
        if(frayed){frayed.enabled=useFrayedEdges&&!masked;if(frayed.enabled)frayed.Apply(properties);}
    }
    Color32 TileColor(TileBase tile) => !tile ? new Color32() :
        new Color32(255, (byte)(map.registry.FromTile(tile)?.id == BlockType.Dirt ? 255 : 0),
            (byte)(map.layerOneTile && map.registry.FromTile(tile) == map.layerOneTile.block ? 255 : 0),
            (byte)(map.layerThreeTile && map.registry.FromTile(tile) == map.layerThreeTile.block ? 255 : 0));
    void OnDisable()
    {
        if (map) map.Generated -= Rebuild;
        Tilemap.tilemapTileChanged -= Changed;
        var renderer = GetComponent<TilemapRenderer>();
        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
        properties.SetFloat("_UseTerrainMasks",0);
        properties.SetVector("_UniformStone", Vector4.zero); renderer.SetPropertyBlock(properties);
        GetComponent<OreOverlayAppearance>()?.ApplyTerrain(properties);
        Release();
        var rubble=GetComponent<TerrainEdgeRubble>();if(rubble)rubble.enabled=false;
        var frayed=GetComponent<TerrainFrayedEdges>();if(frayed)frayed.enabled=false;
    }
    void Release()
    {
        if (!occupancy) return;
        if (Application.isPlaying) Destroy(occupancy); else DestroyImmediate(occupancy);
        occupancy = null;
    }
}
