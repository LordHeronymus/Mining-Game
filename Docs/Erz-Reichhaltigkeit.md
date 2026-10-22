# Erz-Reichhaltigkeit: Small, Medium, Rich

Stand 22.10.2026 (Branch `claude/thin-vein-medium`). Jede Erzzelle bekommt bei der Generierung eine Grösse (`OreRichness`): **Small** (1 Erz), **Medium** (1 oder 2, im Mittel 1,5) oder **Rich** (2). Die Grösse wählt die gemalte Kachel aus den Gruppen `smallOre`/`mediumOre`/`richOre` des Blocks (`Docs/Gemalte-Erze.md`).

## Grundlogik (`OreVeins.BuildSteps`, unverändert)

- Für jede Erzzelle zählt der Abstand zum Aderrand (4er-Nachbarschaft). Rand ist alles, was nicht dasselbe Erz ist: Gestein, Höhle, anderes Erz, Kartenrand.
- Pro zusammenhängender Ader wird der Abstand normalisiert, `Tiefe = (Abstand − 1) / (grösster Abstand − 1)`, dazu ±0,15 Perlin-Variation (ohne `UnityEngine.Random`). Unter 0,32 Small, unter 0,72 Medium, sonst Rich. Randzellen sind immer Small.
- Folgen:
  - **Dünne Adern** (höchstens zwei Blöcke dick, keine Zelle ist rundum vom selben Erz umgeben): alle Zellen liegen am Rand, die ganze Ader bleibt Small, egal wie lang sie ist. Das war die Goldader aus der Todoist-Aufgabe.
  - **Adern mit Rich-Kern** (drei bis vier Blöcke dick, grösster Abstand 2): Rand Small, Kern direkt Rich, kein Medium dazwischen.
  - Dickere Adern haben den vollen Verlauf Small → Medium → Rich.

## Medium in dünnen Adern (`ThinVeinMedium`, neu)

Läuft am Ende von `OreVeins.BuildSteps` je Ader (Datei `Assets/GameObjects/Map/ThinVeinMedium.cs`). Rich bleibt nach der bestehenden Logik; es werden nur Small-Zellen zu Medium.

- **Welche Adern:** dünne Adern (alle Zellen Small) ab der Mindestlänge = Anzahl Erzblöcke der Ader (Standard 8). Mit dem Schalter „Auch Adern mit Rich-Kern ohne Medium“ zusätzlich Adern, die Rich, aber kein Medium haben (Standard aus).
- **Wie viele:** `floor(Blöcke / Erzblöcke pro Medium + u)`, `u` seedabhängig zwischen 0 und 1, begrenzt auf Minimum und Maximum (Standard 1–4, ein Medium je 8 Blöcke): 8–15 Blöcke → 1–2, 16–23 → 2–3, 24–31 → 3–4, ab 32 → 4.
- **Wo:** Die Achse der Ader ist der längste Weg durch sie (zwei Breitensuchen); jede Zelle bekommt eine Position von 0 (ein Ende) bis 1 (anderes Ende). Die Zielpunkte liegen gleichmässig im mittleren Anteil der Ader („Streuung entlang der Ader“, Standard 60 %), die Mitte verschiebt sich je Ader leicht. Für jedes Ziel gewinnt die Small-Zelle mit der besten Bewertung: Nähe zum Ziel (in Blöcken), dickere Stelle (mehr Erz-Nachbarn rundum) bzw. neben Rich („Dicke Stellen bevorzugen“, Standard 0,5), etwas Rauschen. Aderspitzen (höchstens ein Erz-Nachbar) und direkt benachbarte Medium werden stark abgewertet.
- **Seedstabil:** Alles stammt aus `OreVeins.Hash` mit eigenen Streams (`0x7A1D0001`–`0x7A1D0004`), feste Reihenfolge, Gleichstand nach Zellindex. `UnityEngine.Random` wird weder gelesen noch verändert; gleicher Seed und gleiche Einstellungen ergeben dieselbe Belegung.
- **Kosten:** nur Adern, die in Frage kommen, werden genauer angeschaut (drei Breitensuchen über die Ader); Messung siehe unten.

## Einstellungen (GPS / F1)

GPS-Fenster bzw. F1 → **Erzverteilung → Dünne Adern → „Medium-Erze in dünnen Adern“** (`GpsPreferences.thinVeinMedium`, Klasse `ThinVeinMediumSettings`). Wirkt ab der nächsten neu generierten Welt. Die Standardwerte stehen im Code, bis der Abschnitt im GPS gespeichert wird.

| Feld | Bereich | Standard |
| --- | --- | --- |
| Aktiv | an/aus | an |
| Mindestlänge (Erzblöcke) | 2–200 | 8 |
| Medium min. pro Ader | 0–20 | 1 |
| Medium max. pro Ader | 0–20 | 4 |
| Erzblöcke pro Medium | 1–100 | 8 |
| Streuung entlang der Ader (%) | 0–100 (0 = alle um die Mitte, 100 = über die ganze Länge) | 60 |
| Dicke Stellen bevorzugen | 0–1 | 0,5 |
| Auch Adern mit Rich-Kern ohne Medium | an/aus | aus |
| Je Erz | Liste: Erz, Aktiv, Mindestlänge (0 = global), Medium max. (0 = global) | leer (alle Erze mit den globalen Werten) |

Liegt das Minimum über dem Maximum, gilt das Maximum.

## Spielstände

Spielstände speichern die konkreten Erz-Tiles (`GameSaveSystem`, Erz-Ebene). Beim Laden wird die Reichhaltigkeit nicht neu berechnet; auch `PaintedOreMigration` übernimmt die gespeicherte Grösse. Bestehende Welten bleiben deshalb unverändert, nur neu generierte Welten nutzen das neue Muster.

