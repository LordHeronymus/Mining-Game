# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |
| Claude Code (Cloud) | Stirnlampe nachts an der Oberfläche | `claude/stirnlampe-oberflaeche` | Cloud | `MapLighting.cs` (Stirnlampe), `Tests/SurfaceHeadlampChecks.cs` | ja (Testbank) | 23.10.2026 15:45 |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Kein Einsammel-Bling mehr beim Laden eines Spielstands (Startausrüstung wird beim Laden still vergeben), Doppelklick auf einen Spielstand unter „Spielstände“ startet ihn direkt; Prüfskript `Tests/LoadPickupFeedChecks.cs` in der Cloud grün (#187); Branch `claude/load-bling`, mit Freigabe des Benutzers per PR #75 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Musikregler neu skaliert: 100 % = bisherige 20 % (GPS-Wert `musicCeiling`, F1 → Audio → Gesamtpegel), gespeicherte Reglerstände einmalig umgerechnet (20 % wird 100 %, Musik bleibt gleich laut); Cloud-Lauf #184 `MusicRoutingChecks`/`RobustnessFixChecks` grün; Branch `claude/music-volume-scale`, mit Freigabe des Benutzers per PR #70 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Bauplan-Bilder im Shop mit 512 px und BC7 statt unkomprimiert (rund 270 MB weniger im Speicher), lokal auf der Testbank geprüft, kein sichtbarer Unterschied.
- 23.10.2026, Claude Code (Cloud): Shop-Karten „Kaufen“: Bauplan-Bilder alle gleich gross (sichtbares Pergament 160 Einheiten, unabhängig vom durchsichtigen Rand der Bilder und vom GPS-Icon-Faktor) und ganz im Kartenrahmen (`BuyPage.FitBlueprint`), Prüfskript `Tests/ShopBlueprintCardChecks.cs` in der Cloud grün (#182); Branch `claude/shop-blueprint-cards`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Knopf „Werkbank öffnen“ steht über der Dachspitze der Werkstatt statt über der Mitte des ganzen Bildes (das Bild ist leicht asymmetrisch, Abweichung etwa 17 px von 1536), `EntrySign` mit optionaler Achse, `WorkbenchBuilding.signAxis`; Cloud-Lauf #186 grün (`BughuntEntrySignChecks` mit neuer Achsen-Prüfung); Branch `claude/werkbank-knopf-mitte`, PR bereit für `main` (Freigabe und Sichtprüfung Benutzer).
- 23.10.2026, Claude Code (Cloud): Traglast-Anzeige im HUD: Plakette „Last 12,5 / 40 kg“ links oben unter den Balken (Farben wie im Inventar), ein-/ausschaltbar in Einstellungen → Anzeige → „Traglast im HUD“ (gespeichert, Standard Ein); Prüfskript `Tests/CarryWeightHudChecks.cs` in der Cloud grün (#174); Branch `claude/hud-carry-weight`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): HUD-Challenges schöner: Titel zwischen Goldlinien (Variante 2 von 4, Wahl Benutzer), mehr Rand rechts; Branch `claude/challenge-tracker-style`, PR bereit für `main` (lokale Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Baukomponenten (Nägel, Seil, Stoff, Stahl) aus der Werkbank direkt in die Lagerhütte (ohne Hütte in den Rucksack), Shop listet und verkauft Erze aus Rucksack und Lager, im Bauplan-Shop nur die nächste Stufe jeder Upgrade-Reihe, Energie 0,25 Coins je Einheit (100 = 25); GPS-Schalter für alle drei, Prüfskript `Tests/StorageComponentsShopChecks.cs` in der Cloud grün; Branch `claude/komponenten-lager-shop`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code (Cloud): Challenges im HUD verfolgen: Stern auf jeder offenen Kachel, HUD-Liste rechts unter den Zählern mit bis zu 4 markierten Challenges (erledigte fallen heraus, weitere rücken nach), Markierung im Profil (`trackedChallenges`, abwärtskompatibel); Kartenrahmen mit dezentem Stufenleuchten und Edelsteinen statt Farblinien; Cloud-Läufe #167/#173 grün mit Vorschaubildern; Branch `claude/challenge-tracking`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Eingabefelder (Namensfeld „Neues Spiel“, Umbenennen, Suchen, GPS) mit fester Grundlinie, Text springt bei g/y nicht mehr hoch (`HomeUi.StyleInputField` nutzt `ButtonTextCentering` ohne Rahmen), Prüfskript `Tests/InputTextBaselineChecks.cs`; Branch `claude/input-text-baseline`, mit Freigabe des Benutzers per PR #63 in `main`.
