# Kupfererz – gemalte Einzelblöcke

Stand 18.10.2026, Branches `claude/copper-tiles` und `claude/copper-cleanup`. Alle Kupfergrössen (Small, Medium, Large) sind gemalte Erzblöcke. Die früheren Shader-Adern für Kupfer (Linien, Erdwände, Kupferteppich, Erd-/Felskacheln) sind samt Code und Texturen entfernt.

## Aussehen

- Stil „M3 Cuprit“: facettierte, gediegene Kupferbrocken mit hellen Lichtflächen und roten Cuprit-Facetten. Die Grösse zeigt sich über Anzahl und Grösse der Brocken:
  - **Small:** ein kleines Nest (1 Hauptbrocken, 2–3 kleine), 2 Varianten.
  - **Medium:** ein grösseres Nest plus kleine Nebengruppe, 2 Varianten.
  - **Large:** Layout „L2 Zwei Nester“, 3 Varianten.
- Sprites: `Assets/GameObjects/Map/Blocks/Sprites/Ores/Painted/Copper/Copper_{Small,Medium,Large}_0N.png` (544 px, PPU 836 → 418 px = eine Zelle, der Rest ragt leicht in die Nachbarzellen).
- Tiles: `Assets/GameObjects/Map/Blocks/Ores/Copper/Kupfer_08..10_large`, `Kupfer_11..12_small`, `Kupfer_13..14_medium`, eingetragen in `Copper.asset` (`smallOre`/`mediumOre`/`richOre`, Anzahl unverändert 2/2/3) und im `SaveAssetCatalog`. Die alten Tiles `Kupfer_01..07` bleiben unverändert erhalten (alte Spielstände laden sie weiterhin).

## Technik

- **Marker:** Gemalte Erz-Tiles haben Tile-Farbe Alpha 254/255 (`PaintedOre.hlsl`). Ultronium nutzt Alpha < 0,99, normale Erze genau 1 – die Fälle überschneiden sich nicht. Der Wert wird mit dem Spielstand gespeichert und wiederhergestellt.
- **Shader** `OreOverlayLit.shader`: Tiles mit Marker gehen in `PaintedOreFragment` – Sprite in Originalgrösse (Erzgrösse `oreScale` wird zurückgerechnet), normal beleuchtet wie das Gestein, kein Reflexions-Boost, kein Schimmer. Überhang wird über die Belegungsmaske (`_TestOccupancy`) abgeschnitten, damit nichts in abgebaute Zellen hängt.
- Kupfer hat keine Sonderbehandlung mehr in `OreVeinField`, `OreOverlayAppearance` oder `OreVeinSurface.hlsl`. Die frühere Kupfer-Infrastruktur (`CopperVeinTopology`, `CopperOutlineField`, `CopperDepositPlans`, `CopperCoverField`, `CopperSurfaceRenderer`, `Copper*.hlsl`, Texturen unter `Resources/OreVeins/Copper*`, Kupfer-Surface-Pass und Kupfer-/Geländemasken-Eigenschaften im Shader) ist entfernt; die Shader-Adern der übrigen Erze sind unverändert.
- Spielstände: Das Feld `copperVisuals` (gespeicherte Kupfer-Ader-Pläne) gibt es nicht mehr. `JsonUtility` überspringt es in älteren Spielständen, sie laden weiterhin. Alte Kupfer-Tiles `Kupfer_01..07` aus solchen Spielständen erscheinen jetzt als normale Shader-Ader in Kupferfarbe (vorher blieben sie bei aktiven Adern unsichtbar, weil die Kupferoberfläche abgeschaltet war).
- Jede weitere Erzart kann so umgestellt werden: Sprite + Tile mit Marker-Farbe, den Rest übernimmt der Shader.

## Erzeugung und Prüfung

- Grafiken prozedural erzeugt mit `Tests/Archive/CopperOreArt.cs` (`InstallLarge`, `InstallSmallMedium`; erneutes Ausführen überschreibt die installierten Sprites). Designrunden und Vergleichsbilder: `Design/OreVeins/CopperLargeV3/` und `Design/OreVeins/CopperSmallMedium/`.
- Prüfskript: `Tests/CopperPaintedRuntimeChecks.cs` (frischer Play-Start ab Homescreen, isoliertes Testprofil): alle generierten Kupferzellen gemalt, alle Gesteinsarten, Überhang an abgebauten Zellen, Abbau, Speichern/Laden, Kamera.

## Offen

- Mehr Varianz (Spiegeln/weitere Varianten), damit sich lange Reihen nicht wiederholen.
- Die übrigen Erze (Eisen, Silber usw.) laufen noch über die Shader-Adern.
- Alte Spielstände mit `Kupfer_01..07` könnten beim Laden auf die gemalten Tiles umgestellt werden.
- Die alten Prüfskripte der Kupfer-Adern (`Copper*Checks.cs` ausser `CopperPaintedRuntimeChecks`) liegen in `Tests/Archive/` und laufen gegen den entfernten Code nicht mehr.
