# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code (Cloud) | Leuchtstärke getrennt für normalen und Smart-Rahmen (F1) | `claude/leuchtstaerke-rahmen` | Cloud-Klon | `TileMiner.cs` (Cursor), `GpsSchema.Catalog.cs`, `GpsProfile.asset` (Cursor), `Tests/GoldCursorChecks.cs` | ja (Testbank) | 23.10.2026 18:20 |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Fortschrittsbalken der Challenge-Karten: Goldfüllung reicht links bis in die Spitze der Rille, nicht volle Balken enden rund mit leichtem Leuchten, volle Balken folgen beiden Spitzen; gilt auch für die HUD-Liste (dort ohne Spitze); Variante 4 von 4 (Wahl Benutzer); lokal auf der Testbank geprüft; Branch `claude/fortschrittsbalken`, mit Freigabe des Benutzers per PR #84 in `main` (Optik-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Auswahlrahmen (normal und Smart) leuchten: warmes Glühen (Variante A von 4, Wahl Benutzer), über der Dunkelheit gezeichnet und darum auch in völliger Dunkelheit sichtbar, aber keine Lichtquelle; `GoldCursorChecks` und `CursorHighlightEquivalenceChecks` lokal auf der Testbank grün; Branch `claude/leuchtende-auswahlrahmen`, mit Freigabe des Benutzers per PR #85 in `main`.
- 23.10.2026, Claude Code (Cloud): Bauplan-Sammlung zeigt bei erreichtem Level „Freigeschaltet“ statt „Im Fundpool“ (auch im Runabschluss), Hover-Tipp „Level 5 · in Höhlen findbar“; lokal auf der Testbank geprüft (BlueprintCollectionChecks 11/11, ProgressionTimelineChecks 89/89); Branch `claude/bauplan-freigeschaltet`, mit Freigabe des Benutzers per PR #83 in `main`.
- 23.10.2026, Claude Code (Cloud): Kantenhilfe für den Sprung in eine 1 Zelle höhere, knapp spielerhohe Seitennische: kurz vor der Kante wird der Spieler auf Nischenhöhe gesetzt und hineingeschoben, solange man in ihre Richtung drückt (`PlayerMovement`, GPS Bewegung → „Kantenhilfe“); Prüfskript `Tests/LedgeAssistChecks.cs` auf der Testbank grün (48/48), `WallClimbMovementChecks`/`GpsSettingsChecks` grün; Branch `claude/nischen-sprung`, mit Freigabe des Benutzers per PR #81 in `main` (Spielgefühl-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Keine feinen senkrechten Striche mehr im Hintergrund von Layer 2 (Sandstein): Striche steckten in den Pixeln des Bildes, bereinigte Variante `LowerSandstone_Clean.png` (Original bleibt), Layer 2 in GPS und `FixedUnderground.mat` zeigen darauf; Sichtprüfung im Spiel auf der Testbank (Vorher/Nachher in Tiefe 45), keine Konsolenfehler; Branch `claude/l2-hintergrund-striche`, mit Freigabe des Benutzers per PR #82 in `main` (Optik-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Traglast im HUD neu gestaltet (Variante B1 von 4, Wahl Benutzer): statt Holzplakette Rucksack-Symbol in der Spalte von Herz und Blitz und „12,5 / 40 kg“ in einer Grösse ohne Rahmen, mit dunkler Kontur und Schatten; über der Traglast orange, in der roten Stufe rot (Grenzen aus `InventoryManager.CarryStage`); `Tests/CarryWeightHudChecks.cs` auf der Testbank 30/30; Branch `claude/traglast-hud-b1`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Neuer Level-Up-Ton `LevelUp-ChargeTreasure.wav` (5 s, Aufladen, dann Schatz-Treffer bei 1,9 s, mit ElevenLabs erzeugt, Wahl des Benutzers), Medaille wartet das Aufladen ab und landet auf dem Treffer (`soundPeakSeconds`), 5 Level-Up-Prüfskripte lokal auf der Testbank grün; Branch `claude/level-up-sound`, mit Freigabe des Benutzers in `main`.
- 23.10.2026, Claude Code: Werkbank-Filterknopf „Herstellbar“ ohne Kästchen, Text zentriert, eingeschaltet nur oranger Auswahlrahmen (`WorkbenchPanel`); lokal auf der Testbank `Tests/WorkbenchCraftableButtonChecks.cs` grün (11/11); Branch `claude/herstellbar-knopf`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Bäume wachsen langsamer: Nachwuchs 200–480 s statt 100–240 s (halb so oft), volle Grösse nach 2 Stunden Spielzeit statt 15 Minuten (GPS „Wachstum“ 0,375 % pro Minute bei 45 % Grössenbonus); nur GPS-Standardwerte; Branch `claude/baum-nachwachsen`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code (Cloud): Kein Einsammel-Bling mehr beim Laden eines Spielstands (Startausrüstung wird beim Laden still vergeben), Doppelklick auf einen Spielstand unter „Spielstände“ startet ihn direkt; Prüfskript `Tests/LoadPickupFeedChecks.cs` in der Cloud grün (#187); Branch `claude/load-bling`, mit Freigabe des Benutzers per PR #75 in `main` (Klang-Abnahme lokal).
