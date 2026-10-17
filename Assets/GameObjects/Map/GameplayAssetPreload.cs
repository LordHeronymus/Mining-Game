using System;
using System.Collections.Generic;
using UnityEngine;

// Large assets that the first gameplay frames would otherwise read from disk synchronously (up to 40 ms in a
// single loading frame). They are requested in the background while the loading screen builds the map; their
// owners keep their normal Resources.Load calls, which then find the assets already in memory.
// A path that no longer exists only loses its preload, so keep this list in step with the owners.
public static class GameplayAssetPreload
{
    static readonly (string path, Type type)[] Assets =
    {
        // OreOverlayAppearance (vein art)
        ("OreVeins/RockFissure", typeof(Texture2D)), ("OreVeins/CopperCarpet/Copper", typeof(Texture2D)),
        ("OreVeins/CopperEarthWall/EarthWall", typeof(Texture2D)), ("OreVeins/CopperL1Sprite/CopperEmbedded", typeof(Texture2D)),
        ("OreVeins/CopperL1Sprite/CopperLarge", typeof(Texture2D)), ("OreVeins/CopperL1Sprite/CopperSmall", typeof(Texture2D)),
        ("OreVeins/CopperL2Sprite/CopperSmall", typeof(Texture2D)), ("OreVeins/CopperL2Sprite/CopperMedium", typeof(Texture2D)),
        ("OreVeins/CopperL2Sprite/CopperLarge", typeof(Texture2D)), ("OreVeins/CopperRockTiles/Combined", typeof(Texture2DArray)),
        // UltroniumAltarChamber
        ("UltroniumAltar/Chamber", typeof(Shader)), ("UltroniumAltar/DepositButtonGlow", typeof(Shader)),
        ("UltroniumAltar/ChamberWall", typeof(Texture2D)), ("UltroniumAltar/SanctuaryStone", typeof(Texture2D)),
        ("UltroniumAltar/Altar", typeof(Texture2D)), ("UltroniumAltar/DepositButtonFrame", typeof(Sprite)),
        ("UltroniumAltar/DepositButtonFrameActive", typeof(Sprite)), ("Audio/UltroniumAltarAmbience", typeof(AudioClip)),
        ("Audio/UltroniumChargeUp", typeof(AudioClip)), ("Audio/UltroniumFullCharge", typeof(AudioClip)),
        // GameSaveSystem (first save of a new run), ExoticWorldContent, TerrainEdgeRubble
        ("SaveAssetCatalog", typeof(SaveAssetCatalog)), ("CraftingCatalog", typeof(CraftingCatalog)),
        ("Exotics/BlueprintCache", typeof(Sprite)), ("TerrainEdgeRubble", typeof(Material)),
    };
    // The requests also keep the loaded assets referenced until their owners pick them up.
    static readonly List<ResourceRequest> requests = new();

    public static void Begin()
    {
        if (requests.Count > 0) return;
        foreach (var (path, type) in Assets) requests.Add(Resources.LoadAsync(path, type));
    }

    // Enter Play Mode without a domain reload keeps static state; start every play session with fresh requests.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRequests() => requests.Clear();
}
