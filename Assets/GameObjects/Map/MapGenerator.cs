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
    [SerializeField] TileBase[] grassVariants;
    [SerializeField] bool continuousGrassStrip;

    [InspectorName("Erzverteilung nach Tiefe")]
    public AnimationCurve oreDensityCurve = AnimationCurve.Linear(0f, .1f, 1f, 1f);
    [Range(0f, 100f), InspectorName("Multiplikator (%)")]
    public float oreDensityMultiplierPercent = 50f;
    [Min(0), InspectorName("Oberflächenanstieg (Blöcke)")]
    public int surfaceOreRampDepth = 10;
    [InspectorName("Kurve Oberflächenanstieg")]
    public AnimationCurve surfaceOreRampCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Range(1f, 100f), InspectorName("Adergröße im Anfangsbereich (%)")]
    public float surfaceOreVeinSizePercent = 50f;
    [InspectorName("Erz-Übergangskurve")]
    public AnimationCurve oreTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Min(1), InspectorName("Erz-Übergang (Blöcke)")]
    public int oreTransitionDepth = 100;
    [InspectorName("Adergröße im Erz-Übergang")]
    public AnimationCurve oreVeinSizeCurve = AnimationCurve.Linear(0f, .5f, 1f, 1f);
    [Min(1), InspectorName("Minimale Adergröße (Blöcke)")]
    public int minimumOreVeinSize = 4;
    [SerializeField, HideInInspector, FormerlySerializedAs("oreDensityByDepth")]
    AnimationCurve legacyOreDensityByDepth;

    [Header("Layers")]
    public MapLayer[] layers;
    [Header("Terrain Test")]
    public Block uniformTestStone;
    public StoneTestTile uniformTestTile;
    public StoneTestTile surfaceDirtTile;
    public StoneTestTile layerOneTile;
    public StoneTestTile layerThreeTile;

    private Tilemap tilemap;
    [SerializeField] Tilemap oreOverlay;
    [SerializeField] Tilemap grassOverlay;
    public Tilemap Terrain => tilemap ? tilemap : tilemap = GetComponent<Tilemap>();
    public Tilemap OreOverlay => oreOverlay;
    public Tilemap GrassOverlay => grassOverlay;
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

    public sealed class MapGenerationSnapshot
    {
        public readonly int seed;
        public readonly int width;
        public readonly int height;
        public readonly int offsetX;
        public readonly TileBase[] terrainTiles;
        public readonly TileBase[] oreTiles;

        public MapGenerationSnapshot(int seed, int width, int height, int offsetX,
            TileBase[] terrainTiles, TileBase[] oreTiles)
        {
            this.seed = seed;
            this.width = width;
            this.height = height;
            this.offsetX = offsetX;
            this.terrainTiles = terrainTiles;
            this.oreTiles = oreTiles;
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
        MigrateSeedMode();
        MigrateOreDensitySettings();
        tilemap = GetComponent<Tilemap>();
        isGenerated = false;
        if (Application.isPlaying && GetComponent<TilemapCollider2D>() && !GetComponent<TerrainColliderChunks>())
            gameObject.AddComponent<TerrainColliderChunks>();
    }

    void OnValidate()
    {
        MigrateSeedMode();
        MigrateOreDensitySettings();
        UpdateGrassOffset();
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

    void Start()
    {
        if (Application.isPlaying)
            generationRoutine = StartCoroutine(GenerateMapInPlay());
        else
            GenerateMap();
    }

    void OnEnable() => Tilemap.tilemapTileChanged += TerrainChanged;
    void OnDisable() => Tilemap.tilemapTileChanged -= TerrainChanged;

    void TerrainChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (source != Terrain || changes == null) return;
        foreach (var change in changes)
            if (!source.HasTile(change.position))
            {
                if (oreOverlay) oreOverlay.SetTile(change.position, null);
                if (grassOverlay && change.position.y == 0) grassOverlay.SetTile(change.position, null);
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

    void UpdateGrassOffset()
    {
        if (!grassOverlay) return;
        var position = grassOverlay.transform.localPosition;
        position.y = grassYOffset;
        grassOverlay.transform.localPosition = position;
    }

    public void SetGrassVariants(TileBase[] variants, bool continuousStrip = false)
    { grassVariants = variants; continuousGrassStrip = continuousStrip; }

    public void SyncGrassFromTerrain()
    {
        var overlay = EnsureGrassOverlay();
        overlay.ClearAllTiles();
        int width = GeneratedWidth;
        if (width <= 0 || grassVariants == null || grassVariants.Length == 0) return;
        var tiles = new TileBase[width];
        int left = -width / 2;
        for (int x = 0; x < width; x++)
        {
            var cell = new Vector3Int(left + x, 0, 0);
            if (Terrain.HasTile(cell))
                tiles[x] = grassVariants[continuousGrassStrip
                    ? (x + (int)(OreVeins.Hash(ActiveSeed, 0, 0, 0x6A55u) % (uint)grassVariants.Length)) % grassVariants.Length
                    : (int)(OreVeins.Hash(ActiveSeed, x, 0, 0x6A55u) % (uint)grassVariants.Length)];
        }
        overlay.SetTilesBlock(new BoundsInt(left, 0, 0, width, 1, 1), tiles);
        overlay.CompressBounds();
    }

    public OreTile GetOreAt(Vector3Int cell) => Terrain.HasTile(cell) && oreOverlay
        ? oreOverlay.GetTile<OreTile>(cell) : null;

    public Block GetBlockAt(Vector3Int cell)
    {
        var terrain = Terrain.GetTile(cell);
        if (!terrain) return null;
        var ore = oreOverlay ? oreOverlay.GetTile<OreTile>(cell) : null;
        return ore && ore.block ? ore.block : registry ? registry.FromTile(terrain) : null;
    }

    public bool RemoveBlock(Vector3Int cell)
    {
        if (IsSurfaceCellProtected(cell)) return false;
        if (!Terrain.HasTile(cell)) return false;
        if (oreOverlay) oreOverlay.SetTile(cell, null);
        if (grassOverlay && cell.y == 0) grassOverlay.SetTile(cell, null);
        Terrain.SetTile(cell, null);
        GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        return true;
    }

    public void GenerateMap(int? seedOverride = null)
    {
        CancelStreamingGeneration();
        var data = PrepareGeneratedMap(seedOverride ?? ChooseGenerationSeed());
#if UNITY_EDITOR
        InitialMapPrepared?.Invoke(this, data);
#endif
        BeginTileApplication();
        ApplyTileRows(data, 0, data.height);
        tilemap.CompressBounds();
        oreOverlay.CompressBounds();
        ActivateGeneratedMap(data);
        FinishGeneration();
    }

    IEnumerator GenerateMapInPlay()
    {
        var data = PrepareGeneratedMap(ChooseGenerationSeed());
#if UNITY_EDITOR
        InitialMapPrepared?.Invoke(this, data);
#endif
        GetComponent<MapLighting>()?.PrepareForStreamingGeneration(data);
        BeginTileApplication();
        int initialRows = Mathf.Min(InitialGenerationRows, data.height);
        ApplyTileRows(data, 0, initialRows);

        IsGenerationStreaming = initialRows < data.height;
        pendingGeneration = data;
        pendingGenerationRow = initialRows;
        ActivateGeneratedMap(data);

        if (!IsGenerationStreaming)
        {
            FinishGeneration();
            yield break;
        }

        yield return null;
        while (IsGenerationStreaming && pendingGeneration == data && pendingGenerationRow < data.height)
        {
            int rows = Mathf.Min(StreamingRowsPerFrame, data.height - pendingGenerationRow);
            ApplyTileRows(data, pendingGenerationRow, rows);
            pendingGenerationRow += rows;
            yield return null;
        }

        if (IsGenerationStreaming && pendingGeneration == data)
            FinishGeneration();
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
        MigrateOreDensitySettings();
        if (!registry || mapWidth <= 0 || mapHeight <= 0)
            throw new System.InvalidOperationException("Map generation requires a registry and positive dimensions.");
        var sampler = new MapGenerationSampler(registry, usedSeed, mapHeight, layers, oreDensityCurve,
            oreDensityMultiplierPercent, transitionThickness, oreTransitionCurve, oreTransitionDepth,
            oreVeinSizeCurve, surfaceOreRampDepth, surfaceOreRampCurve, surfaceOreVeinSizePercent);
        for (int y = 0; y < mapHeight; y++)
        {
            var stone = sampler.GetStone(y);
            if (!stone || stone.variants == null || stone.variants.Length == 0 ||
                System.Array.Exists(stone.variants, tile => !tile))
                throw new System.InvalidOperationException("Missing stone variants at depth " + y);
        }
        tilemap = Terrain;
        EnsureOreOverlay();
        EnsureGrassOverlay();

        int offsetX = -mapWidth / 2;
        var blocks = new Block[checked(mapWidth * mapHeight)];
        for (int y = 0; y < mapHeight; y++)
            for (int x = 0; x < mapWidth; x++) blocks[y * mapWidth + x] = sampler.GetBlock(x, y);
        OreVeins.PruneSmallVeins(blocks, mapWidth, mapHeight, minimumOreVeinSize, sampler.GetBaseBlock);
        var richness = OreVeins.Build(blocks, mapWidth, mapHeight, usedSeed);
        var terrainTiles = new TileBase[blocks.Length];
        var oreTiles = new TileBase[blocks.Length];
        var previousBlocks = new Block[mapWidth];
        var currentBlocks = new Block[mapWidth];
        var previousVariants = new int[mapWidth];
        var currentVariants = new int[mapWidth];
        var variantGroups = new Dictionary<Block,int[]>();

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
                if (!layered) continue;
                var overlays = chosen.GetOreVariants(richness[i]);
                oreTiles[tileIndex] = overlays[OreVeins.Hash(usedSeed, x, y, 0x5678u) % (uint)overlays.Length];
            }
            (previousBlocks, currentBlocks) = (currentBlocks, previousBlocks);
            (previousVariants, currentVariants) = (currentVariants, previousVariants);
            System.Array.Clear(currentBlocks, 0, currentBlocks.Length);
        }

        return new MapGenerationSnapshot(usedSeed, mapWidth, mapHeight, offsetX, terrainTiles, oreTiles);
    }

    void BeginTileApplication()
    {
        GetComponent<LadderMap>()?.Clear();
        isGenerated = false;
        oreOverlay.ClearAllTiles();
        tilemap.ClearAllTiles();
    }

    void ApplyTileRows(MapGenerationSnapshot data, int firstRow, int rowCount)
    {
        if (rowCount <= 0) return;
        TileBase[] terrain = data.terrainTiles;
        TileBase[] ores = data.oreTiles;
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
            }
            terrain = streamedTerrainRows;
            ores = streamedOreRows;
            System.Array.Copy(data.terrainTiles, sourceOffset, terrain, 0, count);
            System.Array.Copy(data.oreTiles, sourceOffset, ores, 0, count);
            bounds = new BoundsInt(data.offsetX, 1 - firstRow - rowCount, 0, data.width, rowCount, 1);
        }
        tilemap.SetTilesBlock(bounds, terrain);
        oreOverlay.SetTilesBlock(bounds, ores);
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
        generatedSeed = data.seed;
        generatedWidth = data.width;
        generatedHeight = data.height;
        isGenerated = true;
        SyncGrassFromTerrain();
        GetComponent<DirtSurfaceAppearance>()?.Apply(!IsGenerationStreaming);

        Generated?.Invoke();
    }

    void FinishGeneration()
    {
        bool streamed = IsGenerationStreaming;
        tilemap.CompressBounds();
        oreOverlay.CompressBounds();
        IsGenerationStreaming = false;
        generationRoutine = null;
        pendingGeneration = null;
        pendingGenerationRow = 0;
        if (streamed) GetComponent<DirtSurfaceAppearance>()?.Apply();
#if UNITY_EDITOR
        InitialMapGenerated?.Invoke(this);
#endif
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
