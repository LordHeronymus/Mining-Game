using UnityEngine;

public static class SurfaceAnimalCollision
{
    const float Skin = .02f;
    public const float GroundTileProbeDepth = .05f;

    public static bool TryGround(MapGenerator map, float x, float fromY, float distance,
        float width, float height, out float groundY)
    {
        groundY = fromY;
        if (!map || distance < 0f) return false;
        var origin = new Vector2(x, fromY + height * .5f + Skin);
        var hits = Physics2D.BoxCastAll(origin, new Vector2(width, height), 0f,
            Vector2.down, distance + Skin, 1 << map.gameObject.layer);
        float nearest = float.PositiveInfinity;
        foreach (var hit in hits)
        {
            if (!hit.collider || hit.normal.y < .5f || hit.distance >= nearest) continue;
            nearest = hit.distance;
        }
        if (float.IsPositiveInfinity(nearest)) return false;
        groundY = fromY + Skin - nearest;
        return true;
    }

    public static bool CanMove(MapGenerator map, float fromX, float toX, float feetY,
        float width, float height)
    {
        float distance = Mathf.Abs(toX - fromX);
        if (!map || distance < .0001f) return true;
        var direction = toX > fromX ? Vector2.right : Vector2.left;
        var origin = new Vector2(fromX, feetY + height * .5f + Skin);
        var hits = Physics2D.BoxCastAll(origin, new Vector2(width, height), 0f,
            direction, distance, 1 << map.gameObject.layer);
        foreach (var hit in hits)
            if (hit.collider && Vector2.Dot(hit.normal, direction) < -.5f &&
                hit.distance < distance - Skin) return false;
        return true;
    }

    public static bool CanRise(MapGenerator map, float x, float fromY, float toY,
        float width, float height)
    {
        float distance = toY - fromY;
        if (!map || distance < .0001f) return true;
        var origin = new Vector2(x, fromY + height * .5f + Skin);
        var hits = Physics2D.BoxCastAll(origin, new Vector2(width, height), 0f,
            Vector2.up, distance, 1 << map.gameObject.layer);
        foreach (var hit in hits)
            if (hit.collider && hit.normal.y < -.5f && hit.distance < distance - Skin) return false;
        return true;
    }
}
