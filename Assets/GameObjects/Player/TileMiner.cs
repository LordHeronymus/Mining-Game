using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(AudioSource))]
public class TileMiner : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] StatsManager stats;
    [SerializeField] Tilemap tilemap;
    [SerializeField] Tilemap highlightMap;
    [SerializeField] TileBase highlightTile;
    [SerializeField] BlockRegistry blockRegistry;
    [SerializeField] Camera cam;

    [Header("Mining")]
    public int searchRadiusCells = 2;
    public float miningSoundInterval = 0.5f;

    private Dictionary<Vector3Int, float> progress = new();
    private Camera _cam;
    private MapGenerator map;
    private float nextMiningSoundTime = 0f;

    public static Action<Vector2, int> OnBlockMined;

    private bool mining;
    public bool IsMingin => mining;
    public bool IsMining => mining;
    public Vector2 MiningTarget { get; private set; }

    void Awake()
    {
        _cam = cam ? cam : Camera.main;
        map = tilemap ? tilemap.GetComponent<MapGenerator>() : null;
    }

    void Update()
    {
        mining = false;
        if (GameplayDebugPanel.IsOpen)
        {
            mining = false;
            if (highlightMap) highlightMap.ClearAllTiles();
            return;
        }
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

        if (!Input.GetMouseButton(0))
        {
            mining = false;
            return;
        }
        TileBase t = tilemap.GetTile(targetCell);
        if (!t) return;
        mining = true;
        MiningTarget = tilemap.GetCellCenterWorld(targetCell);

        float p = progress.TryGetValue(targetCell, out var cur) ? cur : 0f;
        float targetTime = GetTargetMineTime(targetCell);
        p += Time.deltaTime / Mathf.Max(0.0001f, targetTime);
        progress[targetCell] = p;

        if (p >= 1f)
        {
            CompleteMining(targetCell);
            return;
        }

        if (Time.time >= nextMiningSoundTime)
        {
            SoundType hit = SoundType.DigMedium; // Fallback
            if (blockRegistry != null)
            {
                Block curBlock = GetBlock(targetCell);
                if (curBlock != null) hit = curBlock.digSound;
            }

            AudioManager.Instance.Play(hit, true);
            nextMiningSoundTime = Time.time + miningSoundInterval;
        }
    }

    Block GetBlock(Vector3Int cell)
    {
        if (!map && tilemap) map = tilemap.GetComponent<MapGenerator>();
        return map ? map.GetBlockAt(cell) : blockRegistry ? blockRegistry.FromTile(tilemap.GetTile(cell)) : null;
    }

    // One transaction for rewards, effects and both render layers. Effects read the cell before removal.
    public bool CompleteMining(Vector3Int cell)
    {
        if (!tilemap || !tilemap.HasTile(cell)) return false;
        var block = GetBlock(cell);
        var ore = map ? map.GetOreAt(cell) : null;
        int amount = ore ? OreTile.DropCount(ore.richness, UnityEngine.Random.value) : 1;
        if (block && block.itemDrop && amount > 0) InventoryManager.Instance?.Add(block.itemDrop, amount);
        OnBlockMined?.Invoke(tilemap.GetCellCenterWorld(cell), block ? block.points : 0);
        if (map) map.RemoveBlock(cell);
        else
        {
            tilemap.SetTile(cell, null);
            tilemap.GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        }
        progress.Remove(cell);
        AudioManager.Instance?.Play(block ? block.breakSound : SoundType.BreakRock);
        return true;
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
        if (dist <= stats.Reach) return cell;

        Vector2 dir = (cellWorld - playerPos).normalized;
        Vector3 limited = playerPos + (Vector3)(dir * stats.Reach);
        return tilemap.WorldToCell(limited);
    }

    float GetTargetMineTime(Vector3Int cell)
    {
        float time = 1 / stats.MiningSpeed;

        if (blockRegistry != null)
        {
            Block b = GetBlock(cell);
            if (b != null) time *= Mathf.Max(0.01f, b.hardness <= 0 ? 1f : b.hardness);
        }

        return time;
    }
}
