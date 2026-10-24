# Systemübersicht

Wegweiser für neue Threads: welches System in welchen Dateien steckt. Pfade relativ zu `Assets/`, Skripte ohne `.cs`. Für Details die Feature-Dokus unter `Docs/` und die passenden `Tests/<Name>Checks.cs` (238 Stück, nach System benannt) ansehen. **Bei neuen Systemen oder Umzügen diese Datei nachführen.**

## Einstellungen (F1 / GPS)

- **Alle Spielwerte:** `Resources/Gameplay/GpsProfile.asset` (mehrere MB, nur gezielt nach Feldnamen suchen). Felder und F1-Beschriftungen: `GameObjects/GameManager/GpsSchema.Catalog`, `GpsSchema.Fields`, `GpsSchema`; Laden/Speichern: `GpsSettings`, `GpsProfile`, `GpsCodec`, `GpsFileStore`, `GpsUserOverrides` (eigene Werte des Benutzers, `Docs/GPS-Eigene-Werte.md`). Doku: `Docs/Gameplay-Einstellungen.md`.
- **F1-Fenster im Spiel:** `GameObjects/UI/GameplayDebugWindow` (+ `.Audio`), `GpsRuntimePanel` (+ `.Artifacts`, `.Audio`, `.Ores`), `GameplayDebugPanel`. Im Editor: `Editor/GpsProfileEditor`, `GpsEditorView` (+ `.Audio`, `.Shop`), `GameplaySettingsWindow`.

## Welt und Karte

- **Generierung:** `GameObjects/Map/MapGenerator`, `MapLayer`, `CaveGenerator`, `BoulderField`, `MapGenerationSampler`, `MapWorldBorders`, `ExoticWorldContent`, `UltroniumAltarChamber` / `UltroniumChamberLayout`.
- **Blöcke und Erze:** `Map/Blocks/BlockRegistry`, `BlockSO`, `OreTile`; Adern und Reichhaltigkeit: `OreVeins`, `OreVeinField`, `ConnectedOreVeins`, `VeinRichness` (`Docs/Erz-Reichhaltigkeit.md`); Optik: `OreOverlayAppearance`, `OreSparkles` (`Docs/Ore-Sparkles.md`, `Docs/Gemalte-Erze.md`), `Map/StoneTest/*` (Kanten, Geröll).
- **Gelände-Kollision:** `TerrainColliderChunks`, `TerrainCollisionShape`, `TerrainVariantSelector`.
- **Licht und Dunkelheit:** `Map/MapLighting` (Lichtfeld, Schacht, Oberfläche), `MapDarkness.shader`, `TorchLightField`, `GridDaylight`, `GameObjects/NightGlow`; Fackeln: `PlacedTorch`, `TorchFlame`.
- **Oberfläche:** Gras `Map/Grass/*`, Bäume `Map/Trees/*`, Leitern `Map/Ladders/LadderMap`, Dynamit `PlacedDynamite`, Artefakte `Map/Artifacts/*`, Karte des Spielers `PlayerMapDiscovery`.
- **Hintergrund, Himmel, Tiere:** `GameObjects/Background/*` (Parallax `Docs/Parallax-Hintergrund.md`, `SkyController`, `MoonlightController`, `NightSkyStars`, Vögel, Hasen, Glühwürmchen; `Docs/Surface-Background.md`), Untergrund `Background/Underground/FixedUndergroundBackground`.

## Spieler

- `GameObjects/Player/PlayerMovement` (Laufen, Springen, Kantenhilfe), `PlayerLadder`, `TileMiner` (Abbauen, Abbauklang), `MiningCrackVisual`, `MinerPlayerVisual` / `MinerGait`, `PlayerBaseStats`, Kamera `CameraFollow` / `CameraWorldBorderClamp`.
- **Werte im Lauf:** `GameObjects/GameManager/StatsManager` (Geld, Punkte, Artefakt-Fund), `EnergyManager`, `InventoryManager`, `UpgradeSettings`, `StartingResourcesSettings`.

