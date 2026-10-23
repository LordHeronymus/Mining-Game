# Performance – Übergabe an die Cloud-Sitzung

Stand 18.10.2026, Branch `claude/performance` (main ist eingemergt). Ziel unverändert: nirgends unter 100 FPS, Laden ohne Ruckler, Optik identisch (Gleichheitsnachweise nach Muster `Tests/RubbleCacheEquivalenceChecks.cs`). Vorgeschichte: Changelog-Einträge „Performance Laden und Abbau, Zwischenstand (17.10.2026)“ und „Performance: Audio-Hintergrundladen, V-Sync-Takt, Messwerkzeuge (18.10.2026)“.

## Erledigt (18.10.2026)

- **Audio:** 25 Decompress-on-Load-Clips über 3 s (Home-/Lade-/Altar-Ambience, Meadow, LevelUp, GameOverMusic, ArtifactCollected, Ultronium, Layer-1-Details, Ghost Whispers u. a.) importieren mit „Load In Background“. `AudioManager.WarmGameplayClips` dekodiert kurze Clips weiter synchron in 2-ms-Scheiben und stösst bei langen Clips nur das Hintergrund-Dekodieren an. Klang unverändert (gleiches Format, gleiche Qualität).
- **LoadingWorkBudget:** Mit V-Sync und einem Bildschirm über 120 Hz zielt das Budget auf das Bildintervall (144 Hz: 6,9 ms), die Scheibe ist höchstens ein halbes Bild lang. Ohne V-Sync bleibt alles wie bisher (8,3 ms, 4 ms).
- **Benchmark-Player:** `-benchmarkProfile <datei>` (nur Development-Build) zeichnet den ganzen Lauf in Teilen von 1500 Frames auf. Der Player löscht nach dem Lauf seinen eigenen Spielstand-Slot. Sind alle 10 Slots belegt, bricht er mit FAIL ab, statt bis zum 300-s-Timeout zu warten. Das war die Ursache von zwei „Laden dauerte länger als 300 s“-Läufen.
- **Werkzeuge:** `Tests/BuildPerformanceBenchmark.cs` baut mit der zweiten Args-Zeile `development` einen Development-Player. Neu ist `Tests/BenchmarkProfileSpikes.cs` für die Auswertung dieser Profile (siehe unten).
- **Editor-Punkt 3 (Erzader-Kontur, Rubble beim Erkunden):** Im Editor-Profil (ProfilerHotspots, alle Phasen) liegt „Erkunden“ bei PlayerLoop p95 4,1 ms und max. 8,5 ms. Weder CopperOutlineField noch TerrainEdgeRubble erzeugen Spitzen über 8 ms. Kein Handlungsbedarf erkennbar.

## Messstand (IL2CPP-Build, 2560×1440, Ultra)

| Lauf | Laden <100 FPS | Szenenstart (max) | Stufe 1 max | Stufen 3–7 max | Stillstand Ø | Abbau normal Ø (max) | Abbau schnell Ø (max) | Erkunden Ø (max) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Vorher (Build 1, 18.10.) | 31 | 1071 ms | 25,6 ms | 16–25 ms | 403 | 401 (24) | 355 (97) | 358 (4,7) |
| Audio im Hintergrund (Build 2) | 30 | 706 ms | 9,5 ms | 14–36 ms | 312 | 349 (50) | 318 (24) | 349 (7) |
| Build 5/6 (Audio + Budget + Preload-Experiment) | 25–28 | 231–897 ms | 14–22 ms | 14–28 ms | 352–394 | 347–390 (9) | 222–244 (77) | 297–329 (27) |
| Build 5v, V-Sync 144 Hz | 25 | 236 ms | 13,9 ms (9 Frames) | ≤14 ms | 144 | 144 (13,9) | 144 (11) | 143 (21) |

- Der 370-ms-Frame im Hauptmenü (Dekodieren der Home-Ambience) ist seit Build 2 verschwunden.
- Die Spitzen in Ladestufe 1 (100/62 ms aus dem Zwischenstand, 25 ms in Build 1) liegen seit Build 2 unter 10 ms.
- Exakt der committete Stand (Audio + Budget, ohne Preload-Experiment) wurde nicht mehr separat gemessen. Bitte als Erstes nachmessen.
- Durchschnittswerte schwanken von Lauf zu Lauf um ±15 %. Jeder Lauf erzeugt eine neue Karte (zufälliger Seed).

