using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(MapGenerator), typeof(TilemapCollider2D))]
public sealed class TerrainColliderChunks : MonoBehaviour
{
    const int ChunkSize = 64;

    MapGenerator map;
    TilemapCollider2D sourceCollider;
    CompositeCollider2D sourceComposite;
    Tilemap[,] chunks;
    BoundsInt sourceBounds;

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
        sourceBounds = source.cellBounds;
        int columns = Mathf.CeilToInt(sourceBounds.size.x / (float)ChunkSize);
        int rows = Mathf.CeilToInt(sourceBounds.size.y / (float)ChunkSize);
        if (columns == 0 || rows == 0) return;
        chunks = new Tilemap[columns, rows];

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
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

        sourceCollider.enabled = false;
        if (sourceComposite) sourceComposite.enabled = false;
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
    }
}
