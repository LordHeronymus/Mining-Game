using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Size")]
    public int mapWidth = 100;
    public int mapHeight = 1000;

    [Header("Generation")]
    public BlockRegistry registry;
    [Range(-1e7f, 1e7f)]public int seed = 0;

    private Tilemap tilemap;

    Dictionary<Block, float[]> cdfs = new();  // pro Block: kumulative Verteilung (0..1)
    const int BINS = 256;

    void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    void Start()
    {
        if (seed == 0) seed = Random.Range(-(int)1e7, (int)1e7);
        Random.InitState(seed);
        BuildCDFs();
        GenerateMap();
    }

    void GenerateMap()
    {
        tilemap.ClearAllTiles();

        int offsetX = -mapWidth / 2;
        var earth = registry.GetById(BlockType.Stone);

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int cell = new Vector3Int(x + offsetX, -y, 0);

                // Tiefe normalisieren (0 = Oberfläche, 1 = Boden)
                float depthNorm = (float)y / mapHeight;

                Block chosen = earth; // Default Earth

                // Alle Blöcke prüfen, die Noise-Spawn nutzen
                foreach (var block in registry.blocks)
                {
                    if (block == null || !block.spawnWithNoise)
                        continue;

                    // 1. Perlin Noise mit Blockparametern
                    float nx = (x + seed + block.noiseSeedOffset) * block.noiseScale;
                    float ny = (y + seed + block.noiseSeedOffset) * block.noiseScale;
                    float noise = Mathf.PerlinNoise(nx, ny);

                    float rarity = block.rarityCurve != null
                        ? block.rarityCurve.Evaluate(depthNorm) : 0;

                    // 3. Threshold aus Rarity ableiten
                    float q = 1f - Mathf.Clamp01(rarity);
                    float threshold = Quantile(block, q);

                    // 4. Entscheiden, ob Block platziert wird
                    if (noise > threshold)
                    {
                        chosen = block;
                        break; // dieser Block gewinnt → kein weiterer Check nötig
                    }
                }

                // Setze das Tile
                TileBase tile = registry.GetRandomVariant(chosen);
                if (tile != null)
                    tilemap.SetTile(cell, tile);
            }
        }

        tilemap.CompressBounds();
    }

    void BuildCDFs()
    {
        foreach (var b in registry.blocks)
        {
            if (b == null || !b.spawnWithNoise) continue;
            cdfs[b] = BuildCDFFor(b);
        }
    }

    float[] BuildCDFFor(Block b)
    {
        int w = 256, h = 256;
        var hist = new int[BINS];
        var cdf = new float[BINS];

        // Stichprobe vom Noise-Raum des Blocks (seed/scale/offset beachten)
        for (int ix = 0; ix < w; ix++)
            for (int iy = 0; iy < h; iy++)
            {
                float nx = (ix + seed + b.noiseSeedOffset) * b.noiseScale;
                float ny = (iy + seed + b.noiseSeedOffset) * b.noiseScale;
                float v = Mathf.PerlinNoise(nx, ny);                  // 0..1, NICHT uniform
                int bin = Mathf.Clamp(Mathf.FloorToInt(v * BINS), 0, BINS - 1);
                hist[bin]++;
            }

        // kumulativ (CDF)
        int total = w * h, run = 0;
        for (int i = 0; i < BINS; i++) { run += hist[i]; cdf[i] = (float)run / total; }
        return cdf;
    }

    float Quantile(Block b, float q)  // q in [0,1]
    {
        var cdf = cdfs[b];
        int i = System.Array.FindIndex(cdf, v => v >= q);
        if (i < 0) i = BINS - 1;
        return (i + 0.5f) / BINS;
    }
}
