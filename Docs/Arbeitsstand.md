# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` ist die Testbank der Chats ohne festen Branch (`Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Inventar: Rechtsklick auf ein Item öffnet ein Fenster zum Löschen (Variante C1 von 3, Wahl Benutzer): Regler 1 bis Max mit Zahl im Schieber wie beim Shop-Verkauf, Abbrechen und ganz rot gefüllter Knopf „N löschen“; `Tests/InventoryDeleteChecks.cs` und `ButtonTextCenterChecks` lokal auf der Testbank grün; Branch `claude/items-loeschen`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Challenge erfüllt mit Ton und Banner: neuer Ton (N1, ElevenLabs) und Holzbanner oben in der Mitte mit Symbol, Name und XP, das goldene Siegel landet auf dem Haupttreffer (Variante B von 3, Wahl Benutzer); gilt für alle Challenges inkl. Stufen, Level-Up wartet auf das Banner; `ChallengeCompleteChecks` (28/28), `GameplayLevelUpChecks` (35/35), `LevelUpGlowTimingChecks` (26/26), `ChallengeGameplayChecks` (20/20) lokal auf der Testbank grün; Branch `claude/challenge-erfuellt`, mit Freigabe des Benutzers per PR #99 in `main` (Klang-/Optik-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): „Ruf der Tiefe“ (noch laufende Wochenaufgabe aus dem alten Challenge-System) verlangt 300 statt 750 Blöcke; Fortschritt bleibt, Belohnung (Mittel, 1'000 XP) unverändert, ausbezahlte Aufgaben bleiben 750/750 (`MetaChallengePeriods.LowerLegacyDepth`); `ChallengeReworkChecks` (615) und `ChallengeCoreChecks` lokal auf der Testbank grün; Branch `claude/ruf-der-tiefe-300`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Shop-Verkauf mit Mengenregler: Regler von 1 bis Max (ganzer Bestand des gewählten Erzes), Zahl im Schieber (Variante 2 von 4, Wahl Benutzer), Verkaufsknopf verkauft die gewählte Menge, „Alles verkaufen“ entfällt; `Tests/ShopSellStyleChecks.cs` lokal auf der Testbank grün; Branch `claude/verkaufsregler`, mit Freigabe des Benutzers per PR #97 in `main` (Optik-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Knopftexte im Inventar („×“, Filter, „Sortieren“) sitzen waagrecht genau mittig: `ButtonTextCentering` zentriert die sichtbaren Buchstaben auf die Mitte des Textkastens statt auf die Mitte der Schrittweiten (fette Schrift verschob sie um den Fett-Abstand); lokal auf der Testbank `ButtonTextCenterChecks` 108/108 und `StorageUiChecks` 57/57 grün; Branch `claude/inventar-knopftexte`, mit Freigabe des Benutzers in `main`.
- 23.10.2026, Claude Code (Cloud): Kantenhilfe schiebt sanfter: neuer GPS-Wert Bewegung → „Kantenhilfe: Hineinschiebe-Tempo“ (Anteil Laufgeschwindigkeit, Standard 0,5 statt bisher volle Geschwindigkeit); `Tests/LedgeAssistChecks.cs` (prüft jetzt auch das Tempo, parkt offene Spiel-Panels) und `GpsSettingsChecks` auf der Testbank grün; Branch `claude/kantenhilfe-tempo`, mit Freigabe des Benutzers per PR #94 in `main` (Spielgefühl-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Eigene F1-Werte des Benutzers (20 Stück: Erze, Rezeptmengen, Leuchtstärke, Schwerkraft, Holzertrag, ein Klang) als neue Standardwerte in `GpsProfile.asset`; Profil liest sich zurück, sonst unverändert; Branch `claude/f1-werte-main`, mit Freigabe des Benutzers per PR #93 in `main`.
- 23.10.2026, Claude Code (Cloud): Fackeln tiefer in der Zelle (Flamme wird nicht mehr abgeschnitten), neue Flamme Variante C2 mit Funken und Rauch, Knistern mit ElevenLabs-Schleife, hörbar bis 4 Kacheln; Prüfskript `Tests/TorchFlameCrackleChecks.cs` und Klangtest lokal auf der Testbank grün; per PR #90 mit Freigabe des Benutzers in `main` (Optik- und Klang-Abnahme lokal offen).
- 23.10.2026, Claude Code (Cloud): Leben und Energie als Zahl rechts neben den Kugelreihen („100 / 100“, Variante A, Wahl Benutzer), gleiche Schrift, Grösse, Kontur und Schatten wie die Traglast; unter 25 % rot; Lebenszahl pulsiert zum Herzschlag, Energiezahl zum Warnpiepsen; `Tests/VitalValuesHudChecks.cs` 27/27 und `CarryWeightHudChecks` 30/30 lokal auf der Testbank; Branch `claude/vitalwerte-hud`, mit Freigabe des Benutzers in `main` (Puls/Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Alte Spielstände (z. B. „The One“) laden wieder: ein Bild, das im gespeicherten Kartenstand steht, es im Spiel aber nicht mehr gibt (altes Layer-2-Hintergrundbild nach PR #82), wird beim Laden durch das aktuelle ersetzt statt den Spielstand abzulehnen (`GpsSettings.NormalizeGeneration`); `Tests/SaveMissingAssetChecks.cs` (inkl. Kopie eines echten Spielstands) und die Spielstand-/GPS-Checks lokal auf der Testbank grün; Branch `claude/spielstand-fehlendes-bild`, PR bereit für `main` (Freigabe Benutzer).
