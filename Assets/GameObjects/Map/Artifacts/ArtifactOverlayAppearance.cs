using UnityEngine;
using UnityEngine.Tilemaps;

public static class ArtifactOverlayAppearance
{
    static Material material;
    static readonly int EmbeddingStrength = Shader.PropertyToID("_EmbeddingStrength");

    public static void ApplyTo(TilemapRenderer renderer, float embeddingStrength)
    {
        if (!renderer) return;
        if (!material) material = Resources.Load<Material>("Artifacts/ArtifactOverlayLit");
        if (material) renderer.sharedMaterial = material;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetFloat(EmbeddingStrength, Mathf.Clamp01(embeddingStrength));
        renderer.SetPropertyBlock(properties);
    }

    public static void ApplyTerrain(TilemapRenderer renderer, MaterialPropertyBlock terrain, float embeddingStrength)
    {
        if (!renderer || terrain == null) return;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetFloat(EmbeddingStrength, Mathf.Clamp01(embeddingStrength));
        properties.SetVector("_UniformStone", terrain.GetVector("_UniformStone"));
        properties.SetVector("_TestBounds", terrain.GetVector("_TestBounds"));
        CopyTexture("_TestStoneTex");
        CopyTexture("_SurfaceDirtTex");
        CopyTexture("_LayerOneTex");
        CopyTexture("_LayerThreeTex");
        CopyTexture("_TestOccupancy");
        renderer.SetPropertyBlock(properties);

        void CopyTexture(string name)
        {
            var texture = terrain.GetTexture(name);
            if (texture) properties.SetTexture(name, texture);
        }
    }
}
