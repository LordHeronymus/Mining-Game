# Gameplay-Einstellungen: Analyse und Editorfenster

Öffnen: **Mining Game → Gameplay Settings**. Das Fenster bearbeitet die bestehenden
Assets und Komponenten der aktiven Szene. Es gibt keine zweite Kopie der Standardwerte.
Szenenobjekte werden automatisch gefunden; bei mehreren Instanzen gibt es eine Auswahl.
Änderungen unterstützen Undo. **Einstellungen speichern** sichert Assets und aktive Szene.
Im Play-Modus sind Standardwerte schreibgeschützt; dort dient F1 zum Ausprobieren.

## Konfigurierbare Mechaniken

| Bereich | Werte | Tatsächliche Quelle / Wirkung |
| --- | --- | --- |
| Spieler | Laufgeschwindigkeit, Sprungimpuls, Abbaugeschwindigkeit, Reichweite | Das im StatsManager zugewiesene PlayerBaseStats-Asset. Höherer Abbauwert ist schneller. |
| Bewegungsgefühl | Beschleunigungszeit, Bremszeit, Bodenfaktor | PlayerMovement. Der Bodenfaktor multipliziert Beschleunigung und Bremsung; Luftfaktor ist 1. |
| Sprungphysik | Masse, Gravitationsfaktor | Rigidbody2D am Spieler; beeinflussen Sprunghöhe und Fallverhalten. |
| Kamera | Nachführgeschwindigkeit, X/Y-Versatz, sichtbare Höhe | CameraFollow und dessen orthografische Camera. Z-Abstand bleibt unverändert. |
| Energie | Maximale Energie, Grundverbrauch, Mehrverbrauch bei Bewegung und Abbau | PlayerBaseStats und EnergyManager. Verbräuche werden addiert. |
| Aufladen | Kosten je Energieeinheit | EnergyMonolyth; Rundung auf ganze Münzen erfolgt in der bestehenden Spiellogik. |
| Map | Breite, Tiefe, Seed | MapGenerator. X wird zentriert, Y wächst ab 0 nach unten; neue Werte gelten bei nächstem Spielstart. |
| Erzverteilung | Priorität im Katalog, Noise an/aus, Skalierung, Seed-Versatz, Tiefenkurve | Aktive BlockRegistry und ihre Block-Assets. |
| Erz-Anteile | Medium und Rich nach Aderdicke: an/aus, Mindestgrösse, Medium-/Rich-Anteil für dünn/mittel/dick, Schwankung je Ader, Streuung entlang dünner Adern, dicke Stellen bevorzugen, Faktor je Erz | GPS/F1 → Erzverteilung → Erz-Anteile (`GpsPreferences.veinRichness`, `VeinRichnessSettings`). Gilt ab der nächsten neuen Welt; siehe `Docs/Erz-Reichhaltigkeit.md`. |
| Abbau & Belohnung | Härte, Punkte, Beute-Gegenstand | Je Block-Asset; pro abgebautem Block wird ein Beute-Exemplar vergeben. |
| Wirtschaft | Verkaufspreis je Stück, Kategorie der zugeordneten Beute | ItemSO; 0 ist nicht verkaufbar, im Verkaufsfenster werden nur Ore-Items angezeigt. |
| Debug | JSON-Status, Speicherort, Override zurücksetzen | gameplay-settings.json in Application.persistentDataPath. Zurücksetzen bewahrt eine .bak-Datei. |
| Licht | Tageslichtstärke, Grundhelligkeit, Verlust nach unten, seitlich/aufwärts und durch Blöcke | MapLighting an der Tilemap. Abbau aktualisiert die Lichtwege automatisch. |
| Herausforderungen | XP je Stufe und Zeitraum, Anzahl je Stufe, XXL-Chance, Abschlussbonus, Monatsprojekt-Etappen, Fleiss, Mengenwachstum, Schwellen für Rückkehr und volle Taschen | GPS/F1 → Upgrades → Herausforderungen (`GpsPreferences.challenges`, `ChallengeSettings`). Gilt ab dem nächsten Tages-, Wochen- bzw. Monatswechsel; die Schwellen für Rückkehr und volle Taschen sofort. Siehe unten und `Docs/Challenges.md`. |

