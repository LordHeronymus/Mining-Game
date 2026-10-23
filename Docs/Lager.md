# Lager (Lagerhütte an der Oberfläche)

Die Lagerhütte neben dem Shop funktioniert wie eine Kiste in Minecraft: Items aus dem Rucksack einlagern und bei Bedarf wieder herausnehmen. Der Inhalt gilt pro Durchgang und wird mit dem Spielstand gespeichert (`RunSaveState.storage`).

## Bedienung

- **Knopf „Lager öffnen“** schwebt über der Hütte, sobald der Spieler davorsteht: goldenes Holzschild (`EntrySign`), das auch Shop und Werkbank verwenden. Wer weggeht, schliesst das Lager automatisch, ebenso Esc.
- **Fenster:** links der Rucksack, rechts das Lager, je ein Raster mit 4 Spalten (scrollbar). Oben Suche und Filter (Alle, Erze, Werkzeuge, Verbrauch, Material) für beide Seiten.
- **Verschieben:** Klick = 1 Stück, Shift+Klick = ganzer Stapel, Rechtsklick = Hälfte, Ziehen auf die andere Seite = ganzer Stapel.
- **Schnellknöpfe:** „Erze einlagern“, „Alles einlagern“ (Items in der Schnellleiste bleiben im Rucksack), „Alles entnehmen“ (berücksichtigt den gewählten Filter, so viel wie tragbar).
- Unten zeigt eine Zeile die Bedienung bzw. Name, Anzahl, Gewicht und Wert des Items unter der Maus. Rechts unten steht der Gesamtwert des Lagers. Wert und Gesamtwert zählen nur, was der Shop kauft (Erze mit Wert, `ShopManager.IsSellable`); Werkzeuge und Platzierbares zeigen keinen Wert.

## Gewicht

- Bis zur **Traglast** (`InventoryManager.CarryingCapacity`) läuft der Spieler normal schnell, darüber langsamer.
- Das **Maximum** ist das Dreifache der Traglast (`MaximumWeight`). Bis dahin darf man aus dem Lager entnehmen (`SurfaceStorageBuilding.MaxWithdrawable`/`WithdrawUpTo`); was nicht mehr passt, bleibt im Lager, die Gewichtsanzeige blinkt kurz rot.
- Der Gewichtsbalken zeigt den normalen Bereich gold, den langsameren Bereich korallenrot und markiert die Traglast mit einem Strich. Im Testmodus „Kein Gewicht“ entfällt das Limit.

## Technik

- `Assets/GameObjects/Map/EntrySign.cs`: gemeinsames Eingangsschild für Lager, Shop und Werkbank (Breite nach Text, Weltkoordinaten, Ein-/Ausblenden). Die alten „Enter“-Schilder in Szene/Prefab werden zur Laufzeit deaktiviert.
- Item-Bilder: `ItemIconLayout.CellIconSize` gibt allen Item-Zellen (Inventar, Schnellleiste, Lager) dasselbe Bildfeld.
- `Assets/GameObjects/Map/Storage/SurfaceStorageBuilding.cs`: Lagerinhalt, Ein-/Auslagern, Besucher-Trigger, Eingangsschild über `EntrySign`.
- `Assets/GameObjects/Map/Storage/StoragePanel.cs`: Fenster, zur Laufzeit unter dem Canvas der Werkbank erzeugt (`StoragePanel.Ensure()`), gleiche Grafiken wie Shop/Werkbank (`Shop/BuyBackground-v2`, `Shop/BuyFrames-v2`, `GoldButtonFeedback`, TitleFont).
- Entnahmen laufen über `InventoryManager.AddTransferred`: gleiche Gewichtsprüfung wie `Add`, aber ohne „+N Item“-Einblendung (ist ein Umlagern, kein Fund).
- Prüfskript: `Tests/StorageUiChecks.cs` (frischer Play-Start ab MainMenu, isoliertes Spielstand-/Meta-Profil, Ergebnis in `Temp/StorageUiChecks.txt`).
- Entwurf: `Design/Lager/Entwurf/Lager-Entwurf.html`, umgesetzt: `Design/Lager/Lager-implemented.png`, `Design/Lager/Lager-Knopf-implemented.png`.
