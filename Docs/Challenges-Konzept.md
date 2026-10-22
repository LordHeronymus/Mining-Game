# Challenges-Rework: Konzept (freigegeben)

Vom Benutzer am 18.10.2026 freigegeben mit den Zusätzen: alle neuen Ideen übernehmen, XP-Werte wie vorgeschlagen, jede bestehende Challenge einzeln überarbeiten und auf Passung prüfen. Belohnung vorerst nur XP. Umsetzung und Ergebnis: `Docs/Challenges.md`.

## 1. Aufgaben-Baukasten

Jede Challenge besteht aus **Tätigkeit + Gegenstand/Ort + Menge + Schwierigkeit**. Ein zentraler Katalog (eine Definition pro Vorlage: ID, Name, Familie, Symbol, Mengen pro Stufe und Zeitraum, Freischaltung nach Schicht/Meta-Level, erlaubte Zeiträume) ersetzt die bisherigen parallelen Arrays. Alte Daten, IDs und Zählerpositionen bleiben kompatibel (nur anhängen).

## 2. Familien

- **Abbau:** Blöcke, bestimmtes Erz (Kohle bis Ultronium), Edelsteine, Blöcke in Schicht 2/3, Bäume, Gras.
- **Erkundung:** Tiefe, Gebiete, Schichten besuchen, Altäre, Entdeckungen, Hin- und Rückweg.
- **Handwerk:** herstellen, Werkzeuge, Ausrüstung, Upgrades (Spitzhacke, Energie, Tragkraft), Dynamit, Medkits, Fackeln, Leitern, Exotics ab Meta-Level 5.
- **Handel:** Verkaufserlös, Grossverkauf.
- **Artefakte:** abbauen, bestimmtes, verschiedene.

## 3. Neue Ideen (alle umsetzen)

Neue Zählstellen an echten Gameplay-Ereignissen, mit `RewardsAllowed` und Schutz gegen Doppelzählung nach dem Laden:

- **Sprengmeister:** Blöcke mit Dynamit gesprengt.
- **Lichtbringer:** Fackeln platziert.
- **Kletterer:** Leitern platziert.
- **Volle Taschen:** mit fast voller Tragkraft an die Oberfläche zurückkehren.
- **Ausdauer:** Runs beendet bzw. sicher zurückgekehrt.
- **Erzader leeren:** Erzadern vollständig abgebaut.
- **Vielfalt:** in einem Zeitraum X verschiedene Erzarten.
- **Ein-Run-Aufgaben:** z. B. Tiefe 300 in einem Run, 3000 Gold Verkauf in einem Run.

Nur wenn eine Idee nachweislich keine verlässliche Zählstelle hat, wird sie weggelassen und im Bericht begründet.

## 4. Überarbeitung aller bestehenden Challenges

Jede bestehende Challenge (52 Tagesvorlagen, 16 Wochen-, 17 Langzeit-, 9 Run-Ziele) wird einzeln geprüft: passt sie ins neue System? Name, Menge, Stufe, Familie, Freischaltung, Zählweise; Doppelungen zusammenlegen, unsinnige anpassen, Namen verbessern. Ergebnis als Tabelle (alt → neu, Begründung) in `Docs/Challenges.md`.

## 5. Stufen und XP

Alles über GPS/F1 einstellbar, die Werte gelten fürs Erste.

| Stufe | Farbe | Täglich | Wöchentlich | Monatlich |
| --- | --- | ---: | ---: | ---: |
| Leicht | grün | 300 | 1'500 | 5'000 |
| Mittel | blau | 600 | 3'000 | 10'000 |
| Schwer | violett | 1'200 | 5'000 | 20'000 |
| XXL (selten) | gold | 3'000 | 10'000 | – |

Mengen wachsen moderat mit dem Fortschritt (erreichte Schicht/Level).

## 6. Täglich

Wechsel um Mitternacht (lokal). 7 Aufgaben = 3 leicht, 3 mittel, 1 schwer; 5 % Chance, dass eine davon XXL wird. Familienmischung, keine Tätigkeit doppelt, möglichst keine Wiederholung vom Vortag. Tagesbonus, wenn alle erledigt sind: +1'000 XP.

## 7. Wöchentlich

Wechsel Montag 00:00 (lokal), jetzt rotierend aus eigenem Pool. 8 Aufgaben = 3 leicht, 3 mittel, 2 schwer. Wochenbonus +5'000 XP. Fleiss-Aufgabe: „an 5 verschiedenen Tagen mindestens 3 Tagesaufgaben erledigt“.

## 8. Monatlich (neu)

Wechsel am 1. des Monats 00:00 (lokal, Sommerzeit beachten). 6 Aufgaben = 2 leicht, 2 mittel, 2 schwer, dazu ein Monatsprojekt in 3 Etappen (z. B. Tiefenexpedition 300 → 600 → 1000; jede Etappe gibt XP, die letzte am meisten). Monatsbonus +15'000 XP.

## 9. Langzeit

Die bestehenden 17 Meilensteine bleiben und werden zu Bronze-/Silber-/Gold-Stufen erweitert, dazu Meilensteine für die neuen Tätigkeiten. Bereits erreichte Meilensteine und XP bleiben gültig. Run-Ziele bleiben (geprüft gemäss Punkt 4).

## 10. Fairness

Auswahl deterministisch pro Zeitraum und gespeichert (Laden, Level-Aufstieg und Run-Wechsel ändern sie nicht); nur Erreichbares (Schicht-Gates); kein Doppelzählen; Uhr zurückdrehen bringt nichts.

## 11. Bewahren

Verdiente XP und erledigte Aufgaben bleiben. Laufender Tag bzw. laufende Woche werden nicht umgestellt, das neue System greift ab dem nächsten Wechsel. Migration alter Profile (alle bisherigen Formate) verlustfrei. Effekte dürfen Unitys Zufallszustand der Mapgenerierung nicht verändern (eigenes `System.Random`).

## 12. Oberfläche

Tabs Täglich · Wöchentlich · Monatlich · Langzeit. Kacheln im bestehenden V3-Stil mit Stufenfarbe (Rand/Plakette). Countdown und Bonusleiste („5 / 7 erledigt → +1'000 XP“). Monatsprojekt als breite Kachel mit drei Etappen. Text muss in die Kacheln passen.

## 13. GPS

XP pro Stufe und Zeitraum, Boni, Anzahl pro Zeitraum und Stufe, XXL-Chance, Mengenwachstum (siehe `Docs/Gameplay-Einstellungen.md`, bestehende GPS-Muster).
