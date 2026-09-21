using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(MapGenerator), typeof(TilemapCollider2D))]
public sealed class TerrainColliderChunks : MonoBehaviour
{
    const int ChunkSize = 64;
    const int ChunkRadius = 1;

    MapGenerator map;
    TilemapCollider2D sourceCollider;
    CompositeCollider2D sourceComposite;
    Tilemap[,] chunks;
    BoundsInt sourceBounds;
    PlayerMovement player;
    int loadedCenterX = -1;
    int loadedCenterY = -1;

    void Awake()
    {
        map = GetComponent<MapGenerator>();
        sourceCollider = GetComponent<TilemapCollider2D>();
        sourceComposite = GetComponent<CompositeCollider2D>();
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        sourceCollider.enabled = false;
        if (sourceComposite) sourceComposite.enabled = false;
        map.Generated += Rebuild;
        Tilemap.tilemapTileChanged += OnTilesChanged;
        if (map.IsGenerated) Rebuild();
    }

    void Update()
    {
        if (!Application.isPlaying || chunks == null) return;
        EnsureChunksAroundPlayer();
    }

    void OnDisable()
    {
        if (map) map.Generated -= Rebuild;
        Tilemap.tilemapTileChanged -= OnTilesChanged;
        ClearChunks();
        if (sourceCollider) sourceCollider.enabled = true;
        if (sourceComposite) sourceComposite.enabled = true;
    }

    void Rebuild()
    {
        if (!Application.isPlaying || !map || !map.Terrain) return;
        try { BuildChunks(); }
        catch (System.Exception error)
        {
            ClearChunks();
            sourceCollider.enabled = true;
            if (sourceComposite) sourceComposite.enabled = true;
            Debug.LogException(error, this);
        }
    }

    void BuildChunks()
    {
        ClearChunks();

        var source = map.Terrain;
        sourceBounds = new BoundsInt(-map.GeneratedWidth / 2, 1 - map.GeneratedHeight, 0,
            map.GeneratedWidth, map.GeneratedHeight, 1);
        int columns = Mathf.CeilToInt(sourceBounds.size.x / (float)ChunkSize);
        int rows = Mathf.CeilToInt(sourceBounds.size.y / (float)ChunkSize);
        if (columns == 0 || rows == 0) return;
        chunks = new Tilemap[columns, rows];
        loadedCenterX = -1;
        loadedCenterY = -1;
        EnsureChunksAroundPlayer();

        sourceCollider.enabled = false;
        if (sourceComposite) sourceComposite.enabled = false;
    }

    void EnsureChunksAroundPlayer()
    {
        if (!player) player = FindFirstObjectByType<PlayerMovement>();

        Vector3Int cell;
        if (player)
            cell = map.Terrain.WorldToCell(player.transform.position);
        else
            cell = new Vector3Int(Mathf.FloorToInt(sourceBounds.center.x), sourceBounds.yMax - 1, 0);

        int centerX = Mathf.Clamp((cell.x - sourceBounds.xMin) / ChunkSize, 0, chunks.GetLength(0) - 1);
        int centerY = Mathf.Clamp((cell.y - sourceBounds.yMin) / ChunkSize, 0, chunks.GetLength(1) - 1);
        if (centerX == loadedCenterX && centerY == loadedCenterY) return;

        loadedCenterX = centerX;
        loadedCenterY = centerY;
        int minX = Mathf.Max(0, centerX - ChunkRadius);
        int maxX = Mathf.Min(chunks.GetLength(0) - 1, centerX + ChunkRadius);
        int minY = Mathf.Max(0, centerY - ChunkRadius);
        int maxY = Mathf.Min(chunks.GetLength(1) - 1, centerY + ChunkRadius);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            EnsureChunk(x, y);
    }

    void EnsureChunk(int x, int y)
    {
        if (chunks[x, y]) return;

        var source = map.Terrain;
        int left = sourceBounds.xMin + x * ChunkSize;
        int bottom = sourceBounds.yMin + y * ChunkSize;
        int width = Mathf.Min(ChunkSize, sourceBounds.xMax - left);
        int height = Mathf.Min(ChunkSize, sourceBounds.yMax - bottom);

        var child = new GameObject($"Terrain Collision {x}, {y}");
        child.layer = gameObject.layer;
        child.transform.SetParent(transform.parent, false);
        child.transform.localPosition = transform.localPosition;
        child.transform.localRotation = transform.localRotation;
        child.transform.localScale = transform.localScale;

        var tilemap = child.AddComponent<Tilemap>();
        chunks[x, y] = tilemap;
        tilemap.tileAnchor = source.tileAnchor;
        tilemap.orientation = source.orientation;
        tilemap.orientationMatrix = source.orientationMatrix;

        var body = child.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var composite = child.AddComponent<CompositeCollider2D>();
        if (sourceComposite)
        {
            composite.geometryType = sourceComposite.geometryType;
            composite.sharedMaterial = sourceComposite.sharedMaterial;
            composite.isTrigger = sourceComposite.isTrigger;
            composite.vertexDistance = sourceComposite.vertexDistance;
            composite.offsetDistance = sourceComposite.offsetDistance;
            composite.edgeRadius = sourceComposite.edgeRadius;
        }
        var collider = child.AddComponent<TilemapCollider2D>();
        collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        collider.sharedMaterial = sourceCollider.sharedMaterial;
        collider.isTrigger = sourceCollider.isTrigger;
        collider.extrusionFactor = sourceCollider.extrusionFactor;
        collider.maximumTileChangeCount = 256;

        var area = new BoundsInt(left, bottom, 0, width, height, 1);
        tilemap.SetTilesBlock(area, source.GetTilesBlock(area));
    }

    void OnTilesChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if (chunks == null || source != map.Terrain || changes == null) return;
        foreach (var change in changes)
        {
            var cell = change.position;
            int x = (cell.x - sourceBounds.xMin) / ChunkSize;
            int y = (cell.y - sourceBounds.yMin) / ChunkSize;
            if (cell.x < sourceBounds.xMin || cell.y < sourceBounds.yMin ||
                x < 0 || y < 0 || x >= chunks.GetLength(0) || y >= chunks.GetLength(1)) continue;
            var chunk = chunks[x, y];
            if (!chunk) continue;
            var tile = source.GetTile(cell);
            if (chunk.GetTile(cell) != tile) chunk.SetTile(cell, tile);
        }
    }

    void ClearChunks()
    {
        if (chunks == null) return;
        foreach (var chunk in chunks)
            if (chunk)
            {
                if (Application.isPlaying) Destroy(chunk.gameObject);
                else DestroyImmediate(chunk.gameObject);
        }
        chunks = null;
        loadedCenterX = -1;
        loadedCenterY = -1;
    }
}
