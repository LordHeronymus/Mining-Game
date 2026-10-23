# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code (Cloud, Projekt-Thread „Cloud Play-, Optik- und Performance-Tests“) | Performance abschliessen (Übergabe Docs/Performance-Uebergabe.md), Play-Mode-Checks und Vorschaubilder in der Cloud | claude/compassionate-brahmagupta-32gx9j | Cloud | Loading (LoadingScreen, LoadingProgress, MapGenerator-Ladeablauf), GameSaveSystem.TestDirectory (Player-Build), UI-Panels Awake, Audio-Vorwärmen, GameplayAssetPreload, Tests/CI, Workflow, Tools/CloudDashboard | nein | 23.10.2026 12:45 (übernommen 12:50) |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Traglast-Anzeige im HUD: Plakette „Last 12,5 / 40 kg“ links oben unter den Balken (Farben wie im Inventar), ein-/ausschaltbar in Einstellungen → Anzeige → „Traglast im HUD“ (gespeichert, Standard Ein); Prüfskript `Tests/CarryWeightHudChecks.cs` in der Cloud grün (#174); Branch `claude/hud-carry-weight`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Challenges im HUD verfolgen: Stern auf jeder offenen Kachel, HUD-Liste rechts unter den Zählern mit bis zu 4 markierten Challenges (erledigte fallen heraus, weitere rücken nach), Markierung im Profil (`trackedChallenges`, abwärtskompatibel); Kartenrahmen mit dezentem Stufenleuchten und Edelsteinen statt Farblinien; Cloud-Läufe #167/#173 grün mit Vorschaubildern; Branch `claude/challenge-tracking`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Eingabefelder (Namensfeld „Neues Spiel“, Umbenennen, Suchen, GPS) mit fester Grundlinie, Text springt bei g/y nicht mehr hoch (`HomeUi.StyleInputField` nutzt `ButtonTextCentering` ohne Rahmen), Prüfskript `Tests/InputTextBaselineChecks.cs`; Branch `claude/input-text-baseline`, mit Freigabe des Benutzers per PR #63 in `main`.
- 23.10.2026, Claude Code: Nacharbeiten gemalte Erze: `OreOverlayChecks` (Testfehler bei Seed/Kartengrösse aus GPS, zwei veraltete Balancing-Werte) und `OreVeinRuntimeChecks` (entfernte `OreVeinField`-API) repariert, `OreSparkleChecks` besteht unverändert; auf der Testbank alle grün; Cloud-Suite `smoke` (CaveGeneration/MetaProgressionCore) war schon seit 22.10. grün; Branch `claude/painted-ore-followups`, mit Freigabe des Benutzers per PR #60 in `main`.
- 23.10.2026, Claude Code (Cloud): Fehlersuche über das ganze Spiel in vier Runden (161 Funde, 104 behoben, 15 bewusst offen, je Behebung ein Prüfskript in der Suite `bughunt`, Cloud-Läufe grün), Bericht `Docs/Fehlersuche-2026-10.md`; Branch `claude/bughunt`, PR bereit für `main` (Freigabe und lokale Abnahme Benutzer).
- 23.10.2026, Claude Code: Unity-Importspuren bereinigt: `nameFileIdTable` in 11 Sprite-.meta versioniert (alle IDs = vorhandene internalIDs, Referenzen unverändert), Testbank nach erzwungenem Neuimport sauber; Branch `claude/sprite-meta-ids`, mit Freigabe des Benutzers per PR #61 in `main`.
- 23.10.2026, Claude Code (Cloud): Progression Folgeaufgaben: GPS-Testmodus ohne Challenge-Fortschritt, gekaufte Ausbauten wirken in allen Welten, XP-Tiefenfaktor (×1 Oberfläche bis ×3 ab 1000 Blöcken) und Erz-XP ohne Abnahme pro Welt, Lebenszeit-Zähler je Welt abgesichert, Challenge-Uhr mit Internetzeit und Selbstheilung, Zell-Ledger-Schreiber an (Schritt 2), Profil beim Welt-Speichern im Hintergrund (Schritt 3); Branch `claude/progression-followups`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Drosselung der Actions-Minuten: Schritt „Budget“ im Workflow „Unity Checks“ (grün/gelb/rot nach Budget-Linie 3 % pro Tag), Stufe im Live-Status (Issue #2), Anzeige als Graph im PrivOrg-Kästchen „Actions-Minuten“; Branch `claude/actions-budget`, mit Freigabe des Benutzers per PR #59 in `main` (PrivOrg-Anzeige per PR #28), siehe Docs/Cloud-Tests.md.
- 23.10.2026, Claude Code (Cloud): F1/GPS „Faktor je Erz“ übersichtlich: feste Liste aller Erze mit Name und × Faktor (kein Hinzufügen/Entfernen), Erz-Auswahl in der Erzverteilung nur für Erze/Edelsteine/Ultronium; alte Einträge bleiben erhalten; Cloud-Checks grün (#128, #137, #140); Branch `claude/ore-factor-list`, PR bereit für `main` (Freigabe Benutzer, Optik-Abnahme lokal).
- 23.10.2026, Claude Code: Kein Speicherdialog mehr vor jedem Play-Start (Korrektur 10f619c aus `claude/performance` in `LastPlayedMap.cs` übernommen, Map-Vorschau lässt eine unveränderte SampleScene unverändert), in der Testbank geprüft (`Tests/ScenePreviewDirtyChecks.cs` PASS, zweiter Play-Start ohne Dialog); Branch `claude/scene-clean-preview`, PR bereit für `main` (Freigabe Benutzer).
