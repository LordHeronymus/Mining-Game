using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(OreOverlayAppearance), typeof(DirtSurfaceAppearance))]
public class MapGenerator : MonoBehaviour
{
    const int InitialGenerationRows = 128;
    const int StreamingRowsPerFrame = 4;
    const float OreSubstrateInfluence = .7f;
    static readonly Unity.Profiling.ProfilerMarker LoadingSetupMarker = new("Loading.MapSetup");
    [Header("Map Size")]
    public int mapWidth = 100;
    public int mapHeight = 1000;

    [Header("Generation")]
    public BlockRegistry registry;
    [Range(-1e7f, 1e7f)] public int seed = 0;
    [InspectorName("Seed zufällig generieren")] public bool randomizeSeed;
    [SerializeField, HideInInspector] bool seedModeInitialized;
    [Range(.5f, 3f), InspectorName("Erzgröße (×)")] public float oreScale = 1.35f;
    [Range(1, 100), InspectorName("Übergangsdicke (Kacheln)")] public int transitionThickness = 15;
    [Range(-.5f, .5f), InspectorName("Gras Y-Versatz (Welteinheiten)")] public float grassYOffset;
    [Header("Caves")]
    public CaveGenerationSettings caveGeneration = new CaveGenerationSettings();
    [Range(0f, 60f), InspectorName("Spawnrate (Gruppen/min)")]
    public float caveFireflySpawnRate = 3f;
    [Range(0f, 200f), InspectorName("Helligkeit (%)")]
    public float caveFireflyBrightness = 55f;
    [ColorUsage(false, false), InspectorName("Spektrum Blau")]
    public Color caveFireflyBlue = new Color32(75, 160, 255, 255);
    [ColorUsage(false, false), InspectorName("Spektrum Grün")]
    public Color caveFireflyGreen = new Color32(110, 245, 145, 255);
    [ColorUsage(false, false), InspectorName("Spektrum Gelb")]
    public Color caveFireflyYellow = new Color32(255, 224, 95, 255);
    [Range(1, 12), InspectorName("Gruppengröße min")]
    public int caveFireflyGroupSizeMin = 3;
    [Range(1, 12), InspectorName("Gruppengröße max")]
    public int caveFireflyGroupSizeMax = 6;
    [Header("Map Overview")]
    [SerializeField, HideInInspector] public Color[] mapOverviewColors =
    {
        new Color32(244, 123, 32, 255),  // Stone / Übergang
        new Color32(61, 156, 255, 255),  // Eisen
        new Color32(255, 87, 34, 255),   // Kupfer
        new Color32(255, 211, 0, 255),   // Gold
        new Color32(231, 237, 245, 255), // Silber
        new Color32(23, 25, 29, 255),    // Leer
        new Color32(255, 62, 234, 255),  // Platin
        new Color32(9, 11, 16, 255),     // Kohle
        new Color32(0, 232, 255, 255),   // Diamant
        new Color32(211, 91, 38, 255),   // Stein
        new Color32(142, 63, 255, 255),  // Tiefstein 1
        new Color32(196, 85, 28, 255),   // Erde
        new Color32(49, 77, 255, 255),   // Tiefstein 2
        new Color32(180, 0, 255, 255),   // Ultronium
        new Color32(255, 255, 255, 255), // Titan
        new Color32(111, 160, 208, 255), // Wolfram
        new Color32(255, 112, 24, 255),  // Orange Granat
        new Color32(35, 205, 255, 255), // Mythril
        new Color32(28, 210, 72, 255),  // Emerald
        new Color32(242, 20, 52, 255)   // Ruby
    };
    [SerializeField, HideInInspector] public Color mapOverviewPlayerColor = Color.cyan;
    [Header("Torch Visuals")]
    [Range(-2f, 2f)] public float torchFlameOffsetX;
    [Range(-2f, 2f)] public float torchFlameOffsetY = .29f;
    [Range(.1f, 3f)] public float torchFlameSize = 1f;
    [Range(0f, 120f)] public float torchFlameFrequency = 49f;
    [Range(0f, 2f)] public float torchBrightness = 1f;
    [Header("Ultronium Altar")]
    [Range(0f, .15f)] public float altarButtonPulseAmplitude = .035f;
    [Range(0f, 3f)] public float altarButtonPulseFrequency = .65f;
    [Range(0f, .2f)] public float altarButtonFloatAmplitude = .05f;
    [Range(0f, 3f)] public float altarButtonFloatFrequency = .45f;
    [Header("Map Objects")]
    [Range(.25f, 3f)] public float shopSize = 1f;
    [Range(.25f, 3f)] public float workshopSize = 1f;
    [Range(.25f, 3f)] public float altarSize = 1f;
    [Range(.25f, 3f)] public float energyMonolythSize = 1f;
    [SerializeField, HideInInspector] float appliedShopSize = 1f;
    [SerializeField, HideInInspector] float appliedWorkshopSize = 1f;
    [SerializeField, HideInInspector] float appliedEnergyMonolythSize = 1f;
    [SerializeField, HideInInspector] float appliedAltarSize = 1f;
    [SerializeField] TileBase[] grassVariants;
    [SerializeField] bool continuousGrassStrip;
    [SerializeField] TileBase grassLeftEnd;
    [SerializeField] TileBase grassRightEnd;
    [SerializeField] TileBase grassSingle;
    [SerializeField] TileBase grassHangLeft;
    [SerializeField] TileBase grassHangRight;

    [InspectorName("Tiefenkurve (×)")]
    public AnimationCurve oreDensityCurve = AnimationCurve.Linear(0f, .1f, 1f, 1f);
    [Range(0f, 100f), InspectorName("Basis-Erzdichte (%)")]
    public float oreDensityMultiplierPercent = 50f;
    // Retained as the inherited default for maps/settings created before per-ore minimums.
    [SerializeField, HideInInspector, Min(1)]
    public int minimumOreVeinSize = 4;
    [SerializeField, HideInInspector, FormerlySerializedAs("oreDensityByDepth")]
    AnimationCurve legacyOreDensityByDepth;

