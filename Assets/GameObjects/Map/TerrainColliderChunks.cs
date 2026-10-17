using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Unity.Collections;

[RequireComponent(typeof(MapGenerator), typeof(TilemapCollider2D))]
[DefaultExecutionOrder(-100)]
public sealed class TerrainColliderChunks : MonoBehaviour
{
    static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("Mining.ColliderChunk");
    const int ChunkSize = 16;
    const int ChunkRadius = 1;
    const float AnimalBodyRefreshInterval = .25f;

    MapGenerator map;
    TilemapCollider2D sourceCollider;
    CompositeCollider2D sourceComposite;
    Tilemap[,] chunks;
    readonly Dictionary<Tilemap, Vector2Int> chunkCoordinates = new Dictionary<Tilemap, Vector2Int>();
    readonly HashSet<Tilemap> dirtyChunks = new HashSet<Tilemap>();
    readonly List<Rigidbody2D> animalBodies = new List<Rigidbody2D>();
    BoundsInt sourceBounds;
    PlayerMovement player;
    UniformStoneAppearance appearance;
    float appliedInset = -1f;
    float animalBodyRefreshTimer;
    int loadedCenterX = -1;
    int loadedCenterY = -1;

    void Awake()
    {
        map = GetComponent<MapGenerator>();
        sourceCollider = GetComponent<TilemapCollider2D>();
        sourceComposite = GetComponent<CompositeCollider2D>();
        appearance = GetComponent<UniformStoneAppearance>();
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        sourceCollider.enabled = false;
        if (sourceComposite) sourceComposite.enabled = false;
        map.Generated += Rebuild;
        Tilemap.tilemapPositionsChanged += OnTilesChanged;
        if (map.IsGenerated) Rebuild();
    }

    void Update()
    {
        if (!Application.isPlaying || chunks == null) return;
        if (!Mathf.Approximately(appliedInset, CurrentInset)) { Rebuild(); return; }
        if (!EnsureChunksAroundPlayer()) BuildOneNeighborChunk();
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || chunks == null) return;
        // Apply queued terrain changes before movement queries and the physics step.
        FlushDirtyChunks();
        animalBodyRefreshTimer -= Time.fixedDeltaTime;
        if (animalBodyRefreshTimer <= 0f)
        {
            RefreshAnimalBodies();
            animalBodyRefreshTimer = AnimalBodyRefreshInterval;
        }

