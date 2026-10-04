using System.Collections.Generic;
using UnityEngine;

public static class BoulderField
{
    internal static readonly HashSet<MineBoulder> Active = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => Active.Clear();
    public static MineBoulder At(Vector2 point)
    {
        foreach (var boulder in Active) if (boulder && boulder.Health > 0 && boulder.Contains(point)) return boulder;
        return null;
    }
    public static void Blast(MapGenerator map, Vector2 position, float radius)
    {
        foreach (var boulder in new List<MineBoulder>(Active))
            if (boulder && boulder.Map == map && boulder.Health > 0 &&
                Vector2.Distance(position, boulder.HitPoint) <= radius + boulder.Radius) boulder.Break();
    }
}

public sealed class MineBoulder : MonoBehaviour
{
    public const float MaximumHealth = 12f;
    public float Health { get; private set; }
    public MapGenerator Map { get; private set; }
    public Vector2 HitPoint => transform.position;
    public float Radius { get; private set; }
    Vector3Int cell;
    BoxCollider2D body;
    SpriteRenderer visual;
    public static MineBoulder Create(ExoticWorldContent owner, SavedBoulder saved)
    {
        var go = new GameObject("Boulder"); go.transform.SetParent(owner.transform, false);
        go.layer = owner.Map.Terrain.gameObject.layer;
        var boulder = go.AddComponent<MineBoulder>(); boulder.Map = owner.Map; boulder.cell = saved.cell;
        boulder.Health = Mathf.Clamp(saved.health, 0, MaximumHealth);
        var terrain = owner.Map.Terrain;
        Vector3 a = terrain.GetCellCenterWorld(saved.cell), b = terrain.GetCellCenterWorld(saved.cell + new Vector3Int(1, 1, 0));
        go.transform.position = (a + b) * .5f;
        var size = new Vector2(Mathf.Abs(b.x - a.x) * 1.92f, Mathf.Abs(b.y - a.y) * 1.92f);
        boulder.Radius = size.magnitude * .5f;
        boulder.visual = go.AddComponent<SpriteRenderer>();
        boulder.visual.sprite = Resources.Load<Sprite>("Exotics/Boulder-Painted") ?? Resources.Load<Sprite>("Exotics/Boulder"); boulder.visual.sortingOrder = -1;
        var material = Resources.Load<Material>("Exotics/WorldLit"); if (material) boulder.visual.sharedMaterial = material;
        boulder.visual.drawMode = SpriteDrawMode.Sliced; boulder.visual.size = size;
        boulder.body = go.AddComponent<BoxCollider2D>(); boulder.body.size = size * new Vector2(.94f, .9f);
        boulder.UpdateVisibility(); return boulder;
    }
    void OnEnable() => BoulderField.Active.Add(this);
    void OnDisable() => BoulderField.Active.Remove(this);
    public bool Contains(Vector2 point) => body && body.enabled && body.OverlapPoint(point);
    public bool Hit()
    {
        if (Health <= 0 || GameplayInputBlocker.IsBlocked) return false;
        var inventory = InventoryManager.Instance;
        var hammer = StartingResourcesSettings.Resolve((int)Item.PercussionHammer);
        if (!hammer || !inventory || !inventory.IsPowerupUnlocked(hammer)) { AudioManager.Instance?.PlayBlockedMiningHit(); return false; }
        var energy = FindFirstObjectByType<EnergyManager>();
        if (!GameplayTestSettings.NoEnergyConsume)
        {
            float cost = 2f * MetaProgression.CurrentLoadout.miningEnergyMultiplier;
            if (!energy || energy.energy < cost) return false;
            energy.DrainEnergy(cost);
        }
        Health = Mathf.Max(0, Health - 1);
        AudioManager.Instance?.Play(SoundType.DigDeepStone, true);
        if (Health <= 0) Break();
        else UpdateVisibility();
        return true;
    }
    public void Break()
    {
        if (!body || !body.enabled) return;
        Health = 0; UpdateVisibility();
        AudioManager.Instance?.Play(SoundType.StoneBreak, true);
        if (MetaProgressionRuntime.RewardsAllowed) MetaProgression.RecordDiscovery("boulder:" + cell.x + ":" + cell.y);
    }
    void UpdateVisibility()
    {
        if (visual)
        {
            visual.enabled = Health > 0;
            visual.color = Color.Lerp(Color.white, new Color(.58f, .7f, .75f), 1 - Health / MaximumHealth);
        }
        if (body) body.enabled = Health > 0;
    }
    public SavedBoulder Capture() => new SavedBoulder { cell = cell, health = Health };
}
