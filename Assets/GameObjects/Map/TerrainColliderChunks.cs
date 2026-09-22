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
    TerrainCollisionShape shapes;
    UniformStoneAppearance appearance;
    int loadedCenterX = -1;
    int loadedCenterY = -1;

    void Awake()
    {
        map = GetComponent<MapGenerator>();
        sourceCollider = GetComponent<TilemapCollider2D>();
        sourceComposite = GetComponent<CompositeCollider2D>();
        appearance=GetComponent<UniformStoneAppearance>();
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
        bool masked=appearance&&appearance.useTerrainMasks;
        if(masked!=(shapes!=null)||(shapes!=null&&!shapes.Matches)){Rebuild();return;}
        if (!EnsureChunksAroundPlayer()) BuildOneNeighborChunk();
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
        if(appearance&&appearance.useTerrainMasks)shapes=new TerrainCollisionShape(appearance,source);
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

    bool EnsureChunksAroundPlayer()
    {
        if (!player) player = FindFirstObjectByType<PlayerMovement>();

        Vector3Int cell;
        if (player)
            cell = map.Terrain.WorldToCell(player.transform.position);
        else
            cell = new Vector3Int(Mathf.FloorToInt(sourceBounds.center.x), sourceBounds.yMax - 1, 0);

        int centerX = Mathf.Clamp((cell.x - sourceBounds.xMin) / ChunkSize, 0, chunks.GetLength(0) - 1);
        int centerY = Mathf.Clamp((cell.y - sourceBounds.yMin) / ChunkSize, 0, chunks.GetLength(1) - 1);
        if (centerX == loadedCenterX && centerY == loadedCenterY) return false;

        loadedCenterX = centerX;
        loadedCenterY = centerY;
        EnsureChunk(centerX, centerY);
        return true;
    }

    void BuildOneNeighborChunk()
    {
        if (loadedCenterX < 0 || loadedCenterY < 0) return;
        int minX = Mathf.Max(0, loadedCenterX - ChunkRadius);
        int maxX = Mathf.Min(chunks.GetLength(0) - 1, loadedCenterX + ChunkRadius);
        int minY = Mathf.Max(0, loadedCenterY - ChunkRadius);
        int maxY = Mathf.Min(chunks.GetLength(1) - 1, loadedCenterY + ChunkRadius);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            if (!chunks[x, y])
            {
                EnsureChunk(x, y);
                return;
            }
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

        PopulateChunk(tilemap);
    }

    void PopulateChunk(Tilemap chunk)
    {
        var area=sourceBounds;
        // Resolve the chunk bounds from the owning grid, never from object names.
        int cx=0,cy=0;
        for(int y=0;y<chunks.GetLength(1);y++)for(int x=0;x<chunks.GetLength(0);x++)if(chunks[x,y]==chunk){cx=x;cy=y;}
        int left=sourceBounds.xMin+cx*ChunkSize,bottom=sourceBounds.yMin+cy*ChunkSize;
        area=new BoundsInt(left,bottom,0,Mathf.Min(ChunkSize,sourceBounds.xMax-left),Mathf.Min(ChunkSize,sourceBounds.yMax-bottom),1);
        var tiles=map.Terrain.GetTilesBlock(area);
        var paths=new System.Collections.Generic.List<Vector2[]>();
        for(int i=0;i<tiles.Length;i++)
        {
            var cell=new Vector3Int(left+i%area.size.x,bottom+i/area.size.x,0);
            var outline=shapes?.Get(cell,tiles[i]);if(outline==null)continue;
            var path=new Vector2[outline.Length];
            for(int j=0;j<path.Length;j++)path[j]=chunk.transform.InverseTransformPoint(map.Terrain.CellToWorld(cell)+Vector3.Scale(outline[j],map.Terrain.layoutGrid.cellSize));
            paths.Add(path);tiles[i]=null;
        }
        var polygon=chunk.GetComponent<PolygonCollider2D>();
        if(!polygon){polygon=chunk.gameObject.AddComponent<PolygonCollider2D>();polygon.compositeOperation=Collider2D.CompositeOperation.Merge;polygon.sharedMaterial=sourceCollider.sharedMaterial;polygon.isTrigger=sourceCollider.isTrigger;}
        polygon.pathCount=paths.Count;for(int i=0;i<paths.Count;i++)polygon.SetPath(i,paths[i]);
        chunk.SetTilesBlock(area,tiles);chunk.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
    }

    void OnTilesChanged(Tilemap source, Tilemap.SyncTile[] changes)
    {
        if(chunks==null||source!=map.Terrain||changes==null)return;
        var dirty=new System.Collections.Generic.HashSet<Tilemap>();
        foreach(var change in changes)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
        {
            var cell=change.position+new Vector3Int(dx,dy,0);
            if(!sourceBounds.Contains(cell))continue;
            var chunk=chunks[(cell.x-sourceBounds.xMin)/ChunkSize,(cell.y-sourceBounds.yMin)/ChunkSize];
            if(chunk)dirty.Add(chunk);
        }
        foreach(var chunk in dirty)PopulateChunk(chunk);
    }
    void ClearChunks()
    {
        if (chunks == null) {shapes?.Dispose();shapes=null;return;}
        foreach (var chunk in chunks)
            if (chunk)
            {
                if (Application.isPlaying) Destroy(chunk.gameObject);
                else DestroyImmediate(chunk.gameObject);
        }
        chunks = null;
        loadedCenterX = -1;
        loadedCenterY = -1;
        shapes?.Dispose();shapes=null;
    }
}


