# Kupfererz – gemalte Einzelblöcke

Stand 18.10.2026, Branch `claude/copper-tiles`. Alle Kupfergrössen (Small, Medium, Large) sind gemalte Erzblöcke. Die früheren Shader-Adern für Kupfer (Linien, Erdwände, Kupferteppich, Erd-/Felskacheln) werden nicht mehr gezeichnet.

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
- `OreOverlayAppearance` bereitet die durchgehende Kupferoberfläche (`CopperSurfaceRenderer`) nicht mehr vor; sie bleibt versteckt.
- Jede weitere Erzart kann so umgestellt werden: Sprite + Tile mit Marker-Farbe, den Rest übernimmt der Shader.

## Erzeugung und Prüfung

- Grafiken prozedural erzeugt mit `Tests/Archive/CopperOreArt.cs` (`InstallLarge`, `InstallSmallMedium`; erneutes Ausführen überschreibt die installierten Sprites). Designrunden und Vergleichsbilder: `Design/OreVeins/CopperLargeV3/` und `Design/OreVeins/CopperSmallMedium/`.
- Prüfskript: `Tests/CopperPaintedRuntimeChecks.cs` (frischer Play-Start ab Homescreen, isoliertes Testprofil): alle generierten Kupferzellen gemalt, alle Gesteinsarten, Überhang an abgebauten Zellen, Abbau, Speichern/Laden, Kamera.

## Offen

- Mehr Varianz (Spiegeln/weitere Varianten), damit sich lange Reihen nicht wiederholen.
- Die Kupfer-Adern-Infrastruktur (Topologie, Deposit-Pläne, Teppich, Erdkacheln, Texturen unter `Resources/OreVeins/Copper*`) wird nicht mehr angezeigt und kann entfernt werden.
- Die übrigen Erze (Eisen, Silber usw.) laufen noch über die Shader-Adern.
- Die alten Prüfskripte `Tests/Copper*Checks.cs` (ausser `CopperPaintedRuntimeChecks`) prüfen die abgeschaltete Kupferoberfläche und schlagen jetzt fehl; nach `Tests/Archive/` verschieben oder löschen.
