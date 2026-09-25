using System;
using UnityEngine;

[Serializable]
public sealed class MapLayer
{
    [InspectorName("Name")] public string name;
    [Min(0), InspectorName("Starttiefe (Zellen)")] public int startDepth;
    [Min(0), InspectorName("Übergang (Blöcke)")] public int transitionWidth;
    [InspectorName("Hintergrundsprite")] public Sprite backgroundSprite;
    [InspectorName("Steintyp")] public Block stone;
    [Min(.01f), InspectorName("Gesteinshärte")] public float stoneHardness;
    [HideInInspector] public BlockType[] ores = Array.Empty<BlockType>();
}

[Serializable]
public sealed class OreDistributionSetting
{
    [OreOnly] public BlockType ore;
    public int[] layerIndices = Array.Empty<int>();
    [Min(0f)] public float baseWeight;
    public AnimationCurve weightCurve = AnimationCurve.Constant(0f, 1f, 1f);
    [Min(1f)] public float baseVeinSize = 7f;
    public AnimationCurve veinSizeCurve = AnimationCurve.Constant(0f, 1f, 1f);
}
