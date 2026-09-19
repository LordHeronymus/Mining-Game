# Surface Background

Am SurfaceBackgroundController zeigt das Array Ebenen die vorhandenen ParallaxLayer
direkt im Inspector an. Ein Eintrag referenziert die bestehende Komponente; deren Werte
werden inline bearbeitet, nicht kopiert. Undo und Prefab-Overrides bleiben an der
ursprünglichen Komponente. Bildreferenzen liegen unter Bilder (Sprite-Assets).

Pro Ebene sind Position, Skalierung, Aktivierung und sämtliche ParallaxLayer-Felder
zugänglich. Die globalen Werte des Controllers gelten weiterhin für alle Ebenen.
Neue Bilddateien müssen als Sprite importiert sein.

CollectLayers übernimmt vorhandene untergeordnete Ebenen ins Array. Das Setup ruft
dies nach der Erstellung auf. Das Array ist eine zentrale Bearbeitungsansicht;
Entfernen eines Verweises löscht oder deaktiviert nicht die zugehörige Ebene.
