using System.Collections.Generic;
using UnityEngine;

public sealed class PlacedDynamite : MonoBehaviour
{
    static readonly HashSet<PlacedDynamite> active = new();
    MapGenerator map;
    float fuse = 2.5f;
    SpriteRenderer visual;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => active.Clear();
    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);
    public static bool TryPlace(MapGenerator map, ItemSO item, Vector2 position, Vector2 player, float reach)
    {
        if (!map || !map.IsGenerated || !item || item.item != Item.Dynamite || GameplayInputBlocker.IsBlocked ||
            Vector2.Distance(position, player) > reach || !InventoryManager.Instance) return false;
        var cell = map.Terrain.WorldToCell(position);
        if (map.IsCellProtected(cell) || cell.y > 0 || cell.y < 1 - map.GeneratedHeight ||
            cell.x < -map.GeneratedWidth / 2 || cell.x >= map.GeneratedWidth / 2) return false;
        if (!InventoryManager.Instance.TryRemove(item)) return false;
        Restore(map, new SavedDynamite { position = position, fuse = 2.5f });
        return true;
    }
    public static void Restore(MapGenerator map, SavedDynamite saved)
    {
        var go = new GameObject("Dynamite Charge"); go.transform.SetParent(map.transform, false); go.transform.position = saved.position;
        var charge = go.AddComponent<PlacedDynamite>(); charge.map = map; charge.fuse = Mathf.Clamp(saved.fuse, .05f, 2.5f);
        charge.visual = go.AddComponent<SpriteRenderer>();
        charge.visual.sprite = StartingResourcesSettings.Resolve((int)Item.Dynamite)?.icon;
        charge.visual.sortingOrder = 2;
        if (charge.visual.sprite) go.transform.localScale = Vector3.one * (.45f / Mathf.Max(.01f, charge.visual.sprite.bounds.size.y));
    }
    void Update()
    {
        if (LoadingProgress.Active || RunNavigation.IsTransitioning || GameplayInputBlocker.IsBlocked || Time.timeScale <= 0f) return;
        fuse -= Time.deltaTime;
        if (visual) visual.color = Color.Lerp(Color.white, new Color(1, .28f, .1f), .5f + .5f * Mathf.Sin(Time.time * 18));
        if (fuse > 0) return;
        Explode();
    }
    void Explode()
    {
        var terrain = map ? map.Terrain : null;
        if (!terrain) { Destroy(gameObject); return; }
        Vector2 position = transform.position;
        float cellSize = Vector2.Distance(terrain.GetCellCenterWorld(Vector3Int.zero), terrain.GetCellCenterWorld(Vector3Int.right));
        float radius = cellSize * 2.15f;
        BoulderField.Blast(map, position, radius);
        var miner = FindFirstObjectByType<TileMiner>();
        var center = terrain.WorldToCell(position);
        if (miner) for (int x = -3; x <= 3; x++) for (int y = -3; y <= 3; y++)
        {
            var cell = center + new Vector3Int(x, y, 0);
            if (Vector2.Distance(terrain.GetCellCenterWorld(cell), position) <= radius) miner.CompleteMining(cell, true);
        }
        var player = FindFirstObjectByType<PlayerMovement>();
        if (player && Vector2.Distance(player.transform.position, position) < radius * 1.3f) StatsManager.Instance?.ApplyDamage(25);
        AudioManager.Instance?.Play(SoundType.StoneBreak, true);
        DynamiteFlash.Show(position, radius);
        Destroy(gameObject);
    }
    public static SavedDynamite[] Capture(MapGenerator map)
    {
        var result = new List<SavedDynamite>();
        foreach (var charge in active) if (charge && charge.map == map)
            result.Add(new SavedDynamite { position = charge.transform.position, fuse = charge.fuse });
        return result.ToArray();
    }
    public static void Clear(MapGenerator map)
    {
        foreach (var charge in new List<PlacedDynamite>(active)) if (charge && charge.map == map)
        { charge.gameObject.SetActive(false); Destroy(charge.gameObject); }
    }
}

public sealed class DynamiteFlash : MonoBehaviour
{
    SpriteRenderer visual;
    float age, radius;
    public static void Show(Vector2 position, float radius)
    {
        var root = new GameObject("Dynamite Flash"); root.transform.position = position;
        var flash = root.AddComponent<DynamiteFlash>(); flash.radius = radius;
        flash.visual = root.AddComponent<SpriteRenderer>(); flash.visual.sprite = Resources.Load<Sprite>("Exotics/LavaGlow");
        flash.visual.sortingOrder = 8;
    }
    void Update()
    {
        age += Time.deltaTime; float t = Mathf.Clamp01(age / .45f);
        transform.localScale = Vector3.one * Mathf.Lerp(.3f, radius * 2.8f, t);
        if (visual) visual.color = new Color(1, Mathf.Lerp(.8f, .22f, t), .08f, (1 - t) * .8f);
        if (t >= 1) Destroy(gameObject);
    }
}
