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

- 22.10.2026, Claude Code: Autosave ohne Anhalten: Das Spiel läuft beim Autosave weiter (Kachelschichten zeilenweise erfasst, Änderungen währenddessen nachgeführt, Schreiben im Hintergrund), andere Speichervorgänge und Menüs warten nie darauf; Messskript `Tests/AutosaveStutterChecks.cs`; Branch `claude/autosave-stutter`, bereit für `main` (Freigabe Benutzer).
- 22.10.2026, Claude Code: Eingangsschilder (Werkbank, Shop, Lager) mittig über der sichtbaren Gebäudegrafik mit gleichem Dachabstand; Branch `claude/workshop-button-d81ccb`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Einstellungen, Pausemenü und F1 pausieren das ganze Spiel inkl. Audio (zentrale `GamePause`, Ambience und Stimme setzen fort, keine Energie verloren); Branch `claude/settings-pause-288b53`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Eine Haltezeit für Lager, Inventar und Hotbar (F1 → UI → „Item ziehen“ → „Haltezeit bis Ziehen“, 0 = beim Bewegen); kurzer Klick bleibt Klick; Branch `claude/drag-hold`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 22.10.2026, Claude Code: Warteschlangen-Dienst für PrivOrg startet mit dem ersten Unity Editor statt mit Windows (Editor prüft jede Minute, startet ihn auch nach Absturz neu), Branch `claude/queue-service-on-editor`.
- 22.10.2026, Claude Code (Cloud): Einstellungen (F1 und Editor-Fenster) mit sichtbarer Hierarchie: jede Ebene eine Stufe eingerückt, F1-Stufe breiter; Branch `claude/exciting-ramanujan-l0893x`.
- 22.10.2026, Claude Code: F1 → UI → „Item ziehen“: Pop-Stärke, Grösse beim Tragen und Pop-Dauer einstellbar (Haltezeit weiterhin unter UI → Hotbar); Rechtsklick auf ein Hotbar-Item nimmt es aus der Hotbar (bleibt im Inventar); Branch `claude/drag-settings`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code (Cloud): Sprachausgabe des Charakters (169 Clips, Sprach-System mit Abklingzeit/Priorität, Auslöser an Energie, Traglast, Sturz, Shop, Werkbank, neue Welt; Regler „Stimme“, GPS/F1), Branch `claude/exciting-ramanujan-l0893x`, mit Freigabe des Benutzers in `main` zusammengeführt; lokale Hörabnahme und Commit des aktualisierten `GpsProfile.asset` ausstehend, siehe Docs/Sprachausgabe.md.
- 20.10.2026, Claude Code: Gezogenes Item poppt beim Start des Ziehens kurz auf (128 % → 112 %) und wird deckend mit Schatten getragen (Lager, Inventar, Hotbar); Branch `claude/drag-pop`, mit Freigabe des Benutzers in `main` zusammengeführt.
- 20.10.2026, Claude Code: Testbank-Dialoge: `Tools/editor-dialog.sh list/press` erkennt und schliesst modale Unity-Dialoge der Testbank, Wächter im Queue-Dienst und in `wait` beantwortet bekannte (z. B. „modified externally“ → Reload), meldet unbekannte; `editor-bench` schliesst die Szene vor dem Wechsel; Branch `claude/bench-dialogs-62a367`, bereit für `main` (Freigabe Benutzer), Dienst danach mit `-Install` neu einrichten.
