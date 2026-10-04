using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "Artifact", menuName = "Map/Artifact Tile")]
public sealed class ArtifactTile : Tile
{
    public string displayName;
    public Color themeColor;
    public Color secondaryThemeColor;
    public Color SecondaryThemeColor => secondaryThemeColor.a > 0f
        ? secondaryThemeColor : new Color32(255, 190, 72, 255);
    [SerializeField, HideInInspector] float discoveryIconYOffset;

    public float DiscoveryIconYOffset
    {
        get
        {

            return Mathf.Clamp(discoveryIconYOffset, -300f, 300f);
        }
    }

    public void SetDiscoveryIconYOffset(float offset) => discoveryIconYOffset = Mathf.Clamp(offset, -300f, 300f);

    public void SaveDiscoveryIconYOffset()
    {
        GpsSettings.Capture(this);
    }

    public Color ThemeColor => themeColor.a > 0f ? themeColor : name switch
    {
        "Artifact_02" => new Color32(58, 140, 246, 255),
        "Artifact_03" => new Color32(103, 220, 136, 255),
        "Artifact_04" => new Color32(255, 164, 48, 255),
        "Artifact_05" => new Color32(192, 215, 234, 255),
        "Artifact_06" => new Color32(90, 155, 237, 255),
        "Artifact_07" => new Color32(242, 59, 83, 255),
        "Artifact_08" => new Color32(232, 188, 121, 255),
        "Artifact_09" => new Color32(79, 197, 174, 255),
        "Artifact_10" => new Color32(161, 219, 255, 255),
        _ => new Color32(255, 205, 88, 255)
    };
}

[Serializable]
public sealed class ArtifactDistributionSetting
{
    public ArtifactTile tile;
    public int[] layerIndices = Array.Empty<int>();
    [Range(0f, 100f)] public float chancePercent;
    [Min(0)] public int cash;
    [Min(0)] public int artifactPoints = 1;
}

public static class ArtifactPlacement
{
    public static bool TryPlace(Dictionary<ArtifactTile, List<Vector2Int>> placements,
        ArtifactTile artifact, int x, int depth, int minimumSameTypeDistance)
    {
        if (placements == null) throw new ArgumentNullException(nameof(placements));
        if (!artifact) return false;
        if (!placements.TryGetValue(artifact, out var positions))
        {
            positions = new List<Vector2Int>();
            placements.Add(artifact, positions);
        }

        var candidate = new Vector2Int(x, depth);
        int minimumDistance = Mathf.Max(0, minimumSameTypeDistance);
        int minimumDistanceSquared = minimumDistance * minimumDistance;
        foreach (var position in positions)
            if ((candidate - position).sqrMagnitude < minimumDistanceSquared)
                return false;

        positions.Add(candidate);
        return true;
    }
}
