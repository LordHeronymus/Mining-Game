# Erz-Reichhaltigkeit: Small, Medium, Rich

Stand 23.10.2026 (Branch `claude/vein-richness`). Jede Erzzelle bekommt bei der Generierung eine Grösse (`OreRichness`): **Small** (1 Erz), **Medium** (1 oder 2, im Mittel 1,5) oder **Rich** (2). Die Grösse wählt die gemalte Kachel aus den Gruppen `smallOre`/`mediumOre`/`richOre` des Blocks (`Docs/Gemalte-Erze.md`).

## Regel: Anteile nach Aderdicke (`VeinRichness`)

Eine Regel für alle Adern (`Assets/GameObjects/Map/VeinRichness.cs`, aufgerufen in `OreVeins.BuildSteps` je zusammenhängender Ader):

1. **Dicke-Stufe:** Massgebend ist die dickste Stelle der Ader (grösster Abstand einer Zelle zum Aderrand): **dünn** = 1–2 Blöcke breit (kein Block rundum vom selben Erz umgeben), **mittel** = 3–4 Blöcke, **dick** = ab 5 Blöcken.
2. **Anteile:** Pro Stufe ein Medium- und ein Rich-Anteil in Prozent der Aderblöcke, der Rest bleibt Small.
3. **Mindestgrösse:** Kleinere Adern bleiben nur Small.
4. **Schwankung:** Die Anteile schwanken pro Ader zufällig um bis zu ± den eingestellten Prozentsatz (seedstabil). Bruchteile werden mit der passenden Wahrscheinlichkeit aufgerundet; so bekommen auch kurze Adern im Mittel ihren Anteil.
5. **Faktor je Erz:** multipliziert Medium und Rich einer Erzart (1 = Tabelle, 0 = nur Small).
6. **Platzierung:**
   - Mittlere und dicke Adern: Rich in die innersten Zellen, Medium in die nächsten Schichten darum, der Rand bleibt Small.
   - Dünne Adern haben kein Inneres: Rich (selten) nahe der Mitte, Medium gleichmässig im mittleren Teil der Länge („Streuung“, längster Weg durch die Ader als Achse), bevorzugt an dickeren Stellen und neben Rich. Aderspitzen (höchstens ein Erz-Nachbar) nur, wenn sonst kein Block mehr frei ist.
7. **Seedstabil:** nur `OreVeins.Hash` mit eigenen Streams, eindeutige Reihenfolge; `UnityEngine.Random` wird weder gelesen noch verändert.

Ohne Einstellungen (`OreVeins.Build(...)` ohne Regel, oder „Aktiv“ aus) gilt die frühere feste Verteilung: Abstand zum Rand normiert pro Ader, ±0,15 Perlin-Variation, unter 0,32 Small, unter 0,72 Medium, sonst Rich. Sie liess dünne Adern komplett Small, gab mittleren Adern einen Rich-Kern ohne Medium und dicken Adern wenig Rich.

## Einstellungen (GPS / F1)

GPS-Fenster bzw. F1 → **Erzverteilung → Erz-Anteile → „Medium und Rich nach Aderdicke“** (`GpsPreferences.veinRichness`, Klasse `VeinRichnessSettings`). Wirkt ab der nächsten neu generierten Welt. Die Standardwerte stehen im Code, bis der Abschnitt im GPS gespeichert wird.

| Feld | Bereich | Standard |
| --- | --- | --- |
| Aktiv | an/aus (aus = frühere feste Verteilung) | an |
| Mindestgrösse (Erzblöcke) | 1–200 | 4 |
| Dünn (1–2 Blöcke breit): Medium / Rich (%) | 0–100 | 33 / 4 |
| Mittel (3–4 Blöcke breit): Medium / Rich (%) | 0–100 | 34 / 8 |
| Dick (ab 5 Blöcken breit): Medium / Rich (%) | 0–100 | 36 / 12 |
| Schwankung je Ader (± %) | 0–100 | 25 |
| Streuung entlang dünner Adern (%) | 0 = alle in der Mitte, 100 = ganze Länge | 50 |
| Dicke Stellen bevorzugen | 0–1 | 0,65 |
| Faktor je Erz | Liste: Erz, Faktor 0–5 | leer (alle 1) |

Herkunft der Startwerte: Dünn, Streuung und „Dicke Stellen“ übernehmen die Werte, die der Benutzer am 23.10.2026 für dünne Adern eingestellt hat (1 Medium je 3 Blöcke ≈ 33 %, gemessen 32,6 %), plus 4 % Rich-Chance. Mittel und dick steigen davon aus leicht an, damit dünnere Adern immer weniger Medium und Rich haben als dickere; der Wert der mittleren Adern bleibt nahe am bisherigen.

Die frühere Einstellung „Erzverteilung → Dünne Adern“ (`thinVeinMedium`) gibt es nicht mehr; eigene Werte dafür in `UserSettings/GpsOverrides.json` fallen beim nächsten Start mit einer Konsolenmeldung weg.

## Statistik und Balance

