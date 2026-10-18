# Gemalte Erze – Einzelblöcke statt Shader-Adern

Stand 18.10.2026. Kupfer (Branch `claude/copper-tiles`, Aufräumen `claude/copper-cleanup`), Eisen, Silber, Gold, Kohle, Platin, Titan, Wolfram, Mythril, oranger Granat, Smaragd, Rubin und Diamant (Branch `claude/iron-tiles`) sind gemalte Erzblöcke. Offen ist nur noch Ultronium. Die übrigen Erze laufen noch über die Shader-Adern.

## Prinzip

- Jedes Erz hat pro Grösse fertig gemalte Sprites mit durchsichtigem Hintergrund: facettierte Brocken mit hellen Lichtflächen und Akzent-Facetten. Die Grösse zeigt sich über Anzahl und Grösse der Brocken:
  - **Small:** ein kleines Nest (1 Hauptbrocken, 2–3 kleine), 2 Varianten.
  - **Medium:** ein grösseres Nest plus kleine Nebengruppe, 2 Varianten.
  - **Large:** zwei Nester unterschiedlicher Grösse, 3 Varianten.
- Das Gestein kommt vom normalen Untergrund darunter. Deshalb funktionieren dieselben Sprites auf allen Ebenen und Gesteinsarten.
- Sprite-Format: 544 px, PPU 836 → 418 px = eine Zelle, der Rest ragt leicht in die Nachbarzellen.

## Erze

| Erz | Look | Sprites | Tiles (neu) |
| --- | --- | --- | --- |
| Kupfer | „M3 Cuprit“: orange Kupferbrocken mit roten Cuprit-Facetten | `Blocks/Sprites/Ores/Painted/Copper/Copper_{Small,Medium,Large}_0N.png` | `Ores/Copper/Kupfer_08..14` |
| Eisen | „E1 Stahl“: bläulich-graue Stahlbrocken mit blauschwarzen Facetten, etwas kantiger als Kupfer | `Blocks/Sprites/Ores/Painted/Iron/Iron_{Small,Medium,Large}_0N.png` | `Ores/Iron/Eisen_08..14` |
| Silber | „S1 Silber poliert“: helle, kühl-neutrale Silberbrocken mit dunkelgrauen Anlauffacetten, viele kleine Facetten | `Blocks/Sprites/Ores/Painted/Silver/Silver_{Small,Medium,Large}_0N.png` | `Ores/Silver/Silber_08..14` |
| Gold | „G2b Gold hell“: sattes Gold, rundere Nuggets mit grösseren Facetten, blassgelbe Elektrum-Akzentfacetten | `Blocks/Sprites/Ores/Painted/Gold/Gold_{Small,Medium,Large}_0N.png` | `Ores/Gold/Gold_08..14` |
| Kohle | „K2 Steinkohle geschichtet“: matte, kantige Brocken mit feinen Poren und schwach schimmernden Schichtbändern, ohne braune Steinchen | `Blocks/Sprites/Ores/Painted/Coal/Coal_{Small,Medium,Large}_0N.png` | `Ores/Coal/Kohle_08..14` |
| Platin | „P2 Platin violett facettiert“: grauweisse, kantige Brocken mit violetten Facetten | `Blocks/Sprites/Ores/Painted/Platinum/Platinum_{Small,Medium,Large}_0N.png` | `Ores/Platinum/Platin_08..14` |
| Titan | „T5a Eis lang“: Gruppen langer, spitzer Prismen (wie das alte Titan) in Eisblau mit weissen Spitzen | `Blocks/Sprites/Ores/Painted/Titanium/Titanium_{Small,Medium,Large}_0N.png` | `Ores/Titanium/Titanium_08..14` |
| Wolfram | „W1 Wolfram-Säulen“: grosse, gedrungene dunkle Säulenkristalle mit warmen Reflexen (wie das alte Wolfram) | `Blocks/Sprites/Ores/Painted/Wolfram/Wolfram_{Small,Medium,Large}_0N.png` | `Ores/Wolfram/Wolfram_08..14` |
| Mythril | „M2 Mythril tiefblau“: königsblaue Kristallgruppen mit türkisen Kristallen | `Blocks/Sprites/Ores/Painted/Mythril/Mythril_{Small,Medium,Large}_0N.png` | `Ores/Mythril/Mythril_08..14` |
| Oranger Granat | „G2 Granat blutorange“: tief rot-orange Facettenkörner mit goldenen Facetten | `Blocks/Sprites/Ores/Painted/OrangeGranat/OrangeGranat_{Small,Medium,Large}_0N.png` | `Ores/OrangeGranat/OrangeGranat_08..14` |
| Smaragd | „E2 Smaragd hell“: kurze, helle grüne Kristallgruppen mit mintfarbenen Spitzen | `Blocks/Sprites/Ores/Painted/Emerald/Emerald_{Small,Medium,Large}_0N.png` | `Ores/Emerald/Emerald_08..14` |
| Rubin | „R3 Rubin-Kristalle blutrot“: grosse, kräftige blutrote Prismen ohne dunkles Gestein, helle Kanten | `Blocks/Sprites/Ores/Painted/Ruby/Ruby_{Small,Medium,Large}_0N.png` | `Ores/Ruby/Ruby_08..14` |
| Diamant | „D2 Diamant-Oktaeder“: gedrungene, kühl-weisse Kristalle mit blauen Glanzflächen | `Blocks/Sprites/Ores/Painted/Diamond/Diamond_{Small,Medium,Large}_0N.png` | `Ores/Diamond/Diamond_08..14` |

