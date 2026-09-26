using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public sealed class SurfaceTallGrass : MonoBehaviour
{
    [SerializeField] MapGenerator map;
    [SerializeField] Sprite sprite;
    [SerializeField] Sprite[] variants;
    [SerializeField] Sprite[] medicinalHerbVariants;
    [SerializeField] Material material;
    [SerializeField] ItemSO fiber;
    [SerializeField] ItemSO healingHerbs;
    [SerializeField] ItemSO scythePowerup;
    [SerializeField, Min(0)] int maximumPatches = 60;
    [SerializeField, Range(0f, 100f)] float healingHerbRarityPercent = 30f;
    [SerializeField, Min(0f)] float healingHerbSpawnAreaSize = 18f;
    [SerializeField, Min(1)] int minimumSpacing = 3;
    [SerializeField, Range(0f, 1f)] float randomness = .75f;
    [SerializeField, Min(0f)] float spawnQuietRadius = 18f;
    [SerializeField, Range(0f, 1f)] float nearSpawnDensity = .15f;
    [SerializeField] Vector2 respawnSeconds = new Vector2(90f, 180f);
    [SerializeField] Vector2Int fiberYield = new Vector2Int(2, 5);
    [SerializeField] Vector2Int healingHerbYield = new Vector2Int(1, 3);
    [SerializeField] Vector2Int healingHerbFiberYield = new Vector2Int(1, 3);
    [SerializeField, Min(0f)] float fiberSwayStrength = 2f;
    [SerializeField, Min(0f)] float fiberSwayFrequency = 1.5f;
    [SerializeField, Min(0f)] float healingHerbSwayStrength = 2f;
    [SerializeField, Min(0f)] float healingHerbSwayFrequency = 1.5f;
    [SerializeField] Vector2 heightRange = new Vector2(1.55f, 2.05f);

    readonly Dictionary<int, TallGrassPatch> patches = new();
    readonly List<int> sites = new();
    readonly HashSet<int> healingHerbSites = new();
    Transform generatedRoot;
    System.Random respawnRandom;
    float nextRespawn;
    bool rebuildPending;
    float appliedFiberSwayStrength = float.NaN;
    float appliedFiberSwayFrequency = float.NaN;
    float appliedHealingHerbSwayStrength = float.NaN;
    float appliedHealingHerbSwayFrequency = float.NaN;

    public int PatchCount => patches.Count;
    public int Capacity => sites.Count;
    public IEnumerable<TallGrassPatch> ActivePatches => patches.Values;
    public bool HasScythe => scythePowerup && InventoryManager.Instance &&
        InventoryManager.Instance.IsPowerupUnlocked(scythePowerup);
    public Sprite ScytheIcon => scythePowerup ? scythePowerup.icon : null;

    public TallGrassPatch At(Vector2 worldPoint)
    {
        TallGrassPatch nearest = null;
        float best = float.PositiveInfinity;
        foreach (var patch in patches.Values)
        {
            if (!patch) continue;
            var renderer = patch.GetComponent<SpriteRenderer>();
            if (!renderer || !renderer.bounds.Contains(worldPoint)) continue;
            float distance = ((Vector2)renderer.bounds.center - worldPoint).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            nearest = patch;
        }
        return nearest;
    }

    void OnEnable()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (map) map.Generated += RequestRebuild;
        Tilemap.tilemapTileChanged += OnTilesChanged;
        RequestRebuild();
    }

    void OnDisable()
    {
        if (map) map.Generated -= RequestRebuild;
        Tilemap.tilemapTileChanged -= OnTilesChanged;
        rebuildPending = false;
        Clear();
    }

    void RequestRebuild()
    {
        if (!Application.isPlaying) { Rebuild(); return; }
        // All Generated/OnEnable tree handlers finish before the next Update,
        // regardless of component subscription order. Trees always claim sites first.
        Clear();
        rebuildPending = true;
    }

    void OnValidate()
    {
        heightRange.x = Mathf.Max(.1f, heightRange.x);
        heightRange.y = Mathf.Max(heightRange.x, heightRange.y);
        respawnSeconds.x = Mathf.Max(.1f, respawnSeconds.x);
        respawnSeconds.y = Mathf.Max(respawnSeconds.x, respawnSeconds.y);
        fiberYield.x = Mathf.Max(1, fiberYield.x);
        fiberYield.y = Mathf.Max(fiberYield.x, fiberYield.y);
        healingHerbYield.x = Mathf.Max(1, healingHerbYield.x);
        healingHerbYield.y = Mathf.Max(healingHerbYield.x, healingHerbYield.y);
        healingHerbFiberYield.x = Mathf.Max(1, healingHerbFiberYield.x);
        healingHerbFiberYield.y = Mathf.Max(healingHerbFiberYield.x, healingHerbFiberYield.y);
        healingHerbSpawnAreaSize = Mathf.Max(0f, healingHerbSpawnAreaSize);
        fiberSwayStrength = Mathf.Max(0f, fiberSwayStrength);
        fiberSwayFrequency = Mathf.Max(0f, fiberSwayFrequency);
        healingHerbSwayStrength = Mathf.Max(0f, healingHerbSwayStrength);
        healingHerbSwayFrequency = Mathf.Max(0f, healingHerbSwayFrequency);
    }

    void OnTilesChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (!map || source != map.Terrain || changes == null) return;
        foreach (var change in changes)
            if (change.position.y == 0 && !source.HasTile(change.position))
                RemovePatch(change.position.x);
    }

    public void Rebuild()
    {
        rebuildPending = false;
        Clear();
        sites.Clear();
        healingHerbSites.Clear();
        if (!map || !map.Terrain || (!sprite && (variants == null || variants.Length == 0))) return;

        int width = map.GeneratedWidth;
        if (width <= 0 || maximumPatches <= 0) return;
        respawnRandom = new System.Random(map.ActiveSeed ^ 0x6D925A1);
        var trees = FindTrees();
        var candidates = new List<int>();
        int previous = int.MinValue / 2;
        int left = -width / 2;
        float step = (float)width / maximumPatches;
        float spawnX = map.Terrain.GetCellCenterWorld(new Vector3Int(0, 0)).x;
        for (int index = 0; index < maximumPatches; index++)
        {
            float jitter = (Hash01(index, 0xA41Fu) - .5f) * .9f * randomness;
            int preferredX = Mathf.Clamp(left + Mathf.FloorToInt((index + .5f + jitter) * step),
                left + 1, left + width - 2);
            int slotStart = Mathf.Clamp(left + Mathf.FloorToInt(index * step), left + 1, left + width - 2);
            int slotEnd = Mathf.Clamp(left + Mathf.FloorToInt((index + 1) * step) - 1,
                slotStart, left + width - 2);
            var slotCandidates = new List<int>(slotEnd - slotStart + 1);
            for (int candidateX = slotStart; candidateX <= slotEnd; candidateX++) slotCandidates.Add(candidateX);
            slotCandidates.Sort((a, b) =>
            {
                int distanceOrder = Mathf.Abs(a - preferredX).CompareTo(Mathf.Abs(b - preferredX));
                if (distanceOrder != 0) return distanceOrder;
                uint hashA = OreVeins.Hash(map.ActiveSeed, a, 0, 0xA41Fu);
                uint hashB = OreVeins.Hash(map.ActiveSeed, b, 0, 0xA41Fu);
                return hashA.CompareTo(hashB);
            });

            int selectedX = int.MinValue;
            int fallbackX = int.MinValue;
            foreach (int candidateX in slotCandidates)
            {
                var cell = new Vector3Int(candidateX, 0);
                if (!map.Terrain.HasTile(cell) || !map.Terrain.HasTile(cell + Vector3Int.left) ||
                    !map.Terrain.HasTile(cell + Vector3Int.right) || candidateX - previous < minimumSpacing)
                    continue;
                float worldX = map.Terrain.GetCellCenterWorld(cell).x;
                if (NearBuilding(worldX) || (trees && trees.Protects(cell))) continue;
                if (fallbackX == int.MinValue) fallbackX = candidateX;
                float distance = Mathf.Abs(worldX - spawnX);
                float blend = spawnQuietRadius > 0f ? Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(distance / spawnQuietRadius)) : 1f;
                float chance = Mathf.Lerp(nearSpawnDensity, 1f, blend);
                if (Hash01(index, 0xAD52u) < chance) { selectedX = candidateX; break; }
            }
            if (selectedX == int.MinValue) selectedX = fallbackX;
            if (selectedX == int.MinValue) continue;

            sites.Add(selectedX);
            candidates.Add(selectedX);
            previous = selectedX;
        }

        if (sites.Count < maximumPatches)
        {
            var fallbackCandidates = new List<int>();
            for (int candidateX = left + 1; candidateX < left + width - 1; candidateX++)
            {
                var cell = new Vector3Int(candidateX, 0);
                if (!map.Terrain.HasTile(cell) || !map.Terrain.HasTile(cell + Vector3Int.left) ||
                    !map.Terrain.HasTile(cell + Vector3Int.right)) continue;
                float worldX = map.Terrain.GetCellCenterWorld(cell).x;
                if (NearBuilding(worldX) || (trees && trees.Protects(cell))) continue;
                bool tooClose = false;
                foreach (int existingX in sites)
                    if (Mathf.Abs(candidateX - existingX) < minimumSpacing) { tooClose = true; break; }
                if (!tooClose) fallbackCandidates.Add(candidateX);
            }
            fallbackCandidates.Sort((a, b) =>
            {
                uint hashA = OreVeins.Hash(map.ActiveSeed, a, 0, 0xA41Fu);
                uint hashB = OreVeins.Hash(map.ActiveSeed, b, 0, 0xA41Fu);
                int order = hashA.CompareTo(hashB);
                return order != 0 ? order : a.CompareTo(b);
            });
            foreach (int x in fallbackCandidates)
            {
                if (sites.Count >= maximumPatches) break;
                sites.Add(x);
                candidates.Add(x);
            }
        }

        var eligibleHerbSites = new List<int>();
        foreach (int x in candidates)
        {
            float worldX = map.Terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x;
            if (Mathf.Abs(worldX - spawnX) >= healingHerbSpawnAreaSize)
                eligibleHerbSites.Add(x);
        }
        int herbCount = Mathf.Min(eligibleHerbSites.Count,
            Mathf.RoundToInt(maximumPatches * Mathf.Clamp(healingHerbRarityPercent, 0f, 100f) / 100f));
        var ranked = eligibleHerbSites;
        ranked.Sort((a, b) =>
        {
            uint hashA = OreVeins.Hash(map.ActiveSeed, a, 0, 0x4D4544u);
            uint hashB = OreVeins.Hash(map.ActiveSeed, b, 0, 0x4D4544u);
            int order = hashA.CompareTo(hashB);
            return order != 0 ? order : a.CompareTo(b);
        });
        for (int i = 0; i < herbCount; i++) healingHerbSites.Add(ranked[i]);
        foreach (int x in candidates)
            Spawn(x, map.Terrain.GetCellCenterWorld(new Vector3Int(x, 0)).x);
    }

    void Update()
    {
        RefreshSwaySettings();
        if (rebuildPending)
        {
            if (map && map.IsGenerated) Rebuild();
            return;
        }
        if (!Application.isPlaying || !map || !map.IsGenerated ||
            patches.Count >= sites.Count || Time.time < nextRespawn || respawnRandom == null) return;
        int start = respawnRandom.Next(sites.Count);
        var trees = FindTrees();
        for (int i = 0; i < sites.Count; i++)
        {
            int x = sites[(start + i) % sites.Count];
            if (patches.ContainsKey(x)) continue;
            var cell = new Vector3Int(x, 0);
            if (!map.Terrain.HasTile(cell) || !map.Terrain.HasTile(cell + Vector3Int.left) ||
                !map.Terrain.HasTile(cell + Vector3Int.right)) continue;
            if (trees && trees.Protects(cell)) continue;
            float worldX = map.Terrain.GetCellCenterWorld(cell).x;
            if (NearBuilding(worldX)) continue;
            Spawn(x, worldX);
            break;
        }
        ScheduleRespawn();
    }

    void RefreshSwaySettings()
    {
        if (appliedFiberSwayStrength == fiberSwayStrength &&
            appliedFiberSwayFrequency == fiberSwayFrequency &&
            appliedHealingHerbSwayStrength == healingHerbSwayStrength &&
            appliedHealingHerbSwayFrequency == healingHerbSwayFrequency) return;
        foreach (var patch in patches.Values)
            if (patch) ApplySwaySettings(patch);
        appliedFiberSwayStrength = fiberSwayStrength;
        appliedFiberSwayFrequency = fiberSwayFrequency;
        appliedHealingHerbSwayStrength = healingHerbSwayStrength;
        appliedHealingHerbSwayFrequency = healingHerbSwayFrequency;
    }

    public void ApplySwaySettings(TallGrassPatch patch)
    {
        bool herb = patch.IsHealingHerb;
        patch.SetSwaySettings(herb ? healingHerbSwayStrength : fiberSwayStrength,
            herb ? healingHerbSwayFrequency : fiberSwayFrequency);
    }

    void ScheduleRespawn()
    {
        if (respawnRandom == null) return;
        float earliest = Mathf.Max(.1f, respawnSeconds.x);
        float latest = Mathf.Max(earliest, respawnSeconds.y);
        nextRespawn = Time.time + Mathf.Lerp(earliest, latest,
            (float)respawnRandom.NextDouble());
    }

    SurfaceTrees FindTrees()
    {
        foreach (var trees in FindObjectsByType<SurfaceTrees>(FindObjectsSortMode.None))
            if (trees.Map == map) return trees;
        return null;
    }

    void Spawn(int x, float worldX)
    {
        if (!generatedRoot)
        {
            var root = new GameObject("Tall Grass (generated)");
            root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            generatedRoot = root.transform;
            generatedRoot.SetParent(transform, false);
        }

        var go = new GameObject("Tall Grass " + x, typeof(SpriteRenderer), typeof(TallGrassPatch));
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        go.transform.SetParent(generatedRoot, false);
        go.transform.position = new Vector3(worldX,
            map.Terrain.CellToWorld(new Vector3Int(x, 1)).y, 0f);
        float height = Mathf.Lerp(heightRange.x, heightRange.y, Hash01(x, 0x5EB1u));
        go.transform.localScale = new Vector3(Hash01(x, 0xC3A7u) < .5f ? -height : height,
            height, 1f);

        var renderer = go.GetComponent<SpriteRenderer>();
        bool isHealingHerb = healingHerbSites.Contains(x) && medicinalHerbVariants != null && medicinalHerbVariants.Length > 0;
        Sprite[] visualVariants = isHealingHerb ? medicinalHerbVariants : variants;
        renderer.sprite = visualVariants != null && visualVariants.Length > 0
            ? visualVariants[(int)(OreVeins.Hash(map.ActiveSeed, x, 0, isHealingHerb ? 0x4D5641u : 0xB192u) % (uint)visualVariants.Length)]
            : sprite;
        if (material) renderer.sharedMaterial = material;
        renderer.sortingLayerName = "Grass";
        renderer.sortingOrder = 1;
        var patch = go.GetComponent<TallGrassPatch>();
        patch.Initialize(this, x, isHealingHerb);
        patches.Add(x, patch);
    }

    public bool CutAt(int surfaceCellX)
    {
        if (!HasScythe || !patches.TryGetValue(surfaceCellX, out var patch) || !patch) return false;
        if (Application.isPlaying)
        {
            var particles = GetComponent<TallGrassCutParticles>();
            if (!particles) particles = gameObject.AddComponent<TallGrassCutParticles>();
            particles.Burst(patch.GetComponent<SpriteRenderer>().bounds);
        }
        RemovePatch(surfaceCellX);
        if (Application.isPlaying && InventoryManager.Instance)
        {
            if (patch.IsHealingHerb)
            {
                if (healingHerbs) InventoryManager.Instance.Add(healingHerbs, respawnRandom != null
                    ? respawnRandom.Next(Mathf.Max(1, healingHerbYield.x), Mathf.Max(healingHerbYield.x, healingHerbYield.y) + 1)
                    : Mathf.Max(1, healingHerbYield.x));
                if (fiber) InventoryManager.Instance.Add(fiber, respawnRandom != null
                    ? respawnRandom.Next(Mathf.Max(1, healingHerbFiberYield.x), Mathf.Max(healingHerbFiberYield.x, healingHerbFiberYield.y) + 1)
                    : Mathf.Max(1, healingHerbFiberYield.x));
            }
            else if (fiber) InventoryManager.Instance.Add(fiber, respawnRandom != null
                ? respawnRandom.Next(Mathf.Max(1, fiberYield.x), Mathf.Max(fiberYield.x, fiberYield.y) + 1)
                : Mathf.Max(1, fiberYield.x));
        }
        return true;
    }

    public void RemoveNear(int surfaceCellX, int radius)
    {
        for (int x = surfaceCellX - Mathf.Max(0, radius); x <= surfaceCellX + radius; x++)
            RemovePatch(x);
    }

    void RemovePatch(int x)
    {
        if (!patches.Remove(x, out var patch) || !patch) return;
        patch.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(patch.gameObject);
        else DestroyImmediate(patch.gameObject);
        if (Application.isPlaying && patches.Count < sites.Count) ScheduleRespawn();
    }

    void Clear()
    {
        patches.Clear();
        if (!generatedRoot) return;
        generatedRoot.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(generatedRoot.gameObject);
        else DestroyImmediate(generatedRoot.gameObject);
        generatedRoot = null;
    }

    float Hash01(int x, uint stream) =>
        (OreVeins.Hash(map.ActiveSeed, x, 0, stream) & 0xFFFFFFu) / 16777216f;

    static bool NearBuilding(float x)
    {
        foreach (var building in FindObjectsByType<ShopBuilding>(FindObjectsSortMode.None))
            if (Overlaps(x, building.GetComponent<Collider2D>())) return true;
        foreach (var building in FindObjectsByType<WorkbenchBuilding>(FindObjectsSortMode.None))
            if (Overlaps(x, building.GetComponent<Collider2D>())) return true;
        foreach (var building in FindObjectsByType<EnergyMonolyth>(FindObjectsSortMode.None))
            if (Overlaps(x, building.GetComponent<Collider2D>())) return true;
        return false;
    }

    static bool Overlaps(float x, Collider2D collider) => collider &&
        Mathf.Abs(x - collider.bounds.center.x) < collider.bounds.extents.x + 1.5f;
}
