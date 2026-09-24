using System.Collections.Generic;
using UnityEngine;

public sealed class SurfaceTrees : MonoBehaviour
{
    [SerializeField] MapGenerator map;
    [SerializeField] ChoppableTree prefab;
    [SerializeField] Sprite[] variants;
    [SerializeField] ItemSO wood;
    [SerializeField, Range(0, 20)] int maximumTrees = 12;
    [SerializeField, Min(1)] int hitsToFell = 10;
    [SerializeField] ItemSO axePowerup;
    [SerializeField, Min(1f)] float axeHitMultiplier = 4f;
    [SerializeField, Min(1)] int woodYieldMin = 15;
    [SerializeField, Min(1)] int woodYieldMax = 25;
    [SerializeField, Min(0)] int maximumBonusWood = 10;
    [SerializeField, Min(2)] float minimumTreeSpacing = 2f;
    [SerializeField, Min(1)] float treeHeight = 8.25f;
    [SerializeField, Min(.1f)] float fallDurationSeconds = .9f;
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
    public bool HasAxe => axePowerup && InventoryManager.Instance &&
        InventoryManager.Instance.IsPowerupUnlocked(axePowerup);
    public float HitDamage => HasAxe ? Mathf.Max(1f, axeHitMultiplier) : 1f;
    public float FallDurationSeconds => Mathf.Clamp(fallDurationSeconds, .1f, 10f);
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
        float width = map.GeneratedWidth * map.Terrain.layoutGrid.cellSize.x;
        targetPopulation = Mathf.Clamp(Mathf.FloorToInt(width / Mathf.Max(16f, minimumTreeSpacing * 2f)),
            0, maximumTrees);
        for (int i = 0; i < targetPopulation; i++) TryPlant(true);
        ScheduleGrowth();
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
        int leftCell = -map.GeneratedWidth / 2 + 2;
        int rightCell = leftCell + map.GeneratedWidth - 4;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            var cell = new Vector3Int(random.Next(leftCell, rightCell), 0);
            if (!map.Terrain.HasTile(cell) || !map.Terrain.HasTile(cell + Vector3Int.right) ||
                !map.Terrain.HasTile(cell + Vector3Int.left)) continue;
            float x = map.Terrain.GetCellCenterWorld(cell).x;
            if (!FreeOfBuildings(x) || Mathf.Abs(x) < 4f) continue;
            float nearest = float.PositiveInfinity;
            foreach (var tree in trees)
                if (tree) nearest = Mathf.Min(nearest, Mathf.Abs(tree.transform.position.x - x));
            float requiredSpacing = Mathf.Max(minimumTreeSpacing, map.Terrain.layoutGrid.cellSize.x * 4f);
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
                new Vector3(x, map.Terrain.CellToWorld(new Vector3Int(cell.x, 1)).y, 0),
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

    bool FreeOfBuildings(float x)
    {
        foreach (var building in FindObjectsByType<ShopBuilding>(FindObjectsSortMode.None))
            if (TooClose(x, building.GetComponent<Collider2D>())) return false;
        foreach (var building in FindObjectsByType<WorkbenchBuilding>(FindObjectsSortMode.None))
            if (TooClose(x, building.GetComponent<Collider2D>())) return false;
        foreach (var building in FindObjectsByType<EnergyMonolyth>(FindObjectsSortMode.None))
            if (TooClose(x, building.GetComponent<Collider2D>())) return false;
        return true;
    }

    static bool TooClose(float x, Collider2D collider) => collider &&
        Mathf.Abs(x - collider.bounds.center.x) < collider.bounds.extents.x + 4.75f;

    public void TreeFelled(ChoppableTree tree)
    {
        if (!tree || !trees.Remove(tree)) return;
        ScheduleGrowth();
    }
}