Nummerierung je Erz: `_08..10_large`, `_11..12_small`, `_13..14_medium`. Eingetragen im Block-Asset (`smallOre`/`mediumOre`/`richOre`, Anzahl unverändert 2/2/3) und im `SaveAssetCatalog`. Die alten Tiles `_01..07` bleiben erhalten; alte Spielstände laden sie weiterhin und zeigen sie als Shader-Ader.

## Technik

- **Marker:** Gemalte Erz-Tiles haben Tile-Farbe Alpha 254/255 (`PaintedOre.hlsl`). Ultronium nutzt Alpha < 0,99, normale Erze genau 1 – die Fälle überschneiden sich nicht. Der Wert wird mit dem Spielstand gespeichert und wiederhergestellt.
- **Shader** `OreOverlayLit.shader`: Tiles mit Marker gehen in `PaintedOreFragment` – Sprite in Originalgrösse (Erzgrösse `oreScale` wird zurückgerechnet), normal beleuchtet wie das Gestein, kein Reflexions-Boost, kein Schimmer. Überhang wird über die Belegungsmaske (`_TestOccupancy`) abgeschnitten, damit nichts in abgebaute Zellen hängt.
- Die frühere Kupfer-Infrastruktur (`CopperVeinTopology`, `CopperOutlineField`, `CopperDepositPlans`, `CopperCoverField`, `CopperSurfaceRenderer`, `Copper*.hlsl`, Texturen unter `Resources/OreVeins/Copper*`, Kupfer-Surface-Pass) ist entfernt. Das Spielstand-Feld `copperVisuals` gibt es nicht mehr; `JsonUtility` überspringt es in älteren Spielständen.
- **Paletten-Optionen:** neben Farben und Glanz auch `porosity`/`poreScale` (runde Poren), `banding`/`bandWidth` (Schichtbänder) und `pebbles` (braune Steinchen rund um die Brocken, bei Kohle aus); `shape` (0 facettiert, 1 glatt poliert, 2 Würfelkristalle, 3 Kristallgruppen aus Prismen mit `crystalElong`/`crystalSpread`/`crystalDensity`/`crystalScale`); `iridescence` (Anlauffarben); `detail`/`edgeWhite`/`speckles` (zusätzliche weisse Facetten, Kanten, Sprenkel).
- **Neues Erz umstellen:** In `Tests/PaintedOreArt.cs` eine `Palette` anlegen (Haupt- und Akzentfarben, Glanz, Form-Parameter), mit `PreviewLarge` eine Vorschau rendern, danach `InstallOre(BlockType, Ordner, Tile-Präfix, Palette, Seeds…)` aufrufen (Sprites, Tiles mit Marker, Block-Asset, `SaveAssetCatalog`).

## Erzeugung und Prüfung

- Generator: `Tests/PaintedOreArt.cs` (Paletten `Copper`, `IronPalettes`, `GoldSilverPalettes`, `GoldG2Palettes`; `InstallLarge`/`InstallSmallMedium` für Kupfer, `InstallOre` mit `InstallIron`/`InstallSilver`/`InstallGold`/`InstallCoal`/`InstallPlatinum`/`InstallTitanium`/`InstallTungsten`/`InstallMythril`/`InstallGarnet`/`InstallEmerald`/`InstallRuby`/`InstallDiamond`, Vorschauen `PreviewLarge`, `PreviewSmallMedium`). Erneutes Ausführen einer `Install…`-Methode überschreibt die installierten Sprites. Ältere Kupfer-Fassung: `Tests/Archive/CopperOreArt.cs`.
- Designrunden und Vergleichsbilder: `Design/OreVeins/CopperLargeV3/`, `Design/OreVeins/CopperSmallMedium/`, `Design/OreVeins/IronLarge/`, `Design/OreVeins/GoldSilverLarge/`, `Design/OreVeins/CoalLarge/`, `Design/OreVeins/PlatinumLarge/`, `Design/OreVeins/TitaniumLarge/`, `Design/OreVeins/TungstenMythrilLarge/`, `Design/OreVeins/GemsLarge/`, Laufzeitbilder `Design/OreVeins/PaintedOres/Runtime/`.
- Prüfskript: `Tests/PaintedOreRuntimeChecks.cs` (frischer Play-Start ab Homescreen, isoliertes Testprofil): alle generierten Kupfer-, Eisen-, Silber-, Gold-, Kohle-, Platin-, Titan-, Wolfram- und Mythrilzellen gemalt (seltene Erze wie Mythril können in der Testwelt fehlen und werden dann über platzierte Testblöcke geprüft), alle Gesteinsarten, Überhang an abgebauten Zellen, Abbau, Speichern/Laden, Kamera.

## Offen

- Ultronium umstellen (eigener Shader-Pfad mit Pulsieren, Marker-Alpha < 0,99).
- Mythril und Smaragd sind in keiner Map-Ebene eingetragen und werden derzeit nicht generiert.
- Granat und Kupfer sowie Diamant und Titan liegen farblich nahe beieinander; im Spiel vergleichen.
- Kohle ist auf dem dunkelsten Gestein (Ebene 4) eher dezent; bei Bedarf hellere Schichtbänder oder eine Kontur.
- Mehr Varianz (Spiegeln/weitere Varianten), damit sich lange Reihen nicht wiederholen.
- Eisen hebt sich auf hellgrauem Gestein (Ebene „graues Gestein“) weniger stark ab als Kupfer; bei Bedarf dunklere Kontur oder Akzentfarbe.
- Kleine Steinchen und Kontaktschatten in den Sprites sind neutral braun; auf dunklen Ebenen könnten sie im Shader an das Gestein angepasst werden.
- Alte Spielstände mit `_01..07`-Tiles könnten beim Laden auf die gemalten Tiles umgestellt werden.
