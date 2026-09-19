# Erzfunkeln

OreSparkles auf der Map erzeugt ruhende, sanft ein- und ausblendende Glanzsterne für
Eisen, Kupfer, Silber und Gold. Stein und leere Zellen sind ausgeschlossen.
Gold und Kupfer verwenden warme Farben, Eisen und Silber kühlere Farben.

Ein gemeinsames Partikelsystem verwendet URPs Sprite-Unlit-Shader und eine prozedural
erstellte Glanzstern-Textur. Material und Textur sind als Assets referenziert. Maximal 32 Partikel;
pro Suchschritt maximal 512 Zellen im Kamerabereich, verteilt über mehrere Frames.
Jeder sichtbare, ausreichend helle Erzblock erhält seinen eigenen nächsten Funkeltermin.
Ein zentraler Scheduler bearbeitet die ältesten Termine zuerst (maximal 64 pro Frame).
Ist das Partikelbudget belegt, warten fällige Blöcke, statt ausgelost oder übersprungen
zu werden. Bei sehr vielen sichtbaren Erzen verlängert sich dadurch der Rhythmus.
Keine Objekte pro Erz. Der Effekt verändert nicht Unitys Zufallszustand für die Mapgenerierung.

Die Glanzsterne liegen über der Dunkelheitsmaske und ihre Deckkraft wird anhand der
lokalen Helligkeit reduziert. Unter 3 % Helligkeit entstehen keine
Partikel. Entfernte Erztiles und nachträglich dunkle Zellen werden laufend geprüft.
Intervall pro Block, Lebensdauer, Größe relativ zur Zelle und Deckkraft sind am OreSparkles-
Component einstellbar. Tests/OreSparkleChecks.cs prüft Emission, Budget, Abbau und Dunkelheit
in einer temporären Szene und erstellt eine Vorschau im lokalen Temp-Verzeichnis.
