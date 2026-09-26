using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public sealed class GrassRootFill : MonoBehaviour
{
    [SerializeField] MapGenerator map;
    [SerializeField] TileBase leftTile, rightTile;
    [SerializeField] Tilemap leftOverlay, rightOverlay;
    bool dirty = true;

    void OnEnable() { Tilemap.tilemapTileChanged += Changed; dirty = true; }
    void OnDisable() { Tilemap.tilemapTileChanged -= Changed; }
    void Changed(Tilemap changed, Tilemap.SyncTile[] changes)
    {
        if (map && (changed == map.Terrain || changed == map.GrassOverlay)) dirty = true;
    }
    void LateUpdate()
    {
        if (dirty) Sync();
        if (map && map.GrassOverlay)
        {
            if (leftOverlay) leftOverlay.transform.localPosition = map.GrassOverlay.transform.localPosition;
            if (rightOverlay) rightOverlay.transform.localPosition = map.GrassOverlay.transform.localPosition;
        }
    }
    public void Configure(MapGenerator owner, TileBase left, TileBase right)
    { map = owner; leftTile = left; rightTile = right; Sync(); }

    public void Sync()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (!map || !map.GrassOverlay || !leftTile || !rightTile) return;
        leftOverlay = EnsureOverlay(leftOverlay, "Grass Root Left");
        rightOverlay = EnsureOverlay(rightOverlay, "Grass Root Right");
        leftOverlay.ClearAllTiles();
        rightOverlay.ClearAllTiles();
        int width = map.GeneratedWidth;
        for (int x = -width / 2; x < -width / 2 + width; x++)
        {
            var cell = new Vector3Int(x, 0, 0);
            if (!map.Terrain.HasTile(cell)) continue;
            if (!map.Terrain.HasTile(cell + Vector3Int.left)) leftOverlay.SetTile(cell, leftTile);
            if (!map.Terrain.HasTile(cell + Vector3Int.right)) rightOverlay.SetTile(cell, rightTile);
        }
        dirty = false;
    }

    Tilemap EnsureOverlay(Tilemap overlay, string objectName)
    {
        if (!overlay)
        {
            var child = transform.Find(objectName);
            if (child) overlay = child.GetComponent<Tilemap>();
            if (!overlay)
            {
                var go = new GameObject(objectName, typeof(Tilemap), typeof(TilemapRenderer));
                go.transform.SetParent(transform, false);
                overlay = go.GetComponent<Tilemap>();
            }
        }
        overlay.gameObject.layer = map.GrassOverlay.gameObject.layer;
        overlay.tileAnchor = map.GrassOverlay.tileAnchor;
        overlay.orientation = map.GrassOverlay.orientation;
        overlay.orientationMatrix = map.GrassOverlay.orientationMatrix;
        overlay.transform.localPosition = map.GrassOverlay.transform.localPosition;
        var source = map.GrassOverlay.GetComponent<TilemapRenderer>();
        var renderer = overlay.GetComponent<TilemapRenderer>();
        renderer.sharedMaterial = source.sharedMaterial;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder + 1;
        return overlay;
    }
}