## Tageslicht

Unter **Licht** werden die Standardwerte der aktiven Map eingestellt und mit
**Einstellungen speichern** in der Szene gesichert. Licht startet an der oberen
Mapkante. Pro Zelle verliert es nach unten standardmäßig 0,3 %, seitlich und
aufwärts 8 % des verbleibenden Lichts; ein fester Block absorbiert zusätzlich 28 %.
Die Verluste wirken multiplikativ. So nimmt die Helligkeit zunächst stark ab
und läuft danach weich aus. Offene Schächte bleiben länger hell als Seitengänge.
**Exponentielle Stärke** skaliert den Abfall: 1 ist Standard, höhere Werte machen
ihn stärker, kleinere Werte verlängern die Reichweite. Formel je Schritt:
`Restlicht × (1 − Richtungsverlust)^Stärke × (1 − Blockverlust)^Stärke`
(der Blockfaktor gilt nur für belegte Zellen).
Unter 2 % Restlicht blendet eine weiche Kurve aus; ab 0,1 % endet die Ausbreitung
in vollständigem Schwarz.
Grundhelligkeit 0 macht unbeleuchtete Bereiche vollständig schwarz (Standard).
Höhere Werte halten sie auf Wunsch schwach sichtbar.

Die Ausbreitung nutzt den hellsten Weg zur jeweiligen Zelle. Nach einem Richtungswechsel
bleibt die bisher verlorene Helligkeit verloren. Abwärtsbewegung kostet auch nach
einer Ecke wieder den geringeren Abwärtsverlust. Lichtquellen wie Fackeln sind noch
nicht Teil dieser Mechanik.

Eine gemeinsame Lichttextur verdunkelt die Spielwelt. Die Berechnung läuft bei
Änderungen mit begrenztem Aufwand pro Frame; HUD und Map Overview bleiben unbeeinflusst.
Der Solver-Test liegt unter `Tests/LightingChecks.cs` und kann über
`unity command run_script --file Tests/LightingChecks.cs` ausgeführt werden.

## Herausforderungen

Pfad: GPS-Fenster bzw. F1 → **Upgrades → Herausforderungen → „XP, Anzahl und Mengen“**
(`GpsPreferences.challenges`, Klasse `ChallengeSettings`). Werte ausserhalb des Bereichs werden
begrenzt (`Sanitized()`). Die Standardwerte stehen im Code, bis der Abschnitt in GPS gespeichert wird.

**Wann Änderungen wirken:** Jede Runde speichert XP, Anzahl, Mengen, XXL-Wurf, Bonus, Projekt-XP
und die Fleiss-Einstellungen beim Ziehen. Änderungen daran gelten deshalb erst **ab dem nächsten Tages-,
Wochen- bzw. Monatswechsel**. Die laufende Runde bleibt unverändert. Sofort, also ab der nächsten
Rückkehr, wirken nur die Rückkehr- und Taschenschwellen sowie „Fleiss: Tagesaufgaben pro Tag“ bei
der Anrechnung eines Tages.

Je Zeitraum (Gruppen „Täglich“, „Wöchentlich“, „Monatlich“):

| Feld | Einheit / Bereich | Standard T / W / M |
| --- | --- | --- |
| XP leicht / mittel / schwer / XXL | XP, 0–1'000'000 | 300/600/1'200/3'000 · 1'500/3'000/5'000/10'000 · 5'000/10'000/20'000/0 |
| Anzahl leicht / mittel / schwer | Aufgaben, 0–10 (höchstens 16 pro Runde) | 3/3/1 · 3/3/2 · 2/2/2 |
| XXL-Chance (%) | %, 0–100 | 5 · 5 · 0 |
| Bonus alle erledigt (XP) | XP, 0–1'000'000 | 1'000 · 5'000 · 15'000 |

Monatlich haben „XP XXL“ und „XXL-Chance“ keine Wirkung, weil es keine monatlichen XXL-Mengen gibt.

