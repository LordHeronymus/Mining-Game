# Lager (Lagerhütte an der Oberfläche)

Die Lagerhütte neben dem Shop funktioniert wie eine Kiste in Minecraft: Items aus dem Rucksack einlagern und bei Bedarf wieder herausnehmen. Der Inhalt gilt pro Durchgang und wird mit dem Spielstand gespeichert (`RunSaveState.storage`).

## Bedienung

- **Knopf „Lager öffnen“** schwebt über der Hütte, sobald der Spieler davorsteht (Holzschild im Stil der Shop-/Werkbank-Knöpfe, goldener Rand beim Darüberfahren). Wer weggeht, schliesst das Lager automatisch, ebenso Esc.
- **Fenster:** links der Rucksack, rechts das Lager, je ein Raster mit 4 Spalten (scrollbar). Oben Suche und Filter (Alle, Erze, Werkzeuge, Verbrauch, Material) für beide Seiten.
- **Verschieben:** Klick = 1 Stück, Shift+Klick = ganzer Stapel, Rechtsklick = Hälfte, Ziehen auf die andere Seite = ganzer Stapel.
- **Schnellknöpfe:** „Erze einlagern“, „Alles einlagern“ (Items in der Schnellleiste bleiben im Rucksack), „Alles entnehmen“ (berücksichtigt den gewählten Filter, so viel wie tragbar).
- Unten zeigt eine Zeile die Bedienung bzw. Name, Anzahl, Gewicht und Wert des Items unter der Maus. Rechts unten steht der Gesamtwert des Lagers.

## Gewicht

- Bis zur **Traglast** (`InventoryManager.CarryingCapacity`) läuft der Spieler normal schnell, darüber langsamer.
- Das **Maximum** ist das Dreifache der Traglast (`MaximumWeight`). Bis dahin darf man aus dem Lager entnehmen (`SurfaceStorageBuilding.MaxWithdrawable`/`WithdrawUpTo`); was nicht mehr passt, bleibt im Lager, die Gewichtsanzeige blinkt kurz rot.
- Der Gewichtsbalken zeigt den normalen Bereich gold, den langsameren Bereich korallenrot und markiert die Traglast mit einem Strich. Im Testmodus „Kein Gewicht“ entfällt das Limit.

## Technik

- `Assets/GameObjects/Map/Storage/SurfaceStorageBuilding.cs`: Lagerinhalt, Ein-/Auslagern, Besucher-Trigger, Weltraum-Knopf (`EnterButtonCanvas`, gleiche Ebene wie der Shop-Knopf).
- `Assets/GameObjects/Map/Storage/StoragePanel.cs`: Fenster, zur Laufzeit unter dem Canvas der Werkbank erzeugt (`StoragePanel.Ensure()`), gleiche Grafiken wie Shop/Werkbank (`Shop/BuyBackground-v2`, `Shop/BuyFrames-v2`, `GoldButtonFeedback`, TitleFont).
- Prüfskript: `Tests/StorageUiChecks.cs` (frischer Play-Start, Ergebnis in `Temp/StorageUiChecks-result.txt`).
- Entwurf: `Design/Lager/Entwurf/Lager-Entwurf.html`.
