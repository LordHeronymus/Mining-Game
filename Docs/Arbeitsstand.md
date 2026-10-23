# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code (Cloud) | Traglast-Anzeige im HUD (ein-/ausschaltbar in Einstellungen → Anzeige) | `claude/hud-carry-weight` | Cloud | `CompactHud.cs` (neue Plakette links oben), `PlayerSettings.cs`, `SettingsPanel.cs` (Anzeige-Seite), neues Prüfskript | nein | 23.10.2026 15:50 |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code: Nacharbeiten gemalte Erze: `OreOverlayChecks` (Testfehler bei Seed/Kartengrösse aus GPS, zwei veraltete Balancing-Werte) und `OreVeinRuntimeChecks` (entfernte `OreVeinField`-API) repariert, `OreSparkleChecks` besteht unverändert; auf der Testbank alle grün; Cloud-Suite `smoke` (CaveGeneration/MetaProgressionCore) war schon seit 22.10. grün; Branch `claude/painted-ore-followups`, mit Freigabe des Benutzers per PR #60 in `main`.
- 23.10.2026, Claude Code (Cloud): Fehlersuche über das ganze Spiel in vier Runden (161 Funde, 104 behoben, 15 bewusst offen, je Behebung ein Prüfskript in der Suite `bughunt`, Cloud-Läufe grün), Bericht `Docs/Fehlersuche-2026-10.md`; Branch `claude/bughunt`, PR bereit für `main` (Freigabe und lokale Abnahme Benutzer).
- 23.10.2026, Claude Code: Unity-Importspuren bereinigt: `nameFileIdTable` in 11 Sprite-.meta versioniert (alle IDs = vorhandene internalIDs, Referenzen unverändert), Testbank nach erzwungenem Neuimport sauber; Branch `claude/sprite-meta-ids`, mit Freigabe des Benutzers per PR #61 in `main`.
- 23.10.2026, Claude Code (Cloud): Progression Folgeaufgaben: GPS-Testmodus ohne Challenge-Fortschritt, gekaufte Ausbauten wirken in allen Welten, XP-Tiefenfaktor (×1 Oberfläche bis ×3 ab 1000 Blöcken) und Erz-XP ohne Abnahme pro Welt, Lebenszeit-Zähler je Welt abgesichert, Challenge-Uhr mit Internetzeit und Selbstheilung, Zell-Ledger-Schreiber an (Schritt 2), Profil beim Welt-Speichern im Hintergrund (Schritt 3); Branch `claude/progression-followups`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Drosselung der Actions-Minuten: Schritt „Budget“ im Workflow „Unity Checks“ (grün/gelb/rot nach Budget-Linie 3 % pro Tag), Stufe im Live-Status (Issue #2), Anzeige als Graph im PrivOrg-Kästchen „Actions-Minuten“; Branch `claude/actions-budget`, mit Freigabe des Benutzers per PR #59 in `main` (PrivOrg-Anzeige per PR #28), siehe Docs/Cloud-Tests.md.
- 23.10.2026, Claude Code (Cloud): F1/GPS „Faktor je Erz“ übersichtlich: feste Liste aller Erze mit Name und × Faktor (kein Hinzufügen/Entfernen), Erz-Auswahl in der Erzverteilung nur für Erze/Edelsteine/Ultronium; alte Einträge bleiben erhalten; Cloud-Checks grün (#128, #137, #140); Branch `claude/ore-factor-list`, PR bereit für `main` (Freigabe Benutzer, Optik-Abnahme lokal).
- 23.10.2026, Claude Code: Kein Speicherdialog mehr vor jedem Play-Start (Korrektur 10f619c aus `claude/performance` in `LastPlayedMap.cs` übernommen, Map-Vorschau lässt eine unveränderte SampleScene unverändert), in der Testbank geprüft (`Tests/ScenePreviewDirtyChecks.cs` PASS, zweiter Play-Start ohne Dialog); Branch `claude/scene-clean-preview`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Level-/Progressions-System geprüft und repariert (Audit mit 26 bestätigten Funden: Game-Over/Sieg-„Weiter“ hängt nicht mehr, Challenge-XP in der Run-Bilanz, Level-Ups und Auszahlungen sofort gespeichert, keine Doppelauszahlung beim Neuladen, robusteres Profil-Speichern, kompaktes Zell-Ledger Schritt 1 mit Leser), Prüfskripte `Tests/ProgressionChecks.cs` und `Tests/MetaCellLedgerChecks.cs` in der Cloud grün; Branch `claude/level-system-check`, PR bereit für `main` (Freigabe Benutzer), siehe Docs/Progression.md.
- 23.10.2026, Claude Code (Cloud): Pausemenü: Hinweis „Speichern …“ unter „Hauptmenü“ 13 Einheiten höher, mittig im unteren Streifen (`RunPauseMenu.cs`, nur Position); Branch `claude/pause-save-hint`, mit Freigabe des Benutzers per PR #53 in `main` (Sichtprüfung Benutzer).
- 23.10.2026, Claude Code (Cloud): Knopftexte auch waagrecht nach den sichtbaren Buchstaben zentriert (GPS UI → Knopftexte → `buttonTextInkCenterX`, Prüfskript misst links/rechts), Cloud-Checks grün, lokale Optik-Abnahme offen; Branch `claude/optimistic-edison-k1l9hj`, mit Freigabe des Benutzers per PR in `main`, siehe Docs/Knopftexte.md.
