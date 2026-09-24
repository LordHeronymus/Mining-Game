using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SkyController)), DefaultExecutionOrder(1150)]
public sealed class MoonlightController : MonoBehaviour
{
    public Light2D daylight;
    public Light2D moonlight;
    public Vector2 moonPosition = new Vector2(0.842f, 0.585f);
    [Min(0f)] public float dayIntensity = 1f;
    [Range(0f, 1f)] public float nightAmbient = 0.12f;
    [Min(0f)] public float moonIntensity = 0.9f;
    [Min(1f)] public float moonRadius = 65f;
    public Color moonColor = new Color(0.65f, 0.76f, 1f);
    public Color backgroundNightTint = new Color(0.28f, 0.35f, 0.53f, 1f);
    public Material haloMaterial;
    public Color haloColor = new Color(0.38f, 0.48f, 1f, 0.65f);
    [Min(0f)] public float haloIntensity = 1.4f;
    [Min(0.001f)] public float moonDiameter = 0.036f;
    [Min(1.1f)] public float haloSize = 3.8f;
    [Range(0f, 0.3f)] public float haloPulse = 0.1f;
    [Min(1f)] public float haloPeriod = 8f;
    readonly Dictionary<SpriteRenderer, MeshRenderer> halos = new Dictionary<SpriteRenderer, MeshRenderer>();
    readonly List<SpriteRenderer> staleHalos = new List<SpriteRenderer>();
    Mesh haloMesh;
    MaterialPropertyBlock haloProperties;
    SkyController sky;
    ParallaxLayer skyLayer;
    SurfaceBackgroundController background;
    Color daylightColor;

    void OnEnable()
    {
        sky = GetComponent<SkyController>(); skyLayer = GetComponent<ParallaxLayer>();
        background = GetComponentInParent<SurfaceBackgroundController>();
        if (daylight) daylightColor = daylight.color;
    }
    void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (!sky || !background || !daylight || !moonlight) return;
        daylight.enabled = GameplayTestSettings.GlobalLighting;
        float blend = sky.isActiveAndEnabled ? sky.NightBlend : 0f;
        daylight.intensity = Mathf.Lerp(dayIntensity, nightAmbient, blend);
        daylight.color = Color.Lerp(daylightColor, moonColor, blend);
        moonlight.intensity = 0f;
        staleHalos.Clear();
        foreach (var pair in halos)
        {
            if (!pair.Key || !pair.Value) { staleHalos.Add(pair.Key); continue; }
            pair.Value.enabled = false;
        }
        foreach (var key in staleHalos) halos.Remove(key);
        float closest = float.PositiveInfinity;
        Camera camera = background.RenderCamera;
        foreach (var day in skyLayer.Renderers)
        {
            if (!day || !day.enabled || skyLayer.IsBottomEdge(day.sprite) ||
                !sky.TryGetNightRenderer(day, out var night) || !night || !night.enabled) continue;
            Bounds bounds = night.bounds;
            Vector3 position = new Vector3(bounds.min.x + moonPosition.x * bounds.size.x,
                bounds.min.y + moonPosition.y * bounds.size.y, bounds.center.z);
            UpdateHalo(day, night, position);
            float distance = camera ? Mathf.Abs(position.x - camera.transform.position.x) : 0f;
            if (distance > closest) continue;
            closest = distance;
            moonlight.transform.position = position;
            moonlight.intensity = moonIntensity * night.color.a;
        }
        moonlight.color = moonColor;
        moonlight.pointLightOuterRadius = Mathf.Max(1f, moonRadius);
        moonlight.pointLightInnerRadius = 0f;
        foreach (var entry in background.layers)
            if (entry.layer && entry.layer != skyLayer)
                entry.layer.LightingTint = Color.Lerp(Color.white, backgroundNightTint, blend);
    }

    void UpdateHalo(SpriteRenderer day, SpriteRenderer night, Vector3 position)
    {
        if (!haloMaterial) return;
        if (!haloMesh)
        {
            haloMesh = new Mesh { name = "Moon halo quad", hideFlags = HideFlags.HideAndDontSave };
            haloMesh.vertices = new[] { new Vector3(-.5f,-.5f), new Vector3(.5f,-.5f), new Vector3(-.5f,.5f), new Vector3(.5f,.5f) };
            haloMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            haloMesh.triangles = new[] { 0,2,1,2,3,1 };
            haloMesh.RecalculateBounds();
        }
        if (!halos.TryGetValue(day, out var halo) || !halo)
        {
            var root = new GameObject("Moon halo (generated)") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(day.transform, false);
            root.layer = day.gameObject.layer;
            root.AddComponent<MeshFilter>().sharedMesh = haloMesh;
            halo = root.AddComponent<MeshRenderer>();
            halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halo.receiveShadows = false;
            halos[day] = halo;
        }
        float spread = Mathf.Max(1.1f, haloSize);
        float diameter = night.bounds.size.y * Mathf.Max(.001f, moonDiameter) * spread;
        Vector3 parentScale = day.transform.lossyScale;
        halo.transform.position = position;
        halo.transform.localScale = new Vector3(diameter / parentScale.x, diameter / parentScale.y, 1f);
        halo.sharedMaterial = haloMaterial;
        halo.sortingLayerID = night.sortingLayerID;
        halo.sortingOrder = night.sortingOrder + 1;
        if (haloProperties == null) haloProperties = new MaterialPropertyBlock();
        float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float pulse = 1f + haloPulse * Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(1f, haloPeriod));
        Color tint = haloColor; tint.a *= night.color.a;
        haloProperties.SetColor("_Tint", tint);
        haloProperties.SetFloat("_Intensity", Mathf.Max(0f, haloIntensity) * pulse);
        haloProperties.SetFloat("_DiscRadius", 1f / spread);
        halo.SetPropertyBlock(haloProperties);
        halo.enabled = night.color.a > 0f;
    }

    void OnDisable()
    {
        foreach (var halo in halos.Values)
            if (halo) { halo.gameObject.SetActive(false); ReleaseHalo(halo.gameObject); }
        halos.Clear();
        if (haloMesh) ReleaseHalo(haloMesh);
        haloMesh = null;
        if (daylight) { daylight.enabled = true; daylight.intensity = dayIntensity; daylight.color = daylightColor; }
        if (moonlight) moonlight.intensity = 0f;
        if (background)
            foreach (var entry in background.layers)
                if (entry.layer) entry.layer.LightingTint = Color.white;
    }
    static void ReleaseHalo(Object item)
    {
        if (Application.isPlaying) Destroy(item);
        else DestroyImmediate(item);
    }
}