    [Header("Layers")]
    public MapLayer[] layers;
    [SerializeField, HideInInspector] public bool useOreSettings;
    [SerializeField, HideInInspector] public OreDistributionSetting[] oreSettings;
    [Header("Artifacts")]
    public ArtifactDistributionSetting[] artifactSettings = System.Array.Empty<ArtifactDistributionSetting>();
    [Min(1), InspectorName("Mindesttiefe Artefakte (Y)")] public int artifactMinimumDepth = 10;
    [Range(0, 100), InspectorName("Mindestabstand gleicher Artefakte (Blöcke)")] public int artifactMinimumSameTypeDistance = 10;
    [Range(.1f, 5f)] public float artifactDropChanceMultiplier = 1f;
    [Range(0f, 1f)] public float artifactEmbeddingStrength = 1f;
    [Range(.25f, 8f)] public float artifactOverviewIconScale = 4f;
    [Range(.5f, 10f)] public float artifactDiscoveryDurationSeconds = 3f;
    [Range(0, 60)] public int artifactDiscoveryShardCount = 16;
    [Range(.25f, 2f)] public float artifactDiscoveryShardDistance = 1f;
    [Range(2f, 40f)] public float artifactDiscoveryShardSizeMin = 8f;
    [Range(2f, 40f)] public float artifactDiscoveryShardSizeMax = 17f;
    [Range(-300f, 300f)] public float artifactDiscoveryArtifactYOffset;
    [Range(0f, 2f)] public float artifactDiscoveryIconRotationFrequency = .35f;
    [Range(0f, 15f)] public float artifactDiscoveryIconRotationAngle = 3f;
    [Range(0f, 3f)] public float artifactDiscoveryShardRotationFrequency = .6f;
    [Range(0f, 25f)] public float artifactDiscoveryShardRotationAngle = 8f;
    [Header("Terrain Test")]
    public Block uniformTestStone;
    public StoneTestTile uniformTestTile;
    public StoneTestTile surfaceDirtTile;
    public StoneTestTile layerOneTile;
    public StoneTestTile layerThreeTile;
    public StoneTestTile layerFourTile;

    private Tilemap tilemap;
    [SerializeField] Tilemap oreOverlay;
    [SerializeField] Tilemap artifactOverlay;
    [SerializeField] Tilemap grassOverlay;
    [SerializeField] Tilemap grassHangLeftOverlay;
    [SerializeField] Tilemap grassHangRightOverlay;
    public Tilemap Terrain => tilemap ? tilemap : tilemap = GetComponent<Tilemap>();
    public UltroniumAltarChamber AltarChamber => GetComponent<UltroniumAltarChamber>();
    public bool IsCellProtected(Vector3Int cell) => IsSurfaceCellProtected(cell) || (AltarChamber && AltarChamber.Protects(cell));
    public Tilemap OreOverlay => oreOverlay;
    public Tilemap ArtifactOverlay => artifactOverlay;
    public Tilemap GrassOverlay => grassOverlay;
    public Tilemap GrassHangLeftOverlay => grassHangLeftOverlay;
    public Tilemap GrassHangRightOverlay => grassHangRightOverlay;
    [SerializeField, HideInInspector] bool isGenerated;
    [SerializeField, HideInInspector] int generatedSeed, generatedWidth, generatedHeight;
    public bool IsGenerated => isGenerated;
    public int ActiveSeed => isGenerated ? generatedSeed : seed;
    public int GeneratedWidth => isGenerated ? generatedWidth : mapWidth;
    public int GeneratedHeight => isGenerated ? generatedHeight : mapHeight;
    public event System.Action Generated;
    public event System.Action GenerationCompleted;
    public bool IsGenerationStreaming { get; private set; }
    Coroutine generationRoutine;
    MapGenerationSnapshot pendingGeneration;
    int pendingGenerationRow;
    TileBase[] streamedTerrainRows;
    TileBase[] streamedOreRows;
    TileBase[] streamedArtifactRows;
    bool[] pendingCaveMask;
    bool[] generatedCaveMask;
    int cachedCaveMaskWidth, cachedCaveMaskHeight;
    bool applyingGeneratedTiles;

    public sealed class MapGenerationSnapshot
    {
        public readonly int seed;
        public readonly int width;
        public readonly int height;
        public readonly int offsetX;
        public readonly TileBase[] terrainTiles;
        public readonly TileBase[] oreTiles;
        public readonly TileBase[] artifactTiles;

        public MapGenerationSnapshot(int seed, int width, int height, int offsetX,
            TileBase[] terrainTiles, TileBase[] oreTiles, TileBase[] artifactTiles)
        {
            this.seed = seed;
            this.width = width;
            this.height = height;
            this.offsetX = offsetX;
            this.terrainTiles = terrainTiles;
            this.oreTiles = oreTiles;
            this.artifactTiles = artifactTiles;
        }
    }
    SurfaceTrees surfaceTrees;
    readonly List<Bounds> protectedSurfaceObjectBounds = new();
    public bool IsSurfaceCellProtected(Vector3Int cell)
    {
        if (!Application.isPlaying || cell.y != 0) return false;
        if (!surfaceTrees) surfaceTrees = FindFirstObjectByType<SurfaceTrees>();
        if (surfaceTrees && surfaceTrees.Protects(cell)) return true;

        if (protectedSurfaceObjectBounds.Count == 0) CacheProtectedSurfaceObjectBounds();
        float x = Terrain.GetCellCenterWorld(cell).x;
        foreach (var bounds in protectedSurfaceObjectBounds)
            if (x >= bounds.min.x && x <= bounds.max.x) return true;
        return false;
    }

    void CacheProtectedSurfaceObjectBounds()
    {
        AddProtectedSurfaceObjectBounds<ShopBuilding>();
        AddProtectedSurfaceObjectBounds<WorkbenchBuilding>();
        AddProtectedSurfaceObjectBounds<EnergyMonolyth>();
        AddProtectedSurfaceObjectBounds<SurfaceStorageBuilding>();
    }

    void AddProtectedSurfaceObjectBounds<T>() where T : MonoBehaviour
    {
        foreach (var surfaceObject in FindObjectsByType<T>(FindObjectsSortMode.None))
            foreach (var renderer in surfaceObject.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.bounds.size.x > 0f) protectedSurfaceObjectBounds.Add(renderer.bounds);
    }
#if UNITY_EDITOR
    public static event System.Action<MapGenerator, MapGenerationSnapshot> InitialMapPrepared;
    public static event System.Action<MapGenerator> InitialMapGenerated;
#endif