Allgemein:

| Feld | Einheit / Bereich | Standard |
| --- | --- | --- |
| Monatsprojekt Etappe 1 / 2 / 3 (XP) | XP, 0–1'000'000 | 5'000 / 10'000 / 20'000 |
| Fleiss: Tage pro Woche (0 = aus) | Tage, 0–7 | 5 |
| Fleiss: Tagesaufgaben pro Tag | Aufgaben, 1–10 | 3 |
| XXL ab Schicht | Schicht, 1–5 | 3 |
| Mengenwachstum pro Schicht (%) | %, 0–500 | 10 |
| Mengenwachstum pro 10 Level (%) | %, 0–500 | 2 |
| Max. Mengenwachstum (%) | %, 0–500 | 60 |
| Rückkehr: Mindesttiefe (Blöcke) | Blockreihen, 1–1'200 | 30 |
| Rückkehr: neue Blöcke pro Tour | Blöcke, 0–200 | 10 |
| Volle Taschen: Mindesttiefe (Blöcke) | Blockreihen, 1–1'200 | 10 |
| Volle Taschen: Ladung (% Tragkraft) | %, 10–300 | 90 |
| Volle Taschen: Zuwachs (% Tragkraft) | %, 0–300 | 50 |
| Volle Taschen: Bezugs-Tragkraft max. (kg) | kg, 10–2'000 | 150 |

Bedeutung der Felder, Auswahlregeln und Zählweise: `Docs/Challenges.md`, Abschnitte 2, 3 und 5.

## Map-Generierung im Detail

- Beim Play-Stopp bleibt die zuletzt gespielte Tilemap im Editor erhalten,
  inklusive abgebauter/gesetzter Tiles, Tile-Farben, Drehungen und Tile-Flags.
  Auch Map Overview zeigt diesen tatsächlichen Stand. Die Szene wird als geändert
  markiert. Die Karte selbst wird nie in `SampleScene.unity` gespeichert: Speichern
  leert Terrain-, Erz- und Artefakt-Kacheln (`LoadingSceneData`). AutoSave speichert
  die Gameplay-Szene deshalb nicht, solange eine solche Karte geladen ist (auch nach
  „Map im Editor generieren“); erst manuelles Speichern sichert die Szene und leert die Karte.
  Andere geöffnete Szenen und geänderte Assets speichert AutoSave in dieser Zeit weiter
  (ohne Szenen-Backup der SampleScene).
- Jeder Play-Start leert diese Vorschau und generiert eine frische Map.
  Ein fester Seed erzeugt weiterhin dieselbe Ausgangskarte; Seed 0 wählt zufällig.
  Die Übergabe beim Play-Stopp liegt komprimiert unter `Library/LastPlayedMaps`.
  Spieler-, Inventar- und Debug-Einstellungsstände werden dadurch nicht übernommen.

- Seed 0 erzeugt beim Start einen zufälligen Seed; ein fester Seed ist bei gleichen
  Einstellungen reproduzierbar. Auch Reihenfolge, Kurven und Tile-Varianten wirken mit.
- Zuerst wird Stone als Füllblock gewählt. Anschließend werden aktive Noise-Blöcke
  in Registry-Reihenfolge geprüft. Der erste passende Block gewinnt.
- Die Tiefenkurve nutzt X = relative Tiefe (0 Oberfläche, 1 Boden) und Y = Häufigkeit.
  Der Generator normalisiert mit y / mapHeight: die letzte Zeile liegt knapp unter 1.
- Die Häufigkeit wird über eine empirische Noise-Verteilung in einen Schwellwert
  übersetzt. 5 % auf der Kurve sind keine garantierten 5 % der fertigen Karte:
  Katalogpriorität und räumliches Noise-Muster beeinflussen das Ergebnis.
- Kleinere Noise-Skalierung erzeugt größere zusammenhängende Flächen, größere Werte
  feinere Muster. Der Seed-Versatz verschiebt das Muster eines einzelnen Erztyps.
