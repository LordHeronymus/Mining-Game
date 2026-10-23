using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class SurfaceTrees : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);

    [SerializeField] MapGenerator map;
    [SerializeField] ChoppableTree prefab;
    [SerializeField] Sprite[] variants;
    [SerializeField] ItemSO wood;
    [SerializeField, Min(0)] int maximumTrees = 12;
    [SerializeField, Min(1)] int hitsToFell = 10;
    [SerializeField] ItemSO axePowerup;
    [SerializeField, Min(1f)] float axeHitMultiplier = 4f;
    [SerializeField, Min(1)] int woodYieldMin = 15;
    [SerializeField, Min(1)] int woodYieldMax = 25;
    [SerializeField, Min(0)] int maximumBonusWood = 10;
    [SerializeField, Min(0)] float minimumTreeSpacing = 2f;
    [SerializeField, Min(1)] float treeHeight = 8.25f;
    [SerializeField, Min(.1f)] float fallDurationSeconds = .9f;
    [SerializeField, Range(0f, 1f)] float leafAlpha = 1f;
    [SerializeField] AnimationCurve fallRotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Min(0)] float growthSpeedPercentPerMinute = 10f;
    [SerializeField, Min(0)] float maximumSizeBonusPercent = 50f;
    [SerializeField] Vector2 regrowthSeconds = new Vector2(120, 180);
    [SerializeField, HideInInspector] bool placementMigrationDone;
    [SerializeField, HideInInspector] bool growthSettingsMigrationDone;
    [SerializeField, HideInInspector] int fullyGrownWoodYieldMax = 35;
    [SerializeField, HideInInspector] float timeToFullGrowthSeconds = 300f;

    readonly HashSet<ChoppableTree> trees = new();
    System.Random random;
    float nextGrowth;
    int targetPopulation;

    public int ActiveCount => trees.Count;
    public MapGenerator Map => map;
    public int Capacity => targetPopulation;
    public SavedForest CaptureRunState()
    {
        var saved = new List<SavedTree>();
        bool skippedFalling = false;
        foreach (var tree in trees)
            if (tree && !tree.IsFalling) saved.Add(tree.CaptureRunState(System.Array.FindIndex(variants, x => x && x.name == tree.SourceSpriteName)));
            else if (tree) skippedFalling = true;
        float regrowth = Mathf.Max(0, nextGrowth - Time.time);
        // A falling tree is already harvested but restarts the regrowth timer only when it lands. Keep at least
        // the shortest regrowth so a save mid-fall cannot plant a sapling right after loading; draws no random value.
        if (skippedFalling) regrowth = Mathf.Max(regrowth, Mathf.Max(1, regrowthSeconds.x));
        return new SavedForest { trees = saved.ToArray(), regrowth = regrowth };
    }
    public void RestoreRunState(SavedForest state)
    {
        if (state == null) return;
        foreach (var tree in trees) if (tree) { tree.gameObject.SetActive(false); Destroy(tree.gameObject); }
        trees.Clear();
        foreach (var saved in state.trees ?? System.Array.Empty<SavedTree>())
        {
            if (saved.variant < 0 || saved.variant >= variants.Length || !variants[saved.variant]) continue;
            var grown = Instantiate(prefab, transform);
            grown.Initialize(this, saved.cell, variants[saved.variant], wood, saved.position, saved.height,
                hitsToFell, Mathf.Max(1, woodYieldMin), Mathf.Max(woodYieldMin, woodYieldMax),
                Mathf.Max(0, maximumBonusWood), maximumSizeBonusPercent, growthSpeedPercentPerMinute, true);
            grown.RestoreRunState(saved); trees.Add(grown);
        }
        nextGrowth = Time.time + state.regrowth;
    }
    public bool HasAxe => axePowerup && InventoryManager.Instance &&
        InventoryManager.Instance.IsPowerupUnlocked(axePowerup);
    public float HitDamage => HasAxe ? Mathf.Max(1f, axeHitMultiplier) : 1f;
    public float FallDurationSeconds => Mathf.Clamp(fallDurationSeconds, .1f, 10f);
    public float LeafAlpha => Mathf.Clamp01(leafAlpha);
    public float FallRotationProgress(float normalizedTime) => fallRotationCurve != null
        ? Mathf.Clamp01(fallRotationCurve.Evaluate(Mathf.Clamp01(normalizedTime)))
        : normalizedTime * normalizedTime;
    public bool Protects(Vector3Int cell)
    {
        if (cell.y != 0) return false;
        foreach (var tree in trees)
            if (tree && Mathf.Abs(cell.x - tree.SurfaceCellX) <= 1) return true;
        return false;
    }

    void OnEnable()
    {
        MigrateGrowthSettings();
        if (map) map.Generated += ResetTrees;
        if (Application.isPlaying && map && map.IsGenerated) ResetTrees();
    }

    void MigrateGrowthSettings()
    {
        if (growthSettingsMigrationDone) return;
        maximumSizeBonusPercent = 50f;
        growthSpeedPercentPerMinute = 3000f / Mathf.Max(1f, timeToFullGrowthSeconds);
        maximumBonusWood = Mathf.Max(0, fullyGrownWoodYieldMax - Mathf.Max(woodYieldMin, woodYieldMax));
        growthSettingsMigrationDone = true;
    }

    void Start()
    {
        if (Application.isPlaying && map && map.IsGenerated && random == null) ResetTrees();
    }

    void OnDisable()
    {
        if (map) map.Generated -= ResetTrees;
        if (Application.isPlaying)
            foreach (var tree in trees) if (tree) Destroy(tree.gameObject);
        trees.Clear();
        random = null;
    }

    void ResetTrees()
    {
        if (!Application.isPlaying || !map || !map.IsGenerated || !prefab || !wood ||
            variants == null || variants.Length == 0) return;
        foreach (var tree in trees) if (tree) Destroy(tree.gameObject);
        trees.Clear();
        random = new System.Random(map.ActiveSeed ^ 0x19A23C5);
        targetPopulation = TargetPopulation();
        for (int i = 0; i < targetPopulation; i++) TryPlant(true);
        ScheduleGrowth();
    }

    int TargetPopulation()
    {
        float width = map.GeneratedWidth * map.Terrain.layoutGrid.cellSize.x;
        return Mathf.Clamp(Mathf.FloorToInt(width / Mathf.Max(16f, minimumTreeSpacing * 2f)), 0, maximumTrees);
    }

    // F1/GPS changes reach standing trees and the forest size without replanting or drawing random values.
    public void ApplySettings()
    {
        foreach (var tree in trees)
            if (tree) tree.ApplySettings(Mathf.Max(1, woodYieldMin), Mathf.Max(woodYieldMin, woodYieldMax),
                Mathf.Max(0, maximumBonusWood), Mathf.Max(0f, maximumSizeBonusPercent),
                Mathf.Max(0f, growthSpeedPercentPerMinute));
        if (random != null && map && map.IsGenerated && map.Terrain) targetPopulation = TargetPopulation();
    }

    void Update()
    {
        if (random == null || trees.Count >= targetPopulation || Time.time < nextGrowth) return;
        TryPlant(false);
        ScheduleGrowth();
    }

    void ScheduleGrowth() => nextGrowth = Time.time + Mathf.Lerp(
        Mathf.Max(1, regrowthSeconds.x), Mathf.Max(regrowthSeconds.x, regrowthSeconds.y),
        random == null ? 0 : (float)random.NextDouble());

    bool TryPlant(bool mature)
    {
        // Buildings and trees do not move while a site is chosen. Read their positions once instead of on each
        // of up to 96 attempts; a forest of 32 trees otherwise cost tens of thousands of transform reads.
        // Finding the buildings is itself costly, so a whole forest planted in one frame shares one lookup.
        if (buildingBoundsFrame != Time.frameCount) { CacheBuildingBounds(); buildingBoundsFrame = Time.frameCount; }
        treePositions.Clear();
        foreach (var tree in trees) if (tree) treePositions.Add(tree.transform.position.x);
        var terrain = map.Terrain;
        float requiredSpacing = Mathf.Max(minimumTreeSpacing, terrain.layoutGrid.cellSize.x);
        int leftCell = -map.GeneratedWidth / 2 + 2;
        int rightCell = leftCell + map.GeneratedWidth - 4;
        ReadSurface(terrain, leftCell - 1, rightCell);
        for (int attempt = 0; attempt < 96; attempt++)
        {
            var cell = new Vector3Int(random.Next(leftCell, rightCell), 0);
            if (!SurfaceTile(cell.x) || !SurfaceTile(cell.x + 1) || !SurfaceTile(cell.x - 1)) continue;
            float x = SurfaceCenter(terrain, cell.x);
            if (!FreeOfBuildings(x) || Mathf.Abs(x) < 4f) continue;
            float nearest = float.PositiveInfinity;
            foreach (float treeX in treePositions) nearest = Mathf.Min(nearest, Mathf.Abs(treeX - x));
            if (nearest < requiredSpacing) continue;
            float preferredSpacing = Mathf.Max(16f, requiredSpacing * 4f);
            float distanceFactor = Mathf.Clamp01(
                (nearest - requiredSpacing) / (preferredSpacing - requiredSpacing));
            float chance = Mathf.Lerp(.01f, 1f, distanceFactor * distanceFactor);
            if (random.NextDouble() > chance) continue;
            var sprite = variants[random.Next(variants.Length)];
            if (!sprite) continue;
            var grown = Instantiate(prefab, transform);
            grown.name = "Baum " + (trees.Count + 1);
            grown.Initialize(this, cell.x, sprite, wood,
                new Vector3(x, terrain.CellToWorld(new Vector3Int(cell.x, 1)).y, 0),
                treeHeight * Mathf.Lerp(.9f, 1.1f, (float)random.NextDouble()),
                hitsToFell, Mathf.Max(1, woodYieldMin),
                Mathf.Max(woodYieldMin, woodYieldMax),
                Mathf.Max(0, maximumBonusWood),
                Mathf.Max(0f, maximumSizeBonusPercent),
                Mathf.Max(0f, growthSpeedPercentPerMinute), mature);
            trees.Add(grown);
            // Grass never vetoes a tree site, including during regrowth.
            map.GetComponent<SurfaceTallGrass>()?.RemoveNear(cell.x, 1);
            return true;
        }
        return false;
    }

    // Centre and keep-out half width of every building collider, and the x position of every tree.
    readonly List<Vector2> buildingBounds = new();
    int buildingBoundsFrame = -1;
    readonly List<float> treePositions = new();
    // Surface row of the planting range: one block read, cell centres resolved once each.
    TileBase[] surfaceTiles = System.Array.Empty<TileBase>();
    float[] surfaceCenters = System.Array.Empty<float>();
    int surfaceLeft;

    void ReadSurface(Tilemap terrain, int left, int right)
    {
        int width = Mathf.Max(0, right - left + 1);
        if (surfaceTiles.Length != width) { surfaceTiles = new TileBase[width]; surfaceCenters = new float[width]; }
        surfaceLeft = left;
        terrain.GetTilesBlockNonAlloc(new BoundsInt(left, 0, 0, width, 1, 1), surfaceTiles);
        for (int i = 0; i < width; i++) surfaceCenters[i] = float.NaN;
    }

    bool SurfaceTile(int x) => surfaceTiles[x - surfaceLeft];

    float SurfaceCenter(Tilemap terrain, int x)
    {
        ref float center = ref surfaceCenters[x - surfaceLeft];
        if (float.IsNaN(center)) center = terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x;
        return center;
    }

    void CacheBuildingBounds()
    {
        buildingBounds.Clear();
        foreach (var building in FindObjectsByType<ShopBuilding>(FindObjectsSortMode.None)) AddBuildingBounds(building);
        foreach (var building in FindObjectsByType<WorkbenchBuilding>(FindObjectsSortMode.None)) AddBuildingBounds(building);
        foreach (var building in FindObjectsByType<EnergyMonolyth>(FindObjectsSortMode.None)) AddBuildingBounds(building);
        foreach (var building in FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None)) AddBuildingBounds(building);
    }

    void AddBuildingBounds(Component building)
    {
        var collider = building.GetComponent<Collider2D>();
        if (collider) buildingBounds.Add(new Vector2(collider.bounds.center.x, collider.bounds.extents.x + 4.75f));
    }

    bool FreeOfBuildings(float x)
    {
        foreach (var range in buildingBounds)
            if (Mathf.Abs(x - range.x) < range.y) return false;
        return true;
    }

    public void TreeFelled(ChoppableTree tree)
    {
        if (!tree || !trees.Remove(tree)) return;
        ScheduleGrowth();
    }
}
