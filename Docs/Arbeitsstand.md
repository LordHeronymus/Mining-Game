# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` und `Git Repos/Mining Game 3` sind die Testbänke der Chats ohne festen Branch (Anmeldung über die gemeinsame Warteschlange `Testbank`, `Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Erz-Abbauklang ab Schicht 3: Erz spielt den normalen Gesteinsschlag der Schicht und gleichzeitig den Erzklang von Schicht 1/2 (statt PickaxeHitL3Plus); `BughuntTileMinerChecks` 28/28 lokal auf Testbank 3 grün; mit Freigabe des Benutzers in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Gras wächst überall auf der Karte gleich wahrscheinlich nach (kein Spawn-Abschlag, „Vorzug neben Gras“ 1); `GrassRegrowthChecks` auf der Testbank grün, `GpsSettingsChecks` rot wie auf `main` (Audiokatalog); Branch `claude/gras-ganze-karte`, mit Freigabe des Benutzers per PR #114 in `main`.
- 23.10.2026, Claude Code (Cloud): Geldanzeige zählt weich: HUD und Shop (Kaufen/Verkaufen) zählen bei Zu- und Abgang mit Abbremsen zum neuen Betrag, alle Anzeigen gleich, Runstart und geladene Spielstände springen; Dauer F1 → UI → Geldanzeige (Standard 0,6 s, 0 = springen); `MoneyDisplayChecks` und `GpsSettingsChecks` lokal auf der Testbank grün, Sichttest im Spiel; Branch `claude/geld-weich-zaehlen`, mit Freigabe des Benutzers per PR #111 in `main`.
- 23.10.2026, Claude Code (Cloud): Inventar-Reiter „Ausrüstung“ im Shop-Stil (Wahl Benutzer C3): sechs Karten (Spitzhacke, Sense, Axt, Spitzhammer, Traglast, Energie) mit Stufe bzw. Häkchen, rechts Bauplan und Kästchen Stufe/Wirkung, „Nächste: …“; fünf gleich grosse Reiter; `Tests/InventoryEquipmentChecks.cs` lokal auf der Testbank grün (61); Branch `claude/ausruestung-tab`, Bauplan/Symbol im Detail mittig; mit Freigabe des Benutzers per PR #108 in `main`.
- 23.10.2026, Claude Code (Cloud): Werkbank-Materialliste zeigt nur noch die Gesamtmenge (Rucksack + Lager) statt „0 +10 Lager“, grün wenn genug, rötlich wenn zu wenig; `Tests/WorkbenchStorageChecks.cs` 52/52 lokal auf der Testbank grün; Branch `claude/werkbank-gesamtmenge`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Inventar: Rechtsklick auf ein Item öffnet ein Fenster zum Löschen (Variante C1 von 3, Wahl Benutzer): Regler 1 bis Max mit Zahl im Schieber wie beim Shop-Verkauf, Abbrechen und ganz rot gefüllter Knopf „N löschen“; `Tests/InventoryDeleteChecks.cs` und `ButtonTextCenterChecks` lokal auf der Testbank grün; Branch `claude/items-loeschen`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Gras gibt halb so viele Fasern (1–3), wächst 30 % schneller und frei auf jedem Grasboden nach, neben Gras 3× wahrscheinlicher, bis 4× schneller bei leerer Wiese (zwei neue GPS-Werte); Testbank grün bis auf das neue Prüfskript (Testfehler korrigiert, Nachtest auf Wunsch ausgelassen); Branch `claude/weniger-fasern`, mit Freigabe des Benutzers per PR #96 in `main`.
- 23.10.2026, Claude Code (Cloud): Hover-Klick für alle Knöpfe: kleiner Mausklick (ElevenLabs, Variante F von 8, Wahl Benutzer) beim Eintreten der Maus auf einen bedienbaren Knopf, Reiter oder Auswahlfeld, nicht wiederholt; ein globaler Wächter `UiHoverAudio` deckt alle Knopfarten ab; GPS Audio → Shop und UI → „Knopf-Hover-Klick“ (Lautstärke 0,2); `Tests/UiHoverAudioChecks.cs` lokal auf der Testbank grün; Branch `claude/hover-klick`, mit Freigabe des Benutzers per PR #107 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Stirnlampe leuchtet nachts auch an der Oberfläche (rundum, gleiche Reichweite wie im Boden, am Tag aus), weicher Übergang in den Schacht; `MapLighting` (Oberflächen-Lampe), Prüfskript `Tests/SurfaceHeadlampChecks.cs` lokal auf der Testbank grün; Branch `claude/stirnlampe-oberflaeche`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code: Zweite Testbank `Mining Game 3` (Desktop 3, stumm) und gemeinsame Warteschlange `Testbank`, die wartende Chats automatisch auf die freie Testbank verteilt; Fokus-/Dialog-Wächter und PrivOrg-Dienst kennen beide; `Tests/EditorQueueChecks.ps1` grün; Branch `claude/zweite-testbank`, PR bereit für `main` (Freigabe Benutzer).
