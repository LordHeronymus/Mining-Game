using System;
using UnityEngine;

[Serializable]
public sealed class MapLayer
{
    [InspectorName("Name")] public string name;
    [Min(0), InspectorName("Starttiefe (Zellen)")] public int startDepth;
    [InspectorName("Steintyp")] public Block stone;
    [InspectorName("Erze"), OreOnly] public BlockType[] ores = Array.Empty<BlockType>();
}
