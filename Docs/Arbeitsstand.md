# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner (git worktrees, beide von `main`): `Git Repos/Mining Game` und `Git Repos/Mining Game 2`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code | Performance abschliessen: Ladebildschirm ohne Ruckler, keine Lag-Spikes, ≥ 100 FPS – pausiert, Zwischenstand im Changelog | claude/performance | Mining Game | Map (MapGenerator, Tilemap-Abonnenten, Rubble, Collider, Erzadern, Bäume, Gras), Loading, GPS-Codec/Schema, GameSaveSystem, AudioManager, Tests/Performance* | nein | 17.10.2026 19:45 |

## Zuletzt abgeschlossen

- 19.10.2026, Claude Code: Edelsteine (Granat G2, Smaragd E2, Rubin R3, Diamant D2) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Wolfram (W1) und Mythril (M2) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Titan (T5a) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Platin (P2) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Kohle (K2, ohne Steinchen) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Silber (S1) und Gold (G2b) als gemalte Einzelblöcke, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Eisenerz (Schema E1 Stahl) als gemalte Einzelblöcke und Kupfer-Aufräumen (claude/copper-cleanup) geprüft und zusammengeführt, Branch `claude/iron-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Kupfererz (Small/Medium/Large) als gemalte Einzelblöcke statt Shader-Adern auf Branch `claude/copper-tiles`, siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: Editor-Warteschlange ausserhalb von Git (`Tools/editor-queue.*`, Aktivitätssignal `Assets/Editor/EditorQueueActivity.cs`, Doku `Docs/Editor-Warteschlange.md`) ersetzt die Tabelle „Unity Editor“.
- 18.10.2026, Claude Code (Cloud): Cloud-Tests laufen (Personal-Lizenz über Unity-Konto), Live-Dashboard `Tools/CloudDashboard` und Live-Status-Issue #2; 2 fehlgeschlagene Checks als Todoist-Aufgabe erfasst.
