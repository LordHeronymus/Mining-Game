# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Bauplan-Bilder im Shop mit 512 px und BC7 statt unkomprimiert (rund 270 MB weniger im Speicher), lokal auf der Testbank geprüft, kein sichtbarer Unterschied.
- 23.10.2026, Claude Code (Cloud): Knopf „Werkbank öffnen“ steht über der Dachspitze der Werkstatt statt über der Mitte des ganzen Bildes (das Bild ist leicht asymmetrisch, Abweichung etwa 17 px von 1536), `EntrySign` mit optionaler Achse, `WorkbenchBuilding.signAxis`; Cloud-Lauf #186 grün (`BughuntEntrySignChecks` mit neuer Achsen-Prüfung); Branch `claude/werkbank-knopf-mitte`, PR bereit für `main` (Freigabe und Sichtprüfung Benutzer).
- 23.10.2026, Claude Code (Cloud): Traglast-Anzeige im HUD: Plakette „Last 12,5 / 40 kg“ links oben unter den Balken (Farben wie im Inventar), ein-/ausschaltbar in Einstellungen → Anzeige → „Traglast im HUD“ (gespeichert, Standard Ein); Prüfskript `Tests/CarryWeightHudChecks.cs` in der Cloud grün (#174); Branch `claude/hud-carry-weight`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): HUD-Challenges schöner: Titel zwischen Goldlinien (Variante 2 von 4, Wahl Benutzer), mehr Rand rechts; Branch `claude/challenge-tracker-style`, PR bereit für `main` (lokale Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Baukomponenten (Nägel, Seil, Stoff, Stahl) aus der Werkbank direkt in die Lagerhütte (ohne Hütte in den Rucksack), Shop listet und verkauft Erze aus Rucksack und Lager, im Bauplan-Shop nur die nächste Stufe jeder Upgrade-Reihe, Energie 0,25 Coins je Einheit (100 = 25); GPS-Schalter für alle drei, Prüfskript `Tests/StorageComponentsShopChecks.cs` in der Cloud grün; Branch `claude/komponenten-lager-shop`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code (Cloud): Challenges im HUD verfolgen: Stern auf jeder offenen Kachel, HUD-Liste rechts unter den Zählern mit bis zu 4 markierten Challenges (erledigte fallen heraus, weitere rücken nach), Markierung im Profil (`trackedChallenges`, abwärtskompatibel); Kartenrahmen mit dezentem Stufenleuchten und Edelsteinen statt Farblinien; Cloud-Läufe #167/#173 grün mit Vorschaubildern; Branch `claude/challenge-tracking`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Eingabefelder (Namensfeld „Neues Spiel“, Umbenennen, Suchen, GPS) mit fester Grundlinie, Text springt bei g/y nicht mehr hoch (`HomeUi.StyleInputField` nutzt `ButtonTextCentering` ohne Rahmen), Prüfskript `Tests/InputTextBaselineChecks.cs`; Branch `claude/input-text-baseline`, mit Freigabe des Benutzers per PR #63 in `main`.
- 23.10.2026, Claude Code (Cloud): Play-Mode-Checks laufen in der Cloud (Suite `play`, 4 Checks grün), Vorschaubilder aus Cloud-Läufen auf Branch `ci-previews` (Suite `visual`, `Tools/ci-previews.sh`), Cursor-Texturen einmal pro Sitzung (Szenenstart rund 200 ms kürzer, bitgleich); mit Freigabe des Benutzers per PR #62 in `main`, siehe Docs/Cloud-Tests.md.
- 23.10.2026, Claude Code: Nacharbeiten gemalte Erze: `OreOverlayChecks` (Testfehler bei Seed/Kartengrösse aus GPS, zwei veraltete Balancing-Werte) und `OreVeinRuntimeChecks` (entfernte `OreVeinField`-API) repariert, `OreSparkleChecks` besteht unverändert; auf der Testbank alle grün; Cloud-Suite `smoke` (CaveGeneration/MetaProgressionCore) war schon seit 22.10. grün; Branch `claude/painted-ore-followups`, mit Freigabe des Benutzers per PR #60 in `main`.
- 23.10.2026, Claude Code (Cloud): Fehlersuche über das ganze Spiel in vier Runden (161 Funde, 104 behoben, 15 bewusst offen, je Behebung ein Prüfskript in der Suite `bughunt`, Cloud-Läufe grün), Bericht `Docs/Fehlersuche-2026-10.md`; Branch `claude/bughunt`, PR bereit für `main` (Freigabe und lokale Abnahme Benutzer).
