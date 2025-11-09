using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(AudioSource))]
public class TileMiner : MonoBehaviour
{
    [Header("Refs")]
    public Tilemap tilemap;
    public BlockRegistry blockRegistry;
    public Camera cam;
    public Tilemap highlightMap;
    public TileBase highlightTile;

    [Header("Mining")]
    public float mineTime = 0.6f;
    public float maxReach = 2.0f;
    public int searchRadiusCells = 2;
    public float miningSoundInterval = 0.5f;

    private Dictionary<Vector3Int, float> progress = new();
    private Camera _cam;
    private float nextMiningSoundTime = 0f;

    public static Action<Vector2, int> OnBlockMined;

    void Awake()
    {
        _cam = cam ? cam : Camera.main;
    }

    void Update()
    {
        if (!_cam || !tilemap) return;

        Vector3 mouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        Vector3Int? nearest = FindNearestExistingCell(mouseWorld, searchRadiusCells);

        if (nearest == null)
        {
            if (highlightMap) highlightMap.ClearAllTiles();
            return;
        }

        Vector3Int targetCell = GetReachLimitedCell(nearest.Value);

        if (highlightMap && highlightTile)
        {
            highlightMap.ClearAllTiles();
            if (tilemap.HasTile(targetCell)) highlightMap.SetTile(targetCell, highlightTile);
        }

        if (!Input.GetMouseButton(0)) return;
        TileBase t = tilemap.GetTile(targetCell);
        if (!t) return;

        float p = progress.TryGetValue(targetCell, out var cur) ? cur : 0f;
        float targetTime = GetTargetMineTime(targetCell);
        p += Time.deltaTime / Mathf.Max(0.0001f, targetTime);
        progress[targetCell] = p;

        if (p >= 1f)
        {
            TileBase minedTile = tilemap.GetTile(targetCell);
            Block minedBlock = blockRegistry != null ? blockRegistry.FromTile(minedTile) : null;
            if (minedBlock != null && minedBlock.itemDrop != null)
                InventoryManager.Instance?.Add(minedBlock.itemDrop, 1);

            int points = minedBlock != null ? minedBlock.points : 0;
            OnBlockMined?.Invoke(tilemap.GetCellCenterWorld(targetCell), points);

            tilemap.SetTile(targetCell, null);
            progress.Remove(targetCell);

            SoundType endSfx = SoundType.BreakRock; // Fallback
            if (minedBlock != null) endSfx = minedBlock.breakSound;
            AudioManager.Instance.Play(endSfx);
            return;
        }

        if (Time.time >= nextMiningSoundTime)
        {
            SoundType hit = SoundType.DigMedium; // Fallback
            if (blockRegistry != null)
            {
                TileBase curTile = tilemap.GetTile(targetCell);
                Block curBlock = curTile ? blockRegistry.FromTile(curTile) : null;
                if (curBlock != null) hit = curBlock.digSound;
            }

            AudioManager.Instance.Play(hit);
            nextMiningSoundTime = Time.time + miningSoundInterval;
        }
    }

    Vector3Int? FindNearestExistingCell(Vector3 mouseWorld, int radius)
    {
        Vector3Int center = tilemap.WorldToCell(mouseWorld);
        if (tilemap.HasTile(center)) return center;

        float bestSqr = float.PositiveInfinity;
        Vector3Int? best = null;

        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                var c = new Vector3Int(center.x + dx, center.y + dy, 0);
                if (!tilemap.HasTile(c)) continue;
                float d2 = (tilemap.GetCellCenterWorld(c) - mouseWorld).sqrMagnitude;
                if (d2 < bestSqr) { bestSqr = d2; best = c; }
            }

        return best;
    }

    Vector3Int GetReachLimitedCell(Vector3Int cell)
    {
        Vector3 cellWorld = tilemap.GetCellCenterWorld(cell);
        Vector3 playerPos = transform.position;

        float dist = Vector2.Distance(cellWorld, playerPos);
        if (dist <= maxReach) return cell;

        Vector2 dir = (cellWorld - playerPos).normalized;
        Vector3 limited = playerPos + (Vector3)(dir * maxReach);
        return tilemap.WorldToCell(limited);
    }

    float GetTargetMineTime(Vector3Int cell)
    {
        float time = mineTime;

        if (blockRegistry != null)
        {
            TileBase t = tilemap.GetTile(cell);
            Block b = t ? blockRegistry.FromTile(t) : null;
            if (b != null) time *= Mathf.Max(0.01f, b.hardness <= 0 ? 1f : b.hardness);
        }

        return time;
    }
}
