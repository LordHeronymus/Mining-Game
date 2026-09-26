using UnityEngine;

public sealed class TallGrassPatch : MonoBehaviour
{
    static readonly int SwayBaseY = Shader.PropertyToID("_SwayBaseY");
    static readonly int SwayHeight = Shader.PropertyToID("_SwayHeight");
    static readonly int SwayPhase = Shader.PropertyToID("_SwayPhase");
    static readonly int SwayFrequency = Shader.PropertyToID("_SwayFrequency");
    static readonly int SwayStrength = Shader.PropertyToID("_SwayStrength");
    static readonly int ReachGlow = Shader.PropertyToID("_ReachGlow");

    SurfaceTallGrass owner;
    SpriteRenderer visual;
    MaterialPropertyBlock properties;
    float frequencyVariation;
    int reachGlowLevel;
    public int SurfaceCellX { get; private set; }
    public bool IsHealingHerb { get; private set; }
    public int ReachGlowLevel => reachGlowLevel;

    public void Initialize(SurfaceTallGrass source, int cellX, bool isHealingHerb = false)
    {
        owner = source;
        SurfaceCellX = cellX;
        IsHealingHerb = isHealingHerb;
        visual = GetComponent<SpriteRenderer>();
        var sprite = visual.sprite;
        properties = new MaterialPropertyBlock();
        visual.GetPropertyBlock(properties);
        properties.SetFloat(SwayBaseY, -sprite.pivot.y / sprite.pixelsPerUnit);
        properties.SetFloat(SwayHeight, sprite.rect.height / sprite.pixelsPerUnit);
        uint swaySeed = (uint)SurfaceCellX * 747796405u + 2891336453u;
        swaySeed = (swaySeed ^ (swaySeed >> 16)) * 2246822519u;
        swaySeed ^= swaySeed >> 13;
        properties.SetFloat(SwayPhase, (swaySeed & 0xffffu) / 65535f * Mathf.PI * 2f);
        frequencyVariation = Mathf.Lerp(.85f, 1.15f,
            ((swaySeed >> 16) & 0xffffu) / 65535f);
        properties.SetFloat(ReachGlow, 0f);
        visual.SetPropertyBlock(properties);
        source.ApplySwaySettings(this);
    }

    public void SetSwaySettings(float strength, float frequency)
    {
        if (!visual || properties == null) return;
        properties.SetFloat(SwayStrength, Mathf.Max(0f, strength));
        properties.SetFloat(SwayFrequency, Mathf.Max(0f, frequency) * frequencyVariation);
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
