using UnityEngine;
using UnityEngine.Tilemaps;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Size")]
    public int mapWidth = 100;
    public int mapHeight = 1000;

    [Header("Generation")]
    public BlockRegistry registry;
    [Range(-1e7f, 1e7f)] public int seed = 0;

    private Tilemap tilemap;
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
        Random.InitState(seed);
        GenerateMap();
    }

    void GenerateMap()
    {
        isGenerated = false;
        tilemap.ClearAllTiles();

        int offsetX = -mapWidth / 2;
        var sampler = new MapGenerationSampler(registry, seed, mapHeight);

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int cell = new Vector3Int(x + offsetX, -y, 0);
                Block chosen = sampler.GetBlock(x, y);
                TileBase tile = registry.GetRandomVariant(chosen);
                if (tile != null)
                    tilemap.SetTile(cell, tile);
            }
        }

        tilemap.CompressBounds();
        generatedSeed = seed;
        generatedWidth = mapWidth;
        generatedHeight = mapHeight;
        isGenerated = true;
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
        Generated?.Invoke();
    }
#endif
}
