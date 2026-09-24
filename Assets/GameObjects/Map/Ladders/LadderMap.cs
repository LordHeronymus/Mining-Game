using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent, RequireComponent(typeof(MapGenerator))]
public sealed class LadderMap : MonoBehaviour
{
    public Tile segment;
    public ItemSO ladderItem;
    public Material ladderMaterial;
    [SerializeField] Tilemap tiles;
    MapGenerator map;
    public Tilemap Tiles => tiles;
    public MapGenerator Map => map ? map : map = GetComponent<MapGenerator>();

    public Tilemap EnsureTiles()
    {
        if (!tiles)
        {
            var child = transform.Find("Ladders");
            if (!child)
            {
                child = new GameObject("Ladders", typeof(Tilemap), typeof(TilemapRenderer)).transform;
                child.SetParent(transform, false);
            }
            tiles = child.GetComponent<Tilemap>();
        }
        tiles.tileAnchor = Map.Terrain.tileAnchor;
        var renderer = tiles.GetComponent<TilemapRenderer>();
        renderer.sharedMaterial = ladderMaterial;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = -1;
        return tiles;
    }

    void Awake() => EnsureTiles();
    public void Clear() { if (tiles) tiles.ClearAllTiles(); }
    public bool Has(Vector3Int cell) => tiles && tiles.HasTile(cell);

    public bool CanPlace(Vector3Int cell)
    {
        int width = Map.GeneratedWidth;
        return segment && cell.z == 0 && cell.x >= -width / 2 && cell.x < -width / 2 + width &&
            cell.y >= 1 - Map.GeneratedHeight && !Map.IsGenerationStreaming &&
            !Map.Terrain.HasTile(cell) && !Has(cell);
    }

    public bool InReach(Vector3Int cell, Vector2 player, float reach) =>
        Vector2.Distance(player, Map.Terrain.GetCellCenterWorld(cell)) <= reach;

    public bool TryPlace(Vector3Int cell, InventoryManager inventory, Vector2 player, float reach)
    {
        if (GameplayInputBlocker.IsBlocked || !inventory || !ladderItem || !CanPlace(cell) || !InReach(cell, player, reach)) return false;
        if (!inventory.TryRemove(ladderItem)) return false;
        EnsureTiles().SetTile(cell, segment);
        AudioManager.Instance?.Play(SoundType.LadderPlace, true);
        return true;
    }

    public bool TryRemove(Vector3Int cell, InventoryManager inventory, Vector2 player, float reach)
    {
        if (GameplayInputBlocker.IsBlocked || !inventory || !ladderItem || !Has(cell) || !InReach(cell, player, reach) ||
            inventory.GetCount(ladderItem) == int.MaxValue) return false;
        tiles.SetTile(cell, null);
        inventory.Add(ladderItem);
        return true;
    }

    // Only nearby cells are inspected, independent of the total number of ladder segments.
    public bool FindContact(Bounds body, out Vector3Int cell)
    {
        cell = default;
        if (!tiles || !isActiveAndEnabled) return false;
        var low = tiles.WorldToCell(new Vector3(body.center.x, body.min.y - .06f));
        var high = tiles.WorldToCell(new Vector3(body.center.x, body.center.y));
        for (int y = high.y; y >= low.y; y--)
        {
            var candidate = new Vector3Int(low.x, y, 0);
            if (Has(candidate) && !Map.Terrain.HasTile(candidate)) { cell = candidate; return true; }
        }
        return false;
    }
}
