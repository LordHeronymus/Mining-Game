using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class OreOverlayAppearance : MonoBehaviour
{
    [SerializeField] Material overlayMaterial;
    MapGenerator map;
    TilemapRenderer target;
    MaterialPropertyBlock properties;
    float appliedScale = -1;
    static readonly int ScaleProperty = Shader.PropertyToID("_OreScale");

    public Material OverlayMaterial
    {
        get => overlayMaterial;
        set { overlayMaterial = value; appliedScale = -1; Update(); }
    }

    void OnEnable() { appliedScale = -1; Update(); }
    void OnValidate() => appliedScale = -1;

    void Update()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (!map || !map.OreOverlay || !overlayMaterial) return;
        var renderer = map.OreOverlay.GetComponent<TilemapRenderer>();
        float scale = ValidScale(map.oreScale);
        if (target != renderer || appliedScale != scale || renderer.sharedMaterial != overlayMaterial)
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
        CopyTexture("_LayerOneTex"); CopyTexture("_LayerThreeTex"); CopyTexture("_TestOccupancy");
        target.SetPropertyBlock(properties);
        void CopyTexture(string name) { var value = terrain.GetTexture(name); if(value) properties.SetTexture(name,value); }
    }

    void OnDisable()
    {
        if (!target || !map) return;
        var terrain = map.GetComponent<TilemapRenderer>();
        if (terrain) target.sharedMaterial = terrain.sharedMaterial;
        appliedScale = -1;
    }
}
