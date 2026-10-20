# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code | Performance abschliessen: Ladebildschirm ohne Ruckler, keine Lag-Spikes, ≥ 100 FPS – pausiert, Zwischenstand im Changelog | claude/performance | Mining Game | Map (MapGenerator, Tilemap-Abonnenten, Rubble, Collider, Erzadern, Bäume, Gras), Loading, GPS-Codec/Schema, GameSaveSystem, AudioManager, Tests/Performance* | nein | 17.10.2026 19:45 |
| Claude Code | Pop-Effekt beim Start des Ziehens (Item hebt sich kurz an) | claude/drag-pop | .claude/worktrees/drag-ghost | ItemDragGhost, CompactHud/InventoryUI/StoragePanel (Drag-Teil), Tests/ItemDragGhostChecks | ja (Testbank) | 20.10.2026 |
| Claude Code (Cloud) | Challenges-Rework: Aufgaben-Baukasten, Stufen, wöchentliche Rotation, Monats-Challenges, Boni, gestufte Meilensteine (Konzept Docs/Challenges-Konzept.md) | claude/challenges-rework | Cloud | Progression (MetaProgression*, MetaChallengePeriods, Catalog), ChallengePanel/ChallengeArt, neue Zählstellen (Dynamit, Fackeln, Leitern, Rückkehr), GPS, Tests/*Challenge* | nein (Cloud-Workflow) | 18.10.2026 |

## Zuletzt abgeschlossen

- 20.10.2026, Claude Code: Editor-Warteschlange: Benutzer sortiert Wartende per Drag & Drop um (PrivOrg, `move`), Knopf „Bitte abschliessen“ (5 min, bei laufendem Test einmalig 2 min mehr, Hook meldet es dem Chat), Frist ohne Editor-Nutzung 4 min; Branch `claude/editor-queue-reorder`.
- 20.10.2026, Claude Code: Gezogenes Item (Drag & Drop) sieht in Lager, Inventar und Hotbar genau aus wie im Slot (Lager zeigte Leiter/Fackel/Dynamit gespiegelt), gemeinsames `ItemDragGhost`; Branch `claude/drag-ghost`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code: Alte Spielstände bekommen beim Laden die gemalten Erze (gleiche Erzart, Grösse und Stelle, seedstabil), Branch `claude/painted-ore-migration-3f7f47`, in `main`.
- 20.10.2026, Claude Code: Pausemenü „Speichern“ speichert direkt in den aktiven Spielstand (keine Slot-Auswahl mehr, Rückfall nur ohne aktiven Slot); Branch `claude/save-active`, mit Freigabe des Benutzers in `main`.
- 20.10.2026, Claude Code: Lag beim Baumfällen behoben (Sammelsound wurde beim ersten Holz synchron geladen), Messskript `Tests/TreeChopSpikes.cs`, Branch `claude/tree-lag`, mit Freigabe des Benutzers in `main`.
- 20.10.2026, Claude Code: Testbank `Mining Game 2` für Chats (`Tools/editor-bench.sh`), der Editor von `Mining Game` bleibt dem Benutzer; Editor-Frist 2 min (Benutzer 10 min), Lebenszeichen nur durch Unity-Aufrufe; PrivOrg-Modul „Editor-Warteschlange“ mit lokalem Dienst im Autostart.
- 20.10.2026, Claude Code: Neue Regel „Befehlsaufrufe“ in `AGENTS.md` (Skripte/Git einzeln aufrufen, kein `git -C`, Suchen/Lesen mit eingebauten Werkzeugen, damit Freigaben greifen), Branch `claude/agents-befehlsaufrufe`, mit Freigabe des Benutzers in `main`.
- 20.10.2026, Claude Code: Lager-Oberfläche im Shop-/Werkbank-Stil (Rucksack links, Lager rechts, Entnahme bis 3× Traglast), schwebender Knopf „Lager öffnen“, Kupfer-Icon-Import repariert; Branch `claude/lager-ui`, mit Freigabe des Benutzers in `main` zusammengeführt, siehe Docs/Lager.md.
- 20.10.2026, Claude Code: Ordner `Mining Game` holt jede Minute automatisch den neuesten `main` (`Assets/Editor/MainAutoUpdate.cs`, Docs/Main-Auto-Update.md), Branch `claude/main-autoupdate`, in `main`.
- 20.10.2026, Claude Code: Debug-Einstellungen (F1/Editor-Fenster) werden sofort dauerhaft gespeichert und überleben Play-, Domain- und Editor-Neustart; Branch `claude/debug-persist`, in `main`.
