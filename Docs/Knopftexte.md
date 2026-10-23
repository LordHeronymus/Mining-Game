# Knopftexte

Beschriftungen auf Knöpfen sitzen optisch mittig im sichtbaren Goldrahmen, unabhängig davon, ob der Text Unterlängen (g, p, y) oder Umlaute enthält.

## Regel

- **Mitte:** Die Mitte zwischen Grundlinie und Versalhöhe liegt auf der Mitte des sichtbaren inneren Goldrahmens (nicht der ganzen Knopfgrafik). TMP „Midline“ wird nicht verwendet, weil es nach den tatsächlichen Glyphen zentriert und Texte mit Unterlängen nach oben schiebt.
- **Schriftgrösse:** Wird so begrenzt, dass Ä (höchster Umlaut) und g (tiefste Unterlänge) immer innerhalb des Rahmens bleiben, mit dem Mindestabstand aus GPS. Grössere eingestellte Werte werden nur verkleinert, nie vergrössert.
- **Seiten:** Die Beschriftung bleibt innerhalb der Endkappen des Rahmens (Innenrand per TMP-Margin).
- **Symbole:** Captions ohne Buchstaben/Ziffern (+, −, ×, Pfeile) werden nach ihren Glyphen zentriert.
- Zähler, Hotkey-Ziffern und Kartentitel, die nicht in der Knopfmitte liegen, behalten ihre eigene Ausrichtung.

Umsetzung: `Assets/GameObjects/UI/ButtonTextCentering.cs`. Die Komponente passt sich an, wenn sich die Rahmenhöhe ändert (Layout, Pixeldichte) oder GPS-Werte geändert werden.

## GPS-Werte

F1 → UI → Knopftexte (`GpsPreferences`):

| Wert | Bedeutung | Standard |
| --- | --- | --- |
| `buttonTextOffset` | Vertikaler Versatz in % der Schriftgrösse (+ = hoch), −20 bis +20 | 0 |
| `buttonTextPadding` | Mindestabstand von Ä/g zum Goldrahmen in px, 0 bis 10 | 1 |

## Neue Knöpfe

- Knöpfe mit `GoldButtonFeedback` oder `HomeButtonFeedback` erhalten die Zentrierung automatisch.
- Andere Knöpfe: `ButtonTextCentering.Apply(button)` aufrufen (optional mit der Rahmengrafik als zweitem Argument). Erfasst werden alle vertikal mittig ausgerichteten Texte, deren Box die Rahmenmitte abdeckt.
- Einzelne Beschriftung mit eigener Rahmengrafik: `ButtonTextCentering.Apply(label, frame)`.
- Einzeln eingehängt sind bisher `EntrySign`, `ProgressionTimeline`, `PlayerMapPanel`, `GpsRuntimePanel` und `UpgradeTileArt.StyleTab`.
- Keine eigenen Pixel-Versätze für einzelne Knöpfe einbauen; Feinkorrektur nur über den GPS-Versatz.

## Prüfung

`Tests/ButtonTextCenterChecks.cs` (frischer Play-Start ab MainMenu, isoliertes Profil, nur lokal in der Testbank):

```
unity command --project-path "<Git Repos>/Mining Game 2" run_script --file Tests/ButtonTextCenterChecks.cs
```

Misst alle Knopftexte der Hauptansichten: Abstand über der Versalhöhe gleich Abstand unter der Grundlinie, Ä/g innerhalb des Rahmens. Bericht `Temp/ButtonTextCenterChecks.txt`, Screenshots `Temp/ButtonText/`.
