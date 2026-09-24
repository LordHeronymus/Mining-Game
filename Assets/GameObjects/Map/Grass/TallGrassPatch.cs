using UnityEngine;

public sealed class TallGrassPatch : MonoBehaviour
{
    static readonly int SwayBaseY = Shader.PropertyToID("_SwayBaseY");
    static readonly int SwayHeight = Shader.PropertyToID("_SwayHeight");
    static readonly int SwayPhase = Shader.PropertyToID("_SwayPhase");
    static readonly int SwayStrength = Shader.PropertyToID("_SwayStrength");
    static readonly int ReachGlow = Shader.PropertyToID("_ReachGlow");

    SurfaceTallGrass owner;
    SpriteRenderer visual;
    MaterialPropertyBlock properties;
    int reachGlowLevel;
    public int SurfaceCellX { get; private set; }
    public int ReachGlowLevel => reachGlowLevel;

    public void Initialize(SurfaceTallGrass source, int cellX)
    {
        owner = source;
        SurfaceCellX = cellX;
        visual = GetComponent<SpriteRenderer>();
        var sprite = visual.sprite;
        properties = new MaterialPropertyBlock();
        visual.GetPropertyBlock(properties);
        properties.SetFloat(SwayBaseY, -sprite.pivot.y / sprite.pixelsPerUnit);
        properties.SetFloat(SwayHeight, sprite.rect.height / sprite.pixelsPerUnit);
        properties.SetFloat(SwayPhase, (SurfaceCellX * .6180339f) % (Mathf.PI * 2f));
        properties.SetFloat(SwayStrength, 0f);
        properties.SetFloat(ReachGlow, 0f);
        visual.SetPropertyBlock(properties);
    }

    public void SetReachGlow(int level)
    {
        level = Mathf.Clamp(level, 0, 2);
        if (reachGlowLevel == level || !visual || properties == null) return;
        reachGlowLevel = level;
        properties.SetFloat(ReachGlow, level);
        visual.SetPropertyBlock(properties);
    }

    public bool Cut() => owner && owner.CutAt(SurfaceCellX);
}