## Cloud-Messung 23.10.2026 (Suite `performance`, ohne Zeichnen)

Stand mit Cursor-Cache und erweitertem `GameplayAssetPreload` (Commit 359a3ad + Build-Korrektur), Lauf #156. CPU-Framezeiten, nur mit anderen Cloud-Läufen vergleichbar.

| Phase | Ø ms | p95 | p99 | max |
| --- | --- | --- | --- | --- |
| Laden gesamt | 17,1 | 19,1 | 24,1 | 737 (Stufe 0) |
| Stillstand | 12,7 | 14,0 | 15,4 | 15,9 |
| Abbau normal | 12,5 | 13,7 | 15,1 | 61,8 |
| Abbau schnell | 15,0 | 18,4 | 22,5 | 112,5 |
| Erkunden | 15,9 | 17,5 | 18,6 | 22,4 |

Ladestufen max: 0 = 737 ms (Szenenstart, Punkt 1), 1 = 62 ms, 2 = 20, 3 = 24, 4 = 26, 5 = 31, 6 = 20, 7 = 138 ms. `CursorHighlightEquivalenceChecks`: Cursor-Texturen bitgleich (524'288 Pixel). Die Vergleichsmessung ohne die Änderungen (Lauf #157) brach am Zeitlimit ab (Build ohne Cache).

## Offene Punkte (nach Priorität)

### 1. Szenenstart hinter der Schwarzblende (0,2–1,3 s in einem Frame)
Profil (Editor, ProfilerHotspots): Der Frame „Application.LoadLevelAsync Integrate“ besteht hauptsächlich aus `Loading.AwakeFromLoad` (452 ms) und `GC.Collect` (190 ms, Szenenentladung):
- TileMiner.Awake 133 ms
- WorkbenchPanel.Awake 95 ms
- BuyPage.Awake 88 ms plus OnEnable 30 ms
- SettingsPanel.Awake 42 ms
- CompactHud.Awake 35 ms
- ShopPanel.Awake 14 ms

Die UI-Panels bauen ihre Elemente im Awake auf (AddComponent, TMP-Text, Glyph-Rasterung).

Ansätze:
- Panels erst bei Bedarf oder in Ladestufe 7 in Zeitscheiben aufbauen (Achtung: Die Spitze darf nicht einfach zum ersten Öffnen wandern).
- TMP-Glyphen vorwärmen.
- In TileMiner.Awake nachsehen, was 133 ms kostet (vermutlich Aufbau von Ressourcen oder Tabellen).

Der Frame liegt hinter der Schwarzblende, ist also unsichtbar, verlängert aber die schwarze Pause.

### 2. Einzelne Ladespitzen im Build (Stufen 1, 3–7, je 14–36 ms, einzelne Frames)
Aus dem Development-Build-Profil:
- **Stufe 1, 28 ms reiner Skriptcode** in `GenerateMapInPlay`, im dritten Frame der Stufe. Kandidaten: Abschnitt zwischen den Yields in `PrepareGeneratedMapSteps` (Stone-Prüfschleife, `Ensure*Overlay`, `AltarChamber.PrepareGeneration`, neue Arrays mit 720 000 Zellen). Im Editor nur 8 ms.
- **Stufe 5, 16,5 ms** `ITilemap.FindAllRefreshPositions` in einem einzelnen `SetTilesBlock` (plus `LargeAllocation.Free`).
- **Stufe 7:**
  - `InvokeGeneratedForLoading` mit `Loading.ReadObject` 15 ms und `ExoticWorldContent.GenerateSteps` mit `Loading.ReadObject` 21 ms, also synchrones Nachladen (Resources.Load) von BoulderField (`Exotics/Boulder-Painted`, `Exotics/WorldLit`), Lagerhütte und Ähnlichem.
  - `UniformStoneAppearance` (TerrainRubble) mit `Texture2DArray.GetPixels` 9,5 ms.
- **Ladestufe 0:** `WarmGameplayClips` dekodiert einen einzelnen kurzen Clip in 18,8 ms synchron (File.Read 10,7 ms).

### 3. Spielspitzen (einzelne Frames, Development-Build-Profil)
- **Erster Abbau-Treffer 129 ms:** `Loading.ReadObject` 75 ms (MiningCrackVisual lädt `Mining/MiningCracksFiveStages` und `Mining/MiningCracksLit` synchron) und `TileMiner.Update` 48 ms reiner Skriptcode (Ursache offen).
- **Abbau schnell 71 ms:** `SoundManager.LoadFMODSound` mit File.Read 30 ms. Ein Clip wird beim ersten Abspielen synchron von der Platte gelesen. Vermutlich ein Streaming-Clip (CaveAmbience, UndergroundClay, WindAmbience, CaveTribalSong) beim Start der Layer-Ambience.
- **OreSparkles.LateUpdate 24 ms** (ein Frame, Abbau schnell). `UniformStoneAppearance` (TerrainUpload) 14 ms.
- **Erkunden-Spitze 97 ms (aus dem Zwischenstand):** In sieben Läufen vom 18.10. nicht reproduziert (Erkunden max. 4,7–27 ms).
- **Neu und ungeklärt:** In Build 5 und 6 lagen 3,5–7 s nach Beginn von „Abbau schnell“ 76 bzw. 132 Frames anhaltend über 10 ms (p95 8–10 ms). In Build 1–3 und in den Development-Läufen trat das nicht auf. Verdacht: Preload-Experiment (siehe unten) oder kartenabhängig. Mit dem committeten Stand prüfen.

### 4. Build-Nachmessung des Audio-Vorwärmens
Erledigt für Stufe 1 und Hauptmenü (siehe Messstand). Offen bleibt der 18,8-ms-Frame beim synchronen Vorwärmen eines kurzen Clips (Punkt 2).

## Verworfene oder nicht übernommene Versuche

- **Alle Clips mit „Load In Background“ (81 weitere) und Streaming-Clips als Compressed in Memory mit Preload:** Szenenstart 1,3 s, Spitzen von 80 ms in Stufe 3 und 7. Die Hintergrund-Ladeanfragen für Audio teilen sich die Warteschlange mit `LoadSceneAsync`, und der Ladebildschirm setzt `backgroundLoadingPriority` auf Low. Zurückgenommen. Für die Streaming-Clips ist eher ein früher, stummer Start oder ein Aufwärmen pro AudioSource zu prüfen.
- **Preload-Erweiterung in `GameplayAssetPreload`:** nicht committet, weil sie unbestätigt ist und der Verdacht aus Punkt 3 besteht. Diff:
  ```
  // BoulderField, MapGenerator (storage hut), TerrainCollisionShape
  ("Exotics/Boulder-Painted", typeof(Sprite)), ("Exotics/Boulder", typeof(Sprite)), ("Exotics/WorldLit", typeof(Material)),
  ("SurfaceStorage/StorageHut", typeof(Sprite)), ("TerrainEdgeMasks", typeof(Texture2DArray)),
  // MiningCrackVisual (first mining hit: 75 ms in the player build), PlacedTorch, PlacedDynamite
  ("Mining/MiningCracksFiveStages", typeof(Texture2D)), ("Mining/MiningCracksLit", typeof(Material)),
  ("Torches/TorchHolderSprite", typeof(Sprite)), ("Exotics/LavaGlow", typeof(Sprite)),
  ```
  Diese Erweiterung adressiert die ReadObject-Spitzen (erster Treffer, Stufe 7). Mit einem A/B-Vergleich bei festem Seed nachmessen.
- **Player-Profil über `-profiler-enable -profiler-log-file`:** Die Datei enthielt nur die ersten 3 Frames. Stattdessen `-benchmarkProfile` verwenden.
- **Ladespitzen im Editor-Profil:** Viele sind Editor-Artefakte (Mono.JIT, synchrones Nachladen von Assets). Ladespitzen immer im Build bewerten.

## MetaProgressionCoreChecks in der Cloud („worker owns detached snapshot while play continues“)

Einschätzung: Wahrscheinlich nicht die JSON-Kodierung des Spielstands im Hintergrund. Diese betrifft `GameSaveSystem` (Run-Spielstand), der Check prüft aber `MetaProgression` (Profil). Dessen Hintergrund-Schreiber (`RequestSave` mit `Task.Run(WriteProfile)`) wurde am 17.10. nicht geändert. `CopyProfile` kopiert tief, der Schnappschuss ist also wirklich losgelöst.

Wahrscheinlicher ist ein zweiter Speichervorgang innerhalb von `WaitForSave`:
1. `RecordResource(…, "b")` führt über XP zu einem `RequestSave` (MetaProgression.cs:334 bei Stufenaufstieg oder Meilenstein, oder MetaChallengePeriods.cs:175 bei einem Perioden- oder Challenge-Wechsel, der von der UTC-Uhrzeit abhängt).
2. Während der erste Schreibvorgang läuft, setzt das `saveAfterPending`.
3. `TickActive(0f)` in `WaitForSave` startet danach sofort den zweiten Schreibvorgang (Zeile 226).
4. Die Datei enthält dann count 2.

Lokal hängt das vom Datum oder Zeitpunkt ab, in der Cloud vom Runner-Timing oder vom UTC-Datum.

Prüfen: `saveAfterPending` und `revision` nach `RecordResource b` loggen. Danach den Check deterministisch machen (Challenge-Uhr per `ChallengeTestUtc` fixieren oder nur auf den ersten Schreibvorgang warten), nicht die Spiellogik ändern.

## Messwerkzeuge

- **`Tests/PerformanceBenchmarkChecks.cs`** (Editor, Play Mode): Frischer Play-Start ab MainMenu, danach `unity command run_script --file Tests/PerformanceBenchmarkChecks.cs`. Bericht in `Temp/PerformanceBenchmark.txt` und `.csv`. Danach in Edit Mode `GameSaveSystem.TestDirectory` und `MetaProgression.TestDirectory` auf `null` setzen. Nach dem Neukompilieren zuerst einen Aufwärmlauf machen (JIT).
- **`Tests/ProfilerHotspots.cs`** (Editor, Play Mode, vor dem Benchmark starten): `Temp/ProfilerHotspots.args` mit `phase=*`, `spikes=8`, `loadingSpikes=13`. Bericht in `Temp/ProfilerHotspots.txt` mit Phasenaufteilung und Aufrufbaum jeder Spitze. Editor-Artefakte (Mono.JIT, ReadObject) abziehen.
- **`Tests/BuildPerformanceBenchmark.cs`** (Editor, Edit Mode, nur Windows):
  - `Temp/BuildPerformanceBenchmark.args` enthält in Zeile 1 den Ausgabeordner, zum Beispiel `C:\IT\GameDev\Git Repos\Mining Game\Build Tests\Benchmark`, optional in Zeile 2 `development`.
  - Der `run_script`-Aufruf meldet nach 30 s ein Timeout, der Build läuft aber weiter. Auf „Build Finished“ im Editor.log warten. Kein `--timeout` setzen: Damit wurde ein Build abgebrochen.
  - Danach `git status ProjectSettings` prüfen und `AssetDatabase.SaveAssets()` ausführen, damit `productName` „Tiefenhall“ bleibt.
  - Messen: `"Build Tests\Benchmark\Tiefenhall Benchmark.exe" -performanceBenchmark <report.txt> -benchmarkVsync 0` (oder `1`). Development-Player zusätzlich mit `-benchmarkProfile Temp/BenchDev`.
- **`Tests/BenchmarkProfileSpikes.cs`** (Editor, Edit Mode): `Temp/BenchmarkProfileSpikes.args` mit Präfix (`Temp/BenchDev`) und Schwelle in ms (`9`). Schreibt `Temp/BenchmarkProfileSpikes.txt`. `P<teil> F<frame>` entspricht der CSV-Zeile `teil * 1500 + frame`. Die Frames 1500, 3000 usw. sind Artefakte des Dateiwechsels.
- **Cloud:** Builds und Player-Messungen brauchen Windows mit IL2CPP. Im Linux-Container der „Unity Checks“ laufen nur die Prüfskripte (siehe `Docs/Cloud-Tests.md`). Player-Messungen müssen lokal laufen oder brauchen einen Windows-Runner.
