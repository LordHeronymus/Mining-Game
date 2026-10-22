# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`. `Git Repos/Mining Game 3` ist ein solcher Worktree (kein Unity Editor) für Branch `claude/challenges-rework`, angelegt am 20.10.2026 für die lokalen Tests und den Merge des Challenges-Rework; nach dem Merge in `main` wird er nicht mehr gebraucht und darf entfernt werden (`git worktree remove "../Mining Game 3"`).

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code | Performance abschliessen: Ladebildschirm ohne Ruckler, keine Lag-Spikes, ≥ 100 FPS – pausiert, Zwischenstand im Changelog | claude/performance | Mining Game | Map (MapGenerator, Tilemap-Abonnenten, Rubble, Collider, Erzadern, Bäume, Gras), Loading, GPS-Codec/Schema, GameSaveSystem, AudioManager, Tests/Performance* | nein | 17.10.2026 19:45 |
| Claude Code (Cloud) | Fehlersuche über das ganze Spiel (Multi-Agent), nur bestätigte Fehler mit kleinen Änderungen beheben, Bericht `Docs/Fehlersuche-2026-10.md`; Level-/Meta-Progression und Erzadern-Verteilung im MapGenerator nur lesen (parallele Sitzungen `claude/level-system-check`, `claude/thin-vein-medium`) | claude/bughunt | Cloud | Speichern/Laden, Inventar/Hotbar/Drag & Drop, Lager, Werkbank, Shop, Energie/Gesundheit, Mapgenerierung (ohne Adern-Verteilung), Leitern/Fackeln/Dynamit, Pause/Einstellungen, Sprachausgabe, GPS-Persistenz, Tests/* | nein (Cloud-Checks) | 22.10.2026 22:35 |
| Claude Code | main-Sync robust: eigene F1/GPS-Anpassungen als nicht versionierte Überschreibungs-Schicht (UserSettings), Aktualisierung mit Sicherung lokaler Commits/Änderungen, Statusfenster, Protokoll – Testbank-Checks grün, PR offen, Abnahme im Editor des Benutzers nach dem Merge | claude/main-sync-2e953f | .claude/worktrees/settings-pause-288b53 | GpsSettings (Speichern), GpsUserOverrides, GpsProfileEditor, GpsRuntimePanel/GameplaySettingsWindow (Knöpfe), MainAutoUpdate/MainSync*, GpsDefaultsProposal, Docs/Main-Auto-Update.md, Docs/GPS-Eigene-Werte.md, AGENTS.md | nein (Testbank fertig) | 22.10.2026 |

## Zuletzt abgeschlossen

- 22.10.2026, Claude Code: Challenges-Rework (täglich 7, wöchentlich 8, monatlich 6 plus Monatsprojekt, Langzeit-Medaillen, neue Zählstellen, Tafel mit 4 Tabs, GPS) lokal in der Testbank geprüft (alle Challenge-Prüfungen grün, Tafel-Fehler beim Periodenwechsel behoben; 2 fremde UI-Checks offen, in Todoist), aktuelles `main` hineingeholt; Branch `claude/challenges-rework` (Ordner `Mining Game 3`), PR #6 bereit für `main` (Freigabe Benutzer), siehe Docs/Challenges.md.
- 22.10.2026, Claude Code: Autosave ohne Anhalten: Das Spiel läuft beim Autosave weiter (Kachelschichten zeilenweise erfasst, Änderungen währenddessen nachgeführt, Schreiben im Hintergrund), andere Speichervorgänge und Menüs warten nie darauf; Messskript `Tests/AutosaveStutterChecks.cs`; Branch `claude/autosave-stutter`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Pausemenü ohne „Laden“ (5 Knöpfe, Panel gekürzt, Statuszeile unter „Hauptmenü“), Vorschauen unter `Design/PauseMenu`; Branch `claude/pause-menu-five`, PR bereit für `main` (Freigabe Benutzer).
- 22.10.2026, Claude Code: Testbank springt nicht mehr auf ihren virtuellen Desktop (Play Unfocused in der Testbank, Fokus-Wächter im Queue-Dienst holt das Fenster des Benutzers zurück, `editor-focus.ps1 off/on`); Branch `claude/bench-desktop`, bereit für `main` (Freigabe Benutzer), Dienst neu installiert.
- 22.10.2026, Claude Code (Cloud): main-Update gleich nach Play-Stopp, Hinweis im Game-Fenster bei neuem Stand, Stand („main aktuell“ / „N neue Commits“) in der Editor-Warteschlange und im PrivOrg-Modul, Code-Stand für jeden Editor (auch Testbank/Worktrees) in `editor-queue status`; Branch `claude/exciting-ramanujan-l0893x`, mit Freigabe des Benutzers in `main`.
- 22.10.2026, Claude Code: Neue Leiter-Grafik „Grubenleiter“ (dunkles Eichenholz, Eisenplatten mit Nieten), ausgewählt aus 4 Varianten (Vorschauen unter `Design/Ladder`); Branch `claude/ladder-variants`, bereit für `main` (Freigabe Benutzer).
- 22.10.2026, Claude Code: Energiemonolith nutzt das goldene Eingangsschild („Energie aufladen“, Klick lädt auf) wie Lager, Shop und Werkbank, altes „Energy“-Schild ausgeblendet; Branch `claude/energy-sign`, mit Freigabe des Benutzers per PR in `main`.
- 22.10.2026, Claude Code: Speicher-Wächter: Sind alle Mining-Game-Ordner zusammen > 100 GB, räumt ein geplanter Windows-Task automatisch auf (alte LFS-Versionen, erledigte Arbeitsordner ≥ 2 Tage unbenutzt, alte Build Tests und Szenen-Backups, git gc), siehe Docs/Speicher-Bereinigung.md; Branch `claude/disk-cleanup`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Neue Arbeitsordner entstehen ohne `Design/` und `AudioMastering/` (Git-Hook, rund 1,4 statt 4,6 GB; nachholen mit `git sparse-checkout add Design/<Thema>/`), Regel in AGENTS.md, siehe Docs/Arbeitsordner-ohne-Design.md; Hook eingerichtet, Branch `claude/worktree-sparse`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Werkbank craftet auch mit Materialien aus dem Lager (Anzeige „+N Lager“, zuerst Rucksack, GPS Werkbank → Lager umschaltbar), Branch `claude/workbench-storage`, mit Freigabe des Benutzers in `main` zusammengeführt.
