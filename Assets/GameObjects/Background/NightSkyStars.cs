using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SkyController)), DefaultExecutionOrder(1200)]
public sealed class NightSkyStars : MonoBehaviour
{
    public Material material;
    [Range(0, 1000)] public int starCount = 180;
    public int seed = 731;
    public Vector2 sizeRange = new Vector2(0.002f, 0.005f);
    [Range(0f, 0.6f)] public float pulseAmount = 0.35f;
    [Min(1f)] public float pulsePeriod = 6f;
    [Min(0f)] public float brightness = 3f;
    [Range(0f, 0.5f)] public float brightnessPulse = 0.2f;
    [Range(0f, 20f)] public float rotationAmount = 6f;
    public Color color = new Color(0.9f, 0.95f, 1f, 1f);
    public Vector2 moonCenter = new Vector2(0.842f, 0.585f);
    [Range(0f, 0.1f)] public float moonClearance = 0.025f;

    sealed class Field
    {
        public ParticleSystem system;
        public ParticleSystemRenderer renderer;
    }
    struct Star { public Vector2 position; public float size, phase, speed, rotation, brightness; }
    readonly Dictionary<SpriteRenderer, Field> fields = new Dictionary<SpriteRenderer, Field>();
    readonly List<SpriteRenderer> stale = new List<SpriteRenderer>();
    Star[] stars;
    ParticleSystem.Particle[] particles;
    int builtSeed;
    SkyController sky;
    ParallaxLayer layer;
    MaterialPropertyBlock properties;
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
#if UNITY_EDITOR
    double nextPreviewFrame;
#endif

    void OnEnable()
    {
        sky = GetComponent<SkyController>(); layer = GetComponent<ParallaxLayer>();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += UpdatePreview;
#endif
    }
    void LateUpdate() => Refresh(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
#if UNITY_EDITOR
    void UpdatePreview()
    {
        if (Application.isPlaying || UnityEditor.EditorApplication.timeSinceStartup < nextPreviewFrame) return;
        nextPreviewFrame = UnityEditor.EditorApplication.timeSinceStartup + 1.0 / 30.0;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    public void Refresh(float time)
    {
        if (!sky || !layer) return;
        int count = Mathf.Clamp(starCount, 0, 1000);
        if (stars == null || stars.Length != count || builtSeed != seed)
        {
            builtSeed = seed;
            stars = new Star[count]; particles = new ParticleSystem.Particle[count];
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
                stars[i] = new Star {
                    position = new Vector2((float)random.NextDouble(), Mathf.Lerp(0.18f, 0.98f, (float)random.NextDouble())),
                    size = Mathf.Pow((float)random.NextDouble(), 3f), phase = (float)random.NextDouble() * Mathf.PI * 2f,
                    speed = Mathf.Lerp(0.75f, 1.25f, (float)random.NextDouble()),
                    rotation = (float)random.NextDouble() * 90f, brightness = Mathf.Lerp(0.7f, 1f, (float)random.NextDouble()) };
        }
        stale.Clear();
        foreach (var pair in fields)
        {
            if (!pair.Key || !pair.Value.system) { stale.Add(pair.Key); continue; }
            pair.Value.renderer.enabled = false;
        }
        foreach (var key in stale) fields.Remove(key);
        if (!material || !sky.isActiveAndEnabled || !layer.isActiveAndEnabled) return;
        foreach (var day in layer.Renderers)
        {
            if (!day || !day.enabled || layer.IsBottomEdge(day.sprite) ||
                !sky.TryGetNightRenderer(day, out var night) || !night || !night.enabled) continue;
            if (!fields.TryGetValue(day, out var field))
            {
                var root = new GameObject("Night stars (generated)") { hideFlags = HideFlags.HideAndDontSave };
                root.transform.SetParent(day.transform, false);
                root.layer = gameObject.layer;
                var system = root.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startSpeed = 0f; main.maxParticles = Mathf.Max(1, count);
                var emission = system.emission; emission.enabled = false;
                var shape = system.shape; shape.enabled = false;
                // Paused particles remain renderable without lifetime simulation or spatial motion.
                system.Play(false);
                system.Pause(false);
                field = new Field { system = system, renderer = root.GetComponent<ParticleSystemRenderer>() };
                field.renderer.renderMode = ParticleSystemRenderMode.Billboard;
                field.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                field.renderer.receiveShadows = false;
                fields.Add(day, field);
            }
            Bounds bounds = night.bounds;
            int used = 0;
            for (int i = 0; i < count; i++)
            {
                Star star = stars[i];
                Vector2 distance = star.position - moonCenter;
                distance.x *= bounds.size.x / bounds.size.y;
                if (distance.magnitude < moonClearance) continue;
                float wave = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(1f, pulsePeriod) * star.speed + star.phase);
                Color tint = color;
                tint.a *= night.color.a * star.brightness * (1f - brightnessPulse * (0.5f - 0.5f * wave));
                particles[used++] = new ParticleSystem.Particle {
                    position = new Vector3(bounds.min.x + star.position.x * bounds.size.x,
                        bounds.min.y + star.position.y * bounds.size.y, bounds.center.z),
                    startSize = bounds.size.y * Mathf.Lerp(Mathf.Max(0.0001f, sizeRange.x), Mathf.Max(sizeRange.x, sizeRange.y), star.size)
                        * (1f + wave * pulseAmount),
                    rotation = star.rotation + wave * rotationAmount,
                    startColor = tint, startLifetime = 100000f, remainingLifetime = 100000f,
                    randomSeed = (uint)i + 1 };
            }
            var settings = field.system.main; settings.maxParticles = Mathf.Max(1, count);
            field.system.SetParticles(particles, used);
            field.renderer.sharedMaterial = material;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat(BrightnessId, Mathf.Max(0f, brightness));
            field.renderer.SetPropertyBlock(properties);
            field.renderer.sortingLayerID = night.sortingLayerID;
            field.renderer.sortingOrder = night.sortingOrder + 1;
            field.renderer.enabled = true;
        }
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= UpdatePreview;
#endif
        foreach (var field in fields.Values)
            if (field.system)
            {
                field.system.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(field.system.gameObject);
                else DestroyImmediate(field.system.gameObject);
            }
        fields.Clear();
    }
}
