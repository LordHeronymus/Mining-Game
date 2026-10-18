# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner (git worktrees, beide von `main`): `Git Repos/Mining Game` und `Git Repos/Mining Game 2`.

## Unity Editor

| Ordner | Belegt von | Zweck | Seit |
| --- | --- | --- | --- |
| Mining Game | – | frei | 17.10.2026 22:40 |
| Mining Game 2 | – | frei (Library noch nicht importiert) | 17.10.2026 23:00 |

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code | Performance abschliessen: Ladebildschirm ohne Ruckler, keine Lag-Spikes, ≥ 100 FPS – pausiert, Zwischenstand im Changelog | claude/performance | Mining Game | Map (MapGenerator, Tilemap-Abonnenten, Rubble, Collider, Erzadern, Bäume, Gras), Loading, GPS-Codec/Schema, GameSaveSystem, AudioManager, Tests/Performance* | nein | 17.10.2026 19:45 |

## Zuletzt abgeschlossen

- 18.10.2026, Claude Code (Cloud): Cloud-Tests laufen (Personal-Lizenz über Unity-Konto), Live-Dashboard `Tools/CloudDashboard` und Live-Status-Issue #2; 2 fehlgeschlagene Checks als Todoist-Aufgabe erfasst.
- 18.10.2026, Claude Code (Cloud): Cloud-Tests eingerichtet (GitHub-Workflow „Unity Checks“, CiCheckRunner, Tests/CI), siehe Docs/Cloud-Tests.md; Lizenz-Secrets muss der Benutzer noch anlegen.
- 17.10.2026, Claude Code: Branch-Setup erneuert: `main` auf dem aktuellen Stand, Aufgaben auf eigenen Branches, zweiter Arbeitsordner `Mining Game 2` (Branch `claude/erz-optik`) für parallele Chats; alter Branch `Rebuild` gelöscht.
- 17.10.2026, Claude Code: Fehlerbehebungen aus Code-Analyse (Baupläne, Verkauf, GPS-Speicherung, Spielstand-Kompatibilität, Höhlenmaske u. a.) und Repo-Aufräumen (Design/, SceneBackups/, Archiv, Pakete), siehe Changelog.
- 17.10.2026, Claude Cowork: AGENTS.md in CLAUDE.md, AGENTS.md (Spiegel) und Docs/Changelog.md aufgeteilt; Arbeitsstand-Datei eingeführt.
