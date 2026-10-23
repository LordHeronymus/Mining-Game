using System.Collections.Generic;
using UnityEngine;

[System.Serializable] public struct SavedPlacedLight { public Vector3Int cell; public int itemId; }

[DisallowMultipleComponent]
public sealed class PlacedTorch : MonoBehaviour
{
    const float HolderScale = .07f;
    const float LightAboveFlameEmitter = .13f;
    const float LightCellInset = .03f;
    public const float PropagationDistance = 2f;
    const float LightIntensity = .85f;
    static readonly HashSet<PlacedTorch> active = new();
    public static event System.Action<MapGenerator> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        active.Clear();
        Changed = null;
    }

    MapGenerator map;
    Vector3Int cell;
    ItemSO item;
    public static IEnumerable<PlacedTorch> Active => active;
    public MapGenerator OwnerMap => map;
    public Vector3Int Cell => cell;
    public ItemSO Item => item;
    public bool IsLavaLamp => item && item.item == global::Item.LavaLamp;
    Vector3 FlameLocalPosition => FlameEmitterLocal(map) + Vector3.up * LightAboveFlameEmitter;
    // Holder sits below the cell centre so the flame stays inside the cell; the flame is placed relative to the holder.
    public static float HolderOffsetY(MapGenerator owner) => owner ? owner.torchHolderOffsetY : -.22f;
    public static Vector3 FlameEmitterLocal(MapGenerator owner) => new(owner ? owner.torchFlameOffsetX : 0f,
        HolderOffsetY(owner) + (owner ? owner.torchFlameOffsetY : .47f), 0f);
    // The only terrain cell the light position depends on: a tile here moves it into the holder cell.
    public Vector3Int FlameCell => !IsLavaLamp && map && map.Terrain
        ? map.Terrain.WorldToCell(transform.TransformPoint(FlameLocalPosition)) : cell;
    public Vector3 LightLocalPosition
    {
        get
        {
            if (IsLavaLamp) return Vector3.zero;
            var offset = FlameLocalPosition;
            if (!map || !map.Terrain) return offset;
            var terrain = map.Terrain;
            if (!terrain.HasTile(FlameCell)) return offset;
            Vector3 center = transform.InverseTransformPoint(terrain.GetCellCenterWorld(cell));
            float halfWidth = terrain.transform.TransformVector(
                Vector3.right * terrain.layoutGrid.cellSize.x).magnitude * .5f;
            float halfHeight = terrain.transform.TransformVector(
                Vector3.up * terrain.layoutGrid.cellSize.y).magnitude * .5f;
            offset.x = Mathf.Clamp(offset.x, center.x - halfWidth + LightCellInset,
                center.x + halfWidth - LightCellInset);
            offset.y = Mathf.Clamp(offset.y, center.y - halfHeight + LightCellInset,
                center.y + halfHeight - LightCellInset);
            return offset;
        }
    }
    public Vector2 LightPosition => transform.TransformPoint(LightLocalPosition);
    public float Intensity => LightIntensity * (IsLavaLamp ? 2.8f : 1f) * (map ? Mathf.Clamp(map.torchBrightness, 0f, 2f) : 1f);

    public static void ApplySettings(MapGenerator ownerMap)
    {
        if (!ownerMap) return;
        foreach (var torch in active)
        {
            if (!torch || torch.map != ownerMap) continue;
            torch.PlaceHolder();
            torch.GetComponent<TorchFlame>()?.ApplySettings();
        }
        Changed?.Invoke(ownerMap);
    }

    void OnEnable()
    {
        active.Add(this);
        if (!IsLavaLamp) TorchCrackleAudio.Ensure();
        if (map) map.Generated += OnMapGenerated;
        Changed?.Invoke(map);
    }

    void OnDisable()
    {
        active.Remove(this);
        if (map) map.Generated -= OnMapGenerated;
        Changed?.Invoke(map);
    }

    void OnMapGenerated() => Destroy(gameObject);

    void PlaceHolder()
    {
        if (IsLavaLamp) return;
        var holder = transform.Find("Torch Holder");
        if (holder) holder.localPosition = new Vector3(0f, HolderOffsetY(map), 0f);
    }

    public static bool TryRemoveAt(Vector2 worldPoint, Vector2 playerPosition, float reach)
    {
        var inventory = InventoryManager.Instance;
        if (!Application.isPlaying || GameplayInputBlocker.IsBlocked || !inventory) return false;
        foreach (var torch in active)
        {
            if (!torch || !torch.map || !torch.item) continue;
            var renderer = torch.GetComponentInChildren<SpriteRenderer>();
            if (!renderer) continue;
            var bounds = renderer.bounds;
            if (worldPoint.x < bounds.min.x || worldPoint.x > bounds.max.x ||
                worldPoint.y < bounds.min.y || worldPoint.y > bounds.max.y ||
                Vector2.Distance(playerPosition, torch.map.Terrain.GetCellCenterWorld(torch.cell)) > reach ||
                !inventory.CanAdd(torch.item)) continue;
            inventory.Add(torch.item);
            AudioManager.Instance?.PlayTorchSound(false);
            Unregister(torch);
            Destroy(torch.gameObject);
            return true;
        }
        return false;
    }

    // Destroy takes effect only at frame end: leave the active set right away, so a save capture in the same frame
    // (the autosave captures after Update) does not keep the torch that was just refunded to the inventory.
    static void Unregister(PlacedTorch torch) => active.Remove(torch);

    public static bool TryPlace(MapGenerator map, ItemSO item, Vector2 worldPoint, Vector2 playerPosition, float reach)
    {
        if (!Application.isPlaying || GameplayInputBlocker.IsBlocked || !map || !map.Terrain || !item ||
            (item.item != global::Item.Torche && item.item != global::Item.LavaLamp) ||
            !InventoryManager.Instance || InventoryManager.Instance.GetCount(item) <= 0)
            return false;

        var terrain = map.Terrain;
        Vector3Int target = terrain.WorldToCell(worldPoint);
        Vector2 targetCenter = terrain.GetCellCenterWorld(target);
        if (target.z != 0 || target.x < -map.GeneratedWidth / 2 ||
            target.x >= -map.GeneratedWidth / 2 + map.GeneratedWidth ||
            target.y < 1 - map.GeneratedHeight || target.y > 0 || terrain.HasTile(target) ||
            Vector2.Distance(playerPosition, targetCenter) > reach)
            return false;

        var sprite = item.item == global::Item.LavaLamp ? item.icon : Resources.Load<Sprite>("Torches/TorchHolderSprite");
        if (!sprite) return false;
        foreach (var placed in active)
            if (placed && placed.map == map && placed.cell == target) return false;

        if (!InventoryManager.Instance.TryRemove(item)) return false;
        CreateAt(map, item, target);
        AudioManager.Instance?.PlayTorchSound(true);
        if (item.item == global::Item.Torche) MetaProgressionRuntime.RecordPlaced(MetaProgressionRuntime.TorchesPlaced, CountTorches(map));
        return true;
    }

    // Lava lamps are excluded: the challenge counts torches standing in the map at once (high-water mark).
    public static int CountTorches(MapGenerator owner)
    {
        int count = 0;
        foreach (var torch in active) if (torch && torch.map == owner && !torch.IsLavaLamp) count++;
        return count;
    }

    public static void CreateAt(MapGenerator map, ItemSO item, Vector3Int target)
    {
        if (!map || !map.Terrain || !item) return;
        bool lava = item.item == global::Item.LavaLamp;
        var sprite = lava ? item.icon : Resources.Load<Sprite>("Torches/TorchHolderSprite");
        Vector2 targetCenter = map.Terrain.GetCellCenterWorld(target);
        var instance = new GameObject(lava ? "Placed Lava Lamp" : "Placed Torch");
        instance.SetActive(false);
        instance.transform.SetParent(map.transform, false);
        instance.transform.position = new Vector3(targetCenter.x, targetCenter.y, 0f);
        var holder = new GameObject("Torch Holder");
        holder.transform.SetParent(instance.transform, false);
        holder.transform.localScale = Vector3.one * (lava ? .5f : HolderScale);
        if (!lava) holder.transform.localPosition = new Vector3(0f, HolderOffsetY(map), 0f);
        var renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = -2;
        var torch = instance.AddComponent<PlacedTorch>();
        torch.map = map;
        torch.cell = target;
        torch.item = item;
        TorchFlame flame = null;
        if (!lava) { flame = instance.AddComponent<TorchFlame>(); flame.Initialize(map); }
        else instance.AddComponent<LavaLampGlow>();
        instance.SetActive(true);
        if (flame) flame.Play();
    }
    public static SavedPlacedLight[] Capture(MapGenerator owner)
    {
        var result = new List<SavedPlacedLight>();
        foreach (var light in active) if (light && light.map == owner && light.item)
            result.Add(new SavedPlacedLight { cell = light.cell, itemId = (int)light.item.item });
        return result.ToArray();
    }
    public static void Restore(MapGenerator owner, SavedPlacedLight[] lights)
    {
        if (lights == null) return;
        foreach (var saved in lights)
        {
            var source = StartingResourcesSettings.Resolve(saved.itemId);
            if (source && (source.item == global::Item.Torche || source.item == global::Item.LavaLamp)) CreateAt(owner, source, saved.cell);
        }
    }
}

public sealed class LavaLampGlow : MonoBehaviour
{
    SpriteRenderer glow;
    void Start()
    {
        var go = new GameObject("Molten Core"); go.transform.SetParent(transform, false);
        glow = go.AddComponent<SpriteRenderer>(); glow.sprite = Resources.Load<Sprite>("Exotics/LavaGlow");
        glow.sortingOrder = -1; go.transform.localScale = Vector3.one * .46f;
    }
    void Update()
    {
        if (glow) glow.color = new Color(1, .5f, .12f, .35f + .12f * Mathf.Sin(Time.time * 1.7f + transform.position.x));
    }
}