## Gebäude, Handel, Herstellen

- **Shop (Kaufen/Verkaufen):** `GameObjects/Shop/*` (`BuyPage`, `SellPage`, `ShopPanel`), `GameManager/ShopManager`, Gebäude `Map/Shop/Shop Building`.
- **Werkbank und Baupläne:** `GameObjects/Workbench/*` (`CraftingCatalog`, `CraftingRecipe`, `RecipeUnlocks`, `WorkbenchPanel`). Rezepte: `Resources/WorkbenchRecipes/*.asset` und `GameObjects/Workbench/Recipes/*.asset`; **Preise (`shopPrice`) stehen doppelt: im Rezept und im `GpsProfile.asset`**, beide ändern.
- **Lager:** `Map/Storage/*` (`Docs/Lager.md`). Energie-Monolith: `GameObjects/EnergyMonolyth`.
- **Gegenstände:** `GameObjects/Items/ItemCatalog`, `ItemSO`, Unterordner je Art.

## Fortschritt

- **Level, Herausforderungen, exotische Baupläne:** `GameObjects/Progression/*` (`MetaProgression*`, `ChallengeEngine`, `ChallengeCatalog`, `MetaChallenge*`, `ExoticBlueprintCatalog`). Doku: `Docs/Progression.md`, `Docs/Challenges.md`.

## Speichern

- `GameObjects/Save/GameSaveSystem`, `RunSaveState`, `RunAutosave`, `SaveAssetCatalog` (Asset-Verweise alter Spielstände), `PaintedOreMigration`. Echte Spielstände: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Tiefenhall\Tiefenhall\Saves` (nie in Tests benutzen). Plattenplatz der Arbeitsordner: `Docs/Speicher-Bereinigung.md`.

## Oberfläche (UI)

- Alles unter `GameObjects/UI/` (Skripte) bzw. `UI/` (Grafiken). Wichtige: HUD `CompactHud`, `HudGemBar`, `MoneyDisplay`, `ChallengeTrackerHud`; Inventar `UI/Inventory/*`, `ItemFeed`; Menüs `MainMenuController`, `HomeUi`, `SaveSlotPanel`, `NewGamePanel`, `SettingsPanel`, `RunPauseMenu`, `GamePause`; Ladebildschirm `LoadingScreen`; Fortschritt `MetaProgressionPanel`, `ProgressionTimeline`; Artefakt-Anzeige `ArtifactDiscoveryView`; Knopftexte `ButtonTextCentering` (`Docs/Knopftexte.md`).

## Audio

- `AC Audio/AudioManager` (zentrale Lautstärken, `PlayClip`), Ambiente `SurfaceAmbience`, `FirstLayerAmbience`, `SecondLayerAmbience`, Fackelknistern `TorchCrackleAudio`, Ladebildschirm `LoadingAudio`, UI-Klicks `UiHoverAudio` / `HomeClickAudio`, Sprachausgabe `VoiceLine*` (`Docs/Sprachausgabe.md`), Abbauklänge `Map/LayerMiningAudioSettingsAsset`. Audiokatalog im GPS: `AC Audio/GpsAudio`, `AudioClipTuningSettingsAsset`.

## Szenen, Editor-Werkzeuge, Tests

- Szenen: `Scenes/MainMenu.unity`, `Scenes/SampleScene.unity` (LFS, nur gezielt suchen).
- Editor-Hilfen: `Editor/*` (Testbank `TestBench*`, Warteschlange `EditorQueueActivity`, main-Sync `MainAutoUpdate` / `MainSync*`, `AutoSave`).
- Prüfskripte: `Tests/<Name>Checks.cs` (lokal auf der Testbank, `Docs/Editor-Warteschlange.md`), Cloud: `Tests/CI/` (`Docs/Cloud-Tests.md`). Werkzeuge: `Tools/`.
