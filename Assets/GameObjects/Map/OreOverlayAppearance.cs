using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering;
using System.Collections.Generic;

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
    Texture2D fissureArt;

    public Material OverlayMaterial
    {
        get => overlayMaterial;
        set { overlayMaterial = value; appliedScale = -1; Update(); }
    }

    void OnEnable()
    {
        appliedScale = -1;
        map = GetComponent<MapGenerator>();
        Tilemap.tilemapTileChanged += VeinTilesChanged;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        Update();
    }
    void OnValidate() => appliedScale = -1;

    void InvalidateVeins() { foreach (var field in veinFields.Values) field.Invalidate(); }
    void VeinTilesChanged(Tilemap tiles, Tilemap.SyncTile[] changes)
    { foreach (var field in veinFields.Values) field.Changed(tiles, changes); }

    void BeforeCamera(ScriptableRenderContext context, Camera camera) => PrepareVeins(camera);

    public void PrepareVeins(Camera camera)
    {
        if (!isActiveAndEnabled || !map || !map.OreOverlay || !camera || !overlayMaterial) return;
        if (!target) ApplyTo(map.OreOverlay.GetComponent<TilemapRenderer>());
        if (!target) return;
        properties ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        bool visible = (camera.cullingMask & (1 << map.OreOverlay.gameObject.layer)) != 0;
        if (visible && !map.IsGenerationStreaming && properties.GetVector("_UniformStone").x > 0)
        {
            if (!fissureArt) fissureArt = Resources.Load<Texture2D>("OreVeins/RockFissure");
            if (fissureArt) properties.SetTexture("_VeinRelief", fissureArt);
            if (!veinFields.TryGetValue(camera, out var field))
                veinFields.Add(camera, field = new OreVeinField());
            if (field.Prepare(map, camera)) field.Bind(properties, map);
            else properties.SetFloat("_VeinEnabled", 0);
        }
        else properties.SetFloat("_VeinEnabled", 0);
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
        CopyTexture("_TestStoneTex"); CopyTexture("_SurfaceDirtTex");
        CopyTexture("_LayerOneTex"); CopyTexture("_LayerThreeTex"); CopyTexture("_LayerFourTex"); CopyTexture("_TestOccupancy");
        target.SetPropertyBlock(properties);
        void CopyTexture(string name) { var value = terrain.GetTexture(name); if(value) properties.SetTexture(name,value); }
    }

    void OnDisable()
    {
        if (subscribedMap) subscribedMap.Generated -= InvalidateVeins;
        subscribedMap = null;
        Tilemap.tilemapTileChanged -= VeinTilesChanged;
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
