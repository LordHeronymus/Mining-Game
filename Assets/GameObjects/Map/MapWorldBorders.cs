using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent, RequireComponent(typeof(MapGenerator))]
public sealed class MapWorldBorders : MonoBehaviour
{
    [Min(0), InspectorName("Seitenabstand (Kacheln)")]
    public int sidePaddingCells = 12;
    [InspectorName("Obere Grenze (Welt-Y)")]
    public float topBorderY = 100f;

    MapGenerator map;
    BoxCollider2D leftWall, rightWall, topWall;
    PlayerMovement player;

    public float LeftLimit { get; private set; }
    public float RightLimit { get; private set; }
    public float TopLimit => topBorderY;

    void Awake() { map = GetComponent<MapGenerator>(); EnsureWalls(); }
    void OnEnable() { if (!map) map = GetComponent<MapGenerator>(); map.Generated += Rebuild; Rebuild(); }
    void OnDisable() { if (map) map.Generated -= Rebuild; }
    void OnValidate() { sidePaddingCells = Mathf.Max(0, sidePaddingCells); if (!float.IsFinite(topBorderY)) topBorderY = 100f; Rebuild(); }

    void Rebuild()
    {
        if (!map) map = GetComponent<MapGenerator>();
        if (!map || !map.Terrain || map.GeneratedWidth <= 0) return;
        EnsureWalls();
        var terrain = map.Terrain;
        int xMin = -map.GeneratedWidth / 2;
        int xMax = xMin + map.GeneratedWidth;
        int padding = Mathf.Clamp(sidePaddingCells, 0, Mathf.Max(0, (map.GeneratedWidth - 1) / 2));
        LeftLimit = terrain.CellToWorld(new Vector3Int(xMin + padding, 0, 0)).x;
        RightLimit = terrain.CellToWorld(new Vector3Int(xMax - padding, 0, 0)).x;
        float width = Mathf.Max(1f, RightLimit - LeftLimit);
        float cellHeight = Mathf.Max(.01f, terrain.layoutGrid.cellSize.y);
        float bottom = terrain.CellToWorld(new Vector3Int(0, 1 - map.GeneratedHeight, 0)).y - cellHeight;
        float height = Mathf.Max(cellHeight, topBorderY - bottom);
        const float thickness = .1f;
        SetWall(leftWall, new Vector2(LeftLimit - thickness * .5f, bottom + height * .5f), new Vector2(thickness, height));
        SetWall(rightWall, new Vector2(RightLimit + thickness * .5f, bottom + height * .5f), new Vector2(thickness, height));
        SetWall(topWall, new Vector2((LeftLimit + RightLimit) * .5f, topBorderY + thickness * .5f), new Vector2(width, thickness));
    }

    void EnsureWalls() { leftWall = EnsureWall("World Border Left"); rightWall = EnsureWall("World Border Right"); topWall = EnsureWall("World Border Top"); }
    BoxCollider2D EnsureWall(string name)
    {
        var child = transform.Find(name);
        if (!child) { child = new GameObject(name).transform; child.SetParent(transform, false); }
        child.gameObject.layer = gameObject.layer;
        var body = child.GetComponent<Rigidbody2D>();
        if (!body) body = child.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var collider = child.GetComponent<BoxCollider2D>();
        if (!collider) collider = child.gameObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = false;
        return collider;
    }
    static void SetWall(BoxCollider2D wall, Vector2 center, Vector2 size) { wall.transform.position = center; wall.size = size; wall.offset = Vector2.zero; }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !player) player = FindFirstObjectByType<PlayerMovement>();
        if (!player || !player.TryGetComponent<Collider2D>(out var collider)) return;
        var bounds = collider.bounds;
        Vector3 position = player.transform.position;
        if (bounds.min.x < LeftLimit) position.x += LeftLimit - bounds.min.x;
        else if (bounds.max.x > RightLimit) position.x -= bounds.max.x - RightLimit;
        if (bounds.max.y > topBorderY) position.y -= bounds.max.y - topBorderY;
        if (position == player.transform.position) return;
        player.transform.position = position;
        if (!player.TryGetComponent<Rigidbody2D>(out var body)) return;
        var velocity = body.linearVelocity;
        if ((velocity.x < 0f && bounds.min.x < LeftLimit) || (velocity.x > 0f && bounds.max.x > RightLimit)) velocity.x = 0f;
        if (velocity.y > 0f && bounds.max.y > topBorderY) velocity.y = 0f;
        body.linearVelocity = velocity;
    }

    public Vector3 ClampCameraCenter(Camera camera, Vector3 desired)
    {
        if (!camera || !camera.orthographic) return desired;
        float halfWidth = camera.orthographicSize * camera.aspect;
        float minX = LeftLimit + halfWidth, maxX = RightLimit - halfWidth;
        desired.x = minX > maxX ? (LeftLimit + RightLimit) * .5f : Mathf.Clamp(desired.x, minX, maxX);
        desired.y = Mathf.Min(desired.y, topBorderY - camera.orthographicSize);
        return desired;
    }
}
