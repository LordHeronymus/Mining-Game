using UnityEngine;

public static class SurfaceAnimalCollision
{
    const float Skin = .02f;
    public const float GroundTileProbeDepth = .05f;
    static RaycastHit2D[] hits = new RaycastHit2D[16];

    // Same mask, distance and trigger rule as BoxCastAll, but into a shared buffer instead of a new array per
    // call; animals probe several times per frame. The buffer grows when full, so no hit is ever cut off.
    static int Cast(MapGenerator map, Vector2 origin, Vector2 size, Vector2 direction, float distance)
    {
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = 1 << map.gameObject.layer,
            useTriggers = Physics2D.queriesHitTriggers };
        int count;
        while ((count = Physics2D.BoxCast(origin, size, 0f, direction, filter, hits, distance)) == hits.Length)
            hits = new RaycastHit2D[hits.Length * 2];
        return count;
    }

    public static bool TryGround(MapGenerator map, float x, float fromY, float distance,
        float width, float height, out float groundY)
    {
        groundY = fromY;
        if (!map || distance < 0f) return false;
        var origin = new Vector2(x, fromY + height * .5f + Skin);
        int count = Cast(map, origin, new Vector2(width, height), Vector2.down, distance + Skin);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
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
        int count = Cast(map, origin, new Vector2(width, height), direction, distance);
        for (int i = 0; i < count; i++)
            if (hits[i].collider && Vector2.Dot(hits[i].normal, direction) < -.5f &&
                hits[i].distance < distance - Skin) return false;
        return true;
    }

    public static bool CanRise(MapGenerator map, float x, float fromY, float toY,
        float width, float height)
    {
        float distance = toY - fromY;
        if (!map || distance < .0001f) return true;
        var origin = new Vector2(x, fromY + height * .5f + Skin);
        int count = Cast(map, origin, new Vector2(width, height), Vector2.up, distance);
        for (int i = 0; i < count; i++)
            if (hits[i].collider && hits[i].normal.y < -.5f && hits[i].distance < distance - Skin) return false;
        return true;
    }
}
