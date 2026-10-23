using UnityEngine;

// Flame of a placed torch (variant "C2", 23.10.2026): painted tongues, blue base, sparks and a thin smoke trail.
// Draw order back to front: smoke, holder, outer flame, blue base, core, embers; the blue base stays a glow behind the core
// instead of a pale dot in front of it.
// GPS values scale the layers relative to the profile values the variant was tuned with (size 1.7, 30/s, brightness 2).
[DisallowMultipleComponent]
public sealed class TorchFlame : MonoBehaviour
{
    const float ReferenceSize = 1.7f;
    const float ReferenceFrequency = 30f;
    const float ReferenceBrightness = 2f;
    const float FlameTint = 1.3f;

    sealed class LayerSpec
    {
        public string name;
        public float rate, minLife, maxLife, minWidth, maxWidth, minHeight, maxHeight, minRise, maxRise, spawnWidth;
        public float sway, noise, noiseFrequency;
        public int order;
        public bool tongue, tongueCurve, tinted = true;
        public Gradient colors;
    }

    static readonly LayerSpec[] Specs =
    {
        new() { name = "Smoke", rate = 13f, minLife = 1.1f, maxLife = 1.6f, minWidth = .1f, maxWidth = .17f,
            minHeight = .14f, maxHeight = .22f, minRise = .16f, maxRise = .26f, spawnWidth = .05f,
            sway = .05f, noise = .06f, noiseFrequency = .6f, order = -3, tongue = true, tinted = false,
            colors = MakeGradient(new[] { (new Color(.6f, .55f, .5f), 0f), (new Color(.42f, .39f, .37f), 1f) },
                new[] { (0f, 0f), (.56f, .2f), (0f, 1f) }) },
        new() { name = "Outer Flame", rate = 22f, minLife = .4f, maxLife = .6f, minWidth = .2f, maxWidth = .27f,
            minHeight = .483f, maxHeight = .644f, minRise = .12f, maxRise = .2f, spawnWidth = .04f,
            sway = .025f, noise = .05f, noiseFrequency = 1.4f, order = -1, tongue = true, tongueCurve = true,
            colors = MakeGradient(new[] { (new Color(1f, .82f, .22f), 0f), (new Color(1f, .38f, .035f), .55f), (new Color(.8f, .09f, .01f), 1f) },
                new[] { (0f, 0f), (.85f, .13f), (.7f, .55f), (0f, 1f) }) },
        new() { name = "Flame Core", rate = 18f, minLife = .32f, maxLife = .5f, minWidth = .11f, maxWidth = .16f,
            minHeight = .299f, maxHeight = .414f, minRise = .07f, maxRise = .13f, spawnWidth = .02f,
            sway = .015f, noise = .03f, noiseFrequency = 1.4f, order = 1, tongue = true, tongueCurve = true,
            colors = MakeGradient(new[] { (new Color(1f, 1f, .75f), 0f), (new Color(1f, .91f, .25f), .65f), (new Color(1f, .55f, .06f), 1f) },
                new[] { (0f, 0f), (.95f, .12f), (.75f, .65f), (0f, 1f) }) },
        new() { name = "Blue Base", rate = 16f, minLife = .16f, maxLife = .26f, minWidth = .104f, maxWidth = .143f,
            minHeight = .078f, maxHeight = .117f, minRise = 0f, maxRise = .02f, spawnWidth = .03f,
            sway = .01f, noise = .01f, noiseFrequency = 1.4f, order = 0, tongue = true,
            colors = MakeGradient(new[] { (new Color(.3f, .5f, 1f), 0f), (new Color(.35f, .55f, 1f), 1f) },
                new[] { (0f, 0f), (.75f, .25f), (0f, 1f) }) },
        new() { name = "Embers", rate = 16f, minLife = .5f, maxLife = 1f, minWidth = .024f, maxWidth = .04f,
            minHeight = .024f, maxHeight = .04f, minRise = .35f, maxRise = .75f, spawnWidth = .12f,
            sway = .22f, noise = .16f, noiseFrequency = 1.4f, order = 2,
            colors = MakeGradient(new[] { (new Color(1f, .87f, .28f), 0f), (new Color(1f, .33f, .04f), 1f) },
                new[] { (0f, 0f), (1f, .15f), (0f, 1f) }) },
    };

    static Material sparkMaterial, tongueMaterial;
    ParticleSystem[] systems;
    Material sparkInstance, tongueInstance, smokeInstance;
    MapGenerator map;

    public int LayerCount => systems?.Length ?? 0;
    public ParticleSystem GetLayer(int index) => systems != null && index >= 0 && index < systems.Length ? systems[index] : null;

    public void Initialize(MapGenerator ownerMap)
    {
        if (systems != null) return;
        map = ownerMap;
        EnsureMaterials();
        sparkInstance = Instance(sparkMaterial, "Placed Torch Sparks");
        tongueInstance = Instance(tongueMaterial, "Placed Torch Flame");
        smokeInstance = Instance(tongueMaterial, "Placed Torch Smoke");
        systems = new ParticleSystem[Specs.Length];
        for (int i = 0; i < Specs.Length; i++) systems[i] = CreateLayer(Specs[i]);
        ApplySettings();
    }

