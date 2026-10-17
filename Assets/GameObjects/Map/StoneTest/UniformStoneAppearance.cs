using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MapGenerator))]
public sealed class UniformStoneAppearance : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);
    public Texture2D texture;
    public Texture2D dirtTexture;
    public Texture2D layerOneTexture;
    public Texture2D layerThreeTexture;
    public Texture2D layerFourTexture;
    public bool useFrayedEdges;
    public bool useTerrainMasks;
    [Range(0f,2f)] public float edgeDepth=1f;
    [Range(0f,2f)] public float edgeIrregularity=1f;
    [Range(0f,2f)] public float edgeRounding=1f;
    [Range(0f,.3f)] public float colliderInset=.08f;
    [Range(0f,8f)] public float rubbleAmount=2f;
    [Range(.02f,.6f)] public float rubbleMinSize=.08f;
    [Range(.02f,.6f)] public float rubbleMaxSize=.24f;
    [Range(0f,100f)] public float rubbleProtrusionPercent=50f;
    Texture2DArray edgeMasks;
    MapGenerator map;
    Texture2D occupancy;
    Texture2D occupancyUpload;
    MaterialPropertyBlock properties;
    bool rebuild = true, fullUpload, dirtyRegion, preparedForStreamingGeneration;
    int left, bottom;
    int dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY;
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
        propertiesDirty = true;
        LateUpdate();
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }
    void OnEnable()
    {
        map = GetComponent<MapGenerator>();
        map.Generated += Rebuild;
        Tilemap.tilemapPositionsChanged += Changed;
        rebuild = true; propertiesDirty = true;
        var rubble=GetComponent<TerrainEdgeRubble>();if(rubble)rubble.enabled=true;
    }
    void Rebuild()
    {
        if (preparedForStreamingGeneration) { preparedForStreamingGeneration = false; return; }
        rebuild = true;
    }
    void Changed(Tilemap source, NativeArray<Vector3Int> positions)
    {
        if (!map || source != map.Terrain || !occupancy || rebuild ||
            (Application.isPlaying && map.IsGenerationStreaming)) return;
        foreach (var position in positions)
        {
            int x = position.x - left, y = position.y - bottom;
            if (x < 0 || y < 0 || x >= occupancy.width || y >= occupancy.height) continue;
            var tile = source.GetTile(position);
            var color = TileColor(tile);
            if (!tile)
            {
                var previous = occupancy.GetPixel(x,y);
                color.g = (byte)Mathf.RoundToInt(previous.g * 255);
                color.b = (byte)Mathf.RoundToInt(previous.b * 255);
                color.a = (byte)Mathf.RoundToInt(previous.a * 255);
            }
            occupancy.SetPixel(x, y, color);
            if (!dirtyRegion)
            {
                dirtyMinX = dirtyMaxX = x;
                dirtyMinY = dirtyMaxY = y;
                dirtyRegion = true;
            }
            else
            {
                dirtyMinX = Mathf.Min(dirtyMinX, x); dirtyMaxX = Mathf.Max(dirtyMaxX, x);
                dirtyMinY = Mathf.Min(dirtyMinY, y); dirtyMaxY = Mathf.Max(dirtyMaxY, y);
            }
        }
    }
    static readonly Unity.Profiling.ProfilerMarker UploadMarker = new("Mining.TerrainUpload"),
        PropertiesMarker = new("Mining.TerrainProperties"), RubbleMarker = new("Mining.TerrainRubble");
    static readonly int TestStoneTexId = Shader.PropertyToID("_TestStoneTex"), SurfaceDirtTexId = Shader.PropertyToID("_SurfaceDirtTex"),
        LayerOneTexId = Shader.PropertyToID("_LayerOneTex"), LayerThreeTexId = Shader.PropertyToID("_LayerThreeTex"),
        LayerFourTexId = Shader.PropertyToID("_LayerFourTex"), TestOccupancyId = Shader.PropertyToID("_TestOccupancy"),
        UniformStoneId = Shader.PropertyToID("_UniformStone"), TestBoundsId = Shader.PropertyToID("_TestBounds"),
        UseTerrainMasksId = Shader.PropertyToID("_UseTerrainMasks"), TerrainEdgeTuningId = Shader.PropertyToID("_TerrainEdgeTuning"),
        TerrainEdgeMasksId = Shader.PropertyToID("_TerrainEdgeMasks"), FrayedInsetId = Shader.PropertyToID("_FrayedInset");
    // The terrain properties only change with the occupancy texture or a tuning change (GPS/Inspector call
    // RefreshAppearance). Pushing them to three renderers and every rubble chunk each frame cost up to several ms.
    bool propertiesDirty = true;
    const int UploadPatchSize = 32;
    TilemapRenderer terrainRenderer, appliedArtifactRenderer;
    OreOverlayAppearance appliedOreOverlay;

    void LateUpdate()
    {
        if (!map || !map.uniformTestStone || !texture) return;
        if (Application.isPlaying && (!map.IsGenerated || map.IsGenerationStreaming)) return;
        if (rebuild)
        {
            int width = map.GeneratedWidth, height = map.GeneratedHeight;
            var tiles = map.Terrain.GetTilesBlock(new BoundsInt(-width / 2, 1 - height, 0, width, height, 1));
            var steps = BuildOccupancy(tiles, width, height, map.ActiveSeed);
            while (steps.MoveNext()) { }
        }
        using (UploadMarker.Auto())
        {
            if (fullUpload)
            {
                occupancy.Apply(false, false);
                fullUpload = false;
                dirtyRegion = false;
            }
            else if (dirtyRegion) UploadOccupancyRegion();
        }
        using var measure = PropertiesMarker.Auto();
        if(useTerrainMasks&&!edgeMasks)edgeMasks=Resources.Load<Texture2DArray>("TerrainEdgeMasks");
        bool masked=useTerrainMasks&&edgeMasks;
        var oreOverlay = GetComponent<OreOverlayAppearance>();
        var artifactRenderer = map.ArtifactOverlay ? map.ArtifactOverlay.GetComponent<TilemapRenderer>() : null;
        if (oreOverlay != appliedOreOverlay || artifactRenderer != appliedArtifactRenderer) propertiesDirty = true;
        bool pushed = propertiesDirty;
        if (propertiesDirty)
        {
            if (!terrainRenderer) terrainRenderer = GetComponent<TilemapRenderer>();
            properties ??= new MaterialPropertyBlock();
            terrainRenderer.GetPropertyBlock(properties);
            properties.SetTexture(TestStoneTexId, texture);
            properties.SetTexture(SurfaceDirtTexId, dirtTexture ? dirtTexture : texture);
            properties.SetTexture(LayerOneTexId, layerOneTexture ? layerOneTexture : texture);
            properties.SetTexture(LayerThreeTexId, layerThreeTexture ? layerThreeTexture : texture);
            properties.SetTexture(LayerFourTexId, layerFourTexture ? layerFourTexture : layerThreeTexture ? layerThreeTexture : texture);
            properties.SetTexture(TestOccupancyId, occupancy);
            var origin = map.Terrain.CellToWorld(Vector3Int.zero);
            properties.SetVector(UniformStoneId, new Vector4(map.Terrain.layoutGrid.cellSize.x, 3f, origin.x, origin.y));
            properties.SetVector(TestBoundsId, new Vector4(left, bottom, occupancy.width, occupancy.height));
            properties.SetFloat(UseTerrainMasksId,masked?1:0);
            properties.SetVector(TerrainEdgeTuningId,new Vector4(Mathf.Clamp(edgeDepth,0,2),
                Mathf.Clamp(edgeIrregularity,0,2),Mathf.Clamp(edgeRounding,0,2),0));
            if(edgeMasks)properties.SetTexture(TerrainEdgeMasksId,edgeMasks);
            properties.SetFloat(FrayedInsetId,useFrayedEdges&&!masked?.18f:0);
            terrainRenderer.SetPropertyBlock(properties);
            oreOverlay?.ApplyTerrain(properties);
            if (artifactRenderer) ArtifactOverlayAppearance.ApplyTerrain(artifactRenderer, properties, map.artifactEmbeddingStrength);
            appliedOreOverlay = oreOverlay; appliedArtifactRenderer = artifactRenderer;
            propertiesDirty = false;
        }
        var rubble=GetComponent<TerrainEdgeRubble>();
        if(!rubble)rubble=gameObject.AddComponent<TerrainEdgeRubble>();
        rubble.enabled=rubbleAmount>0;
        if(rubble.enabled)using(RubbleMarker.Auto())rubble.Apply(properties, pushed);
        var frayed=GetComponent<TerrainFrayedEdges>();
        if(useFrayedEdges&&!masked&&!frayed)frayed=gameObject.AddComponent<TerrainFrayedEdges>();
        if(frayed){frayed.enabled=useFrayedEdges&&!masked;if(frayed.enabled)frayed.Apply(properties);}
    }
    public IEnumerator PrepareForLoading(MapGenerator.MapGenerationSnapshot snapshot, System.Action<float> progress = null)
    {
        if (!map || !map.uniformTestStone || !texture || snapshot == null) yield break;
        var steps = BuildOccupancy(snapshot.terrainTiles, snapshot.width, snapshot.height, snapshot.seed, progress);
        while (steps.MoveNext()) yield return null;
        yield return null;
        occupancy.Apply(false, false); fullUpload = false;
        preparedForStreamingGeneration = true;
    }

    IEnumerator BuildOccupancy(TileBase[] tiles, int width, int height, int seed, System.Action<float> progress = null)
    {
        Release(); left = -width / 2; bottom = 1 - height;
        occupancy = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        { name = "Test stone occupancy", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave };
        var colors = new Color32[tiles.Length];
        var tileColors = new Dictionary<TileBase, Color32>();
        var sampler = new MapGenerationSampler(map.registry, seed, height, map.layers,
            map.oreDensityCurve, map.oreDensityMultiplierPercent, map.transitionThickness, prepareNoise: false);
        var budget = new LoadingWorkBudget();
        for (int i = 0; i < colors.Length; i++)
        {
            var tile = tiles[i];
            if (tile)
            {
                if (!tileColors.TryGetValue(tile, out var color)) tileColors.Add(tile, color = TileColor(tile));
                colors[i] = color;
            }
            else
            {
                var block = sampler.GetBaseBlock(i % width, -(bottom + i / width));
                if (map.surfaceDirtTile && block.id == BlockType.Dirt) colors[i].g = 255;
                if (map.layerOneTile && block == map.layerOneTile.block) colors[i].b = 255;
                if (map.layerThreeTile && block == map.layerThreeTile.block) colors[i].a = 128;
                if (map.layerFourTile && block == map.layerFourTile.block) colors[i].a = 255;
            }
            if ((i & 255) == 255 && budget.Expired)
            {
                progress?.Invoke((i + 1f) / colors.Length);
                yield return null; budget.Restart();
            }
        }
        occupancy.SetPixels32(colors);
        rebuild = false; fullUpload = true; dirtyRegion = false; propertiesDirty = true;
        progress?.Invoke(1f);
    }

    Color32 TileColor(TileBase tile) => !tile ? new Color32() :
        new Color32(255, (byte)(map.registry.FromTile(tile)?.id == BlockType.Dirt ? 255 : 0),
            (byte)(map.layerOneTile && map.registry.FromTile(tile) == map.layerOneTile.block ? 255 : 0),
            (byte)(map.layerFourTile && map.registry.FromTile(tile) == map.layerFourTile.block ? 255 :
                map.layerThreeTile && map.registry.FromTile(tile) == map.layerThreeTile.block ? 128 : 0));

    void UploadOccupancyRegion()
    {
        int width = dirtyMaxX - dirtyMinX + 1, height = dirtyMaxY - dirtyMinY + 1;
        if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
        {
            occupancy.Apply(false, false);
            dirtyRegion = false;
            return;
        }
        // Mining edits a few cells per frame. One reusable patch texture avoids creating a texture whenever
        // the size of the edited region changes; larger edits upload the whole occupancy texture.
        if (width > UploadPatchSize || height > UploadPatchSize)
        {
            occupancy.Apply(false, false);
            dirtyRegion = false;
            return;
        }
        if (!occupancyUpload)
            occupancyUpload = new Texture2D(UploadPatchSize, UploadPatchSize, TextureFormat.RGBA32, false, true)
            { name = "Terrain occupancy update", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        try
        {
            var sourcePixels = occupancy.GetPixelData<Color32>(0);
            var patchPixels = occupancyUpload.GetPixelData<Color32>(0);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                patchPixels[y * UploadPatchSize + x] = sourcePixels[(dirtyMinY + y) * occupancy.width + dirtyMinX + x];
            occupancyUpload.Apply(false, false);
            Graphics.CopyTexture(occupancyUpload, 0, 0, 0, 0, width, height,
                occupancy, 0, 0, dirtyMinX, dirtyMinY);
        }
        catch (UnityException)
        {
            // The readable texture still contains the same edited pixels for the full upload path.
            occupancy.Apply(false, false);
        }
        dirtyRegion = false;
    }
    void OnDisable()
    {
        if (map) map.Generated -= Rebuild;
        Tilemap.tilemapPositionsChanged -= Changed;
        var renderer = GetComponent<TilemapRenderer>();
        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
        properties.SetFloat("_UseTerrainMasks",0);
        properties.SetVector("_UniformStone", Vector4.zero); renderer.SetPropertyBlock(properties);
        GetComponent<OreOverlayAppearance>()?.ApplyTerrain(properties);
        if (map && map.ArtifactOverlay)
            ArtifactOverlayAppearance.ApplyTerrain(map.ArtifactOverlay.GetComponent<TilemapRenderer>(),
                properties, map.artifactEmbeddingStrength);
        Release();
        var rubble=GetComponent<TerrainEdgeRubble>();if(rubble)rubble.enabled=false;
        var frayed=GetComponent<TerrainFrayedEdges>();if(frayed)frayed.enabled=false;
    }
    void Release()
    {
        if (occupancy)
        {
            if (Application.isPlaying) Destroy(occupancy); else DestroyImmediate(occupancy);
            occupancy = null;
        }
        if (occupancyUpload)
        {
            if (Application.isPlaying) Destroy(occupancyUpload); else DestroyImmediate(occupancyUpload);
            occupancyUpload = null;
        }
        fullUpload = dirtyRegion = preparedForStreamingGeneration = false;
    }
}
