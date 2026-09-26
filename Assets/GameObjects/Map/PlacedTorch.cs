using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class PlacedTorch : MonoBehaviour
{
    const float SpriteScale = .12f;
    const float LightRadius = 4.5f;
    const float LightIntensity = .85f;
    static readonly HashSet<PlacedTorch> active = new();

    MapGenerator map;
    Vector3Int cell;
    ItemSO item;
    public static IEnumerable<PlacedTorch> Active => active;
    public MapGenerator OwnerMap => map;
    public Vector2 LightPosition => transform.position + new Vector3(.18f, .65f, 0f);
    public float Radius => LightRadius;
    public float Intensity => LightIntensity;

    public static float BrightnessAt(Vector2 worldPosition, MapGenerator map)
    {
        float brightness = 0f;
        foreach (var torch in active)
        {
            if (!torch || torch.map != map) continue;
            float distance = Vector2.Distance(worldPosition, torch.LightPosition);
            if (distance >= torch.Radius) continue;
            float falloff = 1f - Mathf.SmoothStep(0f, 1f, distance / torch.Radius);
            brightness = Mathf.Max(brightness, torch.Intensity * falloff);
        }
        return Mathf.Clamp01(brightness);
    }

    void OnEnable()
    {
        active.Add(this);
        if (map) map.Generated += OnMapGenerated;
    }

    void OnDisable()
    {
        active.Remove(this);
        if (map) map.Generated -= OnMapGenerated;
    }

    void OnMapGenerated() => Destroy(gameObject);

    public static bool TryRemoveAt(Vector2 worldPoint, Vector2 playerPosition, float reach)
    {
        var inventory = InventoryManager.Instance;
        if (!Application.isPlaying || GameplayInputBlocker.IsBlocked || !inventory) return false;
        foreach (var torch in active)
        {
            if (!torch || !torch.map || !torch.item) continue;
            var renderer = torch.GetComponent<SpriteRenderer>();
            if (!renderer) continue;
            var bounds = renderer.bounds;
            if (worldPoint.x < bounds.min.x || worldPoint.x > bounds.max.x ||
                worldPoint.y < bounds.min.y || worldPoint.y > bounds.max.y ||
                Vector2.Distance(playerPosition, torch.map.Terrain.GetCellCenterWorld(torch.cell)) > reach ||
                inventory.GetCount(torch.item) == int.MaxValue) continue;
            inventory.Add(torch.item);
            Destroy(torch.gameObject);
            return true;
        }
        return false;
    }

    public static bool TryPlace(MapGenerator map, ItemSO item, Vector2 worldPoint, Vector2 playerPosition, float reach)
    {
        if (!Application.isPlaying || !map || !map.Terrain || !item || item.item != Item.Torche ||
            !InventoryManager.Instance || InventoryManager.Instance.GetCount(item) <= 0)
            return false;

        var terrain = map.Terrain;
        Vector3Int target = terrain.WorldToCell(worldPoint);
        Vector2 targetCenter = terrain.GetCellCenterWorld(target);
        if (target.z != 0 || target.x < -map.GeneratedWidth / 2 ||
            target.x >= -map.GeneratedWidth / 2 + map.GeneratedWidth ||
            target.y < 1 - map.GeneratedHeight || terrain.HasTile(target) ||
            Vector2.Distance(playerPosition, targetCenter) > reach)
            return false;

        bool supported = terrain.HasTile(target + Vector3Int.down) ||
            terrain.HasTile(target + Vector3Int.left) || terrain.HasTile(target + Vector3Int.right);
        if (!supported) return false;
        var sprite = item.icon;
        if (!sprite) return false;
        foreach (var placed in active)
            if (placed && placed.map == map && placed.cell == target) return false;

        if (!InventoryManager.Instance.TryRemove(item)) return false;
        var instance = new GameObject("Placed Torch");
        instance.SetActive(false);
        instance.transform.SetParent(map.transform, false);
        instance.transform.localScale = Vector3.one * SpriteScale;
        Vector3 cellBottom = terrain.CellToWorld(target);
        instance.transform.position = new Vector3(
            terrain.GetCellCenterWorld(target).x - sprite.bounds.size.x * SpriteScale * .5f,
            cellBottom.y, 0f);
        var renderer = instance.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 10;
        var torch = instance.AddComponent<PlacedTorch>();
        torch.map = map;
        torch.cell = target;
        torch.item = item;
        var light = instance.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1f, .58f, .24f, 1f);
        light.intensity = 1.25f;
        light.pointLightInnerRadius = 1f;
        light.pointLightOuterRadius = LightRadius;
        light.shadowsEnabled = false;
        instance.SetActive(true);
        return true;
    }
}
