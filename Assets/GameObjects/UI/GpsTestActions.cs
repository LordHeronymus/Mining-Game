using UnityEngine;

public static class GpsTestActions
{
    public static void PreviewLevelUp() => GameplayLevelUpPresentation.Preview();
    public static void SetHealth(float health) => StatsManager.Instance?.SetHealthForDebug(health);
    public static void DrainEnergy() { var energy = Object.FindFirstObjectByType<EnergyManager>(); if (energy && energy.stats) energy.DrainEnergy(energy.stats.MaxEnergy * .1f); }
    public static void FillEnergy() { var energy = Object.FindFirstObjectByType<EnergyManager>(); if (energy && energy.stats) energy.energy = energy.stats.MaxEnergy; }
    public static void GiveItem(ItemSO item, int amount) { if (item && amount > 0) InventoryManager.Instance?.Add(item, amount); }
    public static void PreviewClip(AudioClip clip)
    {
        if (!clip) return;
        var tuning = LoadingAudio.Settings ? LoadingAudio.Settings.FindHomeClip(clip) : null;
        if (tuning != null) LoadingAudio.PreviewHomeClip(tuning);
        else AudioManager.Instance?.PreviewClip(clip);
    }
    public static void EndScreen() => Object.FindFirstObjectByType<GameOverPanel>(FindObjectsInactive.Include)?.Show();
    public static void TeleportAltar()
    {
        var altar = Object.FindFirstObjectByType<UltroniumAltarChamber>();
        if (altar && altar.Layout.valid) Teleport(altar.AltarPosition + new Vector3(-altar.CellSize * 2.5f, altar.CellSize * .15f));
    }
    public static void TeleportSpawn() => TeleportSpawn(Object.FindFirstObjectByType<MapGenerator>(), Object.FindFirstObjectByType<PlayerMovement>());
    internal static void TeleportSpawn(MapGenerator map, PlayerMovement player)
    {
        if (!map || !player || !map.Terrain || !map.Terrain.layoutGrid) return;
        var terrain = map.Terrain; var cell = Vector3Int.zero; if (!terrain.HasTile(cell)) return;
        var center = terrain.GetCellCenterWorld(cell);
        float cellHeight = terrain.layoutGrid.transform.TransformVector(Vector3.up * terrain.layoutGrid.cellSize.y).magnitude;
        var collider = player.GetComponent<Collider2D>();
        float half = collider && collider.enabled ? collider.bounds.extents.y : cellHeight * .3f;
        float offset = collider ? collider.transform.TransformVector((Vector3)collider.offset).y : 0;
        Teleport(player, new Vector3(center.x, center.y + cellHeight * .5f + half - offset + .03f, player.transform.position.z));
    }
    static void Teleport(Vector3 position) => Teleport(Object.FindFirstObjectByType<PlayerMovement>(), position);
    static void Teleport(PlayerMovement player, Vector3 position)
    {
        if (!player) return;
        player.GetComponent<PlayerLadder>()?.Detach();
        var body = player.GetComponent<Rigidbody2D>(); if (body) { body.position = position; body.linearVelocity = Vector2.zero; }
        player.transform.position = position;
        // Auto sync is off: sync the collider first, or the next fall would start from the bounds before the teleport.
        Physics2D.SyncTransforms();
        player.RestartFallTracking();
    }
}