- Der Generator befüllt jede Zelle einzeln. Große Karten kosten entsprechend Zeit;
  das Fenster zeigt Zellzahl und eine Warnung ab einer Million Zellen.
- Die aktive Szene enthält aktuell eine Karte mit 400 × 1000 Zellen und fünf
  Blocktypen: Stone, Copper, Iron, Silver und Gold. Das Fenster liest den Katalog
  dynamisch und ist nicht auf diese fünf festgelegt.

## Standardwerte und Debug-Overrides

Das F1-Fenster besitzt die Tabs **Gameplay** und **Testeinstellungen**.
Im Editor steuert **Map nach Play-Stopp im Editor behalten** im Gameplay-Tab, ob die
gespielte Map samt Änderungen übernommen wird. Ausgeschaltet wird die vollständige,
zu Beginn desselben Play-Durchlaufs generierte Map ohne Spieländerungen angezeigt.
Dafür wird direkt nach der Generierung ein separater Snapshot gesichert.
Der Schalter speichert automatisch in der Test-JSON und
gilt unabhängig von **Testmodus aktiv**. Bestehende Einstellungen behalten das
bisherige Verhalten (Übernahme an). Jeder Play-Start generiert weiterhin eine neue Map.
**Testmodus aktiv** schaltet den Testfaktor und alle drei Testmodi gemeinsam wirksam
oder unwirksam. Die einzelnen Werte bleiben erhalten. Der Hauptschalter wird automatisch
in der Test-JSON gespeichert; bestehende Dateien ohne Hauptschalter bleiben aktiv.
Gameplay enthält Basis-Abbaugeschwindigkeit und Licht. Im Test-Tab multipliziert
der Abbau-Testfaktor diese Basisgeschwindigkeit zusätzlich zu Werkzeug-Upgrades.
Änderungen des Testfaktors speichern nach Enter oder Verlassen des Feldes automatisch
in `gameplay-test-settings.json`. **Als Standard festlegen** liest diesen Faktor nicht.
Der Faktor wird beim nächsten Play-Start wieder geladen und wirkt nur im Editor
und in Development Builds. Reguläre Builds verwenden immer Testfaktor 1.
Bereits früher hoch eingestellte Basiswerte werden nicht automatisch umgerechnet.

Die drei Test-Schalter speichern sich beim Umschalten automatisch in derselben
separaten Test-JSON und bleiben nach Play-Stopp erhalten:

- **God Mode:** bereitet Unverwundbarkeit vor. Künftige Schadensmechaniken müssen
  `StatsManager.CanTakeDamage` bzw. `IsInvulnerable` beachten; aktuell gibt es kein Schadenssystem.
- **Kein Energieverbrauch:** unterbindet den Verbrauch im Stand, beim Bewegen und
  beim Abbauen. Bereits verbrauchte Energie wird nicht aufgefüllt.
- **Fly Mode:** setzt die Gravitation auf 0. W fliegt aufwärts, S abwärts und A/D
  seitwärts. Ohne Vertikaleingabe schwebt der Spieler. Horizontale Steuerung
  und Kollisionen bleiben aktiv. Beim Ausschalten gilt die vorherige Gravitation.

Bei geöffnetem Debug-Panel werden keine Flug- oder Bewegungsbefehle verarbeitet.
Alle drei Schalter sind in regulären Builds wirkungslos und bleiben von der
Gameplay-Standardübernahme ausgeschlossen.

Das Editorfenster schreibt Standardwerte. Die F1-JSON überschreibt im Editor und in
Development Builds die Basis-Abbaugeschwindigkeit sowie optional die Lichtwerte. Reguläre
Builds ignorieren diese Datei. Ein gespeicherter Override wird oben im Fenster angezeigt,
damit Änderungen am Asset nicht scheinbar wirkungslos bleiben. Nach dem Zurücksetzen
gilt beim nächsten Spielstart wieder das Asset.

