using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "Artifact", menuName = "Map/Artifact Tile")]
public sealed class ArtifactTile : Tile
{
    public string displayName;
}

[Serializable]
public sealed class ArtifactDistributionSetting
{
    public ArtifactTile tile;
    public int[] layerIndices = Array.Empty<int>();
    [Range(0f, 100f)] public float chancePercent;
    [Min(0)] public int cash;
}

public static class ArtifactPlacement
{
    const int MinimumSameTypeDistance = 10;
    const int MinimumSameTypeDistanceSquared = MinimumSameTypeDistance * MinimumSameTypeDistance;

    public static bool TryPlace(Dictionary<ArtifactTile, List<Vector2Int>> placements,
        ArtifactTile artifact, int x, int depth)
    {
        if (placements == null) throw new ArgumentNullException(nameof(placements));
        if (!artifact) return false;
        if (!placements.TryGetValue(artifact, out var positions))
        {
            positions = new List<Vector2Int>();
            placements.Add(artifact, positions);
        }

        var candidate = new Vector2Int(x, depth);
        foreach (var position in positions)
            if ((candidate - position).sqrMagnitude < MinimumSameTypeDistanceSquared)
                return false;

        positions.Add(candidate);
        return true;
    }
}
