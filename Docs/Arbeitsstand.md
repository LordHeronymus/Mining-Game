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

- 22.10.2026, Claude Code: Eingangsschilder (Werkbank, Shop, Lager) mittig über der sichtbaren Gebäudegrafik mit gleichem Dachabstand; Branch `claude/workshop-button-d81ccb`, PR offen, `main` erst nach Freigabe.
- 22.10.2026, Claude Code: F1 → UI → „Item ziehen“: Pop-Stärke, Grösse beim Tragen und Pop-Dauer einstellbar (Haltezeit weiterhin unter UI → Hotbar); Rechtsklick auf ein Hotbar-Item nimmt es aus der Hotbar (bleibt im Inventar); Branch `claude/drag-settings`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code (Cloud): Sprachausgabe des Charakters (169 Clips, Sprach-System mit Abklingzeit/Priorität, Auslöser an Energie, Traglast, Sturz, Shop, Werkbank, neue Welt; Regler „Stimme“, GPS/F1), Branch `claude/exciting-ramanujan-l0893x`, mit Freigabe des Benutzers in `main` zusammengeführt; lokale Hörabnahme und Commit des aktualisierten `GpsProfile.asset` ausstehend, siehe Docs/Sprachausgabe.md.
- 20.10.2026, Claude Code: Gezogenes Item poppt beim Start des Ziehens kurz auf (128 % → 112 %) und wird deckend mit Schatten getragen (Lager, Inventar, Hotbar); Branch `claude/drag-pop`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code: Testbank-Dialoge: `Tools/editor-dialog.sh list/press` erkennt und schliesst modale Unity-Dialoge der Testbank, Wächter im Queue-Dienst und in `wait` beantwortet bekannte (z. B. „modified externally“ → Reload), meldet unbekannte; `editor-bench` schliesst die Szene vor dem Wechsel; Branch `claude/bench-dialogs-62a367`, bereit für `main` (Freigabe Benutzer), Dienst danach mit `-Install` neu einrichten.
- 20.10.2026, Claude Code: Autosave alle 3 min Spielzeit (GPS Spieler → Autosave), Pausemenü ohne „Speichern“, „Hauptmenü“ und Play-Stopp speichern automatisch; Branch `claude/autosave`, mit Freigabe des Benutzers in `main`.
- 20.10.2026, Claude Code: Editor-Warteschlange: Benutzer sortiert Wartende per Drag & Drop um (PrivOrg, `move`), Knopf „Bitte abschliessen“ (5 min, bei laufendem Test einmalig 2 min mehr, Hook meldet es dem Chat), Frist ohne Editor-Nutzung 4 min; Branch `claude/editor-queue-reorder`.
- 20.10.2026, Claude Code: Gezogenes Item (Drag & Drop) sieht in Lager, Inventar und Hotbar genau aus wie im Slot (Lager zeigte Leiter/Fackel/Dynamit gespiegelt), gemeinsames `ItemDragGhost`; Branch `claude/drag-ghost`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code: Alte Spielstände bekommen beim Laden die gemalten Erze (gleiche Erzart, Grösse und Stelle, seedstabil), Branch `claude/painted-ore-migration-3f7f47`, in `main`.
- 20.10.2026, Claude Code: Pausemenü „Speichern“ speichert direkt in den aktiven Spielstand (keine Slot-Auswahl mehr, Rückfall nur ohne aktiven Slot); Branch `claude/save-active`, mit Freigabe des Benutzers in `main`.
