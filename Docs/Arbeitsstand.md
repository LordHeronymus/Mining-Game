# Arbeitsstand

Koordination zwischen den Agenten (Claude Code, Codex, Claude Cowork). Regeln siehe `CLAUDE.md` / `AGENTS.md` → „Zusammenarbeit mehrerer Agenten“.

Arbeitsordner: `Git Repos/Mining Game` gehört dem Benutzer (sein Unity Editor), `Git Repos/Mining Game 2` und `Git Repos/Mining Game 3` sind die Testbänke der Chats ohne festen Branch (Anmeldung über die gemeinsame Warteschlange `Testbank`, `Tools/editor-bench.sh`); Chats arbeiten in eigenen Worktrees, siehe `AGENTS.md`.

## Unity Editor

Die Belegung steht nicht mehr hier, sondern in der Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`, für alle Branches gleich). Anzeigen mit `Tools/editor-queue.sh status`, Regeln in `Docs/Editor-Warteschlange.md`. Cloud-Läufe (GitHub Actions) belegen keinen lokalen Editor und sind parallel nutzbar.

## Aktiv

| Agent | Aufgabe | Branch | Ordner | Dateien / Systeme | Editor | Start |
| --- | --- | --- | --- | --- | --- | --- |

## Zuletzt abgeschlossen

- 23.10.2026, Claude Code (Cloud): Inventar-Reiter „Ausrüstung“ im Shop-Stil (Wahl Benutzer C3): sechs Karten (Spitzhacke, Sense, Axt, Spitzhammer, Traglast, Energie) mit Stufe bzw. Häkchen, rechts Bauplan und Kästchen Stufe/Wirkung, „Nächste: …“; fünf gleich grosse Reiter; `Tests/InventoryEquipmentChecks.cs` lokal auf der Testbank grün (61); Branch `claude/ausruestung-tab`, Bauplan/Symbol im Detail mittig; mit Freigabe des Benutzers per PR #108 in `main`.
- 23.10.2026, Claude Code (Cloud): Werkbank-Materialliste zeigt nur noch die Gesamtmenge (Rucksack + Lager) statt „0 +10 Lager“, grün wenn genug, rötlich wenn zu wenig; `Tests/WorkbenchStorageChecks.cs` 52/52 lokal auf der Testbank grün; Branch `claude/werkbank-gesamtmenge`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Inventar: Rechtsklick auf ein Item öffnet ein Fenster zum Löschen (Variante C1 von 3, Wahl Benutzer): Regler 1 bis Max mit Zahl im Schieber wie beim Shop-Verkauf, Abbrechen und ganz rot gefüllter Knopf „N löschen“; `Tests/InventoryDeleteChecks.cs` und `ButtonTextCenterChecks` lokal auf der Testbank grün; Branch `claude/items-loeschen`, PR bereit für `main` (Freigabe und Optik-Abnahme Benutzer).
- 23.10.2026, Claude Code (Cloud): Gras gibt halb so viele Fasern (1–3), wächst 30 % schneller und frei auf jedem Grasboden nach, neben Gras 3× wahrscheinlicher, bis 4× schneller bei leerer Wiese (zwei neue GPS-Werte); Testbank grün bis auf das neue Prüfskript (Testfehler korrigiert, Nachtest auf Wunsch ausgelassen); Branch `claude/weniger-fasern`, mit Freigabe des Benutzers per PR #96 in `main`.
- 23.10.2026, Claude Code (Cloud): Hover-Klick für alle Knöpfe: kleiner Mausklick (ElevenLabs, Variante F von 8, Wahl Benutzer) beim Eintreten der Maus auf einen bedienbaren Knopf, Reiter oder Auswahlfeld, nicht wiederholt; ein globaler Wächter `UiHoverAudio` deckt alle Knopfarten ab; GPS Audio → Shop und UI → „Knopf-Hover-Klick“ (Lautstärke 0,2); `Tests/UiHoverAudioChecks.cs` lokal auf der Testbank grün; Branch `claude/hover-klick`, mit Freigabe des Benutzers per PR #107 in `main` (Klang-Abnahme lokal).
- 23.10.2026, Claude Code (Cloud): Stirnlampe leuchtet nachts auch an der Oberfläche (rundum, gleiche Reichweite wie im Boden, am Tag aus), weicher Übergang in den Schacht; `MapLighting` (Oberflächen-Lampe), Prüfskript `Tests/SurfaceHeadlampChecks.cs` lokal auf der Testbank grün; Branch `claude/stirnlampe-oberflaeche`, PR bereit für `main` (Freigabe und Abnahme im Spiel Benutzer).
- 23.10.2026, Claude Code: Zweite Testbank `Mining Game 3` (Desktop 3, stumm) und gemeinsame Warteschlange `Testbank`, die wartende Chats automatisch auf die freie Testbank verteilt; Fokus-/Dialog-Wächter und PrivOrg-Dienst kennen beide; `Tests/EditorQueueChecks.ps1` grün; Branch `claude/zweite-testbank`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Schild-Knöpfe über den Gebäuden (Lager, Werkbank, Shop, Energie) 40 % näher am Dach (`EntrySign.RoofGap` 0,45 → 0,27), Werkstatt bleibt über der Dachspitze; `BughuntEntrySignChecks` 19/19 und `StorageUiChecks` 57/57 lokal auf der Testbank grün; Branch `claude/schild-abstand`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Level-Fortschrittsbalken im Fortschritt-Menü: Balken dockt mit seiner Spitze am Level-Kästchen an statt darunter zu beginnen, Goldfüllung wie bei den Challenge-Balken (Spitze links, rundes Ende) und bleibt in der Rille; Variante A von 4 (Wahl Benutzer); `ProgressionTimelineChecks` 91/91 und `ChallengePanelPreviewChecks` (bis auf den bekannten main-Fehler) lokal auf der Testbank grün; Branch `claude/level-balken`, PR bereit für `main` (Freigabe Benutzer).
- 23.10.2026, Claude Code (Cloud): Spielstand-Kacheln zeigen unten links die maximal je erreichte Tiefe statt der Tiefe beim Speichern; neu gespeichert im Spielstand (Kopf und Inhalt), alte Spielstände zeigen bis zum nächsten Speichern den alten Wert und übernehmen beim Laden die tiefste erreichte Zeile; `Tests/SaveMaxDepthChecks.cs` 18/18 und `SaveRestoreFixChecks` 12/12 lokal auf der Testbank grün; Branch `claude/spielstand-max-tiefe`, PR bereit für `main` (Freigabe Benutzer).
