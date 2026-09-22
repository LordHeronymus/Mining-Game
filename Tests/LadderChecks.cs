using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LadderChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Run()
    {
        Check(Application.isPlaying, "Play Mode required");
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerLadder>();
        var map = player.ladders;
        Check(!map.Map.IsGenerationStreaming, "Wait for initial map generation before running checks");
        var inv = InventoryManager.Instance;
        var movement = player.GetComponent<PlayerMovement>();
        var rb = player.GetComponent<Rigidbody2D>();
        var col = player.GetComponent<Collider2D>();
        var savedPosition = rb.position;
        var savedVelocity = rb.linearVelocity;
        float savedGravity = rb.gravityScale;
        bool wasEnabled = movement.enabled;
        var simulation = Physics2D.simulationMode;
        int count = inv.GetCount(map.ladderItem);
        var cells = new List<Vector3Int>();
        var saved = new Dictionary<Vector3Int, TileBase>();
        var first = new Vector3Int(0, 10, 0);
        for (int y = 10; y <= 19; y++) { var c = new Vector3Int(0, y, 0); cells.Add(c); saved[c] = map.Tiles.GetTile(c); }
        try
        {
            movement.enabled = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            foreach (var c in cells) map.Tiles.SetTile(c, null);
            if (count > 0) inv.TryRemove(map.ladderItem, count);
            var near = (Vector2)map.Tiles.GetCellCenterWorld(first);
            Check(!map.TryPlace(first, inv, near, 3), "Empty inventory must reject placement");
            inv.Add(map.ladderItem, 20);
            Check(!map.TryPlace(first, inv, near + Vector2.right * 10, 3), "Reach must reject placement");
            GameplayInputBlocker.SetBlocked(player, true);
            Check(!map.TryPlace(first, inv, near, 3), "Modal must reject placement");
            GameplayInputBlocker.SetBlocked(player, false);
            Check(map.TryPlace(first, inv, near, 3), "Unsupported floating placement failed");
            Check(inv.GetCount(map.ladderItem) == 19, "Exactly one item per cell");
            Check(!map.TryPlace(first, inv, near, 3), "Duplicate placement must fail");
            Check(inv.GetCount(map.ladderItem) == 19, "Duplicate consumed an item");
            foreach (var candidate in map.Map.Terrain.cellBounds.allPositionsWithin)
                if (map.Map.Terrain.HasTile(candidate))
                {
                    Check(!map.TryPlace(candidate, inv, map.Map.Terrain.GetCellCenterWorld(candidate), 3), "Solid terrain must reject placement");
                    break;
                }
            Check(map.TryRemove(first, inv, near, 3) && inv.GetCount(map.ladderItem) == 20, "Removal refund failed");
            Check(!map.TryRemove(first, inv, near, 3), "Double refund");
            foreach (var c in cells) Check(map.TryPlace(c, inv, map.Tiles.GetCellCenterWorld(c), 3), "Chain placement");
            Check(Mathf.Abs(map.segment.sprite.bounds.size.y - .5f) < .001f && Mathf.Abs(map.segment.sprite.bounds.size.x - .5f) < .001f, "Sprite must fill one .5m cell");
            Check(map.segment.colliderType == Tile.ColliderType.None && !map.Tiles.GetComponent<Collider2D>(), "Ladders must not block movement");
            Check(map.Tiles.GetComponent<TilemapRenderer>().sortingOrder < 30, "Ladder must be behind player");
            rb.position = near;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 3;
            Physics2D.SyncTransforms();
            typeof(PlayerLadder).GetField("reattachAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0f);
            Check(player.Step(0, 1, false, false) && player.IsClimbing && rb.gravityScale == 0, "Climb entry");
            float start = rb.position.y;
            for (int i = 0; i < 45; i++) { player.Step(0, 1, false, false); Physics2D.Simulate(Time.fixedDeltaTime); }
            Check(rb.position.y > start + 1, "Actual upward travel across segments");
            for (int i = 0; i < 180; i++) { player.Step(0, 1, false, false); Physics2D.Simulate(Time.fixedDeltaTime); }
            Check(player.IsClimbing && Mathf.Abs(col.bounds.min.y - 10f) < .03f, "Top exit must stop with feet at top edge");
            player.Step(0, 0, false, false);
            Check(Mathf.Abs(rb.linearVelocity.y) < .001f && player.IsClimbing, "Idle hold");
            GameplayInputBlocker.SetBlocked(player, true);
            player.Step(0, 1, true, false);
            Check(player.IsClimbing && rb.linearVelocity.y == 0, "Modal climb hold");
            GameplayInputBlocker.SetBlocked(player, false);
            float upper = rb.position.y;
            for (int i = 0; i < 20; i++) { player.Step(0, -1, false, false); Physics2D.Simulate(Time.fixedDeltaTime); }
            Check(rb.position.y < upper - .4f, "Actual descent");
            Check(player.Step(0, 0, true, false) && !player.IsClimbing && rb.gravityScale == 3 && rb.linearVelocity.y > 0, "Jump restores gravity");
            typeof(PlayerLadder).GetField("reattachAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0f);
            player.Step(0, 1, false, false);
            player.Step(1, 0, false, false);
            Check(!player.IsClimbing && rb.gravityScale == 3 && rb.linearVelocity.x > 0, "Side exit");
            typeof(PlayerLadder).GetField("reattachAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0f);
            player.Step(0, 1, false, false);
            player.Step(0, 0, false, true);
            Check(!player.IsClimbing && rb.gravityScale == 3, "Fly handover");
            typeof(PlayerLadder).GetField("reattachAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(player, 0f);
            player.Step(0, 1, false, false);
            foreach (var c in cells) map.Tiles.SetTile(c, null);
            player.Step(0, 0, false, false);
            Check(!player.IsClimbing && rb.gravityScale == 3, "Removed ladder must release player");
            return "PASS: inventory/reach/modal/duplicates/solid terrain/refund; unsupported square segments; physics ascent/top edge/descent/hold/jump/side exit/fly/removal.";
        }
        finally
        {
            GameplayInputBlocker.SetBlocked(player, false);
            player.Detach();
            foreach (var c in cells) map.Tiles.SetTile(c, saved[c]);
            map.Tiles.CompressBounds();
            int current = inv.GetCount(map.ladderItem);
            if (current > 0) inv.TryRemove(map.ladderItem, current);
            if (count > 0) inv.Add(map.ladderItem, count);
            rb.position = savedPosition; rb.linearVelocity = savedVelocity; rb.gravityScale = savedGravity;
            Physics2D.SyncTransforms();
            Physics2D.simulationMode = simulation;
            movement.enabled = wasEnabled;
        }
    }
}
