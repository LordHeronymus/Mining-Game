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
    [SerializeField] Texture2D treeCursorTexture;

    [Header("Mining")]
    public int searchRadiusCells = 2;
    public float miningSoundInterval = 0.5f;

    private Dictionary<Vector3Int, float> progress = new();
    private Camera _cam;
    private MapGenerator map;
    private SurfaceTallGrass tallGrass;
    private SurfaceTrees surfaceTrees;
    private float nextMiningSoundTime = 0f;
    private float nextTreeHitTime;
    private float nextGrassCutTime;
    private float grassSwingUntil;
    private bool grassClickConsumed;
    private Vector3Int? highlightedCell;
    private Texture2D scytheCursorTexture;
    private ToolCursor activeCursor;
    static readonly List<UnityEngine.EventSystems.RaycastResult> uiRaycasts = new();
    enum ToolCursor { None, Axe, Scythe }

    public static Action<Vector2, int> OnBlockMined;
    public static Action<Vector2> OnBlockHit;
    public static Action<Vector2, int, ItemSO> OnMiningPoints;

    private bool mining;
    public bool IsMingin => mining;
    public bool IsMining => mining;
    public bool IsChoppingTree { get; private set; }
    public bool HasAxe => surfaceTrees && surfaceTrees.HasAxe;
    public bool IsCuttingGrass { get; private set; }
    public Vector2 MiningTarget { get; private set; }

    void Awake()
    {
        _cam = cam ? cam : Camera.main;
        map = tilemap ? tilemap.GetComponent<MapGenerator>() : null;
        tallGrass = map ? map.GetComponent<SurfaceTallGrass>() : null;
        surfaceTrees = FindFirstObjectByType<SurfaceTrees>();
        if (highlightMap) highlightMap.ClearAllTiles();
    }

    void OnDisable()
    {
        ClearHighlight();
        ClearTreeGlow();
        ClearGrassSelection();
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        activeCursor = ToolCursor.None;
        IsCuttingGrass = false;
        grassSwingUntil = 0f;
        grassClickConsumed = false;
    }

    void OnDestroy()
    {
        if (scytheCursorTexture) Destroy(scytheCursorTexture);
    }

    void Update()
    {
        mining = false;
        IsChoppingTree = false;
        IsCuttingGrass = Time.time < grassSwingUntil;
        if (!Input.GetMouseButton(0)) grassClickConsumed = false;
        if (GameplayInputBlocker.IsBlocked || IsPointerOverUi(Input.mousePosition))
        {
            mining = false;
            ClearHighlight();
            ClearTreeGlow();
            ClearGrassSelection();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            activeCursor = ToolCursor.None;
            IsCuttingGrass = false;
            grassSwingUntil = 0f;
            return;
        }
        if (!_cam || !tilemap) return;

        Vector3 mouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        if (grassClickConsumed)
        {
            ClearHighlight();
            ClearTreeGlow();
            ClearGrassSelection();
            UpdateToolCursor(ToolCursor.None);
            mining = IsCuttingGrass;
            return;
        }
        if (!tallGrass && map) tallGrass = map.GetComponent<SurfaceTallGrass>();
        var grass = tallGrass ? tallGrass.At(mouseWorld) : null;
        bool grassReachable = grass && stats &&
            Vector2.Distance(transform.position, grass.transform.position) <= stats.Reach;
        if (tallGrass)
        {
            foreach (var patch in tallGrass.ActivePatches)
            {
                bool reachable = patch && stats &&
                    Vector2.Distance(transform.position, patch.transform.position) <= stats.Reach;
                patch?.SetReachGlow(reachable && patch == grass ? 2 : reachable ? 1 : 0);
            }
        }
        if (grass)
        {
            ClearHighlight();
            ClearTreeGlow();
            UpdateToolCursor(grassReachable && tallGrass.HasScythe ? ToolCursor.Scythe : ToolCursor.None);
            if (grassReachable && Input.GetMouseButton(0)) TryCutGrassAt(mouseWorld);
            mining = IsCuttingGrass;
            return;
        }
        var tree = ChoppableTree.At(mouseWorld);
        bool hoverReachable = false;
        foreach (var candidate in ChoppableTree.ActiveTrees)
        {
            bool reachable = candidate && candidate.CanChop && stats &&
                Vector2.Distance(transform.position, candidate.HitPoint) <= stats.Reach;
            bool hovered = reachable && candidate == tree;
            candidate?.SetReachGlow(hovered ? 2 : reachable ? 1 : 0);
            hoverReachable |= hovered;
        }
        if (!surfaceTrees) surfaceTrees = FindFirstObjectByType<SurfaceTrees>();
        UpdateToolCursor(hoverReachable && surfaceTrees && surfaceTrees.HasAxe ? ToolCursor.Axe : ToolCursor.None);
        if (tree)
        {
            ClearHighlight();
            if (Input.GetMouseButton(0) && Vector2.Distance(transform.position, tree.HitPoint) <= stats.Reach)
            {
                mining = true;
                IsChoppingTree = true;
                MiningTarget = tree.HitPoint;
                if (Time.time >= nextTreeHitTime)
                {
                    tree.Hit(transform.position);
                    nextTreeHitTime = Time.time + .55f / Mathf.Max(.25f, stats.MiningSpeed);
                }
            }
            return;
        }
        Vector3Int? nearest = FindNearestExistingCell(mouseWorld, searchRadiusCells);

        if (nearest == null)
        {
            ClearHighlight();
            return;
        }

        Vector3Int targetCell = GetReachLimitedCell(nearest.Value);

        if (map && map.IsSurfaceCellProtected(targetCell))
        {
            progress.Remove(targetCell);
            ClearHighlight();
            return;
        }

        ShowHighlight(targetCell);

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
            OnBlockHit?.Invoke(tilemap.GetCellCenterWorld(targetCell));
            nextMiningSoundTime = Time.time + miningSoundInterval;
        }
    }

    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (!eventSystem) return false;
        uiRaycasts.Clear();
        eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = screenPosition }, uiRaycasts);
        return uiRaycasts.Count > 0;
    }

    public bool TryCutGrassAt(Vector2 worldPoint)
    {
        if (GameplayInputBlocker.IsBlocked || !stats || Time.time < nextGrassCutTime) return false;
        if (!tallGrass && map) tallGrass = map.GetComponent<SurfaceTallGrass>();
        if (!tallGrass || !tallGrass.HasScythe) return false;
        var patch = tallGrass ? tallGrass.At(worldPoint) : null;
        if (!patch || Vector2.Distance(transform.position, patch.transform.position) > stats.Reach)
            return false;
        MiningTarget = patch.transform.position;
        if (!patch.Cut()) return false;
        AudioManager.Instance?.Play(SoundType.DryGrass);
        IsCuttingGrass = mining = true;
        grassClickConsumed = true;
        grassSwingUntil = Time.time + .35f;
        nextGrassCutTime = Time.time + .55f / Mathf.Max(.25f, stats.MiningSpeed);
        return true;
    }

    void ShowHighlight(Vector3Int cell)
    {
        if (!highlightMap || !highlightTile) return;
        if (!tilemap.HasTile(cell)) { ClearHighlight(); return; }
        if (highlightedCell == cell && highlightMap.GetTile(cell) == highlightTile) return;
        ClearHighlight();
        highlightMap.SetTile(cell, highlightTile);
        highlightedCell = cell;
    }

    void ClearHighlight()
    {
        if (!highlightMap || !highlightedCell.HasValue) return;
        highlightMap.SetTile(highlightedCell.Value, null);
        highlightedCell = null;
    }

    static void ClearTreeGlow()
    {
        foreach (var tree in ChoppableTree.ActiveTrees) tree?.SetReachGlow(0);
    }

    void ClearGrassSelection()
    {
        if (!tallGrass) return;
        foreach (var patch in tallGrass.ActivePatches) patch?.SetReachGlow(0);
    }

    void UpdateToolCursor(ToolCursor cursor)
    {
        if (cursor == ToolCursor.Scythe && !scytheCursorTexture)
            scytheCursorTexture = CreateScytheCursor(tallGrass ? tallGrass.ScytheIcon : null);
        if (cursor == ToolCursor.Scythe && !scytheCursorTexture) cursor = ToolCursor.None;
        if (activeCursor == cursor) return;
        activeCursor = cursor;
        var texture = cursor == ToolCursor.Axe ? treeCursorTexture :
            cursor == ToolCursor.Scythe ? scytheCursorTexture : null;
        Cursor.SetCursor(texture, texture ? new Vector2(9f, 25f) : Vector2.zero, CursorMode.Auto);
    }

    static Texture2D CreateScytheCursor(Sprite icon)
    {
        if (!icon) return null;
        var source = icon.texture;
        var render = RenderTexture.GetTemporary(48, 48, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Graphics.Blit(source, render);
        RenderTexture.active = render;
        var cursor = new Texture2D(48, 48, TextureFormat.RGBA32, false);
        cursor.ReadPixels(new Rect(0, 0, 48, 48), 0, 0);
        cursor.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(render);
        return cursor;
    }

    Block GetBlock(Vector3Int cell)
    {
        if (!map && tilemap) map = tilemap.GetComponent<MapGenerator>();
        return map ? map.GetBlockAt(cell) : blockRegistry ? blockRegistry.FromTile(tilemap.GetTile(cell)) : null;
    }

    // One transaction for rewards, effects and both render layers. Effects read the cell before removal.
    public bool CompleteMining(Vector3Int cell)
    {
        if (map && map.IsSurfaceCellProtected(cell)) return false;
        if (!tilemap || !tilemap.HasTile(cell)) return false;
        var block = GetBlock(cell);
        var ore = map ? map.GetOreAt(cell) : null;
        int amount = ore ? OreTile.DropCount(ore.richness, UnityEngine.Random.value) : 1;
        if (block && block.itemDrop && amount > 0) InventoryManager.Instance?.Add(block.itemDrop, amount);
        int points = BlockRegistry.GetPoints(block, ore);
        Vector2 minedPosition = tilemap.GetCellCenterWorld(cell);
        OnBlockMined?.Invoke(minedPosition, points);
        if (points > 0) OnMiningPoints?.Invoke(minedPosition, points, block.itemDrop);
        if (map) map.RemoveBlock(cell);
        else
        {
            tilemap.SetTile(cell, null);
            tilemap.GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        }
        progress.Remove(cell);
        AudioManager.Instance?.Play(ore ? SoundType.BreakOre : block ? block.breakSound : SoundType.BreakRock,
            ore != null);
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
