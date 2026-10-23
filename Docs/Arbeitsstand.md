# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Keine feinen senkrechten Striche mehr im Hintergrund von Layer 2 (Sandstein): Striche steckten in den Pixeln des Bildes, bereinigte Variante `LowerSandstone_Clean.png` (Original bleibt), Layer 2 in GPS und `FixedUnderground.mat` zeigen darauf; Sichtprüfung im Spiel auf der Testbank (Vorher/Nachher in Tiefe 45), keine Konsolenfehler; Branch `claude/l2-hintergrund-striche`, mit Freigabe des Benutzers per PR #82 in `main` (Optik-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Traglast im HUD neu gestaltet (Variante B1 von 4, Wahl Benutzer): statt Holzplakette Rucksack-Symbol in der Spalte von Herz und Blitz und „12,5 / 40 kg“ in einer Grösse ohne Rahmen, mit dunkler Kontur und Schatten; über der Traglast orange, in der roten Stufe rot (Grenzen aus `InventoryManager.CarryStage`); `Tests/CarryWeightHudChecks.cs` auf der Testbank 30/30; Branch `claude/traglast-hud-b1`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Neuer Level-Up-Ton `LevelUp-ChargeTreasure.wav` (5 s, Aufladen, dann Schatz-Treffer bei 1,9 s, mit ElevenLabs erzeugt, Wahl des Benutzers), Medaille wartet das Aufladen ab und landet auf dem Treffer (`soundPeakSeconds`), 5 Level-Up-Prüfskripte lokal auf der Testbank grün; Branch `claude/level-up-sound`, mit Freigabe des Benutzers in `main`.
- 23.10.2026, Claude Code: Werkbank-Filterknopf „Herstellbar“ ohne Kästchen, Text zentriert, eingeschaltet nur oranger Auswahlrahmen (`WorkbenchPanel`); lokal auf der Testbank `Tests/WorkbenchCraftableButtonChecks.cs` grün (11/11); Branch `claude/herstellbar-knopf`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Bäume wachsen langsamer: Nachwuchs 200–480 s statt 100–240 s (halb so oft), volle Grösse nach 2 Stunden Spielzeit statt 15 Minuten (GPS „Wachstum“ 0,375 % pro Minute bei 45 % Grössenbonus); nur GPS-Standardwerte; Branch `claude/baum-nachwachsen`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code (Cloud): Kein Einsammel-Bling mehr beim Laden eines Spielstands (Startausrüstung wird beim Laden still vergeben), Doppelklick auf einen Spielstand unter „Spielstände“ startet ihn direkt; Prüfskript `Tests/LoadPickupFeedChecks.cs` in der Cloud grün (#187); Branch `claude/load-bling`, mit Freigabe des Benutzers per PR #75 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Musikregler neu skaliert: 100 % = bisherige 20 % (GPS-Wert `musicCeiling`, F1 → Audio → Gesamtpegel), gespeicherte Reglerstände einmalig umgerechnet (20 % wird 100 %, Musik bleibt gleich laut); Cloud-Lauf #184 `MusicRoutingChecks`/`RobustnessFixChecks` grün; Branch `claude/music-volume-scale`, mit Freigabe des Benutzers per PR #70 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Bauplan-Bilder im Shop mit 512 px und BC7 statt unkomprimiert (rund 270 MB weniger im Speicher), lokal auf der Testbank geprüft, kein sichtbarer Unterschied.
- 23.10.2026, Claude Code (Cloud): Shop-Karten „Kaufen“: Bauplan-Bilder alle gleich gross (sichtbares Pergament 160 Einheiten, unabhängig vom durchsichtigen Rand der Bilder und vom GPS-Icon-Faktor) und ganz im Kartenrahmen (`BuyPage.FitBlueprint`), Prüfskript `Tests/ShopBlueprintCardChecks.cs` in der Cloud grün (#182); Branch `claude/shop-blueprint-cards`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Knopf „Werkbank öffnen“ steht über der Dachspitze der Werkstatt statt über der Mitte des ganzen Bildes (das Bild ist leicht asymmetrisch, Abweichung etwa 17 px von 1536), `EntrySign` mit optionaler Achse, `WorkbenchBuilding.signAxis`; Cloud-Lauf #186 grün (`BughuntEntrySignChecks` mit neuer Achsen-Prüfung); Branch `claude/werkbank-knopf-mitte`, PR bereit für `main` (Freigabe und Sichtprüfung Benutzer).
