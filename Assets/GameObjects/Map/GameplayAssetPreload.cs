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
        ("OreVeins/RockFissure", typeof(Texture2D)),
        // UltroniumAltarChamber
        ("UltroniumAltar/Chamber", typeof(Shader)), ("UltroniumAltar/DepositButtonGlow", typeof(Shader)),
        ("UltroniumAltar/ChamberWall", typeof(Texture2D)), ("UltroniumAltar/SanctuaryStone", typeof(Texture2D)),
        ("UltroniumAltar/Altar", typeof(Texture2D)), ("UltroniumAltar/DepositButtonFrame", typeof(Sprite)),
        ("UltroniumAltar/DepositButtonFrameActive", typeof(Sprite)), ("Audio/UltroniumAltarAmbience", typeof(AudioClip)),
        ("Audio/UltroniumChargeUp", typeof(AudioClip)), ("Audio/UltroniumFullCharge", typeof(AudioClip)),
        // GameSaveSystem (first save of a new run), ExoticWorldContent, TerrainEdgeRubble
        ("SaveAssetCatalog", typeof(SaveAssetCatalog)), ("CraftingCatalog", typeof(CraftingCatalog)),
        ("Exotics/BlueprintCache", typeof(Sprite)), ("TerrainEdgeRubble", typeof(Material)),
        // BoulderField, MapGenerator (storage hut): synchronous loads in loading stage 7 (15–21 ms, player build)
        ("Exotics/Boulder-Painted", typeof(Sprite)), ("Exotics/Boulder", typeof(Sprite)), ("Exotics/WorldLit", typeof(Material)),
        ("SurfaceStorage/StorageHut", typeof(Sprite)),
        // MiningCrackVisual (first mining hit: 75 ms in the player build), PlacedTorch, PlacedDynamite
        ("Mining/MiningCracksFiveStages", typeof(Texture2D)), ("Mining/MiningCracksLit", typeof(Material)),
        ("Torches/TorchHolderSprite", typeof(Sprite)), ("Exotics/LavaGlow", typeof(Sprite)),
    };
    // Read synchronously in the Awake of the game scene's UI panels (WorkbenchPanel, BuyPage, ShopVisualTheme,
    // CompactHud, SettingsPanel): mostly uncompressed 1254² textures, together over 150 MB, in the single frame that
    // integrates the scene. Requested from the main menu, so the scene finds them in memory.
    static readonly (string path, Type type)[] SceneStartAssets =
    {
        ("Workbench/WorkshopBackground-v2", typeof(Sprite)), ("Workbench/ItemGlow", typeof(Shader)),
        ("Shop/BuyFrames-v2", typeof(Texture2D)), ("Shop/BuyBackground-v2", typeof(Sprite)),
        ("GameOverCoin", typeof(Texture2D)), ("ArtifactDiscovery/TitleFont", typeof(TMPro.TMP_FontAsset)),
        ("ItemCatalog", typeof(ItemCatalog)),
        ("HUDv1/Tray", typeof(Sprite)), ("HUDv1/Divider", typeof(Sprite)), ("HUDv1/DividerCap", typeof(Sprite)),
        ("HUDv1/SocketNormal", typeof(Sprite)), ("HUDv1/SocketHover", typeof(Sprite)), ("HUDv1/SocketSelected", typeof(Sprite)),
        // BuyPage blueprint cards (BuyPage.Awake, Add calls)
        ("Shop/MedkitRecipe", typeof(Sprite)), ("Shop/BackpackRecipe", typeof(Sprite)), ("Shop/LoadBeltRecipe", typeof(Sprite)),
        ("Shop/HeavyDutyBootsRecipe", typeof(Sprite)), ("Shop/ReinforcedBackpackRecipe", typeof(Sprite)),
        ("Shop/SpringGreavesRecipe", typeof(Sprite)), ("Shop/LoadFrameRecipe", typeof(Sprite)), ("Shop/ExoskeletonRecipe", typeof(Sprite)),
        ("Shop/CrystalPendantRecipe", typeof(Sprite)), ("Shop/CopperEnergyBraceletRecipe", typeof(Sprite)),
        ("Shop/EnergyStorageVialRecipe", typeof(Sprite)), ("Shop/RuneBeltRecipe", typeof(Sprite)), ("Shop/CrystalHeartRecipe", typeof(Sprite)),
        ("Shop/CrystalHarnessRecipe", typeof(Sprite)), ("Shop/TravelMonolithRecipe", typeof(Sprite)), ("Shop/IronPickaxeRecipe", typeof(Sprite)),
        ("Shop/SteelPickaxeRecipe", typeof(Sprite)), ("Shop/TitaniumPickaxeRecipe", typeof(Sprite)), ("Shop/TungstenPickaxeRecipe", typeof(Sprite)),
        ("Shop/ObsidianPickaxeRecipe", typeof(Sprite)), ("Shop/MythrilPickaxeRecipe", typeof(Sprite)), ("Shop/DiamondPickaxeRecipe", typeof(Sprite)),
    };
    // The requests also keep the loaded assets referenced until their owners pick them up.
    static readonly List<ResourceRequest> requests = new();
    static readonly List<ResourceRequest> sceneStartRequests = new();

    public static void Begin()
    {
        if (requests.Count > 0) return;
        foreach (var (path, type) in Assets) requests.Add(Resources.LoadAsync(path, type));
    }

    // Called by the main menu: the requests load in the background while the player is still choosing.
    public static void BeginSceneStart()
    {
        if (sceneStartRequests.Count > 0) return;
        foreach (var (path, type) in SceneStartAssets) sceneStartRequests.Add(Resources.LoadAsync(path, type));
    }

    // Enter Play Mode without a domain reload keeps static state; start every play session with fresh requests.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRequests() { requests.Clear(); sceneStartRequests.Clear(); }
}
