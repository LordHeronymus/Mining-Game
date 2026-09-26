using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
    [SerializeField] BlockRegistry blockRegistry;
    [SerializeField] Camera cam;
    [SerializeField] Texture2D treeCursorTexture;

    [Header("Mining")]
    public int searchRadiusCells = 2;
    public float miningSoundInterval = 0.5f;
    [Min(.05f)] public float cursorPulseInterval = .9f;
    public Vector2 cursorPulseAlphaRange = new(.76f, 1f);
    [Min(1f)] public float cursorPulseSize = 1.05f;
    [FormerlySerializedAs("cursorBrightness"), Range(0f, 3f)] public float cursorGlowStrength = 1f;

    private Dictionary<Vector3Int, float> progress = new();
    private MiningCrackVisual miningCracks;
    private Camera _cam;
    private MapGenerator map;
    private CompactHud hotbar;
    private SurfaceTallGrass tallGrass;
    private SurfaceTrees surfaceTrees;
    private float nextMiningSoundTime = 0f;
    private bool miningBlockActive;
    private MinerPlayerVisual minerVisual;
    private float nextTreeHitTime;
    private float nextGrassCutTime;
    private float grassSwingUntil;
    private bool grassClickConsumed;
    private bool smartCursor = true;
    private Vector3Int? highlightedCell;
    private Vector3Int? highlightedPointerCell;
    private bool highlightedSmartCursor;
    private readonly List<Vector3Int> highlightedCells = new();
    private Tile normalHighlightTile, smartHighlightTile;
    private Material highlightMaterial, originalHighlightMaterial;
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
        if (map) map.Generated += ClearMiningProgress;
        hotbar = FindFirstObjectByType<CompactHud>();
        tallGrass = map ? map.GetComponent<SurfaceTallGrass>() : null;
        surfaceTrees = FindFirstObjectByType<SurfaceTrees>();
        minerVisual = GetComponent<MinerPlayerVisual>();
        if (highlightMap) highlightMap.ClearAllTiles();
        CreateHighlightTiles();
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
        if (map) map.Generated -= ClearMiningProgress;
        miningCracks?.Dispose();
        if (scytheCursorTexture) Destroy(scytheCursorTexture);
        if (highlightMaterial)
        {
            var renderer = highlightMap ? highlightMap.GetComponent<TilemapRenderer>() : null;
            if (renderer) renderer.sharedMaterial = originalHighlightMaterial;
            Destroy(highlightMaterial);
        }
        DestroyHighlightTile(normalHighlightTile);
        DestroyHighlightTile(smartHighlightTile);
    }

    void Update()
    {
        miningCracks?.Refresh(progress);
        bool continuedBlockMining = miningBlockActive;
        miningBlockActive = false;
        mining = false;
        IsChoppingTree = false;
        IsCuttingGrass = Time.time < grassSwingUntil;
        if (!Input.GetMouseButton(0)) grassClickConsumed = false;
        if (!hotbar) hotbar = FindFirstObjectByType<CompactHud>();
        if (GameplayInputBlocker.IsBlocked || IsPointerOverUi(Input.mousePosition) || (hotbar && hotbar.SelectedSlot != 0))
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
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl) || Input.GetMouseButtonDown(2))
            smartCursor = !smartCursor;
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
        Vector3Int? nearest = smartCursor
            ? FindNearestExistingCell(mouseWorld, searchRadiusCells)
            : tilemap.WorldToCell(mouseWorld);

        if (nearest == null)
        {
            ClearHighlight();
            return;
        }

        Vector3Int targetCell;
        if (smartCursor) targetCell = GetReachLimitedCell(nearest.Value);
        else
        {
            targetCell = nearest.Value;
            if (!stats || Vector2.Distance(tilemap.GetCellCenterWorld(targetCell), transform.position) > stats.Reach)
            {
                ClearHighlight();
                return;
            }
        }

        if (map && map.IsSurfaceCellProtected(targetCell))
        {
            progress.Remove(targetCell);
            ClearHighlight();
            return;
        }

        ShowHighlight(targetCell, tilemap.WorldToCell(mouseWorld));

        if (!Input.GetMouseButton(0))
        {
            mining = false;
            return;
        }
        TileBase t = tilemap.GetTile(targetCell);
        if (!t) return;
        mining = true;
        miningBlockActive = true;
        MiningTarget = tilemap.GetCellCenterWorld(targetCell);
        float hitInterval = GetMiningHitInterval();
        if (!continuedBlockMining) nextMiningSoundTime = Time.time + hitInterval * .5f;
        if (Time.time < nextMiningSoundTime) return;
        nextMiningSoundTime = Time.time + hitInterval;
        ApplyMiningHit(targetCell, hitInterval);
    }

    float GetMiningHitInterval()
    {
        if (!minerVisual) minerVisual = GetComponent<MinerPlayerVisual>();
        return minerVisual && minerVisual.enabled
            ? 1f / Mathf.Max(.1f, minerVisual.miningSwingsPerSecond)
            : Mathf.Max(.05f, miningSoundInterval);
    }

    void ApplyMiningHit(Vector3Int cell, float hitInterval)
    {
        float p = progress.TryGetValue(cell, out var current) ? current : 0f;
        p += hitInterval / Mathf.Max(.0001f, GetTargetMineTime(cell));
        if (p >= 1f) { CompleteMining(cell); return; }
        progress[cell] = p;
        miningCracks ??= new MiningCrackVisual(tilemap);
        miningCracks.Show(cell, p);

        PlayMiningHitSound(GetBlock(cell));
        OnBlockHit?.Invoke(tilemap.GetCellCenterWorld(cell));
    }

    void PlayMiningHitSound(Block block)
    {
        SoundType hit = SoundType.DigMedium;
        int hitLayer = -1;
        if (block != null)
        {
            hitLayer = GetTerrainLayerIndex(block);
            hit = hitLayer >= 2 ? SoundType.DigDeepStone : block.digSound;
        }
        if (hitLayer >= 0) AudioManager.Instance?.PlayLayerMiningSound(hitLayer, hit, false);
        else AudioManager.Instance?.Play(hit, true);
    }

    void ShowMiningCracks(Vector3Int cell, float amount)
    {
        miningCracks ??= new MiningCrackVisual(tilemap);
        miningCracks.Show(cell, amount);
    }

    void ClearMiningProgress()
    {
        progress.Clear();
        miningCracks?.Dispose();
        miningCracks = null;
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

    void ShowHighlight(Vector3Int cell, Vector3Int pointerCell)
    {
        if (!highlightMap || !normalHighlightTile || !tilemap.HasTile(cell)) { ClearHighlight(); return; }
        if (highlightedCell == cell && highlightedPointerCell == pointerCell &&
            highlightedSmartCursor == smartCursor)
        {
            UpdateHighlightPulse();
            return;
        }
        ClearHighlight();
        highlightMap.SetTile(cell, smartCursor ? smartHighlightTile : normalHighlightTile);
        highlightMap.SetColor(cell, Color.white);
        highlightMap.SetTransformMatrix(cell, Matrix4x4.identity);
        highlightedCells.Add(cell);
        highlightedCell = cell;
        highlightedPointerCell = pointerCell;
        highlightedSmartCursor = smartCursor;
        UpdateHighlightPulse();
    }

    void UpdateHighlightPulse()
    {
        if (!highlightMap || !highlightedCell.HasValue) return;
        float glowStrength = Mathf.Clamp(cursorGlowStrength, 0f, 3f);
        if (highlightMaterial && highlightMaterial.HasProperty("_Color"))
            highlightMaterial.SetColor("_Color", Color.white * glowStrength);
        float interval = Mathf.Max(.05f, cursorPulseInterval);
        float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / interval));
        float minAlpha = Mathf.Clamp01(Mathf.Min(cursorPulseAlphaRange.x, cursorPulseAlphaRange.y));
        float maxAlpha = Mathf.Clamp01(Mathf.Max(cursorPulseAlphaRange.x, cursorPulseAlphaRange.y));
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, pulse);
        float size = Mathf.Lerp(1f, Mathf.Clamp(cursorPulseSize, 1f, 1.5f), pulse);
        highlightMap.SetColor(highlightedCell.Value,
            new Color(1f, 1f, 1f, alpha * Mathf.Lerp(.3f, 1f, Mathf.Clamp01(glowStrength))));
        highlightMap.SetTransformMatrix(highlightedCell.Value, Matrix4x4.Scale(new Vector3(size, size, 1f)));
    }

    void ClearHighlight()
    {
        if (highlightMap)
            foreach (var cell in highlightedCells)
            {
                highlightMap.SetColor(cell, Color.white);
                highlightMap.SetTransformMatrix(cell, Matrix4x4.identity);
                highlightMap.SetTile(cell, null);
            }
        highlightedCells.Clear();
        highlightedCell = null;
        highlightedPointerCell = null;
    }

    void CreateHighlightTiles()
    {
        if (!highlightMap) return;
        var renderer = highlightMap.GetComponent<TilemapRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (renderer && shader)
        {
            originalHighlightMaterial = renderer.sharedMaterial;
            highlightMaterial = new Material(shader)
            {
                name = "Cursor Highlight Unlit",
                hideFlags = HideFlags.HideAndDontSave
            };
            renderer.sharedMaterial = highlightMaterial;
        }
        float orientationScale = Mathf.Abs(highlightMap.orientationMatrix.lossyScale.x);
        float frameScale = orientationScale / Mathf.Max(.001f, highlightMap.cellSize.x);
        normalHighlightTile = CreateAssetHighlightTile("Normal Cursor", "Cursor/NormalCursorFrame", frameScale);
        smartHighlightTile = CreateAssetHighlightTile("Smart Cursor", "Cursor/SmartCursorFrame", frameScale);
    }

    static Tile CreateAssetHighlightTile(string name, string resourcePath, float frameScale)
    {
        var texture = Resources.Load<Texture2D>(resourcePath);
        if (!texture) return null;
        texture.filterMode = FilterMode.Trilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        // The gold outline spans 85% of the texture; the remaining space holds its soft halo.
        float pixelsPerUnit = texture.width * .85f * frameScale;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = name;
        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.None;
        tile.flags = TileFlags.None;
        tile.hideFlags = HideFlags.HideAndDontSave;
        return tile;
    }

    static void DestroyHighlightTile(Tile tile)
    {
        if (!tile) return;
        if (tile.sprite)
        {
            Destroy(tile.sprite);
        }
        Destroy(tile);
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

    int GetTerrainLayerIndex(Block block)
    {
        if (!block || !map || map.layers == null) return -1;

        // Transition bands can contain stones from an incoming layer before its start depth.
        // Identify the source layer from the actual block so sound and tuning follow its material.
        for (int i = 0; i < map.layers.Length; i++)
            if (map.layers[i] != null && map.layers[i].stone == block)
                return i;

        return -1;
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
        miningCracks?.Hide(cell);
        int terrainLayer = !ore ? GetTerrainLayerIndex(block) : -1;
        SoundType breakSound = ore ? SoundType.BreakOre : block ? block.breakSound : SoundType.BreakRock;
        if (terrainLayer >= 2) breakSound = SoundType.StoneBreak;
        else if (terrainLayer >= 0) breakSound = SoundType.ClayBreak;
        PlayMiningHitSound(block);
        if (terrainLayer >= 0) AudioManager.Instance?.PlayLayerMiningSound(terrainLayer, breakSound, true);
        else AudioManager.Instance?.Play(breakSound, ore != null);
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

        if (blockRegistry != null || map)
        {
            Block b = GetBlock(cell);
            if (b != null) time *= map ? map.GetHardnessAt(cell, b) :
                Mathf.Max(0.01f, b.hardness <= 0 ? 1f : b.hardness);
        }

        return time;
    }
}
