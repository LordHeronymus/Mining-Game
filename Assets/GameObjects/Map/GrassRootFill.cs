using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

[ExecuteAlways]
public sealed class GrassRootFill : MonoBehaviour
{
    [SerializeField] MapGenerator map;
    [SerializeField] TileBase leftTile, rightTile;
    [SerializeField] Tilemap leftOverlay, rightOverlay;
    bool dirty = true;
    readonly HashSet<int> dirtyColumns = new HashSet<int>();

    void OnEnable()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (map) map.Generated += Invalidate;
        Tilemap.tilemapTileChanged += Changed;
        Invalidate();
    }
    void OnDisable()
    {
        if (map) map.Generated -= Invalidate;
        Tilemap.tilemapTileChanged -= Changed;
        dirtyColumns.Clear();
    }
    void Invalidate() { dirty = true; dirtyColumns.Clear(); }
    void Changed(Tilemap changed, Tilemap.SyncTile[] changes)
    {
        if (dirty || !map || changes == null ||
            (changed != map.Terrain && changed != map.GrassOverlay)) return;
        foreach (var change in changes)
        {
            if (change.position.y != 0 || change.position.z != 0) continue;
            dirtyColumns.Add(change.position.x - 1);
            dirtyColumns.Add(change.position.x);
            dirtyColumns.Add(change.position.x + 1);
        }
    }
    void LateUpdate()
    {
        if (dirty) Sync();
        else if (dirtyColumns.Count > 0)
        {
            if (!leftOverlay || !rightOverlay) Sync();
            else
            {
                foreach (int x in dirtyColumns) SyncColumn(x);
                dirtyColumns.Clear();
            }
        }
        if (map && map.GrassOverlay)
        {
            if (leftOverlay) leftOverlay.transform.localPosition = map.GrassOverlay.transform.localPosition;
            if (rightOverlay) rightOverlay.transform.localPosition = map.GrassOverlay.transform.localPosition;
        }
    }
    public void Configure(MapGenerator owner, TileBase left, TileBase right)
    {
        if (map) map.Generated -= Invalidate;
        map = owner; leftTile = left; rightTile = right;
        if (map && isActiveAndEnabled) map.Generated += Invalidate;
        Invalidate();
        Sync();
    }

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
            SyncColumn(x);
        dirty = false;
        dirtyColumns.Clear();
    }

    void SyncColumn(int x)
    {
        int width = map.GeneratedWidth;
        if (x < -width / 2 || x >= -width / 2 + width) return;
        var cell = new Vector3Int(x, 0, 0);
        bool solid = map.Terrain.HasTile(cell);
        TileBase left = solid && !map.Terrain.HasTile(cell + Vector3Int.left) ? leftTile : null;
        TileBase right = solid && !map.Terrain.HasTile(cell + Vector3Int.right) ? rightTile : null;
        if (leftOverlay.GetTile(cell) != left) leftOverlay.SetTile(cell, left);
        if (rightOverlay.GetTile(cell) != right) rightOverlay.SetTile(cell, right);
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