## Statistik und Balance

Cloud-Lauf „Unity Checks“ #96 (22.10.2026), `Tests/ThinVeinMediumChecks.cs`: 30 Seeds, Welt 600 × 1200 mit den GPS-Werten, Höhlen und Altarkammer wie im Spiel, Standardeinstellungen. „Lange Adern“ = ab 8 Blöcken.

**Vorher (betrifft alle Erzarten):** 9,0 % der langen Adern waren komplett Small (8'830 von 98'125). Am stärksten die seltenen Erze mit kleinen Adern und die oberste Ebene:

| Erz | lange Adern nur Small | | Ebene | lange Adern nur Small |
| --- | --- | --- | --- | --- |
| Rubin | 50,6 % | | Ebene 1 | 39,3 % |
| Diamant | 46,3 % | | Ebene 2 | 11,2 % |
| Ultronium | 25,0 % (7 von 28) | | Ebene 3 | 9,3 % |
| Oranger Granat | 15,9 % | | Ebene 4 | 5,8 % |
| Gold, Platin, Kohle, Silber | 10,6–10,8 % | | Ebene 5 | 11,1 % |
| Titan | 7,8 % | | | |
| Eisen, Kupfer, Wolfram | 3,6–4,7 % | | | |

Diamant, Rubin, Granat und Ultronium hatten vorher überhaupt keine Medium-Zellen (0,0 %).

**Nachher:** alle 8'830 dünnen Adern ab 8 Blöcken haben 1–4 Medium (100 %), keine blieb nur Small. Von 11'092 neuen Medium liegt keines an einer Aderspitze; Abstand zum nächsten Aderende 0–10 % der Länge: 1, 10–20 %: 319, 20–30 %: 1'495, 30–40 %: 3'961, 40–50 % (Mitte): 5'316.

**Wert:** Medium bringt im Mittel 1,5 statt 1 Erz. Der erwartete Erzertrag pro Welt steigt um **+0,21 %** (Medium-Anteil 9,3 % → 9,8 %); je Erz +0,09 % (Eisen, Kupfer) bis +0,55 % (Diamant), Ultronium +0,10 % (rund 0,2 betroffene Adern pro Welt, Siegbedingung praktisch unverändert). Je Ebene +0,15 % bis +0,89 % (Ebene 1). Keine Ausgleichsmassnahme nötig.

**Schalter Rich-Kern (Standard aus):** 75 % der langen Adern (73'598) haben einen Rich-Kern, aber kein Medium (drei bis vier Blöcke dick). Mit dem Schalter bekämen auch sie 1–4 Medium: Medium-Anteil 16,8 %, Wert **+3,12 %** (Rubin +0,98 % bis Platin +4,23 %). Das ist eine sichtbare Änderung über die dünnen Adern hinaus und deshalb nur zuschaltbar.

**Laufzeit:** Reichhaltigkeit je Welt 18 ms vorher, 19 ms nachher (+1 ms; zum Vergleich Aderwachstum 269 ms), gemessen im Cloud-Editor (ohne GPU, Mono). Die Ladezeit ändert sich nicht spürbar.

## Prüfung

- `Tests/ThinVeinMediumChecks.cs` (Edit Mode, auch Cloud): synthetische Adern (Anzahl, Spitzen, Lage, Rich unverändert, Je-Erz-Einträge, Minimum/Maximum, Seed, `UnityEngine.Random`), danach vollständige Welten wie im Spiel (GPS-Werte, Höhlen, Altarkammer) für bis zu 30 Seeds über alle Ebenen: Statistik vorher/nachher je Erz und Ebene, Seed-Stabilität (zweimal generiert), Zufallszustand, Wert, Laufzeit. Bericht `Temp/ThinVeinMediumChecks.txt` mit `VEIN`/`CORE`-Zeilen (Aderraster vorher/nachher).
- `Tests/ThinVeinMediumPreviewChecks.cs` (Edit Mode, mit Grafik): rendert echte Adern mit den Spiel-Tiles vorher | nachher nach `Temp/ThinVeinMedium/Vein_*.png` (im Cloud-Artefakt) und prüft, dass sich jedes Paar sichtbar unterscheidet.
- Cloud: Workflow „Unity Checks“, Eingabe `checks` = `ThinVeinMediumPreviewChecks, ThinVeinMediumChecks`.
- Vorschauen im Repo: `Design/OreVeins/MediumInThinVeins/` – `Uebersicht.png` und `Vergleich_01..12_*.png` (je links vorher, rechts nachher; 10 dünne Adern verschiedener Erze und Ebenen, 2 Rich-Kern-Adern mit Schalter). Zusammengesetzt mit `compose_previews.py` aus den echten Sprites (Gestein der Ebene, gemalte Erz-Tiles, gleiche Variantenwahl wie das Spiel) und den `VEIN`/`CORE`-Zeilen in `veins.txt` aus Lauf #96; ohne Licht und Terrain-Shader. Die im Editor gerenderten Standbilder liegen im Artefakt des Cloud-Laufs (`Temp/ThinVeinMedium/Vein_*.png`).
- Ergebnis Lauf #96: beide Checks grün (Statistik oben). Mitgelaufen: `ConnectedOreVeinChecks`, `GpsSettingsChecks` (1238), `ChallengeSettingsChecks` (84) grün; `OreOverlayChecks` („Ore has wrong substrate“) und `EditorMapGenerationChecks` („Grass variants missing from the scene“) scheitern an Gestein-Kacheln bzw. Gras-Varianten der Szene, nicht an den Erzgrössen.
