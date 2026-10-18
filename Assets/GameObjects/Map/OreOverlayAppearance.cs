using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Collections;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class OreOverlayAppearance : MonoBehaviour
{
    [SerializeField] Material overlayMaterial;
    MapGenerator map, subscribedMap;
    TilemapRenderer target;
    MaterialPropertyBlock properties;
    float appliedScale = -1;
    int appliedPulsesPerMinute = -1;
    static readonly int ScaleProperty = Shader.PropertyToID("_OreScale");
    static readonly int PulseRateProperty = Shader.PropertyToID("_UltroniumPulsesPerMinute");
    readonly Dictionary<Camera, OreVeinField> veinFields = new();
    readonly List<Camera> expiredViews = new();
    Texture2D fissureArt, copperCarpet, copperEarthWall, copperL1Sprite, copperL1Large, copperL1Small;
    Texture2D copperL2Small, copperL2Medium, copperL2Large;
    Texture2DArray copperRockCombined;
    public CopperDepositPlans CopperPlans { get; } = new CopperDepositPlans();
    readonly CopperSurfaceRenderer copperSurface=new();
    public MeshRenderer CopperSurface=>copperSurface.Renderer;

    public Material OverlayMaterial
    {
        get => overlayMaterial;
        set { overlayMaterial = value; appliedScale = -1; Update(); }
    }

    void OnEnable()
    {
        appliedScale = -1;
        map = GetComponent<MapGenerator>();
        Tilemap.tilemapPositionsChanged += VeinTilesChanged;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        Update();
    }
    void OnValidate() => appliedScale = -1;

    void InvalidateVeins() { CopperPlans.Clear(); foreach (var field in veinFields.Values) field.Invalidate(); }
    public void RestoreCopperPlans(CopperDepositState[] saved)
    {CopperPlans.Restore(saved);foreach(var field in veinFields.Values)field.Invalidate();}
    void VeinTilesChanged(Tilemap tiles, NativeArray<Vector3Int> positions)
    { foreach (var field in veinFields.Values) field.Changed(tiles, positions); }

    void BeforeCamera(ScriptableRenderContext context, Camera camera) => PrepareVeins(camera);

    static readonly int UniformStoneId = Shader.PropertyToID("_UniformStone"), VeinEnabledId = Shader.PropertyToID("_VeinEnabled"),
        VeinReliefId = Shader.PropertyToID("_VeinRelief"), CopperCarpetId = Shader.PropertyToID("_CopperCarpet"),
        CopperEarthWallId = Shader.PropertyToID("_CopperEarthWall"), CopperCarpetEnabledId = Shader.PropertyToID("_CopperCarpetEnabled"),
        CopperL1SpriteId = Shader.PropertyToID("_CopperL1Sprite"), CopperL1LargeId = Shader.PropertyToID("_CopperL1Large"),
        CopperL1SmallId = Shader.PropertyToID("_CopperL1Small"), CopperEarthTilesEnabledId = Shader.PropertyToID("_CopperEarthTilesEnabled"),
        CopperL2SmallId = Shader.PropertyToID("_CopperL2Small"), CopperL2MediumId = Shader.PropertyToID("_CopperL2Medium"),
        CopperL2LargeId = Shader.PropertyToID("_CopperL2Large"), CopperL2SpritesEnabledId = Shader.PropertyToID("_CopperL2SpritesEnabled"),
        CopperRockCombinedId = Shader.PropertyToID("_CopperRockCombined"), CopperRockTilesEnabledId = Shader.PropertyToID("_CopperRockTilesEnabled");

    public void PrepareVeins(Camera camera)
    {
        copperSurface.Hide();
        if (!isActiveAndEnabled || !map || !map.OreOverlay || !camera || !overlayMaterial) return;
        // While playing, only game cameras get a vein field; editor Scene views would otherwise rebuild a second one.
        if (Application.isPlaying && camera.cameraType != CameraType.Game) return;
        if (!target) ApplyTo(map.OreOverlay.GetComponent<TilemapRenderer>());
        if (!target) return;
        properties ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        bool visible = (camera.cullingMask & (1 << map.OreOverlay.gameObject.layer)) != 0;
        if (visible && !map.IsGenerationStreaming && properties.GetVector(UniformStoneId).x > 0)
        {
            if (!fissureArt) fissureArt = Resources.Load<Texture2D>("OreVeins/RockFissure");
            if (fissureArt) properties.SetTexture(VeinReliefId, fissureArt);
            if (!copperCarpet) copperCarpet = Resources.Load<Texture2D>("OreVeins/CopperCarpet/Copper");
            if (copperCarpet) properties.SetTexture(CopperCarpetId, copperCarpet);
            if (!copperEarthWall) copperEarthWall = Resources.Load<Texture2D>("OreVeins/CopperEarthWall/EarthWall");
            if (copperEarthWall) properties.SetTexture(CopperEarthWallId, copperEarthWall);
            properties.SetFloat(CopperCarpetEnabledId, copperCarpet ? 1 : 0);
            if (!copperL1Sprite) copperL1Sprite = Resources.Load<Texture2D>("OreVeins/CopperL1Sprite/CopperEmbedded");
            if (copperL1Sprite) properties.SetTexture(CopperL1SpriteId, copperL1Sprite);
            if (!copperL1Large) copperL1Large = Resources.Load<Texture2D>("OreVeins/CopperL1Sprite/CopperLarge");
            if (copperL1Sprite) properties.SetTexture(CopperL1LargeId, copperL1Large ? copperL1Large : copperL1Sprite);
            if (!copperL1Small) copperL1Small = Resources.Load<Texture2D>("OreVeins/CopperL1Sprite/CopperSmall");
            if (copperL1Sprite) properties.SetTexture(CopperL1SmallId, copperL1Small ? copperL1Small : copperL1Sprite);
            properties.SetFloat(CopperEarthTilesEnabledId, copperL1Sprite ? 1 : 0);
            if (!copperL2Small) copperL2Small = Resources.Load<Texture2D>("OreVeins/CopperL2Sprite/CopperSmall");
            if (!copperL2Medium) copperL2Medium = Resources.Load<Texture2D>("OreVeins/CopperL2Sprite/CopperMedium");
            if (!copperL2Large) copperL2Large = Resources.Load<Texture2D>("OreVeins/CopperL2Sprite/CopperLarge");
            if (copperL2Small) properties.SetTexture(CopperL2SmallId, copperL2Small);
            if (copperL2Medium) properties.SetTexture(CopperL2MediumId, copperL2Medium);
            if (copperL2Large) properties.SetTexture(CopperL2LargeId, copperL2Large);
            properties.SetFloat(CopperL2SpritesEnabledId, copperL2Small && copperL2Medium && copperL2Large ? 1 : 0);
            if (!copperRockCombined) copperRockCombined = Resources.Load<Texture2DArray>("OreVeins/CopperRockTiles/Combined");
            if (copperRockCombined) properties.SetTexture(CopperRockCombinedId, copperRockCombined);
            properties.SetFloat(CopperRockTilesEnabledId, copperRockCombined && copperRockCombined.depth == 4 ? 1 : 0);
            if (!veinFields.TryGetValue(camera, out var field))
                veinFields.Add(camera, field = new OreVeinField());
            if (field.Prepare(map, camera))
            // All copper sizes are painted ore tiles now (Docs/Kupfer-Large.md); the continuous copper surface stays hidden.
            field.Bind(properties, map);
            else properties.SetFloat(VeinEnabledId, 0);
        }
        else properties.SetFloat(VeinEnabledId, 0);
        target.SetPropertyBlock(properties);
    }

    void Update()
    {
        expiredViews.Clear();
        foreach (var entry in veinFields)
            if (!entry.Key) { entry.Value.Dispose(); expiredViews.Add(entry.Key); }
        foreach (var view in expiredViews) veinFields.Remove(view);
        if (!map) map = GetComponent<MapGenerator>();
        if (map && map != subscribedMap)
        {
            if (subscribedMap) subscribedMap.Generated -= InvalidateVeins;
            subscribedMap = map;
            subscribedMap.Generated += InvalidateVeins;
        }
        if (!map || !map.OreOverlay || !overlayMaterial) return;
        var renderer = map.OreOverlay.GetComponent<TilemapRenderer>();
        float scale = ValidScale(map.oreScale);
        var lighting = GetComponent<MapLighting>();
        int pulsesPerMinute = lighting ? Mathf.Clamp(lighting.ultroniumPulsesPerMinute, 0, 120) : 0;
        if (target != renderer || appliedScale != scale || appliedPulsesPerMinute != pulsesPerMinute ||
            renderer.sharedMaterial != overlayMaterial)
            ApplyTo(renderer);
    }

    public static float ValidScale(float value) => float.IsNaN(value) || float.IsInfinity(value)
        ? 1f : Mathf.Clamp(value, .5f, 3f);

    public void ApplyTo(TilemapRenderer renderer)
    {
        if (!isActiveAndEnabled || !overlayMaterial || !renderer) return;
        if (!map) map = GetComponent<MapGenerator>();
        if (!map) return;
        target = renderer;
        target.sharedMaterial = overlayMaterial;
        properties ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        appliedScale = ValidScale(map.oreScale);
        properties.SetFloat(ScaleProperty, appliedScale);
        var lighting = GetComponent<MapLighting>();
        appliedPulsesPerMinute = lighting ? Mathf.Clamp(lighting.ultroniumPulsesPerMinute, 0, 120) : 0;
        properties.SetFloat(PulseRateProperty, appliedPulsesPerMinute);
        target.SetPropertyBlock(properties);
    }

    public void ApplyTerrain(MaterialPropertyBlock terrain)
    {
        if (!isActiveAndEnabled) return;
        if (!target && map && map.OreOverlay) ApplyTo(map.OreOverlay.GetComponent<TilemapRenderer>());
        if (!target) return;
        properties ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        properties.SetVector("_UniformStone", terrain.GetVector("_UniformStone"));
        properties.SetVector("_TestBounds", terrain.GetVector("_TestBounds"));
        properties.SetFloat("_UseTerrainMasks", terrain.GetFloat("_UseTerrainMasks"));
        properties.SetFloat("_FrayedInset", terrain.GetFloat("_FrayedInset"));
        properties.SetVector("_TerrainEdgeTuning", terrain.GetVector("_TerrainEdgeTuning"));
        CopyTexture("_TerrainEdgeMasks");
        CopyTexture("_TestStoneTex"); CopyTexture("_SurfaceDirtTex");
        CopyTexture("_LayerOneTex"); CopyTexture("_LayerThreeTex"); CopyTexture("_LayerFourTex"); CopyTexture("_TestOccupancy");
        target.SetPropertyBlock(properties);
        void CopyTexture(string name) { var value = terrain.GetTexture(name); if(value) properties.SetTexture(name,value); }
    }

    void OnDisable()
    {
        copperSurface.Dispose();
        if (subscribedMap) subscribedMap.Generated -= InvalidateVeins;
        subscribedMap = null;
        Tilemap.tilemapPositionsChanged -= VeinTilesChanged;
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        foreach (var field in veinFields.Values) field.Dispose();
        veinFields.Clear();
        if (!target || !map) return;
        target.GetPropertyBlock(properties);
        properties.SetFloat("_VeinEnabled", 0);
        target.SetPropertyBlock(properties);
        var terrain = map.GetComponent<TilemapRenderer>();
        if (terrain) target.sharedMaterial = terrain.sharedMaterial;
        appliedScale = -1;
        appliedPulsesPerMinute = -1;
    }
}