    static Material Instance(Material source, string name) =>
        new(source) { name = name, hideFlags = HideFlags.DontSave };

    public void Play()
    {
        foreach (var system in systems) system.Play();
    }

    public void ApplySettings()
    {
        if (systems == null || !map) return;
        float rateScale = Mathf.Clamp(map.torchFlameFrequency, 0f, 120f) / ReferenceFrequency;
        float sizeScale = Mathf.Clamp(map.torchFlameSize, .1f, 3f) / ReferenceSize;
        float brightness = Mathf.Clamp(map.torchBrightness, 0f, 2f) / ReferenceBrightness;
        var emitter = PlacedTorch.FlameEmitterLocal(map);
        for (int i = 0; i < systems.Length; i++)
        {
            var system = systems[i];
            var spec = Specs[i];
            system.transform.localPosition = emitter;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.startSizeX = new ParticleSystem.MinMaxCurve(spec.minWidth * sizeScale, spec.maxWidth * sizeScale);
            main.startSizeY = new ParticleSystem.MinMaxCurve(spec.minHeight * sizeScale, spec.maxHeight * sizeScale);
            var emission = system.emission;
            emission.rateOverTime = spec.rate * rateScale;
        }
        SetTint(sparkInstance, FlameTint * brightness);
        SetTint(tongueInstance, FlameTint * brightness);
        SetTint(smokeInstance, FlameTint);
        foreach (var system in systems)
            if (system.gameObject.activeInHierarchy) system.Play();
    }

    static void SetTint(Material material, float value)
    {
        if (!material) return;
        Color tint = Color.white * value;
        tint.a = 1f;
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
    }

    ParticleSystem CreateLayer(LayerSpec spec)
    {
        var flame = new GameObject(spec.name, typeof(ParticleSystem));
        flame.transform.SetParent(transform, false);
        var particles = flame.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(spec.minLife, spec.maxLife);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeZ = .01f;
        main.maxParticles = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spec.spawnWidth, .012f, 0f);

        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-spec.sway, spec.sway);
        velocity.y = new ParticleSystem.MinMaxCurve(spec.minRise, spec.maxRise);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(spec.colors);

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, spec.tongueCurve
            ? new AnimationCurve(new Keyframe(0f, .8f), new Keyframe(.25f, 1f), new Keyframe(.6f, .7f), new Keyframe(1f, .2f))
            : new AnimationCurve(new Keyframe(0f, .55f), new Keyframe(.22f, 1f), new Keyframe(.72f, .8f), new Keyframe(1f, .12f)));

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = spec.noise;
        noise.frequency = spec.noiseFrequency;
        noise.scrollSpeed = .4f;

        var renderer = flame.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = !spec.tinted ? smokeInstance : spec.tongue ? tongueInstance : sparkInstance;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = spec.order;
        return particles;
    }

    void OnDestroy()
    {
        if (sparkInstance) Destroy(sparkInstance);
        if (tongueInstance) Destroy(tongueInstance);
        if (smokeInstance) Destroy(smokeInstance);
    }

    static Gradient MakeGradient((Color color, float time)[] colors, (float alpha, float time)[] alphas)
    {
        var gradient = new Gradient();
        var colorKeys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) colorKeys[i] = new GradientColorKey(colors[i].color, colors[i].time);
        var alphaKeys = new GradientAlphaKey[alphas.Length];
        for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    static void EnsureMaterials()
    {
        if (sparkMaterial && tongueMaterial) return;
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        sparkMaterial = new Material(shader)
        {
            name = "Torch Spark Particles",
            mainTexture = BuildTexture(false),
            hideFlags = HideFlags.DontSave
        };
        tongueMaterial = new Material(shader)
        {
            name = "Torch Flame Particles",
            mainTexture = BuildTexture(true),
            hideFlags = HideFlags.DontSave
        };
    }

    // Spark: the original narrow flame shape. Tongue: rounder belly with a sharp tip and softer edges.
    static Texture2D BuildTexture(bool tongue)
    {
        int width = tongue ? 64 : 32, height = tongue ? 128 : 64;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = tongue ? "Torch Flame Tongue" : "Torch Flame Shape",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float h = (y + .5f) / height;
            float halfWidth, fade, center, softness;
            if (tongue)
            {
                halfWidth = .42f * Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Min(1f, h + .08f), .7f)) * Mathf.Pow(1f - h, .35f);
                fade = Mathf.Clamp01(Mathf.Min(h * 10f, (1f - h) * 14f));
                center = .5f;
                softness = 9f;
            }
            else
            {
                halfWidth = Mathf.Lerp(.38f, .015f, Mathf.Pow(h, .8f));
                fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(h * 18f, (1f - h) * 12f));
                center = .5f + .055f * Mathf.Sin(h * Mathf.PI);
                softness = 18f;
            }
            for (int x = 0; x < width; x++)
            {
                float distance = Mathf.Abs((x + .5f) / width - center);
                float coverage = Mathf.Clamp01((halfWidth - distance) * softness) * fade;
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)(255f * coverage));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
