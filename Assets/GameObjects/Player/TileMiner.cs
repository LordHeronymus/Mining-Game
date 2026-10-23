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
    public float miningSoundInterval = 0.5f;
    [Min(.05f)] public float cursorPulseInterval = .9f;
    public Vector2 cursorPulseAlphaRange = new(.76f, 1f);
    [Min(1f)] public float cursorPulseSize = 1.05f;
    [FormerlySerializedAs("cursorBrightness"), Range(0f, 3f)] public float cursorGlowStrength = 1f;
    [Min(.1f)] public float cursorGlintInterval = 1.6f;
    [Range(0f, 1f)] public float cursorGlintSize = .32f;
    [Range(0f, 3f)] public float cursorGlintStrength = 1f;

    private Dictionary<Vector3Int, float> progress = new();
    private readonly Dictionary<Vector3Int, float> lastMiningHitTime = new();
    private readonly Dictionary<Vector3Int, float> lastMiningProgressUpdateTime = new();
    private readonly List<Vector3Int> miningProgressCells = new();
    const float MiningRegenerationDelay = 60f;
    const float MiningRegenerationDuration = 60f;
    private readonly Dictionary<Vector3Int, int> pendingDropCounts = new();
    private MiningCrackVisual miningCracks;
    private Camera _cam;
    private MapGenerator map;
    private CompactHud hotbar;
    private SurfaceTallGrass tallGrass;
    private SurfaceTrees surfaceTrees;
    private float nextMiningHitTime = 0f;
    private bool miningTargetActive;
    private bool miningStalled;
    private Vector3Int? stalledMiningCell;
    private MinerPlayerVisual minerVisual;
    private float nextGrassCutTime;
    private float grassSwingUntil;
    private bool grassClickConsumed;
    private bool smartCursor = false;
    private Vector3Int? highlightedCell;
    private Vector3Int? highlightedPointerCell;
    private bool highlightedSmartCursor;
    private readonly List<Vector3Int> highlightedCells = new();
    private Tile normalHighlightTile, smartHighlightTile;
    private Material highlightMaterial, originalHighlightMaterial;
    private TilemapRenderer highlightRenderer;
    private int originalHighlightLayer, originalHighlightOrder;
    private GameObject glintRoot;
    private readonly SpriteRenderer[] glints = new SpriteRenderer[4];
    private Sprite glintSprite;
    private Texture2D scytheCursorTexture;
    private ToolCursor activeCursor;
    static readonly List<UnityEngine.EventSystems.RaycastResult> uiRaycasts = new();
    static UnityEngine.EventSystems.PointerEventData uiPointer;
    static UnityEngine.EventSystems.EventSystem uiEventSystem;
    static int uiRaycastFrame = -1;
    static Vector2 uiRaycastPosition;
    static bool uiRaycastBlocked, uiRaycastResult;
    enum ToolCursor { None, Axe, Scythe }

    public static Action<Vector2, int> OnBlockMined;
    public static Action<Vector2> OnBlockHit;
    public static Action<Vector2, int, ItemSO> OnMiningPoints;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        OnBlockMined = null;
        OnBlockHit = null;
        OnMiningPoints = null;
        uiRaycasts.Clear();
        uiPointer = null;
        uiEventSystem = null;
        uiRaycastFrame = -1;
    }

    private bool mining;
    // Energy drain: a block whose drop no longer fits the carry limit keeps the swing but costs nothing.
    public bool IsMingin => mining && !miningStalled;
    public bool IsMining => mining;
    public bool IsChoppingTree { get; private set; }
    public bool HasAxe => surfaceTrees && surfaceTrees.HasAxe;
    public bool IsCuttingGrass { get; private set; }
    public Vector2 MiningTarget { get; private set; }

    public SavedMiningCell[] CaptureRunState()
    {
        var cells = new List<SavedMiningCell>();
        foreach (var entry in progress)
            cells.Add(new SavedMiningCell { cell = entry.Key, progress = entry.Value,
                hitAgo = lastMiningHitTime.TryGetValue(entry.Key, out float hit) ? Time.time - hit : 0,
                pendingDrop = pendingDropCounts.TryGetValue(entry.Key, out int count) ? count : 0 });
        return cells.ToArray();
    }
    public void RestoreRunState(SavedMiningCell[] cells)
    {
        ClearMiningProgress();
        foreach (var entry in cells ?? Array.Empty<SavedMiningCell>())
        {
            if (!tilemap.HasTile(entry.cell)) continue;
            progress[entry.cell] = Mathf.Clamp01(entry.progress);
            lastMiningHitTime[entry.cell] = Time.time - entry.hitAgo;
            lastMiningProgressUpdateTime[entry.cell] = Time.time;
            if (entry.pendingDrop > 0) pendingDropCounts[entry.cell] = entry.pendingDrop;
            ShowMiningCracks(entry.cell, entry.progress);
        }
    }

    void Awake()
    {
        GpsSettings.ApplyComponent(this);
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
            if (renderer)
            {
                renderer.sharedMaterial = originalHighlightMaterial;
                renderer.sortingLayerID = originalHighlightLayer;
                renderer.sortingOrder = originalHighlightOrder;
            }
            Destroy(highlightMaterial);
        }
        DestroyHighlightTile(normalHighlightTile);
        DestroyHighlightTile(smartHighlightTile);
        if (glintRoot) Destroy(glintRoot);
        if (glintSprite) { Destroy(glintSprite.texture); Destroy(glintSprite); }
    }

    void Update()
    {
        RegenerateMiningProgress(Time.time);
        miningCracks?.Refresh(progress);
        bool continuedMining = miningTargetActive;
        miningTargetActive = false;
        mining = false;
        miningStalled = false;
        IsChoppingTree = false;
        IsCuttingGrass = Time.time < grassSwingUntil;
        if (!GameBindings.Held(GameAction.Mine)) grassClickConsumed = false;
        if (!hotbar) hotbar = FindFirstObjectByType<CompactHud>();
        if (GameplayInputBlocker.IsBlocked || IsPointerOverUi(Input.mousePosition) || HotbarBlocksMining(hotbar))
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
        if (GameBindings.Down(GameAction.SmartCursor))
            smartCursor = !smartCursor;
        if (!_cam || !tilemap) return;

        Vector3 mouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        var boulder = BoulderField.At(mouseWorld);
        if (boulder)
        {
            ClearHighlight(); ClearTreeGlow(); ClearGrassSelection();
            UpdateToolCursor(ToolCursor.None);
            if (stats && GameBindings.Held(GameAction.Mine) &&
                Vector2.Distance(transform.position, boulder.HitPoint) <= stats.Reach)
            {
                mining = true; miningTargetActive = true;
                MiningTarget = boulder.HitPoint;
                if (TryBeginMiningHit(continuedMining)) boulder.Hit();
            }
            return;
        }
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
            if (grassReachable && GameBindings.Held(GameAction.Mine)) TryCutGrassAt(mouseWorld);
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
            if (GameBindings.Held(GameAction.Mine) && Vector2.Distance(transform.position, tree.HitPoint) <= stats.Reach)
            {
                mining = true;
                miningTargetActive = true;
                IsChoppingTree = true;
                MiningTarget = tree.HitPoint;
                if (TryBeginMiningHit(continuedMining)) tree.Hit(transform.position);
            }
            return;
        }
        Vector3Int? selectedCell = smartCursor
            ? FindSmartMiningTarget(mouseWorld)
            : tilemap.WorldToCell(mouseWorld);

        if (selectedCell == null)
        {
            ClearHighlight();
            return;
        }

        Vector3Int targetCell = selectedCell.Value;
        if (!stats || Vector2.Distance(tilemap.GetCellCenterWorld(targetCell), transform.position) > stats.Reach)
        {
            ClearHighlight();
            return;
        }

        if (map && map.IsCellProtected(targetCell))
        {
            progress.Remove(targetCell);
            lastMiningHitTime.Remove(targetCell);
            lastMiningProgressUpdateTime.Remove(targetCell);
            pendingDropCounts.Remove(targetCell);
            ClearHighlight();
            return;
        }

        ShowHighlight(targetCell, tilemap.WorldToCell(mouseWorld));

        if (!GameBindings.Held(GameAction.Mine))
        {
            mining = false;
            return;
        }
        TileBase t = tilemap.GetTile(targetCell);
        if (!t) return;
        mining = true;
        miningTargetActive = true;
        miningStalled = stalledMiningCell == targetCell;
        if (!miningStalled) stalledMiningCell = null;
        MiningTarget = tilemap.GetCellCenterWorld(targetCell);
        if (!TryBeginMiningHit(continuedMining)) return;
        ApplyMiningHit(targetCell, GetBaseMiningHitInterval());
    }

    // Dragging a hotbar item holds the mine button; it must not dig into the world.
    static bool HotbarBlocksMining(CompactHud hotbar) =>
        hotbar && (hotbar.SelectedSlot != 0 || hotbar.IsDraggingItem);

    bool TryBeginMiningHit(bool continuedMining) =>
        TryBeginMiningHit(ref nextMiningHitTime, Time.time, GetMiningHitInterval(), continuedMining);

    // A new press gets a quick first hit, but tapping never skips the cooldown of the previous hit.
    static bool TryBeginMiningHit(ref float nextHitTime, float now, float hitInterval, bool continuedMining)
    {
        if (!continuedMining) nextHitTime = Mathf.Max(nextHitTime, now + hitInterval * .5f);
        if (now < nextHitTime) return false;
        nextHitTime = now + hitInterval;
        return true;
    }

    float GetMiningHitInterval()
    {
        return GetBaseMiningHitInterval() / GameplayTestSettings.DiggingMultiplier;
    }

    float GetBaseMiningHitInterval()
    {
        if (!minerVisual) minerVisual = GetComponent<MinerPlayerVisual>();
        return minerVisual && minerVisual.enabled
            ? 1f / Mathf.Max(.1f, minerVisual.miningSwingsPerSecond)
            : Mathf.Max(.05f, miningSoundInterval);
    }

    void ApplyMiningHit(Vector3Int cell, float damageInterval)
    {
        if (!CanMineBlock(cell))
        {
            AudioManager.Instance?.PlayBlockedMiningHit();
            return;
        }
        float p = progress.TryGetValue(cell, out var current) ? current : 0f;
        float pickaxeMultiplier = InventoryManager.Instance
            ? InventoryManager.Instance.EquippedPickaxeProgressMultiplier : 1f;
        p += damageInterval * pickaxeMultiplier / Mathf.Max(.0001f, GetTargetMineTime(cell));
        lastMiningHitTime[cell] = Time.time;
        lastMiningProgressUpdateTime[cell] = Time.time;
        if (p >= 1f)
        {
            // Hardness and the target were checked above: a failed completion means the drop is too heavy.
            if (CompleteMining(cell)) stalledMiningCell = null;
            else
            {
                stalledMiningCell = cell;
                miningStalled = true;
                AudioManager.Instance?.PlayBlockedMiningHit();
            }
            return;
        }
        progress[cell] = p;
        miningCracks ??= new MiningCrackVisual(tilemap);
        miningCracks.Show(cell, p);

        bool isOre = map && map.GetOreAt(cell);
        PlayMiningHitSound(GetBlock(cell), isOre, isOre ? GetSubstrateBlock(cell) : null);
        OnBlockHit?.Invoke(tilemap.GetCellCenterWorld(cell));
    }

    void PlayMiningHitSound(Block block, bool isOre, Block substrate)
    {
        SoundType hit = GetMiningHitSound(map ? map.layers : null, block, isOre, substrate, out int hitLayer);
        if (hitLayer >= 0) AudioManager.Instance?.PlayLayerMiningSound(hitLayer, hit, false);
        else AudioManager.Instance?.Play(hit, true);
    }

    // Ore cells report the ore block, so deep ores take their layer from the host stone.
    // Ores above layer three keep their own dig sound.
    static SoundType GetMiningHitSound(MapLayer[] layers, Block block, bool isOre, Block substrate, out int hitLayer)
    {
        hitLayer = -1;
        if (block == null) return SoundType.DigMedium;
        hitLayer = GetTerrainLayerIndex(layers, isOre ? substrate : block);
        if (isOre && hitLayer < 2) hitLayer = -1;
        return hitLayer >= 2
            ? (isOre ? SoundType.DigDeepOreHit : SoundType.DigDeepStone)
            : block.digSound;
    }

    void ShowMiningCracks(Vector3Int cell, float amount)
    {
        miningCracks ??= new MiningCrackVisual(tilemap);
        miningCracks.Show(cell, amount);
    }

    void ClearMiningProgress()
    {
        progress.Clear();
        lastMiningHitTime.Clear();
        lastMiningProgressUpdateTime.Clear();
        pendingDropCounts.Clear();
        miningCracks?.Dispose();
        miningCracks = null;
        stalledMiningCell = null;
    }

    void RegenerateMiningProgress(float now)
    {
        if (progress.Count == 0) return;
        miningProgressCells.Clear();
        miningProgressCells.AddRange(progress.Keys);
        for (int i = 0; i < miningProgressCells.Count; i++)
        {
            Vector3Int cell = miningProgressCells[i];
            if (!progress.TryGetValue(cell, out float amount)) continue;
            if (!lastMiningHitTime.TryGetValue(cell, out float lastHit))
            {
                lastMiningHitTime[cell] = now;
                lastMiningProgressUpdateTime[cell] = now;
                continue;
            }

            if (!lastMiningProgressUpdateTime.TryGetValue(cell, out float lastUpdate))
                lastUpdate = lastHit;
            float decayStart = lastHit + MiningRegenerationDelay;
            float decayTime = Mathf.Max(0f, now - Mathf.Max(lastUpdate, decayStart));
            lastMiningProgressUpdateTime[cell] = now;
            if (decayTime <= 0f) continue;

            amount = Mathf.Max(0f, amount - decayTime / MiningRegenerationDuration);
            if (amount <= 0f)
            {
                progress.Remove(cell);
                lastMiningHitTime.Remove(cell);
                lastMiningProgressUpdateTime.Remove(cell);
                pendingDropCounts.Remove(cell);
                miningCracks?.Hide(cell);
            }
            else progress[cell] = amount;
        }
    }

    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (!eventSystem) return false;
        bool blocked = GameplayInputBlocker.IsBlocked;
        if (uiRaycastFrame == Time.frameCount && uiEventSystem == eventSystem &&
            uiRaycastPosition == screenPosition && uiRaycastBlocked == blocked)
            return uiRaycastResult;
        if (uiEventSystem != eventSystem || uiPointer == null)
        {
            uiEventSystem = eventSystem;
            uiPointer = new UnityEngine.EventSystems.PointerEventData(eventSystem);
        }
        uiPointer.Reset();
        uiPointer.position = screenPosition;
        uiRaycasts.Clear();
        eventSystem.RaycastAll(uiPointer, uiRaycasts);
        uiRaycastFrame = Time.frameCount;
        uiRaycastPosition = screenPosition;
        uiRaycastBlocked = blocked;
        uiRaycastResult = uiRaycasts.Count > 0;
        return uiRaycastResult;
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
        nextGrassCutTime = Time.time + .55f / Mathf.Max(.25f, stats.MiningSpeed) / GameplayTestSettings.DiggingMultiplier;
        return true;
    }

    void ShowHighlight(Vector3Int cell, Vector3Int pointerCell)
    {
        if (!highlightMap || !normalHighlightTile || !tilemap.HasTile(cell)) { ClearHighlight(); return; }
        EnsureHighlightSorting();
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
        UpdateGlints(highlightedCell.Value, size, glowStrength);
    }

    // Small stars twinkle one after another on the four frame corners (anticlockwise from top right).
    void UpdateGlints(Vector3Int cell, float frameSize, float glowStrength)
    {
        if (!EnsureGlints()) return;
        Vector3 min = highlightMap.CellToWorld(cell), max = highlightMap.CellToWorld(cell + new Vector3Int(1, 1, 0));
        Vector3 centre = (min + max) * .5f, half = (max - min) * .5f * frameSize;
        float cellSize = Mathf.Abs(max.x - min.x);
        float strength = Mathf.Clamp(cursorGlintStrength, 0f, 3f) * Mathf.Clamp01(glowStrength);
        float phase = Time.unscaledTime / Mathf.Max(.1f, cursorGlintInterval);
        glintRoot.SetActive(strength > 0f && cursorGlintSize > 0f);
        for (int i = 0; i < glints.Length; i++)
        {
            float sx = i == 0 || i == 3 ? 1f : -1f, sy = i < 2 ? 1f : -1f;
            float twinkle = Mathf.Pow(Mathf.Max(0f, Mathf.Sin((phase - i * .25f) * Mathf.PI * 2f)), 3f);
            float scale = cellSize * Mathf.Clamp01(cursorGlintSize) * Mathf.Lerp(.45f, 1f, twinkle);
            var glint = glints[i];
            glint.transform.position = new Vector3(centre.x + sx * half.x, centre.y + sy * half.y, centre.z);
            glint.transform.localScale = new Vector3(scale, scale, 1f);
            glint.color = new Color(1f, 1f, 1f, Mathf.Clamp01(strength * Mathf.Lerp(.3f, 1f, twinkle)));
        }
    }

    bool EnsureGlints()
    {
        if (!highlightRenderer || !highlightMaterial) return false;
        if (!glintRoot)
        {
            if (!glintSprite) glintSprite = CreateGlintSprite();
            glintRoot = new GameObject("Cursor Glints") { hideFlags = HideFlags.DontSave, layer = highlightMap.gameObject.layer };
            for (int i = 0; i < glints.Length; i++)
            {
                var go = new GameObject("Glint " + i) { hideFlags = HideFlags.DontSave, layer = glintRoot.layer };
                go.transform.SetParent(glintRoot.transform, false);
                glints[i] = go.AddComponent<SpriteRenderer>();
                glints[i].sprite = glintSprite;
                glints[i].sharedMaterial = highlightMaterial;
            }
        }
        foreach (var glint in glints)
        {
            glint.sortingLayerID = highlightRenderer.sortingLayerID;
            glint.sortingOrder = highlightRenderer.sortingOrder + 1;
        }
        return true;
    }

    void HideGlints()
    {
        if (glintRoot) glintRoot.SetActive(false);
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
        HideGlints();
    }

    void CreateHighlightTiles()
    {
        if (!highlightMap) return;
        var renderer = highlightMap.GetComponent<TilemapRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (renderer && shader)
        {
            highlightRenderer = renderer;
            originalHighlightLayer = renderer.sortingLayerID;
            originalHighlightOrder = renderer.sortingOrder;
            originalHighlightMaterial = renderer.sharedMaterial;
            highlightMaterial = new Material(shader)
            {
                name = "Cursor Highlight Unlit",
                hideFlags = HideFlags.HideAndDontSave
            };
            renderer.sharedMaterial = highlightMaterial;
            EnsureHighlightSorting();
        }
        float orientationScale = Mathf.Abs(highlightMap.orientationMatrix.lossyScale.x);
        float frameScale = orientationScale / Mathf.Max(.001f, highlightMap.cellSize.x);
        normalHighlightTile = CreateHighlightTile("Normal Cursor", false, frameScale);
        smartHighlightTile = CreateHighlightTile("Smart Cursor", true, frameScale);
    }

    void EnsureHighlightSorting()
    {
        if (!highlightRenderer || !tilemap) return;
        var terrainRenderer = tilemap.GetComponent<TilemapRenderer>();
        if (!terrainRenderer) return;
        // The frame glows on its own: it is drawn above the darkness overlay (MapLighting, order 32760) and the
        // ore glints (32761), so it stays visible in total darkness. It is unlit and no Light2D: it lights nothing.
        highlightRenderer.sortingLayerID = SortingLayer.layers[SortingLayer.layers.Length - 1].id;
        highlightRenderer.sortingOrder = AboveDarknessOrder;
    }

    const int AboveDarknessOrder = 32762;

    // The two cursor textures (512² pixels each) took most of TileMiner.Awake (~130 ms at every scene start).
    // They do not depend on the scene, so they are built once per session and kept (HideAndDontSave).
    static Texture2D normalCursorTexture, cornerCursorTexture;

    static Tile CreateHighlightTile(string name, bool cornersOnly, float frameScale)
    {
        ref var cached = ref cornersOnly ? ref cornerCursorTexture : ref normalCursorTexture;
        if (!cached) cached = CreateHighlightTexture(name, cornersOnly);
        var texture = cached;
        // The gold outline spans FrameSpan of the texture; the remaining space holds its glow.
        float pixelsPerUnit = texture.width * FrameSpan * frameScale;
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

    const int HighlightResolution = 512;
    const float FrameSpan = .62f;

    static Texture2D CreateHighlightTexture(string name, bool cornersOnly)
    {
        var texture = new Texture2D(HighlightResolution, HighlightResolution, TextureFormat.RGBA32, true)
        {
            name = name + " Texture",
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(HighlightPixels(cornersOnly));
        texture.Apply(true, true);
        texture.filterMode = FilterMode.Trilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    static Color[] HighlightPixels(bool cornersOnly)
    {
        const int resolution = HighlightResolution, half = resolution / 2;
        var pixels = new Color[resolution * resolution];
        // Each pixel depends only on its mirrored distance to the centre: compute one quadrant, mirror it exactly.
        for (int y = 0; y < half; y++)
        for (int x = 0; x < half; x++)
        {
            var color = HighlightPixel(new Vector2(Mathf.Abs((x + .5f) / resolution - .5f),
                Mathf.Abs((y + .5f) / resolution - .5f)), cornersOnly);
            int mirrorX = resolution - 1 - x, mirrorY = resolution - 1 - y;
            pixels[y * resolution + x] = color;
            pixels[y * resolution + mirrorX] = color;
            pixels[mirrorY * resolution + x] = color;
            pixels[mirrorY * resolution + mirrorX] = color;
        }
        return pixels;
    }

    // p: distance from the texture centre (0..0.5). Bright gold core with a warm glow inside and outside.
    static Color HighlightPixel(Vector2 p, bool cornersOnly)
    {
        const float halfFrame = FrameSpan * .5f;
        const float armLength = FrameSpan * .22f;
        float end = cornersOnly ? halfFrame - armLength : 0f;
        var horizontal = new Vector2(Mathf.Clamp(p.x, end, halfFrame), halfFrame);
        var vertical = new Vector2(halfFrame, Mathf.Clamp(p.y, end, halfFrame));
        float horizontalDistance = Vector2.Distance(p, horizontal);
        float verticalDistance = Vector2.Distance(p, vertical);
        bool nearestHorizontal = horizontalDistance < verticalDistance;
        float distance = Mathf.Min(horizontalDistance, verticalDistance) / FrameSpan; // in cell widths
        float taper = cornersOnly ? Mathf.SmoothStep(0f, 1f,
            ((nearestHorizontal ? horizontal.x : vertical.y) - end) / armLength) : 1f;
        float width = cornersOnly ? Mathf.Lerp(.003f, .014f, taper) : .012f;
        float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width - .003f, width + .003f, distance));
        float glow = Mathf.Exp(-distance * distance / (.06f * .06f)) * .7f
            + Mathf.Exp(-distance * distance / (.16f * .16f)) * .22f;
        float alpha = Mathf.Clamp01(core + glow) * taper;
        float heat = Mathf.Clamp01(core * .75f + glow * .25f);
        var color = Color.Lerp(new Color(1f, .55f, .1f), new Color(1f, .95f, .78f), heat);
        color.a = alpha;
        return color;
    }

    // Four-pointed star with a soft core, white so the renderer colour controls its strength.
    static Sprite CreateGlintSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "Cursor Glint Texture",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Abs((x + .5f) / size - .5f) * 2f, dy = Mathf.Abs((y + .5f) / size - .5f) * 2f;
            float rays = Mathf.Exp(-dy * dy / (.035f * .035f)) * Mathf.Exp(-dx / .3f)
                + Mathf.Exp(-dx * dx / (.035f * .035f)) * Mathf.Exp(-dy / .3f);
            float dot = Mathf.Exp(-(dx * dx + dy * dy) / (.14f * .14f));
            float edge = Mathf.Clamp01((1f - Mathf.Max(dx, dy)) * 8f);
            float a = Mathf.Clamp01(rays + dot) * edge;
            var c = Color.Lerp(new Color(1f, .78f, .35f), Color.white, Mathf.Clamp01(dot * 1.5f + rays * .4f));
            c.a = a;
            pixels[y * size + x] = c;
        }
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
        sprite.name = "Cursor Glint";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    static void DestroyHighlightTile(Tile tile)
    {
        if (!tile) return;
        if (tile.sprite) Destroy(tile.sprite); // the cursor textures stay cached for the next scene start
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

    bool CanMineBlock(Vector3Int cell)
    {
        var block = GetBlock(cell);
        var inventory = InventoryManager.Instance;
        return !block || !inventory || block.hardnessIndex <= inventory.EquippedPickaxeMaximumHardness;
    }

    Block GetSubstrateBlock(Vector3Int cell) =>
        map && map.registry ? map.registry.FromTile(tilemap.GetTile(cell)) : null;

    int GetTerrainLayerIndex(Block block) => map ? GetTerrainLayerIndex(map.layers, block) : -1;

    static int GetTerrainLayerIndex(MapLayer[] layers, Block block)
    {
        if (!block || layers == null) return -1;

        // Transition bands can contain stones from an incoming layer before its start depth.
        // Identify the source layer from the actual block so sound and tuning follow its material.
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && layers[i].stone == block)
                return i;

        return -1;
    }

    // One transaction for rewards, effects and both render layers. Effects read the cell before removal.
    public bool CompleteMining(Vector3Int cell, bool explosive = false)
    {
        if (map && map.IsCellProtected(cell)) return false;
        if (!tilemap || !tilemap.HasTile(cell)) return false;
        if (!explosive && !CanMineBlock(cell)) return false;
        var block = GetBlock(cell);
        var ore = map ? map.GetOreAt(cell) : null;
        var artifact = map ? map.GetArtifactAt(cell) : null;
        var substrate = ore ? GetSubstrateBlock(cell) : null;
        if (!pendingDropCounts.TryGetValue(cell, out int amount))
        {
            amount = ore ? OreTile.DropCount(ore.richness, UnityEngine.Random.value) : 1;
            pendingDropCounts[cell] = amount;
        }
        var inventory = InventoryManager.Instance;
        if (block && block.itemDrop && amount > 0 &&
            (!inventory || !inventory.CanAdd(block.itemDrop, amount))) return false;
        if (block && block.itemDrop && amount > 0) inventory.Add(block.itemDrop, amount);
        MetaProgressionRuntime.RecordMining(cell, block && block.itemDrop ? block.itemDrop : null, amount, artifact,
            block, map ? map.layers : null, ore != null, explosive, map);
        int points = BlockRegistry.GetPoints(block, ore);
        Vector2 minedPosition = tilemap.GetCellCenterWorld(cell);
        OnBlockMined?.Invoke(minedPosition, points);
        if (points > 0) OnMiningPoints?.Invoke(minedPosition, points, block.itemDrop);
        if (artifact) StatsManager.Instance?.CollectArtifact(artifact,
            map.GetArtifactCash(artifact), map.GetArtifactPoints(artifact));
        if (cell.y == 0 && map) map.GetComponent<SurfaceTallGrass>()?.RemoveWithoutYieldAt(cell.x);
        if (map) map.RemoveBlock(cell);
        else
        {
            tilemap.SetTile(cell, null);
            tilemap.GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        }
        progress.Remove(cell);
        lastMiningHitTime.Remove(cell);
        lastMiningProgressUpdateTime.Remove(cell);
        pendingDropCounts.Remove(cell);
        miningCracks?.Hide(cell);
        int terrainLayer = !ore ? GetTerrainLayerIndex(block) : -1;
        SoundType breakSound = ore ? SoundType.BreakOre : block ? block.breakSound : SoundType.BreakRock;
        if (terrainLayer >= 2) breakSound = SoundType.StoneBreak;
        else if (terrainLayer >= 0) breakSound = SoundType.ClayBreak;
        PlayMiningHitSound(block, ore != null, substrate);
        if (terrainLayer >= 0) AudioManager.Instance?.PlayLayerMiningSound(terrainLayer, breakSound, true);
        else AudioManager.Instance?.Play(breakSound, ore != null);
        return true;
    }

    Vector3Int? FindSmartMiningTarget(Vector3 mouseWorld)
    {
        if (!stats || stats.Reach <= 0f) return null;

        Vector2 playerPosition = transform.position;
        Vector2 aim = (Vector2)mouseWorld - playerPosition;
        float aimDistance = aim.magnitude;
        Vector2 direction = aimDistance > .001f ? aim / aimDistance : Vector2.right;
        float pathLength = Mathf.Min(aimDistance, stats.Reach);
        Vector2 pathEnd = playerPosition + direction * pathLength;
        Vector3Int hoveredCell = tilemap.WorldToCell(mouseWorld);
        float reachSqr = stats.Reach * stats.Reach;
        if (tilemap.HasTile(hoveredCell) &&
            ((Vector2)tilemap.GetCellCenterWorld(hoveredCell) - playerPosition).sqrMagnitude <= reachSqr)
            return hoveredCell;

        Vector3Int playerCell = tilemap.WorldToCell(playerPosition);
        Vector2 cellCenter = tilemap.GetCellCenterWorld(playerCell);
        float cellWidth = Vector2.Distance(cellCenter, tilemap.GetCellCenterWorld(playerCell + Vector3Int.right));
        float cellHeight = Vector2.Distance(cellCenter, tilemap.GetCellCenterWorld(playerCell + Vector3Int.up));
        if (cellWidth <= 0f || cellHeight <= 0f) return null;

        float tileSize = Mathf.Min(cellWidth, cellHeight);
        float strokeRadius = GameplayTestSettings.ConfiguredSmartCursorStrokeWidth * tileSize * .5f;
        int scanRadiusX = Mathf.CeilToInt(pathLength / cellWidth) + Mathf.CeilToInt(strokeRadius / cellWidth) + 2;
        int scanRadiusY = Mathf.CeilToInt(pathLength / cellHeight) + Mathf.CeilToInt(strokeRadius / cellHeight) + 2;
        float bestPlayerDistanceSqr = float.PositiveInfinity;
        float bestPathProjection = float.PositiveInfinity;
        Vector3Int? best = null;

        for (int dx = -scanRadiusX; dx <= scanRadiusX; dx++)
            for (int dy = -scanRadiusY; dy <= scanRadiusY; dy++)
            {
                Vector3Int cell = new(playerCell.x + dx, playerCell.y + dy, 0);
                if (!tilemap.HasTile(cell)) continue;

                Vector2 targetPosition = tilemap.GetCellCenterWorld(cell);
                Vector2 fromPlayer = targetPosition - playerPosition;
                float playerDistanceSqr = fromPlayer.sqrMagnitude;
                if (playerDistanceSqr > reachSqr) continue;
                if (!StrokeIntersectsCell(playerPosition, pathEnd, targetPosition,
                        new Vector2(cellWidth * .5f, cellHeight * .5f), strokeRadius)) continue;

                float pathProjection = Vector2.Dot(fromPlayer, direction);
                if (playerDistanceSqr < bestPlayerDistanceSqr - .0001f ||
                    (Mathf.Abs(playerDistanceSqr - bestPlayerDistanceSqr) <= .0001f && pathProjection < bestPathProjection))
                {
                    bestPlayerDistanceSqr = playerDistanceSqr;
                    bestPathProjection = pathProjection;
                    best = cell;
                }
            }

        return best;
    }

    static bool StrokeIntersectsCell(Vector2 start, Vector2 end, Vector2 center, Vector2 halfExtents, float radius)
    {
        Vector2 minimum = center - halfExtents;
        Vector2 maximum = center + halfExtents;
        Vector2 segment = end - start;
        float entry = 0f;
        float exit = 1f;
        if (ClipSegmentAxis(start.x, segment.x, minimum.x, maximum.x, ref entry, ref exit) &&
            ClipSegmentAxis(start.y, segment.y, minimum.y, maximum.y, ref entry, ref exit))
            return true;

        float closestDistanceSqr = PointToRectDistanceSqr(start, minimum, maximum);
        closestDistanceSqr = Mathf.Min(closestDistanceSqr, PointToRectDistanceSqr(end, minimum, maximum));
        closestDistanceSqr = Mathf.Min(closestDistanceSqr, PointToSegmentDistanceSqr(minimum, start, end));
        closestDistanceSqr = Mathf.Min(closestDistanceSqr, PointToSegmentDistanceSqr(
            new Vector2(minimum.x, maximum.y), start, end));
        closestDistanceSqr = Mathf.Min(closestDistanceSqr, PointToSegmentDistanceSqr(maximum, start, end));
        closestDistanceSqr = Mathf.Min(closestDistanceSqr, PointToSegmentDistanceSqr(
            new Vector2(maximum.x, minimum.y), start, end));
        return closestDistanceSqr <= radius * radius + .000001f;
    }

    static bool ClipSegmentAxis(float origin, float delta, float minimum, float maximum, ref float entry, ref float exit)
    {
        if (Mathf.Abs(delta) < .000001f) return origin >= minimum && origin <= maximum;

        float first = (minimum - origin) / delta;
        float second = (maximum - origin) / delta;
        if (first > second) (first, second) = (second, first);
        entry = Mathf.Max(entry, first);
        exit = Mathf.Min(exit, second);
        return entry <= exit;
    }

    static float PointToRectDistanceSqr(Vector2 point, Vector2 minimum, Vector2 maximum)
    {
        float dx = point.x < minimum.x ? minimum.x - point.x : point.x > maximum.x ? point.x - maximum.x : 0f;
        float dy = point.y < minimum.y ? minimum.y - point.y : point.y > maximum.y ? point.y - maximum.y : 0f;
        return dx * dx + dy * dy;
    }

    static float PointToSegmentDistanceSqr(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;
        float t = lengthSqr > .000001f ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr) : 0f;
        return (point - (start + segment * t)).sqrMagnitude;
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