Im F1-Panel hat **Licht** eine eigene Sektion mit An/Aus, Tageslicht, Grundhelligkeit,
Verlust nach unten, seitlich/oben, Blockverlust und exponentieller Stärke. Änderungen
wirken nach Enter oder Verlassen des Feldes direkt auf die laufende Map.
Gültige Änderungen werden automatisch in der Debug-JSON gespeichert; der Lichtschalter
speichert sofort. **Standardwerte** stellt die beim Spielstart gelesenen Asset- und
Szenenwerte wieder her und speichert die Rücksetzung automatisch in der Debug-JSON.
Die Projekt-Standardwerte werden ausschließlich über **Als Standard festlegen** geändert.
Alte JSON-Dateien ohne Lichtwerte verwenden weiterhin
die Szene. Lichtwerte werden erst nach einer Änderung im Debug-Panel zu Overrides.

**Als Standard festlegen** (nur im Unity Editor) merkt die aktuellen Abbau- und
Lichtwerte vor. Beim Beenden des Play-Modus werden sie im PlayerBaseStats-Asset
und in der Map-Szene gespeichert und damit auch für reguläre Builds übernommen.
Weitere Änderungen nach dem Klick werden erst mit einem erneuten Klick vorgemerkt.
Der bisherige JSON-Override wird als `.defaults-….bak` gesichert und entfernt,
damit er spätere Änderungen der Standardwerte nicht verdeckt. Wurde nach dem Klick
eine neue Debug-JSON gespeichert, bleibt diese absichtlich erhalten.

## Bewusst nicht aufgenommen

- UI-Fades, Farben, Schriftgrößen, Zahleneffekte, Soundabstände und Audio-Clips:
  Darstellung und Feedback, keine grundlegenden Balancingwerte.
- Referenzverkabelung, LayerMask, Colliderdetails, Tile-Sprites, interne IDs und
  Noise-Stichprobenauflösung: technische Einrichtung statt Gameplay-Balancing.
- Aktuelles Geld, Punkte, Energie und MiningSpeedMultiplier: Laufzeitstände,
  keine dauerhaft zu bearbeitenden Basiswerte.
- Block.isSolid ist zwar vorhanden, wird von den aktiven Mechaniken nicht ausgewertet;
  die tatsächliche Kollision hängt von Tiles/Collidern ab. Ein Regler wäre irreführend.
- Inventarlimits, Startinventar, Kaufpreise, Werkzeugschaden, Sprengradius, Upgradekosten
  und Verhalten bei leerer Energie: dafür existiert noch keine entsprechende Mechanik.
  Das Fenster erfindet keine unwirksamen Einstellungen dafür.

## Bei der Analyse festgestellte Grenzen im bestehenden Spiel

Diese Punkte wurden durch das Editorfenster nicht verändert:

1. Energie 0 verhindert Bewegung oder Abbau derzeit nicht.
2. Bei Teilaufladung zieht EnergyMonolyth zuerst das gesamte Geld ab und berechnet
   erst danach die Auflademenge aus dem nun leeren Geldbestand. Ergebnis: keine
   zusätzliche Energie bei zu wenig Geld. Vollaufladung rundet Kosten ab.
3. Startenergie wird in EnergyManager.Start aus StatsManager.MaxEnergy gelesen;
   die Initialisierungsreihenfolge gegenüber StatsManager.Start ist nicht explizit.
4. Das Kauf-Panel hat noch keine Kaufmechanik. Tool-Assets allein implementieren
   keine Leiter, Fackel oder Dynamitfunktion.
5. Map-Seed initialisiert UnityEngine.Random global; Änderungen am Seed beeinflussen
   daher auch spätere Nutzer desselben Zufallsgenerators.
6. Der Abbau wird technisch auf mindestens 0,0001 Sekunden begrenzt und entfernt
   höchstens einen Block pro Update. Extrem hohe Werte sind dadurch begrenzt.

## Erweiterung

Neue Mechaniken erhalten zuerst wirksame Werte in ihrem Asset oder ihrer Komponente.
Anschließend ergänzt man im passenden Fensterbereich ein benanntes Feld mit Einheit,
Tooltip und Wertebereich. Damit bleibt das Fenster eine Übersicht der wirklich
genutzten Konfiguration statt eines zweiten, unabhängigen Einstellungssystems.
