using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public sealed class SurfaceTallGrass : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);

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
    [SerializeField, Range(0f, 100f)] float healingHerbConversionLimitPercent = 15f;
    [SerializeField, Min(0f)] float healingHerbConversionFactor = .5f;
    [SerializeField, Min(1)] int minimumSpacing = 3;
    [SerializeField, Range(0f, 100f)] float clusterPercent = 60f;
    [SerializeField] Vector2Int clusterSize = new Vector2Int(3, 7);
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
    [SerializeField, Range(.25f, 3f)] float grassSizeMultiplier = 1f;
    [SerializeField, Range(.25f, 3f)] float healingHerbSizeMultiplier = 1f;

    readonly Dictionary<int, TallGrassPatch> patches = new();
    readonly List<int> sites = new();
    readonly HashSet<int> healingHerbSites = new();
    Transform generatedRoot;
    System.Random respawnRandom;
    float nextRespawn;
    float nextConversion;
    float scheduledConversionRate = float.NaN;
    bool rebuildPending;
    float appliedFiberSwayStrength = float.NaN;
    float appliedFiberSwayFrequency = float.NaN;
    float appliedHealingHerbSwayStrength = float.NaN;
    float appliedHealingHerbSwayFrequency = float.NaN;

    public int PatchCount => patches.Count;
    public int Capacity => sites.Count;
    public IEnumerable<TallGrassPatch> ActivePatches => patches.Values;
    public SavedGrassland CaptureRunState()
    {
        var saved = new List<SavedGrass>();
        foreach (var patch in patches.Values) if (patch) saved.Add(new SavedGrass { cell = patch.SurfaceCellX, herb = patch.IsHealingHerb });
        return new SavedGrassland { patches = saved.ToArray(), sites = sites.ToArray(), herbSites = new List<int>(healingHerbSites).ToArray(),
            respawn = Mathf.Max(0, nextRespawn - Time.time), conversion = float.IsPositiveInfinity(nextConversion) ? -1 : Mathf.Max(0, nextConversion - Time.time) };
    }
    public void RestoreRunState(SavedGrassland state)
    {
        if (state == null) return;
        Rebuild(); Clear(); healingHerbSites.Clear();
        if (state.sites != null) { sites.Clear(); sites.AddRange(state.sites); }
        foreach (int site in state.herbSites ?? System.Array.Empty<int>()) healingHerbSites.Add(site);
        foreach (var saved in state.patches ?? System.Array.Empty<SavedGrass>())
        {
            if (!map.Terrain.HasTile(new Vector3Int(saved.cell, 0)) || patches.ContainsKey(saved.cell)) continue;
            if (saved.herb) healingHerbSites.Add(saved.cell);
            Spawn(saved.cell, map.Terrain.GetCellCenterWorld(new Vector3Int(saved.cell, 0)).x);
        }
        nextRespawn = Time.time + state.respawn; nextConversion = state.conversion < 0 ? float.PositiveInfinity : Time.time + state.conversion;
    }
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
        healingHerbConversionLimitPercent = Mathf.Clamp(healingHerbConversionLimitPercent, 0f, 100f);
        healingHerbConversionFactor = Mathf.Max(0f, healingHerbConversionFactor);
        clusterPercent = Mathf.Clamp(clusterPercent, 0f, 100f);
        clusterSize.x = Mathf.Max(2, clusterSize.x);
        clusterSize.y = Mathf.Max(clusterSize.x, clusterSize.y);
        fiberSwayStrength = Mathf.Max(0f, fiberSwayStrength);
        fiberSwayFrequency = Mathf.Max(0f, fiberSwayFrequency);
        healingHerbSwayStrength = Mathf.Max(0f, healingHerbSwayStrength);
        healingHerbSwayFrequency = Mathf.Max(0f, healingHerbSwayFrequency);
        grassSizeMultiplier = Mathf.Clamp(grassSizeMultiplier, .25f, 3f);
        healingHerbSizeMultiplier = Mathf.Clamp(healingHerbSizeMultiplier, .25f, 3f);
    }

    public void ApplyPatchSizes()
    {
        foreach (var patch in patches.Values)
            if (patch) ApplyPatchSize(patch);
    }

    void ApplyPatchSize(TallGrassPatch patch)
    {
        var renderer = patch.GetComponent<SpriteRenderer>();
        if (!renderer || !renderer.sprite) return;
        float size = patch.IsHealingHerb ? healingHerbSizeMultiplier : grassSizeMultiplier;
        float baseHeight = Mathf.Lerp(heightRange.x, heightRange.y, Hash01(patch.SurfaceCellX, 0x5EB1u));
        float widthSign = Hash01(patch.SurfaceCellX, 0xC3A7u) < .5f ? -1f : 1f;
        var targetScale = new Vector3(widthSign * baseHeight * size, baseHeight * size, 1f);
        if (patch.transform.localScale == targetScale) return;
        float bottom = renderer.bounds.min.y;
        patch.transform.localScale = targetScale;
        patch.transform.position += Vector3.up * (bottom - renderer.bounds.min.y);
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
        ScheduleConversion();
        var trees = FindTrees();
        var buildingBounds = new List<Vector2>();
        foreach (var building in FindObjectsByType<ShopBuilding>(FindObjectsSortMode.None)) CacheBounds(building, buildingBounds);
        foreach (var building in FindObjectsByType<WorkbenchBuilding>(FindObjectsSortMode.None)) CacheBounds(building, buildingBounds);
        foreach (var building in FindObjectsByType<EnergyMonolyth>(FindObjectsSortMode.None)) CacheBounds(building, buildingBounds);
        foreach (var building in FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None)) CacheBounds(building, buildingBounds);
        var candidates = new List<int>();
        int left = -width / 2;
        float spawnX = map.Terrain.GetCellCenterWorld(new Vector3Int(0, 0)).x;
        int clusteredCount = Mathf.RoundToInt(maximumPatches * clusterPercent / 100f);
        var groupSizes = new List<int>();
        int remainingClustered = clusteredCount;
        int groupIndex = 0;
        while (remainingClustered >= clusterSize.x)
        {
            int maximum = Mathf.Min(clusterSize.y, remainingClustered);
            int size = clusterSize.x + (int)(OreVeins.Hash(map.ActiveSeed, groupIndex++, 0, 0xC1A57u) %
                (uint)(maximum - clusterSize.x + 1));
            if (remainingClustered - size > 0 && remainingClustered - size < clusterSize.x)
            {
                if (remainingClustered <= clusterSize.y) size = remainingClustered;
                else if (remainingClustered - clusterSize.x >= clusterSize.x)
                    size = remainingClustered - clusterSize.x;
            }
            groupSizes.Add(size);
            remainingClustered -= size;
        }
        int singles = maximumPatches - clusteredCount + remainingClustered;
        for (int i = 0; i < singles; i++) groupSizes.Add(1);
        var layoutRandom = new System.Random(map.ActiveSeed ^ 0x31C057A);
        for (int i = groupSizes.Count - 1; i > 0; i--)
        {
            int other = layoutRandom.Next(i + 1);
            (groupSizes[i], groupSizes[other]) = (groupSizes[other], groupSizes[i]);
        }
        float step = (float)width / groupSizes.Count;
        for (int index = 0; index < groupSizes.Count; index++)
        {
            int size = groupSizes[index];
            float jitter = (Hash01(index, 0xA41Fu) - .5f) * .9f * randomness;
            int preferredX = left + Mathf.FloorToInt((index + .5f + jitter) * step) - size / 2;
            int selectedX = int.MinValue;
            int fallbackX = int.MinValue;
            for (int offset = 0; offset < width && selectedX == int.MinValue; offset++)
            {
                int candidateX = preferredX + (offset % 2 == 0 ? offset / 2 : -(offset + 1) / 2);
                if (candidateX < left + 1 || candidateX + size > left + width - 1) continue;
                bool valid = true;
                for (int x = candidateX; x < candidateX + size && valid; x++)
                {
                    var cell = new Vector3Int(x, 0);
                    if (!map.Terrain.HasTile(cell) || !map.Terrain.HasTile(cell + Vector3Int.left) ||
                        !map.Terrain.HasTile(cell + Vector3Int.right) ||
                        NearBuilding(map.Terrain.GetCellCenterWorld(cell).x, buildingBounds) || (trees && trees.Protects(cell)))
                        valid = false;
                    foreach (int existingX in sites)
                        if (Mathf.Abs(x - existingX) < Mathf.Max(2, minimumSpacing)) { valid = false; break; }
                }
                if (!valid) continue;
                if (fallbackX == int.MinValue) fallbackX = candidateX;
                float worldX = map.Terrain.GetCellCenterWorld(new Vector3Int(candidateX, 0)).x;
                float blend = spawnQuietRadius > 0f ? Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(Mathf.Abs(worldX - spawnX) / spawnQuietRadius)) : 1f;
                if (Hash01(index, 0xAD52u) < Mathf.Lerp(nearSpawnDensity, 1f, blend))
                    selectedX = candidateX;
            }
            if (selectedX == int.MinValue) selectedX = fallbackX;
            if (selectedX == int.MinValue) continue;
            for (int x = selectedX; x < selectedX + size; x++)
            {
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
            if (map && map.IsGenerated && !map.IsGenerationStreaming) Rebuild();
            return;
        }
        if (!Application.isPlaying || !map || !map.IsGenerated || respawnRandom == null) return;
        if (scheduledConversionRate != ConversionRate) ScheduleConversion();
        if (Time.time >= nextConversion)
        {
            ConvertOneGrass();
            ScheduleConversion();
        }
        if (patches.Count >= sites.Count || Time.time < nextRespawn) return;
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
            if (Mathf.Abs(worldX - map.Terrain.GetCellCenterWorld(new Vector3Int(0, 0)).x) >= healingHerbSpawnAreaSize &&
                medicinalHerbVariants != null && medicinalHerbVariants.Length > 0 &&
                respawnRandom.NextDouble() < Mathf.Clamp01(healingHerbRarityPercent / 100f))
                healingHerbSites.Add(x);
            else healingHerbSites.Remove(x);
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

    float ConversionRate => Mathf.Clamp01(healingHerbRarityPercent / 100f) *
        Mathf.Max(0f, healingHerbConversionFactor);

    void ScheduleConversion()
    {
        scheduledConversionRate = ConversionRate;
        if (respawnRandom == null || scheduledConversionRate <= 0f)
        {
            nextConversion = float.PositiveInfinity;
            return;
        }
        float earliest = Mathf.Max(.1f, respawnSeconds.x);
        float latest = Mathf.Max(earliest, respawnSeconds.y);
        nextConversion = Time.time + Mathf.Lerp(earliest, latest,
            (float)respawnRandom.NextDouble()) / scheduledConversionRate;
    }

    void ConvertOneGrass()
    {
        if (ConversionRate <= 0f || healingHerbConversionLimitPercent <= 0f ||
            medicinalHerbVariants == null || medicinalHerbVariants.Length == 0 || patches.Count == 0) return;
        int herbs = 0;
        foreach (var patch in patches.Values)
            if (patch && patch.IsHealingHerb) herbs++;
        if (herbs >= Mathf.CeilToInt(patches.Count * healingHerbConversionLimitPercent / 100f)) return;

        int selectedX = int.MinValue;
        int eligible = 0;
        float spawnX = map.Terrain.GetCellCenterWorld(new Vector3Int(0, 0)).x;
        foreach (var pair in patches)
        {
            if (!pair.Value || pair.Value.IsHealingHerb) continue;
            float worldX = map.Terrain.GetCellCenterWorld(new Vector3Int(pair.Key, 0)).x;
            if (Mathf.Abs(worldX - spawnX) < healingHerbSpawnAreaSize) continue;
            if (respawnRandom.Next(++eligible) == 0) selectedX = pair.Key;
        }
        if (selectedX == int.MinValue) return;
        var target = patches[selectedX];
        var renderer = target.GetComponent<SpriteRenderer>();
        renderer.sprite = medicinalHerbVariants[(int)(OreVeins.Hash(map.ActiveSeed, selectedX, 0, 0x4D5641u) %
            (uint)medicinalHerbVariants.Length)];
        healingHerbSites.Add(selectedX);
        target.Initialize(this, selectedX, true);
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
        ApplyPatchSize(patch);
        patches.Add(x, patch);
    }

    public bool CutAt(int surfaceCellX)
    {
        if (!HasScythe || !patches.TryGetValue(surfaceCellX, out var patch) || !patch) return false;
        var inventory = InventoryManager.Instance;
        int herbAmount = patch.IsHealingHerb && healingHerbs ? RollYield(healingHerbYield) : 0;
        int fiberAmount = fiber ? RollYield(patch.IsHealingHerb ? healingHerbFiberYield : fiberYield) : 0;
        double gainedWeight = (healingHerbs ? healingHerbs.EffectiveWeight * (double)herbAmount : 0d) +
            (fiber ? fiber.EffectiveWeight * (double)fiberAmount : 0d);
        if (!inventory || !inventory.CanFitWeight(gainedWeight) ||
            (healingHerbs && herbAmount > 0 && inventory.GetCount(healingHerbs) > int.MaxValue - herbAmount) ||
            (fiber && fiberAmount > 0 && inventory.GetCount(fiber) > int.MaxValue - fiberAmount)) return false;
        if (Application.isPlaying)
        {
            var particles = GetComponent<TallGrassCutParticles>();
            if (!particles) particles = gameObject.AddComponent<TallGrassCutParticles>();
            particles.Burst(patch.GetComponent<SpriteRenderer>().bounds);
        }
        RemovePatch(surfaceCellX);
        if (Application.isPlaying)
        {
            if (herbAmount > 0) inventory.Add(healingHerbs, herbAmount);
            if (fiberAmount > 0) inventory.Add(fiber, fiberAmount);
        }
        return true;
    }

    public bool RemoveWithoutYieldAt(int surfaceCellX)
    {
        if (!patches.TryGetValue(surfaceCellX, out var patch) || !patch) return false;
        if (Application.isPlaying)
        {
            var renderer = patch.GetComponent<SpriteRenderer>();
            var particles = GetComponent<TallGrassCutParticles>();
            if (!particles) particles = gameObject.AddComponent<TallGrassCutParticles>();
            if (renderer) particles.Burst(renderer.bounds);
            AudioManager.Instance?.Play(SoundType.DryGrass);
        }
        RemovePatch(surfaceCellX);
        return true;
    }

    int RollYield(Vector2Int range) => respawnRandom != null
        ? respawnRandom.Next(Mathf.Max(1, range.x), Mathf.Max(range.x, range.y) + 1)
        : Mathf.Max(1, range.x);

    public void RemoveNear(int surfaceCellX, int radius)
    {
        for (int x = surfaceCellX - Mathf.Max(0, radius); x <= surfaceCellX + radius; x++)
            RemovePatch(x);
    }

    void RemovePatch(int x)
    {
        if (!patches.Remove(x, out var patch) || !patch) return;
        healingHerbSites.Remove(x);
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
        foreach (var building in FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None))
            if (Overlaps(x, building.GetComponent<Collider2D>())) return true;
        return false;
    }

    static void CacheBounds(Component building, List<Vector2> bounds)
    {
        var collider = building.GetComponent<Collider2D>();
        if (collider) bounds.Add(new Vector2(collider.bounds.center.x, collider.bounds.extents.x + 1.5f));
    }

    static bool NearBuilding(float x, List<Vector2> bounds)
    {
        foreach (var range in bounds) if (Mathf.Abs(x - range.x) < range.y) return true;
        return false;
    }

    static bool Overlaps(float x, Collider2D collider) => collider &&
        Mathf.Abs(x - collider.bounds.center.x) < collider.bounds.extents.x + 1.5f;
}
