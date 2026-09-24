using UnityEngine;

public sealed class TallGrassCutParticles : MonoBehaviour
{
    ParticleSystem blades;
    Texture2D bladeTexture;
    Material bladeMaterial;

    public int ActiveCount => blades ? blades.particleCount : 0;

    public void Burst(Bounds grassBounds)
    {
        if (!Application.isPlaying) return;
        if (!blades) CreateSystem();

        Vector3 center = new Vector3(grassBounds.center.x,
            grassBounds.min.y + grassBounds.size.y * .42f, grassBounds.center.z);
        for (int i = 0; i < 20; i++)
        {
            float sideways = Random.Range(-2.7f, 2.7f);
            var blade = new ParticleSystem.EmitParams
            {
                position = center + new Vector3(Random.Range(-grassBounds.extents.x * .4f,
                    grassBounds.extents.x * .4f), Random.Range(-.12f, .12f), 0f),
                velocity = new Vector3(sideways, Random.Range(1.4f, 3.5f), 0f),
                startSize3D = new Vector3(Random.Range(.12f, .19f),
                    Random.Range(.31f, .48f), 1f),
                startLifetime = Random.Range(.85f, 1.4f),
                startColor = Color.Lerp(new Color(.22f, .72f, .06f),
                    new Color(.55f, .92f, .12f), Random.value),
                rotation = Random.Range(0f, 360f)
            };
            blades.Emit(blade, 1);
        }
    }

    void CreateSystem()
    {
        var root = new GameObject("Flying Grass Blades", typeof(ParticleSystem));
        root.transform.SetParent(transform, false);
        blades = root.GetComponent<ParticleSystem>();
        blades.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = blades.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.gravityModifier = .75f;
        main.maxParticles = 256;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var emission = blades.emission;
        emission.enabled = false;
        var shape = blades.shape;
        shape.enabled = false;

        var color = blades.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, .45f),
                new GradientAlphaKey(.65f, .7f),
                new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);

        var noise = blades.noise;
        noise.enabled = true;
        noise.strength = .38f;
        noise.frequency = .7f;
        noise.scrollSpeed = .3f;
        var rotation = blades.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-2.8f, 2.8f);

        bladeTexture = CreateBladeTexture();
        bladeMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        bladeMaterial.mainTexture = bladeTexture;
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = bladeMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = "Grass";
        renderer.sortingOrder = 2;
    }

    static Texture2D CreateBladeTexture()
    {
        const int width = 24, height = 64;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float t = (y + .5f) / height;
            float center = 7f + 8f * t * t;
            float halfWidth = 6.5f * Mathf.Pow(1f - t, .8f);
            for (int x = 0; x < width; x++)
            {
                float coverage = Mathf.Clamp01(halfWidth + .5f - Mathf.Abs(x + .5f - center));
                if (coverage <= 0f) continue;
                bool ridge = Mathf.Abs(x + .5f - center) < 1f;
                pixels[y * width + x] = ridge
                    ? new Color32(227, 255, 187, (byte)(255f * coverage))
                    : new Color32(191, 247, 138, (byte)(255f * coverage));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    void OnDestroy()
    {
        if (bladeMaterial) Destroy(bladeMaterial);
        if (bladeTexture) Destroy(bladeTexture);
    }
}
