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

- 20.10.2026, Claude Code: Lager-Oberfläche im Shop-/Werkbank-Stil (Rucksack links, Lager rechts, Entnahme bis 3× Traglast), schwebender Knopf „Lager öffnen“, Kupfer-Icon-Import repariert; Branch `claude/lager-ui`, mit Freigabe des Benutzers in `main` zusammengeführt, siehe Docs/Lager.md.
- 20.10.2026, Claude Code: Debug-Einstellungen (F1/Editor-Fenster) werden sofort dauerhaft gespeichert und überleben Play-, Domain- und Editor-Neustart; Branch `claude/debug-persist`, bereit für `main` (Freigabe Benutzer).
- 20.10.2026, Claude Code: Start-Items liegen bei neuem Spiel lückenlos in Hotbar-Slots 1, 2, 3 … (keine alte Belegung mehr), Branch `claude/hotbar-start`, in `main`.
- 20.10.2026, Claude Code: Testbank-Editor läuft immer stumm (`Assets/Editor/TestBenchAudioMute.cs`), Ton nur für Audio-Tests per `editor-bench audio-on`.
- 19.10.2026, Claude Code: Alle 14 Erze als gemalte Einzelblöcke statt Shader-Adern (Kupfer, Eisen, Silber, Gold, Kohle, Platin, Titan, Wolfram, Mythril, Granat, Smaragd, Rubin, Diamant, Ultronium mit Pulsieren), Kupfer-Adern-System entfernt; Branch `claude/iron-tiles` mit `main` zusammengeführt und geprüft, bereit für `main` (Freigabe Benutzer), siehe Docs/Gemalte-Erze.md.
- 18.10.2026, Claude Code: HUD-Größe in den Einstellungen bis 30 % einstellbar (vorher 50 %), Branch `claude/hud-scale`.
- 18.10.2026, Claude Code: Editor-Warteschlange ausserhalb von Git (`Tools/editor-queue.*`, Aktivitätssignal `Assets/Editor/EditorQueueActivity.cs`, Doku `Docs/Editor-Warteschlange.md`) ersetzt die Tabelle „Unity Editor“.
- 18.10.2026, Claude Code (Cloud): Cloud-Läufe mit 20-min-Limit und Abbruchgrund, `// CI: local-only`, Performance-Suite ohne Zeichnen (Lauf #17: 12 min, aussagekräftige CPU-Werte).
- 18.10.2026, Claude Code (Cloud): Suite `performance` läuft in der Cloud vollständig durch (Benchmark-Player, Lauf #16); Werte wegen Software-Rendering nur eingeschränkt aussagekräftig, siehe Docs/Cloud-Tests.md.
- 18.10.2026, Claude Code (Cloud): Cloud-Tests laufen (Personal-Lizenz über Unity-Konto), Live-Dashboard `Tools/CloudDashboard` und Live-Status-Issue #2; 2 fehlgeschlagene Checks als Todoist-Aufgabe erfasst.