    void Awake()
    {
        GpsSettings.ApplyComponent(this);
        if (Application.isPlaying && !FindFirstObjectByType<SurfaceStorageBuilding>())
            SurfaceStorageBuilding.CreateNearShop(FindFirstObjectByType<ShopBuilding>(),
                Resources.Load<Sprite>("SurfaceStorage/StorageHut"));
        ApplyMapObjectSizes();
        if (!GetComponent<UltroniumAltarChamber>()) gameObject.AddComponent<UltroniumAltarChamber>();
        MigrateSeedMode();
        MigrateOreDensitySettings();
        tilemap = GetComponent<Tilemap>();
        isGenerated = false;
        if (Application.isPlaying && GetComponent<TilemapCollider2D>() && !GetComponent<TerrainColliderChunks>())
            gameObject.AddComponent<TerrainColliderChunks>();
    }

    void OnEnable()
    {
        Tilemap.tilemapTileChanged += TerrainChanged;
        if (!Application.isPlaying || GetComponent<CaveFireflies>()) return;
        var fireflies = gameObject.AddComponent<CaveFireflies>();
        fireflies.map = this;
    }

    void OnDisable() => Tilemap.tilemapTileChanged -= TerrainChanged;

    void OnValidate()
    {
        MigrateSeedMode();
        MigrateOreDensitySettings();
        UpdateGrassOffset();
    }

    public void ApplyMapObjectSizes()
    {
        ApplyGroundedSize<ShopBuilding>(shopSize, ref appliedShopSize);
        ApplyGroundedSize<WorkbenchBuilding>(workshopSize, ref appliedWorkshopSize);
        ApplyGroundedSize<EnergyMonolyth>(energyMonolythSize, ref appliedEnergyMonolythSize);
        if (!Mathf.Approximately(altarSize, appliedAltarSize))
        {
            appliedAltarSize = altarSize;
            if (AltarChamber && AltarChamber.Layout.valid) AltarChamber.RebuildVisuals();
        }
        protectedSurfaceObjectBounds.Clear();
    }