        foreach (var body in animalBodies)
            if (body && body.gameObject.activeInHierarchy)
                EnsureChunksAroundPosition(body.position);
    }

    void LateUpdate()
    {
        if (Application.isPlaying && chunks != null) FlushDirtyChunks();
    }

    void FlushDirtyChunks()
    {
        foreach (var chunk in dirtyChunks)
            if (chunk) PopulateChunk(chunk);
        dirtyChunks.Clear();
    }

    void OnDisable()
    {
        if (map) map.Generated -= Rebuild;
        Tilemap.tilemapPositionsChanged -= OnTilesChanged;
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
        appliedInset = CurrentInset;
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

    void RefreshAnimalBodies()
    {
        animalBodies.Clear();
        int animalLayer = LayerMask.NameToLayer("Player");
        if (animalLayer < 0) return;

        foreach (var body in FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None))
        {
            if (!body || body.bodyType != RigidbodyType2D.Dynamic || body.gameObject.layer != animalLayer) continue;

            var critters = body.GetComponentInParent<SurfaceCritters>();
            if (critters)
            {
                if (critters.map == map) animalBodies.Add(body);
                continue;
            }

            var rabbit = body.GetComponent<SurfaceRabbit>();
            if (rabbit && rabbit.map == map) animalBodies.Add(body);
        }
    }

    public void EnsureAnimalCollisionAt(Vector2 worldPosition)
    {
        if (!Application.isPlaying || chunks == null || !map || !map.Terrain) return;
        EnsureChunksAroundPosition(worldPosition);
        FlushDirtyChunks();
        Physics2D.SyncTransforms();
    }

    void EnsureChunksAroundPosition(Vector2 worldPosition)
    {
        var cell = map.Terrain.WorldToCell(worldPosition);
        int centerX = Mathf.Clamp((cell.x - sourceBounds.xMin) / ChunkSize, 0, chunks.GetLength(0) - 1);
        int centerY = Mathf.Clamp((cell.y - sourceBounds.yMin) / ChunkSize, 0, chunks.GetLength(1) - 1);
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
        chunkCoordinates[tilemap] = new Vector2Int(x, y);
        tilemap.tileAnchor = source.tileAnchor;
        tilemap.orientation = source.orientation;
        tilemap.orientationMatrix = source.orientationMatrix;

        var body = child.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var composite = child.AddComponent<CompositeCollider2D>();
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
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

    // Reused by every chunk rebuild (main thread only) so a mined block does not allocate per edge tile.
    readonly List<Vector2[]> pathPool = new List<Vector2[]>();
    TileBase[] chunkTiles = new TileBase[ChunkSize * ChunkSize];

    void PopulateChunk(Tilemap chunk)
    {
        using var profile = UpdateMarker.Auto();
        if (!chunkCoordinates.TryGetValue(chunk, out var coordinates)) return;
        int cx=coordinates.x,cy=coordinates.y;
        int left=sourceBounds.xMin+cx*ChunkSize,bottom=sourceBounds.yMin+cy*ChunkSize;
        var area=new BoundsInt(left,bottom,0,Mathf.Min(ChunkSize,sourceBounds.xMax-left),Mathf.Min(ChunkSize,sourceBounds.yMax-bottom),1);
        var source=map.Terrain;
        int count=area.size.x*area.size.y;
        if(chunkTiles.Length!=count)chunkTiles=new TileBase[count];
        var tiles=chunkTiles;
        source.GetTilesBlockNonAlloc(area,tiles);
        int pathCount=0;
        if(appliedInset>0f)
        {
            var cellSize=source.layoutGrid.cellSize;
            for(int i=0;i<tiles.Length;i++)
            {
                if(!tiles[i])continue;
                var cell=new Vector3Int(left+i%area.size.x,bottom+i/area.size.x,0);
                bool openLeft=!source.HasTile(cell+Vector3Int.left);
                bool openRight=!source.HasTile(cell+Vector3Int.right);
                bool openBottom=!source.HasTile(cell+Vector3Int.down);
                bool openTop=cell.y!=0&&!source.HasTile(cell+Vector3Int.up);
                if(!openLeft&&!openRight&&!openBottom&&!openTop)continue;
                float inset=map.AltarChamber && map.AltarChamber.Protects(cell)?0f:appliedInset;
                float x0=openLeft?inset:0f,x1=openRight?1f-inset:1f;
                float y0=openBottom?inset:0f,y1=openTop?1f-inset:1f;
                if(pathCount==pathPool.Count)pathPool.Add(new Vector2[4]);
                var path=pathPool[pathCount++];
                var corner=source.CellToWorld(cell);
                path[0]=chunk.transform.InverseTransformPoint(corner+Vector3.Scale(new Vector3(x0,y0),cellSize));
                path[1]=chunk.transform.InverseTransformPoint(corner+Vector3.Scale(new Vector3(x1,y0),cellSize));
                path[2]=chunk.transform.InverseTransformPoint(corner+Vector3.Scale(new Vector3(x1,y1),cellSize));
                path[3]=chunk.transform.InverseTransformPoint(corner+Vector3.Scale(new Vector3(x0,y1),cellSize));
                tiles[i]=null;
            }
        }
        var polygon=chunk.GetComponent<PolygonCollider2D>();
        if(pathCount>0&&!polygon)
        {
            polygon=chunk.gameObject.AddComponent<PolygonCollider2D>();
            polygon.compositeOperation=Collider2D.CompositeOperation.Merge;
            polygon.sharedMaterial=sourceCollider.sharedMaterial;
            polygon.isTrigger=sourceCollider.isTrigger;
        }
        if(polygon)
        {
            // SetPath on an enabled collider rebuilds its shapes every time, which cost more than all other
            // collider work of a mined block. Paths set while it is disabled are built once on enabling.
            polygon.enabled=false;
            polygon.pathCount=pathCount;
            for(int i=0;i<pathCount;i++)polygon.SetPath(i,pathPool[i]);
            polygon.enabled=true;
        }
        chunk.SetTilesBlock(area,tiles);
        System.Array.Clear(tiles,0,tiles.Length);
        chunk.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        // SetPath and tile changes must complete before combining the geometry once.
        chunk.GetComponent<CompositeCollider2D>().GenerateGeometry();
    }
    float CurrentInset => appearance ? Mathf.Clamp(appearance.colliderInset,0f,.3f) : 0f;

    void OnTilesChanged(Tilemap source, NativeArray<Vector3Int> positions)
    {
        if(chunks==null||source!=map.Terrain)return;
        foreach(var position in positions)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
        {
            var cell=position+new Vector3Int(dx,dy,0);
            if(!sourceBounds.Contains(cell))continue;
            var chunk=chunks[(cell.x-sourceBounds.xMin)/ChunkSize,(cell.y-sourceBounds.yMin)/ChunkSize];
            if(chunk)dirtyChunks.Add(chunk);
        }
    }
    void ClearChunks()
    {
        dirtyChunks.Clear();
        if (chunks == null) return;
        foreach (var chunk in chunks)
            if (chunk)
            {
                if (Application.isPlaying) Destroy(chunk.gameObject);
                else DestroyImmediate(chunk.gameObject);
        }
        chunks = null;
        chunkCoordinates.Clear();
        animalBodies.Clear();
        animalBodyRefreshTimer = 0f;
        loadedCenterX = -1;
        loadedCenterY = -1;
    }
}


