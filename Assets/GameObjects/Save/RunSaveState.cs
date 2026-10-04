using System;
using UnityEngine;

[Serializable] public struct SavedItem { public int id, count; }
[Serializable] public sealed class SavedInventory
{
    public SavedItem[] items;
    public int[] owned, powerups;
    public int carryingLevel, energyLevel;
}
[Serializable] public sealed class SavedStats
{
    public int money, points, artifactPoints;
    public bool won;
    public float health, medkitRemaining, damageCooldown, miningMultiplier;
    public string[] artifacts;
}
[Serializable] public struct SavedMiningCell
{
    public Vector3Int cell;
    public float progress, hitAgo;
    public int pendingDrop;
}
[Serializable] public struct SavedTree
{
    public int cell, variant, pendingWood;
    public Vector3 position;
    public float height, health, growth, sproutScale, sproutElapsed;
}
[Serializable] public sealed class SavedForest { public SavedTree[] trees; public float regrowth; }
[Serializable] public struct SavedGrass { public int cell; public bool herb; }
[Serializable] public sealed class SavedGrassland
{
    public SavedGrass[] patches;
    public int[] sites, herbSites;
    public float respawn, conversion;
}
[Serializable] public sealed class SavedStorage { public string key; public SavedItem[] items; }
[Serializable] public sealed class RunSaveState
{
    public int version = 1, seed, width, height;
    public MetaRunState progression;
    public ExoticWorldState exotics;
    public SavedPlacedLight[] placedLights;
    public GpsValue[] generationSettings;
    public long savedUtc;
    public float playedSeconds, energy;
    public Vector3 playerPosition;
    public Vector2 playerVelocity;
    public bool facingLeft;
    public SavedInventory inventory;
    public SavedStats stats;
    public int[] recipes, hotbar;
    public int selectedSlot;
    public UltroniumAltarChamber.SaveState altar;
    public PlayerMapDiscovery.State discovery;
    public SavedStorage[] storage;
    public Vector3Int[] torches;
    public SavedMiningCell[] mining;
    public SavedForest forest;
    public SavedGrassland grass;
}
