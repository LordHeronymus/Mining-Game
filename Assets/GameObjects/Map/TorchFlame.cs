using UnityEngine;

[DisallowMultipleComponent]
public sealed class TorchFlame : MonoBehaviour
{
    const float OuterRatePerParticle = 25f / 49f;
    const float CoreRatePerParticle = 18f / 49f;
    const float EmberRatePerParticle = 6f / 49f;
    static Texture2D flameTexture;
    static Material flameMaterial;
    ParticleSystem outerFlame;
    ParticleSystem flameCore;
    ParticleSystem embers;
    Material torchMaterial;
    MapGenerator map;

    public void Initialize(MapGenerator ownerMap)
    {
        if (outerFlame) return;
        map = ownerMap;
        EnsureMaterial();
        torchMaterial = new Material(flameMaterial)
        {
            name = "Placed Torch Flame",
            hideFlags = HideFlags.DontSave
        };
        outerFlame = CreateFlame("Outer Flame", 25f, .35f, .6f, .11f, .19f,
            .22f, .36f, .24f, .5f, .065f, OuterColors());
        flameCore = CreateFlame("Flame Core", 18f, .22f, .4f, .06f, .11f,
            .14f, .25f, .12f, .3f, .035f, CoreColors());
        embers = CreateFlame("Embers", 6f, .3f, .65f, .012f, .025f,
            .025f, .055f, .45f, .85f, .09f, EmberColors());
        ApplySettings();
    }

    public void Play()
    {
        outerFlame.Play();
        flameCore.Play();
        embers.Play();
    }

    public void ApplySettings()
    {
        if (!outerFlame || !map) return;
        float frequency = Mathf.Clamp(map.torchFlameFrequency, 0f, 120f);
        float size = Mathf.Clamp(map.torchFlameSize, .1f, 3f);
        float brightness = Mathf.Clamp(map.torchBrightness, 0f, 2f);
        var emitterPosition = new Vector3(map.torchFlameOffsetX, map.torchFlameOffsetY, 0f);
        foreach (var particle in new[] { outerFlame, flameCore, embers })
        {
            particle.transform.localPosition = emitterPosition;
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        SetLayer(outerFlame, frequency * OuterRatePerParticle, .11f, .19f, .22f, .36f, size);
        SetLayer(flameCore, frequency * CoreRatePerParticle,
            .06f, .11f, .14f, .25f, size);
        SetLayer(embers, frequency * EmberRatePerParticle,
            .012f, .025f, .025f, .055f, size);
        if (torchMaterial)
        {
            Color tint = Color.white * brightness;
            tint.a = 1f;
            if (torchMaterial.HasProperty("_Color")) torchMaterial.SetColor("_Color", tint);
            if (torchMaterial.HasProperty("_BaseColor")) torchMaterial.SetColor("_BaseColor", tint);
        }
        foreach (var particle in new[] { outerFlame, flameCore, embers })
            if (particle.gameObject.activeInHierarchy) particle.Play();
    }

    ParticleSystem CreateFlame(string objectName, float rate, float minLife, float maxLife,
        float minWidth, float maxWidth, float minHeight, float maxHeight,
        float minRise, float maxRise, float spawnWidth, Gradient colors)
    {
        var flame = new GameObject(objectName, typeof(ParticleSystem));
        flame.transform.SetParent(transform, false);
        flame.transform.localPosition = Vector3.zero;
        var particles = flame.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(minWidth, maxWidth);
        main.startSizeY = new ParticleSystem.MinMaxCurve(minHeight, maxHeight);
        main.startSizeZ = .01f;
        main.maxParticles = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var emission = particles.emission;
        emission.rateOverTime = rate;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnWidth, .012f, 0f);

        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-.055f, .055f);
        velocity.y = new ParticleSystem.MinMaxCurve(minRise, maxRise);

        var color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(colors);

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .55f), new Keyframe(.22f, 1f),
            new Keyframe(.72f, .8f), new Keyframe(1f, .12f)));

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = .045f;
        noise.frequency = 1.2f;
        noise.scrollSpeed = .4f;

        var renderer = flame.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = torchMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = objectName == "Flame Core" ? 0 : -1;
        return particles;
    }

    static void SetLayer(ParticleSystem system, float rate, float minWidth, float maxWidth,
        float minHeight, float maxHeight, float scale)
    {
        var main = system.main;
        main.startSizeX = new ParticleSystem.MinMaxCurve(minWidth * scale, maxWidth * scale);
        main.startSizeY = new ParticleSystem.MinMaxCurve(minHeight * scale, maxHeight * scale);
        var emission = system.emission;
        emission.rateOverTime = rate;
    }

    void OnDestroy()
    {
        if (torchMaterial) Destroy(torchMaterial);
    }

    static Gradient OuterColors()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, .82f, .22f), 0f),
                new GradientColorKey(new Color(1f, .38f, .035f), .55f),
                new GradientColorKey(new Color(.8f, .09f, .01f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(.85f, .13f),
                new GradientAlphaKey(.7f, .55f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static Gradient CoreColors()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 1f, .75f), 0f),
                new GradientColorKey(new Color(1f, .91f, .25f), .65f),
                new GradientColorKey(new Color(1f, .55f, .06f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(.95f, .12f),
                new GradientAlphaKey(.75f, .65f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static Gradient EmberColors()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, .87f, .28f), 0f),
                new GradientColorKey(new Color(1f, .33f, .04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, .15f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static void EnsureMaterial()
    {
        if (flameMaterial) return;
        flameTexture = new Texture2D(32, 64, TextureFormat.RGBA32, false)
        {
            name = "Torch Flame Shape",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[32 * 64];
        for (int y = 0; y < 64; y++)
        {
            float height = (y + .5f) / 64f;
            float width = Mathf.Lerp(.38f, .015f, Mathf.Pow(height, .8f));
            float center = .5f + .055f * Mathf.Sin(height * Mathf.PI);
            float endFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(height * 18f, (1f - height) * 12f));
            for (int x = 0; x < 32; x++)
            {
                float distance = Mathf.Abs((x + .5f) / 32f - center);
                float coverage = Mathf.Clamp01((width - distance) * 18f) * endFade;
                pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)(255f * coverage));
            }
        }
        flameTexture.SetPixels32(pixels);
        flameTexture.Apply();
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        flameMaterial = new Material(shader)
        {
            name = "Torch Flame Particles",
            mainTexture = flameTexture,
            hideFlags = HideFlags.DontSave
        };
    }
}