**Heute gemessen** (Unity Checks #109, einmaliges Messskript `VeinRichnessBaselineChecks`, inzwischen in `VeinRichnessChecks` aufgegangen, 12 Seeds): dünne Adern 15 % aller Erzzellen, 0 % Medium / 0 % Rich; mittlere 57 %, 0 % / 23 %; dicke 28 %, 33 % / 8 %.

**Nachher** (Unity Checks #115, `Tests/VeinRichnessChecks.cs`, 30 Seeds, Welt 600 × 1200 mit GPS-Werten, Höhlen und Altarkammer):

| Dicke | Anteil Erzzellen | Medium vorher → nachher | Rich vorher → nachher | Wert je Zelle (Erz) |
| --- | --- | --- | --- | --- |
| dünn | 14,9 % | 0,0 → 32,2 % | 0,0 → 3,9 % | 1,000 → 1,200 (+20,0 %) |
| mittel | 57,0 % | 0,0 → 34,0 % | 22,8 → 8,0 % | 1,228 → 1,250 (+1,8 %) |
| dick | 28,1 % | 33,2 → 36,0 % | 7,5 → 12,0 % | 1,241 → 1,300 (+4,8 %) |
| **gesamt** | | 9,3 → 34,3 % | 15,1 → 8,5 % | 1,197 → 1,257 (**+4,9 %**) |

- Je Erz: Eisen +3,1 %, Kupfer +2,3 %, Gold +6,1 %, Silber +4,5 %, Platin +5,1 %, Kohle +4,5 %, Titan +3,7 %, Wolfram +2,8 %. Die seltenen Erze mit kleinen, dünnen Adern gewinnen am meisten: Diamant +19,1 %, Rubin +19,5 %, Granat +14,7 %, **Ultronium +14,1 %** (Siegbedingung). Wer das nicht will, setzt unter „Faktor je Erz“ z. B. Ultronium auf 0,5.
- Je Ebene: Ebene 1 +15,8 %, Ebene 2 +5,2 %, Ebene 3 +4,8 %, Ebene 4 +4,2 %, Ebene 5 +5,6 %.
- Rund 1,8 % der Medium/Rich in dünnen Adern liegen an einer Spitze, und nur, wenn in der Ader sonst kein Block mehr frei war (kurze Adern mit vielen Medium).
- **Laufzeit:** Reichhaltigkeit je Welt 13 ms vorher, 19 ms nachher (Aderwachstum 194 ms), Cloud-Editor ohne GPU. Die Ladezeit ändert sich nicht spürbar.

## Spielstände

Spielstände speichern die konkreten Erz-Tiles (`GameSaveSystem`, Erz-Ebene). Beim Laden wird die Reichhaltigkeit nicht neu berechnet; auch `PaintedOreMigration` übernimmt die gespeicherte Grösse. Bestehende Welten bleiben unverändert, nur neu generierte Welten nutzen die Regel.

## Prüfung

- `Tests/VeinRichnessChecks.cs` (Edit Mode, auch Cloud): synthetische Adern (Anteile je Stufe, Rich im Kern und Medium darum, dünne Adern ohne äusserste Spalten und Spitzen, Mindestgrösse, Faktor je Erz, Schwankung innerhalb ±25 %, „Aktiv“ aus = frühere Verteilung, Seed und `UnityEngine.Random`), danach bis zu 30 vollständige Welten: Anteile je Stufe innerhalb 3 bzw. 2 Prozentpunkten der Vorgabe, dünn < mittel < dick im Wert, Spitzen nur wenn nötig, Seed-Stabilität (zweimal generiert), Zufallszustand, Statistik je Erz und Ebene, Laufzeit. Bericht `Temp/VeinRichnessChecks.txt` mit `THIN`/`MID`/`THICK`-Zeilen (Aderraster vorher/nachher).
- `Tests/VeinRichnessPreviewChecks.cs` (Edit Mode, mit Grafik): rendert je zwei echte dünne, mittlere und dicke Adern mit den Spiel-Tiles vorher | nachher nach `Temp/VeinRichness/Vein_*.png` (im Cloud-Artefakt) und prüft, dass sich jedes Paar sichtbar unterscheidet.
- Cloud: Workflow „Unity Checks“, Eingabe `checks` = `VeinRichnessPreviewChecks, VeinRichnessChecks`. Lauf #113: zusätzlich `ConnectedOreVeinChecks`, `GpsSettingsChecks` (1238), `ChallengeSettingsChecks` (84) grün; Lauf #115: beide Checks grün.
- Vorschauen im Repo: `Design/OreVeins/VeinRichness/` – `Uebersicht.png` und `Vergleich_01..09_*.png` (je links vorher, rechts nachher; je drei dünne, mittlere, dicke Adern). Zusammengesetzt mit `compose_previews.py` aus den echten Sprites und den Zeilen in `veins.txt` aus Lauf #115, ohne Licht und Terrain-Shader. Frühere Vergleiche der reinen Dünn-Regel: `Design/OreVeins/MediumInThinVeins/`.
