# Oberflächenhintergrund

In `SampleScene` liegt das Set unter `SurfaceBackground`. Die vier Kinder enthalten jeweils einen `ParallaxLayer`:

| Objekt | Bild | Faktor X/Y |
| --- | --- | --- |
| Sky | Parallax_01_Himmel | 0 / 0 |
| Mountains | Parallax_02_Berge | 0,08 / 0,08 |
| WoodedCliffs | Berge_Ebene_02_Vordergrund | 0,25 / 0,25 |
| NearHills | Parallax_04_Huegel_Baeume | 0,50 / 0,50 |

- **Vertical Offset:** individueller Höhenversatz; positiv = nach oben. Bezieht sich auf die Bildmitte inklusive transparenter Flächen, relativ zum Ebenen-Transform.
- **Horizontal Offset:** individueller horizontaler Versatz.
- **Horizontale Anzahl:** 1 = Panorama einmal, 2 = zweimal, 3 = dreimal usw.; 0 = endlose Wiederholung. Gezählt wird die komplette Segmentfolge. Begrenzte Wiederholungen sind gemeinsam um die Ebenenmitte zentriert und werden nicht an der Kamera nachgesetzt. Parallax, Versätze, Zoom und Himmelsfortsetzung bleiben wirksam. Der bisherige Toggle wird automatisch übernommen: an → 0, aus → 1.
- **Height:** vollständige Bildhöhe in lokalen Welteinheiten. Die Breite ergibt sich aus dem echten Seitenverhältnis (6144 / 2046). Positive Transform-Skalierungen werden zusätzlich berücksichtigt; keine Rotation der Ebenen.
- **Horizontal / Vertical Parallax:** 0 = bildschirmfest, 1 = weltfest. Beide Richtungen sind unabhängig.
- **Segments:** ein komplettes Panorama oder mehrere aufeinanderfolgende Sprites von links nach rechts. Alle werden auf dieselbe Höhe gebracht; die gesamte Folge wiederholt sich. Leerstellen in der Liste verhindern die Darstellung der Ebene.
- **Tint:** Farbe und Deckkraft der Ebene.
- **Opacity:** eigener Regler für die Deckkraft der Ebene (0 = unsichtbar, 1 = vollständig sichtbar). Wird mit der globalen Opacity, dem Alpha-Wert von Tint und der Tiefen-Ausblendung multipliziert. Gilt beim Himmel auch für die Fortsetzung unterhalb der Bildkante.
- **Extend Bottom To Camera:** für `Sky` aktiviert. Setzt die unterste Pixelreihe bis unter den sichtbaren Kamerabereich fort, damit durch leicht transparente Landschaften keine harte Himmelsunterkante scheint. Verwendet die vorhandene Textur; die Originalbilder werden nicht verändert.

Die Vorschau erscheint in Scene- und Game-View und orientiert sich immer an der zugewiesenen Spielkamera. Die generierten Kopien werden nicht in der Szene gespeichert. Sie passen ihre Anzahl an Kamerabreite und Zoom an und verwenden dasselbe Sprite/Material, keine Texturkopien. Änderungen außerhalb des Play-Modus bleiben gespeichert.

Der `SurfaceBackgroundController` hält die gemeinsame Kamera und einen festen **Camera Reference Position**-Wert. Bei dieser Kameraposition gelten die eingestellten Ebenenpositionen unverändert. Eine Änderung dieses Bezugspunkts verschiebt die Komposition; er wird beim Start nicht neu erfasst. Die Kamera darf kein Kind des Hintergrunds sein und der Hintergrund kein Kind der Kamera.

Am `SurfaceBackground` gelten zusätzlich zwei gemeinsame Einstellungen:

- **Y-Versatz:** verschiebt alle Ebenen um denselben Betrag in Welteinheiten, zusätzlich zum individuellen Höhenversatz. Standard: 0.
- **Zoom:** multipliziert Breite und Höhe aller Hintergrundbilder um ihre jeweilige Bildmitte. 1 = unverändert, 2 = doppelte Größe, 0,5 = halbe Größe. Individuelle Versätze, gemeinsamer Y-Versatz und Spielkamera bleiben unverändert. Die nahtlose Wiederholung passt sich automatisch an.

**Opacity** blendet das ganze Set aus. **Fade With Depth** ist für die Oberfläche eingeschaltet: zwischen Kamera-Y −5 und −20 wird weich ausgeblendet. Schwellen sind Weltkoordinaten und frei einstellbar. Für einen späteren Übergang kann ein zweites Hintergrund-Set über seine Deckkraft eingeblendet werden. Ein Untergrund-Set ist noch nicht enthalten.

Die vier PNGs sind als Full-Rect-Sprites mit 8192er Importlimit, unverändertem Seitenverhältnis, Bilinear-Filter, ohne Kompression und ohne Mipmaps eingerichtet. Die Bilddateien selbst bleiben unverändert. Die eigene Sorting Layer `Background` liegt hinter `Default`; das Unlit-Material erhält die gemalten Farben unabhängig von der Spielbeleuchtung.

Neue Szene: **Mining Game → Background → Create Surface Background** erstellt dieses Set mit den vorhandenen vier Dateien. Ein vorhandenes Set wird ausgewählt, nicht überschrieben. Weitere Ebenen durch Duplizieren eines Ebenenobjekts ergänzen.

Prüfung im verbundenen Unity Editor: `unity command run_script --file Tests/ParallaxChecks.cs` (außerhalb des Play-Modus). Die Prüfungen verwenden eine temporäre Vorschau-Szene und verändern die Spielszene nicht.

Himmel und Wolken sind wieder in einer gemeinsamen Sky-Ebene zusammengefasst. Parallax, Deckkraft und Versätze gelten gemeinsam für dieses Panorama.


