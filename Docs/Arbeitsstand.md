# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code | Performance abschliessen: Ladebildschirm ohne Ruckler, keine Lag-Spikes, ≥ 100 FPS – pausiert, Zwischenstand im Changelog | claude/performance | Mining Game | Map (MapGenerator, Tilemap-Abonnenten, Rubble, Collider, Erzadern, Bäume, Gras), Loading, GPS-Codec/Schema, GameSaveSystem, AudioManager, Tests/Performance* | nein | 17.10.2026 19:45 |
| Claude Code (Cloud) | Challenges-Rework: Aufgaben-Baukasten, Stufen, wöchentliche Rotation, Monats-Challenges, Boni, gestufte Meilensteine (Konzept Docs/Challenges-Konzept.md) | claude/challenges-rework | Cloud | Progression (MetaProgression*, MetaChallengePeriods, Catalog), ChallengePanel/ChallengeArt, neue Zählstellen (Dynamit, Fackeln, Leitern, Rückkehr), GPS, Tests/*Challenge* | nein (Cloud-Workflow) | 18.10.2026 |

## Zuletzt abgeschlossen

- 22.10.2026, Claude Code (Cloud): main-Update gleich nach Play-Stopp, Hinweis im Game-Fenster bei neuem Stand, Stand („main aktuell“ / „N neue Commits“) in der Editor-Warteschlange und im PrivOrg-Modul, Code-Stand für jeden Editor (auch Testbank/Worktrees) in `editor-queue status`; Branch `claude/exciting-ramanujan-l0893x`, mit Freigabe des Benutzers in `main`.
- 22.10.2026, Claude Code: Energiemonolith nutzt das goldene Eingangsschild („Energie aufladen“, Klick lädt auf) wie Lager, Shop und Werkbank, altes „Energy“-Schild ausgeblendet; Branch `claude/energy-sign`, mit Freigabe des Benutzers per PR in `main`.
- 22.10.2026, Claude Code: Speicher-Wächter: Sind alle Mining-Game-Ordner zusammen > 100 GB, räumt ein geplanter Windows-Task automatisch auf (alte LFS-Versionen, erledigte Arbeitsordner ≥ 2 Tage unbenutzt, alte Build Tests und Szenen-Backups, git gc), siehe Docs/Speicher-Bereinigung.md; Branch `claude/disk-cleanup`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Neue Arbeitsordner entstehen ohne `Design/` und `AudioMastering/` (Git-Hook, rund 1,4 statt 4,6 GB; nachholen mit `git sparse-checkout add Design/<Thema>/`), Regel in AGENTS.md, siehe Docs/Arbeitsordner-ohne-Design.md; Hook eingerichtet, Branch `claude/worktree-sparse`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Werkbank craftet auch mit Materialien aus dem Lager (Anzeige „+N Lager“, zuerst Rucksack, GPS Werkbank → Lager umschaltbar), Branch `claude/workbench-storage`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Eingangsschilder (Werkbank, Shop, Lager) mittig über der sichtbaren Gebäudegrafik mit gleichem Dachabstand; Branch `claude/workshop-button-d81ccb`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Einstellungen, Pausemenü und F1 pausieren das ganze Spiel inkl. Audio (zentrale `GamePause`, Ambience und Stimme setzen fort, keine Energie verloren); Branch `claude/settings-pause-288b53`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Eine Haltezeit für Lager, Inventar und Hotbar (F1 → UI → „Item ziehen“ → „Haltezeit bis Ziehen“, 0 = beim Bewegen); kurzer Klick bleibt Klick; Branch `claude/drag-hold`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Warteschlangen-Dienst für PrivOrg startet mit dem ersten Unity Editor statt mit Windows (Editor prüft jede Minute, startet ihn auch nach Absturz neu), Branch `claude/queue-service-on-editor`.
- 22.10.2026, Claude Code (Cloud): Einstellungen (F1 und Editor-Fenster) mit sichtbarer Hierarchie: jede Ebene eine Stufe eingerückt, F1-Stufe breiter; Branch `claude/exciting-ramanujan-l0893x`.