    static void ApplyGroundedSize<T>(float requested, ref float applied) where T : MonoBehaviour
    {
        requested = Mathf.Clamp(requested, .25f, 3f);
        if (Mathf.Approximately(requested, applied)) return;
        float ratio = requested / Mathf.Max(.001f, applied);
        foreach (var item in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            var sprites = item.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length == 0) continue;
            float bottom = float.PositiveInfinity;
            foreach (var sprite in sprites)
                if (sprite.sprite) bottom = Mathf.Min(bottom, sprite.bounds.min.y);
            if (float.IsInfinity(bottom)) continue;
            item.transform.localScale *= ratio;
            float scaledBottom = float.PositiveInfinity;
            foreach (var sprite in sprites)
                if (sprite.sprite) scaledBottom = Mathf.Min(scaledBottom, sprite.bounds.min.y);
            item.transform.position += Vector3.up * (bottom - scaledBottom);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
                UnityEditor.EditorUtility.SetDirty(item.transform);
            }
#endif
        }
        applied = requested;
    }

    public void MigrateSeedMode()
    {
        if (seedModeInitialized) return;
        randomizeSeed = seed == 0;
        seedModeInitialized = true;
    }

    public int ChooseGenerationSeed()
    {
        MigrateSeedMode();
        if (!randomizeSeed) return seed;
        int value;
        do value = Random.Range(-10000000, 10000001); while (value == 0);
        return value;
    }

    public bool MigrateOreDensitySettings()
    {
        if (legacyOreDensityByDepth == null || legacyOreDensityByDepth.length == 0) return false;
        var keys = legacyOreDensityByDepth.keys;
        float maximum = 0f;
        foreach (var key in keys) maximum = Mathf.Max(maximum, key.value);
        oreDensityMultiplierPercent = Mathf.Clamp(maximum, 0f, 100f);
        float factor = oreDensityMultiplierPercent > 0f ? 1f / oreDensityMultiplierPercent : 0f;
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            key.value *= factor;
            key.inTangent *= factor;
            key.outTangent *= factor;
            keys[i] = key;
        }
        oreDensityCurve = new AnimationCurve(keys)
        {
            preWrapMode = legacyOreDensityByDepth.preWrapMode,
            postWrapMode = legacyOreDensityByDepth.postWrapMode
        };
        legacyOreDensityByDepth = null;
        return true;
    }

    public int GetMinimumVeinSize(Block ore)
    {
        if (!ore) return Mathf.Max(1, minimumOreVeinSize);
        var setting = oreSettings == null ? null : System.Array.Find(oreSettings,
            entry => entry != null && entry.ore == ore.id);
        if (setting != null && setting.minimumVeinSize > 0)
            return Mathf.Max(1, setting.minimumVeinSize);
        return ore.id == BlockType.UltroniumOre ? 1 : Mathf.Max(1, minimumOreVeinSize);
    }

    void Start()
    {
        if (Application.isPlaying)
            generationRoutine = StartCoroutine(GenerateMapInPlay());
        else
            GenerateMap();
    }

    void TerrainChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (source != Terrain || changes == null) return;
        foreach (var change in changes)
        {
            if (!source.HasTile(change.position))
            {
                if (oreOverlay) oreOverlay.SetTile(change.position, null);
                if (artifactOverlay) artifactOverlay.SetTile(change.position, null);
            }
            if (!applyingGeneratedTiles && grassOverlay && change.position.y == 0)
            {
                RefreshGrassAt(change.position.x - 1);
                RefreshGrassAt(change.position.x);
                RefreshGrassAt(change.position.x + 1);
            }
        }
    }

    public Tilemap EnsureOreOverlay()
    {
        if (!oreOverlay)
        {
            var existing = transform.Find("Ore Overlay");
            if (existing) oreOverlay = existing.GetComponent<Tilemap>();
            if (!oreOverlay)
            {
                var child = new GameObject("Ore Overlay", typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(transform, false);
                oreOverlay = child.GetComponent<Tilemap>();
            }
        }
        oreOverlay.gameObject.layer = gameObject.layer;
        oreOverlay.tileAnchor = Terrain.tileAnchor;
        oreOverlay.orientation = Terrain.orientation;
        oreOverlay.orientationMatrix = Terrain.orientationMatrix;
        var source = GetComponent<TilemapRenderer>();
        var target = oreOverlay.GetComponent<TilemapRenderer>();
        if (source && target)
        {
            // The same lit material and darkness mask as stone; no emissive ore pass.
            target.sharedMaterial = source.sharedMaterial;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder + 1;
            target.mode = source.mode;
            target.sortOrder = source.sortOrder;
            GetComponent<OreOverlayAppearance>()?.ApplyTo(target);
        }
        return oreOverlay;
    }

    public Tilemap EnsureArtifactOverlay()
    {
        if (!artifactOverlay)
        {
            var existing = transform.Find("Artifact Overlay");
            if (existing) artifactOverlay = existing.GetComponent<Tilemap>();
            if (!artifactOverlay)
            {
                var child = new GameObject("Artifact Overlay", typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(transform, false);
                artifactOverlay = child.GetComponent<Tilemap>();
            }
        }
        artifactOverlay.gameObject.layer = gameObject.layer;
        artifactOverlay.tileAnchor = Terrain.tileAnchor;
        artifactOverlay.orientation = Terrain.orientation;
        artifactOverlay.orientationMatrix = Terrain.orientationMatrix;
        var source = GetComponent<TilemapRenderer>();
        var target = artifactOverlay.GetComponent<TilemapRenderer>();
        if (source && target)
        {
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder + 2;
            target.mode = source.mode;
            target.sortOrder = source.sortOrder;
            ArtifactOverlayAppearance.ApplyTo(target, artifactEmbeddingStrength);
        }
        return artifactOverlay;
    }

    public Tilemap EnsureGrassOverlay()
    {
        if (!grassOverlay)
        {
            var existing = transform.Find("Grass Overlay");
            if (existing) grassOverlay = existing.GetComponent<Tilemap>();
            if (!grassOverlay)
            {
                var child = new GameObject("Grass Overlay", typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(transform, false);
                grassOverlay = child.GetComponent<Tilemap>();
            }
        }
        grassOverlay.gameObject.layer = gameObject.layer;
        grassOverlay.tileAnchor = Terrain.tileAnchor;
        grassOverlay.orientation = Terrain.orientation;
        grassOverlay.orientationMatrix = Terrain.orientationMatrix;
        UpdateGrassOffset();
        var source = GetComponent<TilemapRenderer>();
        var target = grassOverlay.GetComponent<TilemapRenderer>();
        if (source && target)
        {
            target.sharedMaterial = source.sharedMaterial;
            target.sortingLayerName = "Grass";
            target.sortingOrder = 0;
            target.mode = source.mode;
            target.sortOrder = source.sortOrder;
        }
        return grassOverlay;
    }

    public void EnsureGrassHangOverlays()
    {
        if (!grassHangLeft && !grassHangRight) return;
        var baseOverlay = EnsureGrassOverlay();
        grassHangLeftOverlay = EnsureGrassHangOverlay(grassHangLeftOverlay, "Grass Hang Left", baseOverlay);
        grassHangRightOverlay = EnsureGrassHangOverlay(grassHangRightOverlay, "Grass Hang Right", baseOverlay);
        UpdateGrassOffset();
    }

    Tilemap EnsureGrassHangOverlay(Tilemap overlay, string name, Tilemap baseOverlay)
    {
        if (!overlay)
        {
            var existing = transform.Find(name);
            if (existing) overlay = existing.GetComponent<Tilemap>();
            if (!overlay)
            {
                var child = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
                child.transform.SetParent(transform, false);
                overlay = child.GetComponent<Tilemap>();
            }
        }
        overlay.gameObject.layer = gameObject.layer;
        overlay.tileAnchor = Terrain.tileAnchor;
        overlay.orientation = Terrain.orientation;
        overlay.orientationMatrix = Terrain.orientationMatrix;
        var source = baseOverlay.GetComponent<TilemapRenderer>();
        var target = overlay.GetComponent<TilemapRenderer>();
        target.sharedMaterial = source.sharedMaterial;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder - 1;
        target.mode = source.mode;
        target.sortOrder = source.sortOrder;
        return overlay;
    }

    void UpdateGrassOffset()
    {
        SetGrassOffset(grassOverlay);
        SetGrassOffset(grassHangLeftOverlay);
        SetGrassOffset(grassHangRightOverlay);
    }

    void SetGrassOffset(Tilemap overlay)
    {
        if (!overlay) return;
        var position = overlay.transform.localPosition;
        position.y = grassYOffset;
        overlay.transform.localPosition = position;
    }

    public void SetGrassVariants(TileBase[] variants, bool continuousStrip = false)
    { grassVariants = variants; continuousGrassStrip = continuousStrip; }

    public void SetGrassEdgeTiles(TileBase leftEnd, TileBase rightEnd, TileBase single)
    { grassLeftEnd = leftEnd; grassRightEnd = rightEnd; grassSingle = single; }

    public void SetGrassHangTiles(TileBase left, TileBase right)
    { grassHangLeft = left; grassHangRight = right; }

    TileBase GrassTileAt(int x)
    {
        var cell = new Vector3Int(x, 0, 0);
        if (!Terrain.HasTile(cell) || grassVariants == null || grassVariants.Length == 0) return null;
        bool hasLeft = Terrain.HasTile(cell + Vector3Int.left);
        bool hasRight = Terrain.HasTile(cell + Vector3Int.right);
        if (!hasLeft && !hasRight && grassSingle) return grassSingle;
        if (!hasLeft && grassLeftEnd) return grassLeftEnd;
        if (!hasRight && grassRightEnd) return grassRightEnd;
        int offset = continuousGrassStrip
            ? (int)(OreVeins.Hash(ActiveSeed, 0, 0, 0x6A55u) % (uint)grassVariants.Length)
            : (int)(OreVeins.Hash(ActiveSeed, x, 0, 0x6A55u) % (uint)grassVariants.Length);
        return grassVariants[continuousGrassStrip
            ? ((x % grassVariants.Length + grassVariants.Length + offset) % grassVariants.Length)
            : offset];
    }

    void RefreshGrassAt(int x)
    {
        if (!grassOverlay) return;
        var cell = new Vector3Int(x, 0, 0);
        grassOverlay.SetTile(cell, GrassTileAt(x));
        bool occupied = Terrain.HasTile(cell);
        if (grassHangLeftOverlay)
            grassHangLeftOverlay.SetTile(cell, occupied && !Terrain.HasTile(cell + Vector3Int.left)
                ? grassHangLeft : null);
        if (grassHangRightOverlay)
            grassHangRightOverlay.SetTile(cell, occupied && !Terrain.HasTile(cell + Vector3Int.right)
                ? grassHangRight : null);
    }

    public void SyncGrassFromTerrain()
    {
        var overlay = EnsureGrassOverlay();
        EnsureGrassHangOverlays();
        overlay.ClearAllTiles();
        if (grassHangLeftOverlay) grassHangLeftOverlay.ClearAllTiles();
        if (grassHangRightOverlay) grassHangRightOverlay.ClearAllTiles();
        int width = GeneratedWidth;
        if (width <= 0 || grassVariants == null || grassVariants.Length == 0) return;
        var tiles = new TileBase[width];
        var leftHangTiles = grassHangLeftOverlay ? new TileBase[width] : null;
        var rightHangTiles = grassHangRightOverlay ? new TileBase[width] : null;
        int left = -width / 2;
        for (int x = 0; x < width; x++)
        {
            var cell = new Vector3Int(left + x, 0, 0);
            tiles[x] = GrassTileAt(cell.x);
            if (!Terrain.HasTile(cell)) continue;
            if (leftHangTiles != null && !Terrain.HasTile(cell + Vector3Int.left))
                leftHangTiles[x] = grassHangLeft;
            if (rightHangTiles != null && !Terrain.HasTile(cell + Vector3Int.right))
                rightHangTiles[x] = grassHangRight;
        }
        var bounds = new BoundsInt(left, 0, 0, width, 1, 1);
        overlay.SetTilesBlock(bounds, tiles);
        overlay.CompressBounds();
        if (grassHangLeftOverlay)
        {
            grassHangLeftOverlay.SetTilesBlock(bounds, leftHangTiles);
            grassHangLeftOverlay.CompressBounds();
        }
        if (grassHangRightOverlay)
        {
            grassHangRightOverlay.SetTilesBlock(bounds, rightHangTiles);
            grassHangRightOverlay.CompressBounds();
        }
    }

    public OreTile GetOreAt(Vector3Int cell) => Terrain.HasTile(cell) && oreOverlay
        ? oreOverlay.GetTile<OreTile>(cell) : null;

    public ArtifactTile GetArtifactAt(Vector3Int cell) => Terrain.HasTile(cell) && artifactOverlay
        ? artifactOverlay.GetTile<ArtifactTile>(cell) : null;

    public int GetArtifactCash(ArtifactTile tile)
    {
        if (!tile || artifactSettings == null) return 0;
        foreach (var setting in artifactSettings)
            if (setting != null && setting.tile == tile) return Mathf.Max(0, setting.cash);
        return 0;
    }

    public int GetArtifactPoints(ArtifactTile tile)
    {
        if (!tile || artifactSettings == null) return 0;
        foreach (var setting in artifactSettings)
            if (setting != null && setting.tile == tile) return Mathf.Max(0, setting.artifactPoints);
        return 0;
    }

    public Block GetBlockAt(Vector3Int cell)
    {
        var terrain = Terrain.GetTile(cell);
        if (!terrain) return null;
        var ore = oreOverlay ? oreOverlay.GetTile<OreTile>(cell) : null;
        return ore && ore.block ? ore.block : registry ? registry.FromTile(terrain) : null;
    }

    public float GetHardnessAt(Vector3Int cell, Block block)
    {
        if (!block) return 1f;
        float hardness = Mathf.Max(.01f, block.hardness <= 0f ? 1f : block.hardness);
        if (!block.HasOreOverlays || !registry) return hardness;

        var substrate = registry.FromTile(Terrain.GetTile(cell));
        if (!substrate || substrate.HasOreOverlays) return hardness;
        float substrateHardness = Mathf.Max(.01f, substrate.hardness <= 0f ? 1f : substrate.hardness);
        return Mathf.Max(.01f, hardness + OreSubstrateInfluence * (substrateHardness - 1f));
    }

    public bool RemoveBlock(Vector3Int cell)
    {
        if (IsCellProtected(cell)) return false;
        if (!Terrain.HasTile(cell)) return false;
        if (oreOverlay) oreOverlay.SetTile(cell, null);
        if (artifactOverlay) artifactOverlay.SetTile(cell, null);
        if (grassOverlay && cell.y == 0) grassOverlay.SetTile(cell, null);
        Terrain.SetTile(cell, null);
        GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        return true;
    }

    public void GenerateMap(int? seedOverride = null)
    {
        GpsSettings.ApplyGeneration(this);
        CancelStreamingGeneration();
        var data = PrepareGeneratedMap(seedOverride ?? ChooseGenerationSeed());
#if UNITY_EDITOR
        InitialMapPrepared?.Invoke(this, data);
#endif
        BeginTileApplication();
        ApplyTileRows(data, 0, data.height);
        tilemap.CompressBounds();
        oreOverlay.CompressBounds();
        artifactOverlay.CompressBounds();
        ActivateGeneratedMap(data);
        FinishGeneration();
    }

    IEnumerator GenerateMapInPlay()
    {
        if (!LoadingProgress.Active) LoadingScreen.Show();
        while (!LoadingScreen.ContentVisible) yield return null;
        // Do not combine Awake/Start scene activation with map restoration.
        yield return null;
        if (GameSaveSystem.HasPendingLoad)
        {
            yield return GameSaveSystem.RestorePending(this);
            yield break;
        }
        GpsSettings.ApplyGeneration(this);
        LoadingProgress.Configure(mapWidth, mapHeight);
        yield return null;
        MapGenerationSnapshot data = null;
        var prepare = PrepareGeneratedMapSteps(ChooseGenerationSeed(), value => data = value, true);
        while (prepare.MoveNext()) yield return null;
#if UNITY_EDITOR
        InitialMapPrepared?.Invoke(this, data);
#endif
        LoadingProgress.SetStage(5);
        BeginTileApplication();
        IsGenerationStreaming = true;
        pendingGeneration = data;
        var tileBudget = new LoadingWorkBudget();
        for (pendingGenerationRow = 0; pendingGenerationRow < data.height;)
        {
            // SetTilesBlock also queues native Tilemap work later in the frame.
            // Bound cells, not just rows, so wider maps keep the loading animation responsive.
            int rows = Mathf.Min(Mathf.Max(1, 1024 / data.width), data.height - pendingGenerationRow);
            ApplyTileRows(data, pendingGenerationRow, rows);
            pendingGenerationRow += rows;
            tileBudget.ChargeTiles(rows * data.width * 3);
            LoadingProgress.Report(pendingGenerationRow / (float)data.height * .9f);
            if (tileBudget.Expired) { yield return null; tileBudget.Restart(); }
        }
        var appearance = GetComponent<UniformStoneAppearance>();
        if (appearance && appearance.isActiveAndEnabled)
        {
            var appearanceSteps = appearance.PrepareForLoading(data, value => LoadingProgress.Report(.9f + value * .09f));
            while (appearanceSteps.MoveNext()) yield return null;
        }
        InitializeGeneratedMapState(data);
        yield return InvokeGeneratedForLoading();
        var surface = GetComponent<DirtSurfaceAppearance>();
        if (surface && surface.isActiveAndEnabled)
        {
            var surfaceSteps = surface.PrepareForLoading();
            while (surfaceSteps.MoveNext()) yield return null;
        }
        LoadingProgress.Report(1f);
        yield return null;
        LoadingProgress.SetStage(6);
        var lighting = GetComponent<MapLighting>();
        if (lighting)
        {
            var preparation = lighting.PrepareForLoading(data, LoadingProgress.Report);
            while (preparation.MoveNext()) yield return null;
        }
        LoadingProgress.SetStage(7);
        FinishGeneration(true);
        LoadingProgress.Report(.5f);
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return GameSaveSystem.SaveNewRunForLoading(this);
        LoadingProgress.Complete();
    }
    public void CompleteStreamingGeneration()
    {
        if (!IsGenerationStreaming || pendingGeneration == null) return;
        if (generationRoutine != null) StopCoroutine(generationRoutine);
        generationRoutine = null;
        ApplyTileRows(pendingGeneration, pendingGenerationRow, pendingGeneration.height - pendingGenerationRow);
        pendingGenerationRow = pendingGeneration.height;
        FinishGeneration();
    }

    void CancelStreamingGeneration()
    {
        if (generationRoutine != null) StopCoroutine(generationRoutine);
        generationRoutine = null;
        pendingGeneration = null;
        pendingGenerationRow = 0;
        IsGenerationStreaming = false;
    }

    MapGenerationSnapshot PrepareGeneratedMap(int usedSeed)
    {
        MapGenerationSnapshot result = null;
        var steps = PrepareGeneratedMapSteps(usedSeed, data => result = data, false);
        while (steps.MoveNext()) { }
        return result;
    }
    IEnumerator PrepareGeneratedMapSteps(int usedSeed, System.Action<MapGenerationSnapshot> complete, bool loading)
    {
        if (loading) LoadingProgress.SetStage(1);
        MigrateOreDensitySettings();
        if (!registry || mapWidth <= 0 || mapHeight <= 0)
            throw new System.InvalidOperationException("Map generation requires a registry and positive dimensions.");
        MapGenerationSampler sampler;
        using (LoadingSetupMarker.Auto()) sampler = new MapGenerationSampler(registry, usedSeed, mapHeight, layers, oreDensityCurve,
            oreDensityMultiplierPercent, transitionThickness,
            useOreSettings ? oreSettings ?? System.Array.Empty<OreDistributionSetting>() : null, prepareNoise: !loading);
        if (loading)
        {
            var noise = sampler.PrepareNoiseSteps();
            while (noise.MoveNext()) yield return null;
        }
        for (int y = 0; y < mapHeight; y++)
        {
            var stone = sampler.GetStone(y);
            if (!stone || stone.variants == null || stone.variants.Length == 0 ||
                System.Array.Exists(stone.variants, tile => !tile))
                throw new System.InvalidOperationException("Missing stone variants at depth " + y);
        }
        tilemap = Terrain;
        EnsureOreOverlay();
        EnsureArtifactOverlay();
        EnsureGrassOverlay();

        int offsetX = -mapWidth / 2;
        var altar = AltarChamber;
        if (altar) altar.PrepareGeneration(usedSeed, mapWidth, mapHeight);
        var chamber = altar ? altar.Layout : default;
        var caves = new bool[checked(mapWidth * mapHeight)];
        pendingCaveMask = caves;
        var blocks = new Block[checked(mapWidth * mapHeight)];
        var preparationBudget = new LoadingWorkBudget();
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                var cell = new Vector3Int(x + offsetX, -y, 0);
                int index = y * mapWidth + x;
                blocks[index] = chamber.IsOpen(cell) || caves[index] ? null :
                    chamber.IsShell(cell) ? sampler.GetBaseBlock(x, y) : sampler.GetBlock(x, y);
            }
            if (preparationBudget.Expired || y == mapHeight - 1)
            {
                if (loading) LoadingProgress.Report((y + 1f) / mapHeight);
                yield return null;
                preparationBudget.Restart();
            }
        }
        if (loading) LoadingProgress.SetStage(2);
        var caveSteps = CaveGenerator.GenerateSteps(caveGeneration, usedSeed, mapWidth, mapHeight, caves,
            reserved: (x, y) => chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)),
            progress: loading ? LoadingProgress.Report : null);
        while (caveSteps.MoveNext()) yield return null;
        for (int i = 0; i < blocks.Length; i++) if (caves[i]) blocks[i] = null;
        if (loading) LoadingProgress.SetStage(3);
        var veinSteps = ConnectedOreVeins.GenerateSteps(blocks, mapWidth, mapHeight, usedSeed, sampler,
            GetMinimumVeinSize, (x, y) => caves[y * mapWidth + x] ||
                chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)),
            progress: loading ? LoadingProgress.Report : null);
        while (veinSteps.MoveNext()) yield return null;
        OreRichness[] richness = null;
        var richnessSteps = OreVeins.BuildSteps(blocks, mapWidth, mapHeight, usedSeed, value => richness = value);
        while (richnessSteps.MoveNext()) yield return null;
        if (loading) LoadingProgress.SetStage(4);
        yield return null;
        var terrainTiles = new TileBase[blocks.Length];
        var oreTiles = new TileBase[blocks.Length];
        var artifactTiles = new TileBase[blocks.Length];
        var previousBlocks = new Block[mapWidth];
        var currentBlocks = new Block[mapWidth];
        var previousVariants = new int[mapWidth];
        var currentVariants = new int[mapWidth];
        var variantGroups = new Dictionary<Block,int[]>();
        var placedArtifacts = new Dictionary<ArtifactTile, List<Vector2Int>>();

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                int i = y * mapWidth + x, tileIndex = (mapHeight - 1 - y) * mapWidth + x;
                Block chosen = blocks[i];
                if (!chosen) continue;
                bool layered = chosen.HasOreOverlays;
                Block baseBlock = layered ? sampler.GetBaseBlock(x, y) : chosen;
                bool surfaceDirt = surfaceDirtTile && baseBlock.id == BlockType.Dirt;
                bool firstLayerStone = layerOneTile && baseBlock == layerOneTile.block;
                bool thirdLayerStone = layerThreeTile && baseBlock == layerThreeTile.block;
                bool fourthLayerStone = layerFourTile && baseBlock == layerFourTile.block;
                if (uniformTestStone) baseBlock = uniformTestStone;
                var baseVariants = baseBlock.variants;
                if (baseVariants == null || baseVariants.Length == 0)
                    throw new System.InvalidOperationException("Missing terrain variants for " + chosen.name);
                int left = x > 0 && currentBlocks[x - 1] == baseBlock ? currentVariants[x - 1] : -1;
                int above = previousBlocks[x] == baseBlock ? previousVariants[x] : -1;
                if(!variantGroups.TryGetValue(baseBlock,out var groups))
                {
                    groups=TerrainVariantSelector.VisualGroups(baseVariants);
                    variantGroups.Add(baseBlock,groups);
                }
                int variant = TerrainVariantSelector.Choose(usedSeed, x, y, groups, left, above);
                currentBlocks[x] = baseBlock;
                currentVariants[x] = variant;
                terrainTiles[tileIndex] = baseVariants[variant];
                if (uniformTestStone && uniformTestTile) terrainTiles[tileIndex] = uniformTestTile;
                if (uniformTestStone && surfaceDirt) terrainTiles[tileIndex] = surfaceDirtTile;
                if (uniformTestStone && firstLayerStone) terrainTiles[tileIndex] = layerOneTile;
                if (uniformTestStone && thirdLayerStone) terrainTiles[tileIndex] = layerThreeTile;
                if (uniformTestStone && fourthLayerStone) terrainTiles[tileIndex] = layerFourTile;
                if (!layered)
                {
                    if (chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0))) continue;
                    if (!OreVeins.HasVeinInNeighborhood(blocks, mapWidth, mapHeight, x, y))
                    {
                        var artifact = SelectArtifact(usedSeed, x, y);
                        if (ArtifactPlacement.TryPlace(placedArtifacts, artifact, x, y,
                            artifactMinimumSameTypeDistance))
                            artifactTiles[tileIndex] = artifact;
                    }
                    continue;
                }
                var overlays = chosen.GetOreVariants(richness[i]);
                oreTiles[tileIndex] = overlays[OreVeins.Hash(usedSeed, x, y, 0x5678u) % (uint)overlays.Length];
            }
            (previousBlocks, currentBlocks) = (currentBlocks, previousBlocks);
            (previousVariants, currentVariants) = (currentVariants, previousVariants);
            System.Array.Clear(currentBlocks, 0, currentBlocks.Length);
            if (preparationBudget.Expired || y == mapHeight - 1)
            {
                if (loading) LoadingProgress.Report((y + 1f) / mapHeight);
                yield return null;
                preparationBudget.Restart();
            }
        }

        complete(new MapGenerationSnapshot(usedSeed, mapWidth, mapHeight, offsetX, terrainTiles, oreTiles, artifactTiles));
    }

    public bool[] CreateCaveMask(int usedSeed, int width, int height, UltroniumChamberLayout chamber)
    {
        int offsetX = -width / 2;
        return CaveGenerator.Generate(caveGeneration, usedSeed, width, height,
            reserved: (x, y) => chamber.IsReserved(new Vector3Int(x + offsetX, -y, 0)));
    }

    public bool IsGeneratedCaveCell(Vector3Int cell)
    {
        if (!isGenerated || !Terrain || cell.z != 0) return false;
        int x = cell.x + generatedWidth / 2;
        int depth = -cell.y;
        if (x < 0 || x >= generatedWidth || depth < Mathf.Max(1, caveGeneration.minimumDepth) ||
            depth >= generatedHeight) return false;

        if (generatedCaveMask == null || cachedCaveMaskWidth != generatedWidth ||
            cachedCaveMaskHeight != generatedHeight)
        {
            generatedCaveMask = CreateCaveMask(generatedSeed, generatedWidth, generatedHeight,
                AltarChamber ? AltarChamber.Layout : default);
            cachedCaveMaskWidth = generatedWidth;
            cachedCaveMaskHeight = generatedHeight;
        }

        return generatedCaveMask[depth * generatedWidth + x] && !Terrain.HasTile(cell);
    }

    public ArtifactTile SelectArtifact(int usedSeed, int x, int depth)
    {
        if (depth < Mathf.Max(1, artifactMinimumDepth) || artifactSettings == null || layers == null) return null;
        int layer = -1;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && depth >= layers[i].startDepth) layer = i;
        if (layer < 0) return null;
        double roll = OreVeins.Hash(usedSeed, x, depth, 0xA74Fu) / (double)uint.MaxValue * 100d;
        double cumulative = 0d;
        foreach (var setting in artifactSettings)
        {
            if (setting == null || !setting.tile || setting.layerIndices == null ||
                System.Array.IndexOf(setting.layerIndices, layer) < 0 ||
                float.IsNaN(setting.chancePercent) || float.IsInfinity(setting.chancePercent)) continue;
            cumulative += Mathf.Clamp(setting.chancePercent, 0f, 100f) *
                Mathf.Clamp(artifactDropChanceMultiplier, .1f, 5f);
            if (roll < cumulative) return setting.tile;
        }
        return null;
    }

    void BeginTileApplication()
    {
        applyingGeneratedTiles = true;
        GetComponent<LadderMap>()?.Clear();
        isGenerated = false;
        generatedCaveMask = null;
        cachedCaveMaskWidth = cachedCaveMaskHeight = 0;
        oreOverlay.ClearAllTiles();
        artifactOverlay.ClearAllTiles();
        tilemap.ClearAllTiles();
    }

    void ApplyTileRows(MapGenerationSnapshot data, int firstRow, int rowCount)
    {
        if (rowCount <= 0) return;
        TileBase[] terrain = data.terrainTiles;
        TileBase[] ores = data.oreTiles;
        TileBase[] artifacts = data.artifactTiles;
        BoundsInt bounds;
        if (firstRow == 0 && rowCount == data.height)
        {
            bounds = new BoundsInt(data.offsetX, 1 - data.height, 0, data.width, data.height, 1);
        }
        else
        {
            int count = checked(data.width * rowCount);
            int sourceOffset = checked((data.height - firstRow - rowCount) * data.width);
            if (streamedTerrainRows == null || streamedTerrainRows.Length != count)
            {
                streamedTerrainRows = new TileBase[count];
                streamedOreRows = new TileBase[count];
                streamedArtifactRows = new TileBase[count];
            }
            terrain = streamedTerrainRows;
            ores = streamedOreRows;
            artifacts = streamedArtifactRows;
            System.Array.Copy(data.terrainTiles, sourceOffset, terrain, 0, count);
            System.Array.Copy(data.oreTiles, sourceOffset, ores, 0, count);
            System.Array.Copy(data.artifactTiles, sourceOffset, artifacts, 0, count);
            bounds = new BoundsInt(data.offsetX, 1 - firstRow - rowCount, 0, data.width, rowCount, 1);
        }
        tilemap.SetTilesBlock(bounds, terrain);
        oreOverlay.SetTilesBlock(bounds, ores);
        artifactOverlay.SetTilesBlock(bounds, artifacts);
        for (int y = firstRow; y < firstRow + rowCount; y++)
            for (int x = 0; x < data.width; x++)
            {
                var ore = data.oreTiles[(data.height - 1 - y) * data.width + x] as OreTile;
                if (!ore) continue;
                var cell = new Vector3Int(x + data.offsetX, -y, 0);
                oreOverlay.SetTileFlags(cell, TileFlags.None);
                oreOverlay.SetTransformMatrix(cell, ore.transform);
            }
    }

    void ActivateGeneratedMap(MapGenerationSnapshot data)
    {
        InitializeGeneratedMapState(data);
        Generated?.Invoke();
    }

    void InitializeGeneratedMapState(MapGenerationSnapshot data)
    {
        generatedSeed = data.seed;
        generatedWidth = data.width;
        generatedHeight = data.height;
        generatedCaveMask = pendingCaveMask;
        cachedCaveMaskWidth = data.width;
        cachedCaveMaskHeight = data.height;
        pendingCaveMask = null;
        isGenerated = true;
        applyingGeneratedTiles = false;
        SyncGrassFromTerrain();
        if (!IsGenerationStreaming) GetComponent<DirtSurfaceAppearance>()?.Apply();

    }

    IEnumerator InvokeGeneratedForLoading()
    {
        var handlers = Generated?.GetInvocationList();
        if (handlers == null) yield break;
        foreach (System.Action handler in handlers)
        {
            yield return null;
            handler();
        }
    }

    void FinishGeneration(bool appearanceReady = false)
    {
        bool streamed = IsGenerationStreaming;
        // Streaming already sets the intended rectangular world bounds. A
        // synchronous scan only removes a few empty cells from compressed saves.
        if (!Application.isPlaying)
        {
            tilemap.CompressBounds();
            oreOverlay.CompressBounds();
            artifactOverlay.CompressBounds();
        }
        IsGenerationStreaming = false;
        generationRoutine = null;
        pendingGeneration = null;
        pendingGenerationRow = 0;
        if (streamed && !appearanceReady) GetComponent<DirtSurfaceAppearance>()?.Apply();
#if UNITY_EDITOR
        InitialMapGenerated?.Invoke(this);
#endif
        GenerationCompleted?.Invoke();
    }

    public void BeginSavedMapRestore(int usedSeed, int width, int height)
    {
        CancelStreamingGeneration();
        EnsureOreOverlay(); EnsureArtifactOverlay(); BeginTileApplication();
        generatedSeed = usedSeed; generatedWidth = width; generatedHeight = height;
        mapWidth = width; mapHeight = height; IsGenerationStreaming = true;
    }
    public void FinishSavedMapRestore(TileBase[] restoredTerrain = null)
    {
        var steps = FinishSavedMapRestoreSteps(restoredTerrain);
        while (steps.MoveNext()) { }
    }
    public IEnumerator FinishSavedMapRestoreSteps(TileBase[] restoredTerrain = null)
    {
        generatedCaveMask = new bool[generatedWidth * generatedHeight];
        bool hasSnapshot = restoredTerrain != null && restoredTerrain.Length == generatedCaveMask.Length;
        var budget = new LoadingWorkBudget();
        for (int depth = 0; depth < generatedHeight; depth++)
        {
            for (int x = 0; x < generatedWidth; x++)
                generatedCaveMask[depth * generatedWidth + x] = hasSnapshot
                    ? !restoredTerrain[(generatedHeight - 1 - depth) * generatedWidth + x]
                    : !Terrain.HasTile(new Vector3Int(-generatedWidth / 2 + x, -depth));
            if (budget.Expired) { yield return null; budget.Restart(); }
        }
        cachedCaveMaskWidth = generatedWidth; cachedCaveMaskHeight = generatedHeight;
        isGenerated = true; applyingGeneratedTiles = false;
        UpdateGrassOffset(); SyncGrassFromTerrain();
        if (!LoadingProgress.Active) GetComponent<DirtSurfaceAppearance>()?.Apply();
        yield return InvokeGeneratedForLoading();
        IsGenerationStreaming = false;
        GenerationCompleted?.Invoke();
    }

#if UNITY_EDITOR
    public void RestorePreviewMetadata(int usedSeed, int width, int height)
    {
        generatedSeed = usedSeed; generatedWidth = width; generatedHeight = height;
        isGenerated = true;
        UpdateGrassOffset();
        SyncGrassFromTerrain();
        GetComponent<DirtSurfaceAppearance>()?.Apply();

        Generated?.Invoke();
    }
#endif
}
