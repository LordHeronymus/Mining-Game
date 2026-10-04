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
        => TryPlace(cell, inventory, player, reach, ladderItem);

    public bool TryPlace(Vector3Int cell, InventoryManager inventory, Vector2 player, float reach, ItemSO materialItem)
    {
        if (GameplayInputBlocker.IsBlocked || !inventory || !materialItem || !CanPlace(cell) || !InReach(cell, player, reach)) return false;
        if (materialItem.item != Item.Ladder && materialItem.item != Item.IronLadder) return false;
        var tile = materialItem.item == Item.IronLadder ? Resources.Load<Tile>("Exotics/IronLadderTile") : segment;
        if (!tile || !inventory.TryRemove(materialItem)) return false;
        EnsureTiles().SetTile(cell, tile);
        AudioManager.Instance?.Play(SoundType.LadderPlace, true);
        return true;
    }

    public bool TryRemove(Vector3Int cell, InventoryManager inventory, Vector2 player, float reach)
    {
        var ironTile = Resources.Load<Tile>("Exotics/IronLadderTile");
        var returnedItem = Has(cell) && ironTile && tiles.GetTile(cell) == ironTile
            ? StartingResourcesSettings.Resolve((int)Item.IronLadder) : ladderItem;
        if (GameplayInputBlocker.IsBlocked || !inventory || !returnedItem || !Has(cell) || !InReach(cell, player, reach) ||
            !inventory.CanAdd(returnedItem)) return false;
        tiles.SetTile(cell, null);
        inventory.Add(returnedItem);
        AudioManager.Instance?.Play(SoundType.LadderRemove, true);
        return true;
    }

    // Only nearby cells are inspected, independent of the total number of ladder segments.
    public bool FindContact(Bounds body, out Vector3Int cell)
    {
        cell = default;
        if (!tiles || !isActiveAndEnabled) return false;
        var left = tiles.WorldToCell(new Vector3(body.min.x, body.center.y));
        var right = tiles.WorldToCell(new Vector3(body.max.x, body.center.y));
        var low = tiles.WorldToCell(new Vector3(body.center.x, body.min.y - .06f));
        var high = tiles.WorldToCell(new Vector3(body.center.x, body.center.y));
        float closestDistance = float.PositiveInfinity;
        for (int y = high.y; y >= low.y; y--)
        {
            for (int x = left.x; x <= right.x; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (!Has(candidate) || Map.Terrain.HasTile(candidate)) continue;
                float distance = Mathf.Abs(tiles.GetCellCenterWorld(candidate).x - body.center.x);
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                cell = candidate;
            }
        }
        return !float.IsPositiveInfinity(closestDistance);
    }
}
