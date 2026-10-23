# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Level-Fortschrittsbalken im Fortschritt-Menü: Balken dockt mit seiner Spitze am Level-Kästchen an statt darunter zu beginnen, Goldfüllung wie bei den Challenge-Balken (Spitze links, rundes Ende) und bleibt in der Rille; Variante A von 4 (Wahl Benutzer); `ProgressionTimelineChecks` 91/91 und `ChallengePanelPreviewChecks` (bis auf den bekannten main-Fehler) lokal auf der Testbank grün; Branch `claude/level-balken`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Kantenhilfe schiebt sanfter: neuer GPS-Wert Bewegung → „Kantenhilfe: Hineinschiebe-Tempo“ (Anteil Laufgeschwindigkeit, Standard 0,5 statt bisher volle Geschwindigkeit); `Tests/LedgeAssistChecks.cs` (prüft jetzt auch das Tempo, parkt offene Spiel-Panels) und `GpsSettingsChecks` auf der Testbank grün; Branch `claude/kantenhilfe-tempo`, mit Freigabe des Benutzers per PR #94 in `main` (Spielgefühl-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Eigene F1-Werte des Benutzers (20 Stück: Erze, Rezeptmengen, Leuchtstärke, Schwerkraft, Holzertrag, ein Klang) als neue Standardwerte in `GpsProfile.asset`; Profil liest sich zurück, sonst unverändert; Branch `claude/f1-werte-main`, mit Freigabe des Benutzers per PR #93 in `main`.
- 23.10.2026, Claude Code (Cloud): Fackeln tiefer in der Zelle (Flamme wird nicht mehr abgeschnitten), neue Flamme Variante C2 mit Funken und Rauch, Knistern mit ElevenLabs-Schleife, hörbar bis 4 Kacheln; Prüfskript `Tests/TorchFlameCrackleChecks.cs` und Klangtest lokal auf der Testbank grün; per PR #90 mit Freigabe des Benutzers in `main` (Optik- und Klang-Abnahme lokal offen).
- 23.10.2026, Claude Code (Cloud): Leben und Energie als Zahl rechts neben den Kugelreihen („100 / 100“, Variante A, Wahl Benutzer), gleiche Schrift, Grösse, Kontur und Schatten wie die Traglast; unter 25 % rot; Lebenszahl pulsiert zum Herzschlag, Energiezahl zum Warnpiepsen; `Tests/VitalValuesHudChecks.cs` 27/27 und `CarryWeightHudChecks` 30/30 lokal auf der Testbank; Branch `claude/vitalwerte-hud`, mit Freigabe des Benutzers in `main` (Puls/Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Alte Spielstände (z. B. „The One“) laden wieder: ein Bild, das im gespeicherten Kartenstand steht, es im Spiel aber nicht mehr gibt (altes Layer-2-Hintergrundbild nach PR #82), wird beim Laden durch das aktuelle ersetzt statt den Spielstand abzulehnen (`GpsSettings.NormalizeGeneration`); `Tests/SaveMissingAssetChecks.cs` (inkl. Kopie eines echten Spielstands) und die Spielstand-/GPS-Checks lokal auf der Testbank grün; Branch `claude/spielstand-fehlendes-bild`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Leuchtstärke des Auswahlrahmens getrennt einstellbar: F1 → Spieler → Cursor → „Leuchtstärke normal“ und „Leuchtstärke Smart“ (0 = unsichtbar, 1 = voll, darüber heller); `GoldCursorChecks`, `CursorHighlightEquivalenceChecks`, `GpsSettingsChecks` (1242) lokal auf der Testbank grün; Branch `claude/leuchtstaerke-rahmen`, mit Freigabe des Benutzers per PR #89 in `main`.
- 23.10.2026, Claude Code (Cloud): Traglast höchstens doppelt (vorher dreifach), Tempo sinkt ab der Traglast gleichmässig bis auf ein Drittel, Sprung voll beladen nie unter 1,2 Blöcke (eine Zelle hoch geht immer), rote Stufe ab 90 % der maximalen Last; vier neue GPS-Werte unter Bewegung → Traglast; `Tests/CarryOverloadChecks.cs` auf der Testbank grün (30); Branch `claude/traglast-ueberladung`, mit Freigabe des Benutzers per PR #80 in `main` (Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code (Cloud): Eingangsschilder (`EntrySign`) setzen die Rahmendichte vor der Textzentrierung, sonst blieben zu breite Seitenränder am Text hängen und `Tests/StorageUiChecks.cs` brach bei „Lager öffnen“ ab; Prüfung zeigt jetzt die Masse; lokal auf der Testbank `StorageUiChecks` 57/57 und `BughuntEntrySignChecks` grün; Branch `claude/lager-schildbreite`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Fortschritt-Menü ohne vorausgewählten Knopf: Beim Öffnen (Zeitleiste, Upgrades, Herausforderungen, Baupläne) und beim Zurückkehren ist kein Knopf mehr ausgewählt, damit leuchtet nur noch der Knopf unter der Maus; `MetaProgressionUiChecks` lokal auf der Testbank grün (232 Checks, Bauplan-Zählung im Prüfskript an die Sammlung ohne Baukomponenten angepasst); Branch `claude/fortschritt-keine-vorauswahl`, PR bereit für `main` (Freigabe Benutzer).
