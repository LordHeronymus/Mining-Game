using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(OreOverlayAppearance), typeof(DirtSurfaceAppearance))]
public class MapGenerator : MonoBehaviour
{
    [Header("Map Size")]
    public int mapWidth = 100;
    public int mapHeight = 1000;

    [Header("Generation")]
    public BlockRegistry registry;
    [Range(-1e7f, 1e7f)] public int seed = 0;
    [Range(.5f, 3f), InspectorName("Erzgröße (×)")] public float oreScale = 1.35f;

    [InspectorName("Erzanteil (%) nach Tiefe (0–1)")]
    public AnimationCurve oreDensityByDepth = AnimationCurve.Linear(0f, 5f, 1f, 50f);

    [Header("Layers")]
    public MapLayer[] layers;

    private Tilemap tilemap;
    [SerializeField] Tilemap oreOverlay;
    public Tilemap Terrain => tilemap ? tilemap : tilemap = GetComponent<Tilemap>();
    public Tilemap OreOverlay => oreOverlay;
    [SerializeField, HideInInspector] bool isGenerated;
    [SerializeField, HideInInspector] int generatedSeed, generatedWidth, generatedHeight;
    public bool IsGenerated => isGenerated;
    public int ActiveSeed => isGenerated ? generatedSeed : seed;
    public int GeneratedWidth => isGenerated ? generatedWidth : mapWidth;
    public int GeneratedHeight => isGenerated ? generatedHeight : mapHeight;
    public event System.Action Generated;
#if UNITY_EDITOR
    public static event System.Action<MapGenerator> InitialMapGenerated;
#endif

    void Awake()
    {
        tilemap = GetComponent<Tilemap>();
        isGenerated = false;
    }

    void Start()
    {
        if (seed == 0) seed = Random.Range(-(int)1e7, (int)1e7);
        GenerateMap();
    }

    void OnEnable() => Tilemap.tilemapTileChanged += TerrainChanged;
    void OnDisable() => Tilemap.tilemapTileChanged -= TerrainChanged;

    void TerrainChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (source != Terrain || !oreOverlay || changes == null) return;
        foreach (var change in changes)
            if (!source.HasTile(change.position)) oreOverlay.SetTile(change.position, null);
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
        if (!Terrain.HasTile(cell)) return false;
        if (oreOverlay) oreOverlay.SetTile(cell, null);
        Terrain.SetTile(cell, null);
        GetComponent<MapLighting>()?.NotifyTileChanged(cell);
        return true;
    }

    public void GenerateMap()
    {
        if (!registry || mapWidth <= 0 || mapHeight <= 0)
            throw new System.InvalidOperationException("Map generation requires a registry and positive dimensions.");
        var sampler = new MapGenerationSampler(registry, seed, mapHeight, layers, oreDensityByDepth);
        for (int y = 0; y < mapHeight; y++)
        {
            var stone = sampler.GetStone(y);
            if (!stone || stone.variants == null || stone.variants.Length == 0 ||
                System.Array.Exists(stone.variants, tile => !tile))
                throw new System.InvalidOperationException("Missing stone variants at depth " + y);
        }
        tilemap = Terrain;
        EnsureOreOverlay();
        isGenerated = false;
        oreOverlay.ClearAllTiles();
        tilemap.ClearAllTiles();

        int offsetX = -mapWidth / 2;
        var blocks = new Block[checked(mapWidth * mapHeight)];
        for (int y = 0; y < mapHeight; y++)
            for (int x = 0; x < mapWidth; x++) blocks[y * mapWidth + x] = sampler.GetBlock(x, y);
        var richness = OreVeins.Build(blocks, mapWidth, mapHeight, seed);
        var terrainTiles = new TileBase[blocks.Length];
        var oreTiles = new TileBase[blocks.Length];

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                int i = y * mapWidth + x, tileIndex = (mapHeight - 1 - y) * mapWidth + x;
                Block chosen = blocks[i];
                if (!chosen) continue;
                bool layered = chosen.HasOreOverlays;
                var baseVariants = (layered ? sampler.GetBaseBlock(x, y) : chosen).variants;
                if (baseVariants == null || baseVariants.Length == 0)
                    throw new System.InvalidOperationException("Missing terrain variants for " + chosen.name);
                terrainTiles[tileIndex] = baseVariants[OreVeins.Hash(seed, x, y, 0x1234u) % (uint)baseVariants.Length];
                if (!layered) continue;
                var overlays = chosen.GetOreVariants(richness[i]);
                oreTiles[tileIndex] = overlays[OreVeins.Hash(seed, x, y, 0x5678u) % (uint)overlays.Length];
            }
        }
        var bounds = new BoundsInt(offsetX, 1 - mapHeight, 0, mapWidth, mapHeight, 1);
        tilemap.SetTilesBlock(bounds, terrainTiles);
        oreOverlay.SetTilesBlock(bounds, oreTiles);
        for (int y = 0; y < mapHeight; y++)
            for (int x = 0; x < mapWidth; x++)
            {
                if (!oreTiles[(mapHeight - 1 - y) * mapWidth + x]) continue;
                var cell = new Vector3Int(x + offsetX, -y, 0);
                int turns = (int)(OreVeins.Hash(seed, x, y, 0x9abcu) % 4);
                oreOverlay.SetTileFlags(cell, TileFlags.None);
                var ore = (OreTile)oreTiles[(mapHeight - 1 - y) * mapWidth + x];
                oreOverlay.SetTransformMatrix(cell, Matrix4x4.Rotate(Quaternion.Euler(0, 0, turns * 90)) * ore.transform);
            }

        tilemap.CompressBounds();
        oreOverlay.CompressBounds();
        generatedSeed = seed;
        generatedWidth = mapWidth;
        generatedHeight = mapHeight;
        isGenerated = true;
        GetComponent<DirtSurfaceAppearance>()?.Apply();

#if UNITY_EDITOR
        InitialMapGenerated?.Invoke(this);
#endif
        Generated?.Invoke();
    }

#if UNITY_EDITOR
    public void RestorePreviewMetadata(int usedSeed, int width, int height)
    {
        generatedSeed = usedSeed; generatedWidth = width; generatedHeight = height;
        isGenerated = true;
        GetComponent<DirtSurfaceAppearance>()?.Apply();

        Generated?.Invoke();
    }
#endif
}
