# Challenges (Herausforderungen)

Umsetzung des freigegebenen Konzepts `Docs/Challenges-Konzept.md` (18.10.2026). Massgebend für Namen, Mengen und Stufen ist der Code: `Assets/GameObjects/Progression/ChallengeCatalog.cs` (Katalog), `ChallengeEngine.cs` (Auswahl, Zählen, Prüfen), `ChallengeSettings.cs` (GPS-Werte), `MetaChallengeRounds.cs` (Runden, Zählstellen, Langzeit) und `MetaChallengePeriods.cs` (Speicherprüfung, Umwandlung alter Profile). Die Oberfläche steckt in `Assets/GameObjects/UI/ChallengePanel.cs` und `ChallengeArt.cs`.

Abkürzungen: **T** täglich, **W** wöchentlich, **M** monatlich, **S1–S5** Schicht 1–5. Mengen stehen als Leicht/Mittel/Schwer/XXL (monatlich Leicht/Mittel/Schwer), „–“ heisst, dass die Stufe nicht angeboten wird.

## 1. Überblick

### Zeiträume

| Zeitraum | Wechsel (Ortszeit) | Aufgaben | Bonus, wenn alles erledigt ist |
| --- | --- | --- | --- |
| Täglich | Mitternacht | 7: 3 leicht, 3 mittel, 1 schwer. Mit 5 % Chance wird die schwere zur XXL-Aufgabe (ab Schicht 3) | +1'000 XP |
| Wöchentlich | Montag 00:00 | 8: 3 leicht, 3 mittel (eine davon ist immer die Fleiss-Aufgabe), 2 schwer. 5 % XXL-Chance | +5'000 XP |
| Monatlich (neu) | 1. des Monats 00:00 | Monatsprojekt mit 3 Etappen, dazu 6 Aufgaben: 2 leicht, 2 mittel, 2 schwer. Kein XXL | +15'000 XP |
| Langzeit | – | 25 Meilenstein-Linien mit zusammen 74 Stufen, dazu die 3 Run-Ziele des laufenden Runs | – |

Sommer- und Winterzeit sind berücksichtigt: Ein Umstellungstag hat 23 oder 25 Stunden, ein Monat mit Umstellung ist eine Stunde kürzer oder länger. Jede Runde wird beim ersten Zugriff im Zeitraum einmal gezogen und gespeichert. Laden, Level-Aufstieg und Run-Wechsel ändern sie nicht mehr. Die erste Monatsrunde beginnt sofort und gilt für den ganzen laufenden Monat.

### Stufen und XP (Standardwerte, über GPS einstellbar)

| Stufe | Farbe | Täglich | Wöchentlich | Monatlich |
| --- | --- | ---: | ---: | ---: |
| Leicht | grün | 300 | 1'500 | 5'000 |
| Mittel | blau | 600 | 3'000 | 10'000 |
| Schwer | violett | 1'200 | 5'000 | 20'000 |
| XXL (selten) | gold | 3'000 | 10'000 | – |
| Monatsprojekt, Etappen 1/2/3 | gold | – | – | 5'000 / 10'000 / 20'000 |
| Bonus für alles erledigt | – | 1'000 | 5'000 | 15'000 |

Ist alles erledigt, gibt es höchstens 4'900 XP pro Tag (6'700 mit XXL), 28'500 pro Woche (33'500 mit XXL) und 120'000 pro Monat. Die gesamte XP aus Zeiträumen, Boni, Projekten und den neuen Langzeitstufen fliesst in `periodRewardXp`, also in denselben Topf wie bisher.

### Fleiss-Aufgabe

„Fleissige Woche“ (Familie Spezial) belegt in jeder Woche einen Mittel-Platz und bringt die XP der Stufe Mittel (3'000). Ziel sind **5 Tage**, an denen jeweils mindestens **3 Tagesaufgaben** erledigt wurden. Die Einheit lautet „Tage mit 3 Tagesaufgaben“. Ein Tag wird angerechnet, sobald seine Tagesrunde diese Zahl erreicht, höchstens aber so viele, wie die Runde Aufgaben hat, und mindestens eine. Jeder Wochentag zählt nur einmal, und zwar nur Tage dieser Woche mit einer neuen Tagesrunde. Ein umgewandelter alter Tag zählt nicht. Mit „Fleiss: Tage pro Woche = 0“ ist die Aufgabe abgeschaltet.

### Monatsprojekt

Jeden Monat wird eines von 9 Projekten gezogen, möglichst nicht dasselbe wie im Vormonat. Es erscheint als breite Kachel mit drei Etappen. Jede Etappe zahlt ihre XP sofort aus, die letzte am meisten. Das Projekt zählt für den Monatsbonus als eine Aufgabe: Es ist erledigt, wenn alle drei Etappen geschafft sind. Die Tätigkeit des Projekts kommt im selben Monat nicht noch einmal als normale Aufgabe vor. Die Projekte stehen in Abschnitt 9.

### Langzeit-Linien und Medaillen

Die 17 bisherigen Meilensteine bleiben mit ihren IDs, Zielen und XP gültig. Sie sind jetzt die ersten Stufen von Linien. Dazu kommen neue Stufen und 13 neue Linien. Die Medaille richtet sich nach der Zahl der erreichten Stufen: 1 Bronze, 2 Silber, 3 Gold, 4 Platin.

- Die meisten Linien haben 3 Stufen.
- „Abstieg in die Tiefe“ hat 4 Stufen und reicht bis Platin.
- „Meister fremder Künste“ und „Edelsteinjäger“ haben nur 2 Stufen, weil mehr nicht erreichbar ist: Es gibt nur 3 herstellbare Exotics und 4 erreichbare seltene Funde.

Es gibt zwei Arten von Linien:

- **Ledger-Linien** rechnen mit den Run-Ledgern aller bisherigen Runs, also auch mit der Vergangenheit. Dazu gehören Tiefe, Exotics, Siege, seltene Funde, Erze, Kohle, Kupfer, Eisen, Silber, Gold, Gebiete, Entdeckungen, Edelsteine und Ultronium. Erfahrene Spieler erhalten neue Stufen dieser Linien deshalb beim nächsten Run-Start sofort.
- **Zähler-Linien** beginnen mit diesem Update bei 0. Das sind Sprengen, Fackeln, Leitern, volle Rückkehren, Rückkehren, Erzadern, Verkauf, Herstellen, Artefakte, Bäume und erledigte Aufgaben.

Die 17 alten Stufen bringen zusammen 38'050 XP, die 57 neuen 242'900 XP. Die Run-Ziele sind unverändert, siehe Abschnitt 8.4.

## 2. Auswahlregeln

**Anzahl je Stufe:** Sie kommt aus GPS (Standard: T 3/3/1, W 3/3/2, M 2/2/2) und ist auf höchstens 16 Aufgaben pro Runde begrenzt, Projekt und Fleiss eingerechnet. Ein Platz, der sich nicht füllen lässt, bleibt leer.

**Familien und Grenzen:**

| Familie | max. T | max. W | max. M | Mindestens |
| --- | ---: | ---: | ---: | --- |
| Abbau | 3 | 3 | 2 | 1 |
| Erkundung | 2 | 3 | 2 | 1 |
| Handwerk | 2 | 2 | 2 | 1 |
| Handel | 1 | 1 | 1 | – |
| Artefakte | 1 | 2 | 1 | – |

Fleiss und Monatsprojekt zählen nicht zu diesen Grenzen. Das Projekt belegt aber seine Tätigkeit.

**Keine doppelte Tätigkeit:** Vorlagen mit derselben Tätigkeit stehen nie zusammen in einer Runde, das Projekt eingeschlossen. Gemeinsame Tätigkeiten haben:

- Blöcke und Schichtblöcke
- „Schätze des Berges“, die Erzvarianten und „Erzmagnat“
- Edelsteine und Edelsteinvarianten
- Tiefe, Schichten besucht und „Tiefenexpedition“
- „Hin und zurück“, „Sicher zurück“ und „Expeditionsleiter“
- Artefakte, Entdeckungen und „Schatzkammer“. Jedes abgebaute Artefakt ist auch eine Entdeckung.

Jedes andere Projekt sperrt die gleichnamige Vorlage: Verkauf, Gebiete, Sprengen, Herstellen und Erzadern.

**Vortagsvermeidung:** Folgt eine Runde direkt auf die vorherige, meidet die Auswahl deren Tätigkeiten. Das gilt in den ersten 45 von 60 Versuchen. Bei Vorlagen mit Gegenstand (Erzart, Artefakttyp, Schicht) wird nach Möglichkeit ein anderer Gegenstand gewählt. Das Projekt des Vormonats wird ausgelassen. Ab Versuch 46 fällt die Vermeidung weg. Gelingt auch dann kein Versuch mit allen Mindestfamilien, folgt ein letzter Versuch ohne Vermeidung und ohne Mindestwerte. In der Simulation mit 226'800 Runden kam das nie vor.

**XXL:** Mit 5 % Chance (T und W) wird der letzte Schwer-Platz zu einem XXL-Platz, gibt es keinen, dann der letzte Mittel-Platz. Voraussetzungen:

- Schicht 3 ist erreicht (GPS „XXL ab Schicht“).
- Es gibt einen XXL-tauglichen Kandidaten.

Monatlich gibt es kein XXL. Der Wurf ist pro Zeitraum fest und unabhängig von der übrigen Auswahl. In einzelnen Kalenderjahren weicht die Quote spürbar ab: 2026 in Zürich sind es nur 2,5 %, langfristig 5,06 %.

**Freischaltungen:** Alle Bedingungen werden beim Ziehen geprüft.

- **Erreichte Schicht:** Massgebend ist die tiefste Schicht des laufenden Runs oder die tiefste Heimatschicht eines gespeicherten Spielstands. Sie ist begrenzt auf die je erreichte Schicht des Profils. Eine frische Welt bekommt also keine Aufgaben für Schicht 5, nur weil früher einmal Schicht 5 erreicht wurde.
- **Gegenstände nach Schicht:**
  - Silber ab S2
  - Gold, Titan, Granat, Edelsteine ab S3
  - Platin, Wolfram, Rubin, Diamant ab S4
  - Ultronium und Altar ab S5
  - „Sicher zurück“, „Hin und zurück“ und „Expeditionsleiter“ ab S2
  - Artefakttypen ab ihrer ersten Schicht: S1 Sonnenscheibe und Alter Kompass, S2 Bernsteinskarabäus, S3 Jademaske und Siegelring, S4 Astrolabium, Antike Vase, Fossile Spirale und Kupfertafel, S5 Kristall-Sanduhr.
- **Vielfalt:** Eine Stufe wird nur angeboten, wenn ihr Ziel höchstens der Zahl der verfügbaren Arten entspricht.
  - Erzarten: 3 / 4 / 7 / 11 / 12 je erreichter Schicht
  - Artefaktarten: 2 / 3 / 5 / 9 / 10
  - Schichten besucht: Ziel höchstens gleich der erreichten Schicht
- **Ultronium täglich:** nur mit einer Spitzhacke der Härte 8 (Diamant).
- **Level:** Exotics ab Meta-Level 5.
- **Herstellbarkeit** (T und W): Gezählt wird, was im laufenden Run freigeschaltet und noch nicht besessen ist.
  - „Werkzeuge“ und „Ausrüstung“: herstellbare Anzahl mindestens gleich dem Ziel.
  - „Upgrades“: herstellbare Stufen der Spitzhacke, der Energie oder der Tragkraft mindestens gleich dem Ziel.
  - Eisenspitzhacke, Obsidianspitzhacke und Reise-Monolith zählen nie mit, siehe Abschnitt 10.
  - Wird eine Runde ohne laufenden Run gezogen (Hauptmenü), gilt ein frischer Run: nur Rezepte ohne Bauplan (Axt, Sense, Dynamit). Werkzeug-Aufgaben bis 2 sind dann möglich, Ausrüstung, Upgrades und Medkits (alle mit Bauplan, der in jedem Run neu gekauft wird) entfallen für diesen Zeitraum.
  - Dynamit braucht das freie Dynamit-Rezept, Medkits den Medkit-Bauplan.

**Mengenwachstum:** Die Vorlagen Blöcke, „Schätze des Berges“, Erzvarianten, „Meister am Amboss“, „Erzmagnat“ und „Meisterschmiede“ (in den Tabellen mit ↑ markiert) wachsen um den Faktor 1 + min(Max, pro Schicht × (Schicht − 1) + pro 10 Level × ⌊(Level − 1) / 10⌋) / 100. Standard: 10 % pro Schicht, 2 % pro 10 Level, höchstens 60 %. Gerundet wird „schön“:

| Wert | Rundung |
| --- | --- |
| unter 20 | ganze Zahl |
| unter 100 | auf 5 |
| unter 1'000 | auf 10 |
| unter 10'000 | auf 50 |
| darüber | auf 500 |

Ist eine Stufe nach dem Runden nicht grösser als die nächstniedrigere angebotene Stufe, entfällt sie.

**Gewicht der Schichten:** Alle Vorlagen haben dasselbe Gewicht. Bei „Schichtblöcke“ zählt die tiefste erreichte Schicht doppelt. Schicht 1 wird nur angeboten, solange höchstens Schicht 2 erreicht ist. Weitere Faktoren für Schichtblöcke: S3 ×0,8, S4 ×0,6, S5 ×0,5.

**Handelsfaktoren** (statt Wachstum):

| Schicht | 1 | 2 | 3 | 4 | 5 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Verkaufserlös, „Goldener Lauf“, „Handelsimperium“ | ×0,3 | ×0,6 | ×1 | ×2,5 | ×5 |
| „Grosser Handschlag“ (Einzelverkauf) | ×0,1 | ×0,25 | ×0,5 | ×2,5 | ×5 |

**Tiefe** (Blockreihen, keine Rundung):

| Erreichte Schicht | „Ruf der Tiefe“ T | „Ruf der Tiefe“ W | „Tiefenexpedition“ (M) |
| --- | --- | --- | --- |
| 1 | 15/25/40/– | 30/45/80/– | 40/80/150 |
| 2 | 40/80/140/– | 80/140/200/– | 100/200/350 |
| 3 | 100/200/350/440 | 200/350/440/600 | 200/400/600 |
| 4 | 250/450/700/880 | 450/700/880/1'050 | 300/600/900 |
| 5 | 400/700/950/1'150 | 700/950/1'100/1'190 | 300/600/1'000 |

**„Hin und zurück“** (Anzahl × „Rückkehr ab Schicht n oder tiefer“):

| Erreichte Schicht | Täglich | Wöchentlich | Monatlich |
| --- | --- | --- | --- |
| 2 | 1×S2 / – / – / – | 3×S2 / – / 4×S2 / – | 10×S2 / 12×S2 / 15×S2 |
| 3 | 1×S2 / – / 1×S3 / – | 3×S2 / – / 4×S3 / – | 10×S2 / 12×S2 / 15×S3 |
| 4 | 1×S2 / 1×S3 / 1×S4 / – | 3×S2 / 3×S3 / 4×S4 / – | 10×S2 / 12×S3 / 15×S4 |
| 5 | 1×S3 / 1×S4 / 1×S5 / 2×S5 | 3×S3 / 3×S4 / 4×S5 / 6×S5 | 10×S3 / 12×S4 / 15×S5 |

**Determinismus:**

- Der Zufall hängt nur vom Start des Zeitraums und vom Zeitraum ab, mit je einem eigenen `System.Random` für XXL, Projekt und Aufgaben. Unitys Zufallszustand bleibt unberührt.
- Die Uhr zurückzustellen bringt nichts, weil die Profiluhr nur vorwärts läuft.
- Nach einem Zeitzonenwechsel beginnt die nächste Runde frühestens dort, wo die alte endete.

## 3. Zählweise

Grundregel: Gezählt wird nur in einem laufenden, nicht beendeten Run und nur, wenn Belohnungen erlaubt sind. Belohnungen sind gesperrt in diesen Fällen:

- während Laden oder Szenenwechsel
- Fly, NoClip, God Mode, kein Energieverbrauch, unendlich Geld oder kein Gewicht ist aktiv
- Abbau- oder Bewegungsfaktor über 1

Jedes Ereignis zählt gleichzeitig für die Tages-, Wochen- und Monatsrunde. Pro Runde zahlt jede Aufgabe einmal.

| Kennzahl | Aufgaben | Zählt, wenn … | Schutz gegen Doppelzählung |
| --- | --- | --- | --- |
| `blocks`, `layer-blocks`, `blasted` | Schaufel und Hacke, Schichtblöcke, Sprengmeister, Grosse Sprengung, Linie „Meister der Sprengung“ | … ein Feld abgebaut wird: Erde, Stein, Erz oder Artefakt. Die Schicht ergibt sich aus der Tiefe der Zelle. Mit Dynamit zählt es zusätzlich als „gesprengt“. | Abgebaute Zelle im Zellen-Ledger der Welt (Blöcke `#1M:…`, siehe `Docs/Progression.md` „Grösse des Profils“). Die Schlüssel `mined:x:y` und die älteren `surface-block:`, `l2-block:`, `l3-block:`, `xxl-layer:` werden beim Laden in das Ledger gefaltet; diese Zellen zählen nicht noch einmal. Bis Schritt 2 schreibt das Spiel neue Zellen weiter als `mined:x:y`. |
| `resources`, `ore`, `gems`, `ore-variety` | Schätze des Berges, Erzvarianten, Edelsteine und Varianten, Ultronium, Bunte Ausbeute, Erzmagnat | … Erz ins Inventar kommt. Gezählt werden Stück, ein Feld gibt 1–2. Edelsteine sind Granat, Rubin und Diamant. | Erz-Zelle pro Welt im Zellen-Ledger (Blöcke `#1O<Item>:…`; bis Schritt 2 schreibt das Spiel neue Erz-Zellen weiter als `item:x:y`). Die Vielfalt läuft über eine Bitmaske pro Runde. |
| `veins` | Ader um Ader, Aderjäger, Aderleerer | … das letzte Feld einer Erzader abgebaut wird. Eine Ader hat mindestens 2 zusammenhängende Felder derselben Erzart, und kein Nachbarfeld darf dieses Erz noch enthalten. | Schlüssel `vein:Erz:x:y` mit einer festen Bezugszelle der Ader (die tiefste, bei Gleichstand die linke). Die Suche reicht höchstens 1'024 Felder weit. |
| `trees`, `grass` | Ruf der Wälder, Grüne Ernte, Holzfäller | … ein Baum gefällt oder ein Grasbüschel geerntet wird. Heilkraut zählt nicht. | Bestehender Ernteschlüssel pro Quelle und Welt. |
| `depth` | Ruf der Tiefe, Tiefenexpedition | … auf einer Tour, die in dieser Sitzung an der Oberfläche begann, eine grössere Tiefe erreicht wird. | Nur Touren ab der Oberfläche zählen, das Laden eines tiefen Spielstands zählt nichts. Höchstwert pro Runde. |
| `layers` | Grenzgänger | … auf einer Tour ab der Oberfläche eine neue Schicht betreten wird. | Wie Tiefe. Bitmaske pro Runde. |
| `altars` | Ruf des Altars | … man auf einer Tour ab der Oberfläche bis auf 8 Einheiten an den Altar herankommt. | Wie Tiefe. Höchstens 1 pro Runde, in jeder Runde neu erreichbar. |
| `regions` | Unbetretene Pfade, Kartograf | … in einem 12×12-Gebiet 24 Felder aufgedeckt sind. | Bestehender Gebietsschlüssel pro Welt. |
| `cells` | Verborgene Weiten | … ein Kartenfeld zum ersten Mal aufgedeckt wird. | Aufgedeckte Zelle im Zellen-Ledger der Welt (Blöcke `#1R:…`; alte Schlüssel `revealed:x:y` werden gefaltet und bis Schritt 2 weiter geschrieben). Felder, die beim Laden schon sichtbar sind, zählen nicht. |
| `discoveries` | Geheimnisse im Stein | … eine Entdeckung gelingt: Artefakt abgebaut, Findling zerstört, Bauplan-Versteck gefunden oder Altar zum ersten Mal erreicht. | Bestehender Entdeckungsschlüssel pro Welt. |
| `returns`, `round-trips`, `full-returns` | Sicher zurück, Hin und zurück, Volle Taschen, Expeditionsleiter, Heimkehrer, Schwer beladen | … man von einer Tour ab der Oberfläche an die Oberfläche zurückkehrt und unterwegs mindestens **10 neue Blöcke** gezählt wurden. Dann gilt: **Sicher zurück** ab einer Tiefe von 30. **Hin und zurück**, wenn die tiefste Schicht der Tour mindestens die Zielschicht ist (ab S2). **Volle Taschen**, wenn die Ladung mindestens 90 % und der Zuwachs auf der Tour mindestens 50 % der Bezugs-Tragkraft beträgt und die Tiefe mindestens 10 ist. Die Bezugs-Tragkraft ist die kleinere von Tragkraft und 150 kg. | Mindestzahl neuer Blöcke pro Tour, damit Pendeln an einer Leiter nichts bringt. Die Tour beginnt nur an der Oberfläche. Ein Debugmodus bricht sie ab. Pausenmenü, Karte, Werkbank und Artefakt-Popup unterbrechen sie nicht. |
| `torches-placed`, `ladders-placed` | Lichtbringer, Kletterer, gleichnamige Linien | … die Zahl der gleichzeitig platzierten Fackeln (ohne Lavalampen) oder Leitern in der Welt über den bisherigen Höchststand steigt. | Hochwassermarke pro Welt. Umsetzen zählt nicht. Platzierungen, die beim Laden schon da sind oder in einem Debugmodus entstanden, heben nur die Marke. |
| `crafted`, `tools`, `dynamite`, `medkits`, `torches-crafted`, `ladders-crafted`, `exotics` | Meister am Amboss, Werkzeugschmiede, Donner der Schmiede, Heilende Vorräte, Funken im Dunkel, Himmelsleiter, Fremde Künste, Meisterschmiede, Meisterhandwerk | … etwas hergestellt wird. Gezählt wird die Stückzahl. Ausnahmen: Bei „Meister am Amboss“ zählen Nägel, Seil, Stoff und Stahl nicht. Leitern zählen samt Eisenleitern. Bei Exotics zählen die Herstellvorgänge. | Wiederholungsschutz pro Welt: Der Wert folgt dem Spielstand, der Höchststand sinkt nie. Wer einen älteren Stand lädt, bekommt erst wieder etwas, wenn der Höchststand überschritten ist. |
| `gear` | Für den Aufbruch | … eine Ausrüstung zum ersten Mal in dieser Welt hergestellt wird, vom Rucksack bis zum Kristallgeschirr. | Schlüssel pro Gegenstand und Welt. |
| `upgrades` | Die nächste Klinge, Funken des Lebens, Schultern aus Stahl, Aufrüsten | … eine Stufe der Spitzhacke, der Energie oder der Tragkraft hergestellt wird. | Schlüssel pro Stufe und Welt. |
| `sales`, `run-sales`, `sale-single` | Goldene Geschäfte, Goldener Lauf, Grosser Handschlag, Handelsimperium, Handelsfürst | … im Laden verkauft wird. | Wiederholungsschutz wie beim Herstellen: Verkaufssumme pro Welt mit Höchststand. „Goldener Lauf“ zählt die Summe pro Welt, und die beste Welt der Runde gilt (bis zu 16 Welten). „Grosser Handschlag“ zählt den grössten einzelnen angerechneten Verkauf. |
| `artifacts`, `artifact`, `artifact-variety` | Relikte der Vorzeit, Spuren der Vorzeit, Spuren der Alten, Schatzkammer, Hüter der Relikte | … ein Artefakt abgebaut wird. | `artifact-mined:x:y` pro Welt. Die Vielfalt läuft über eine Bitmaske. |
| `diligence` | Fleissige Woche | … siehe Abschnitt 1. | Ein Bit pro Wochentag. |
| `tasks-done` | Linie „Herausforderer“ | … eine Aufgabe oder ein ganzes Monatsprojekt erledigt wird. Der Bonus zählt nicht. | – |
| `depth-gain`, `artifact-sighted`, `unobtainable` | nur umgewandelte alte Aufgaben | … die Run-Maximaltiefe steigt, ein Artefakttyp gesichtet wird bzw. nie (Obsidian, Smaragd). | Wie bisher. |

Eine Tour beginnt an der Oberfläche, also bei Zellenreihe 0 oder darüber. Beim ersten Messpunkt einer Sitzung wird sie nur gestartet, wenn man dort steht. Wer in der Tiefe lädt, bekommt Tiefe, Schichten, Altar und Rückkehr erst nach dem nächsten Aufstieg.

## 4. Speicherung und Migration

**Neue Profilfelder** (ältere Builds ignorieren sie):

| Feld | Inhalt |
| --- | --- |
| `challengeRounds` | Genau eine Runde je Zeitraum (T, W, M). Gespeichert werden: Format (1 = umgewandelte alte Runde, 2 = neu), Start und Ende (UTC), Wochentag und die Aufgaben. Jede Aufgabe hält ID, Name, Einheit, Kennzahl, Modus, Gegenstand, Stufe, Ziel, XP, Fortschritt, erledigt, ausbezahlt und gegebenenfalls Etappen. Dazu kommen Bonus-XP mit Auszahlung, Bitmasken, Run-Werte und „Fleiss angerechnet“. |
| `milestoneAwards` | Erreichte neue Langzeitstufen (ID und XP). |
| `lifetimeCounters` | Zähler der Zähler-Linien. |
| `tallies` je Run/Welt | Wert und Höchststand für den Wiederholungsschutz: `sales`, `crafted`, `tools`, `dynamite`, `medkits`, `torches-crafted`, `ladders-crafted`, `exotics`, `placed:torches-placed`, `placed:ladders-placed`. |
| `resourceSources` (bestehend) | Neue Schlüssel `vein:`, die Zahlungsmarken `cr:`/`cm:` und die Zellen-Blöcke `#1R:`/`#1M:`/`#1O<Item>:` des Zellen-Ledgers. Diese ersetzen die Zellschlüssel `revealed:`, `mined:` und `item:x:y`: Schritt 1 liest sie und schreibt noch die alten Schlüssel, ab Schritt 2 stehen nur noch Blöcke im Profil (`Docs/Progression.md`, „Grösse des Profils“). |

`challengePeriods` (alt) wird umgewandelt und bleibt danach leer. `FormatVersion` und die Run-Version bleiben 1. `completedMilestones` enthält weiterhin nur die 17 alten IDs, und die Formel für die Gesamt-XP ist unverändert.

**Prüfung beim Laden:**

- Challenge-Daten machen ein Profil oder einen Spielstand nie ungültig, sie werden repariert oder verworfen:
  - Ungültige Runden fallen weg. Pro Zeitraum bleibt eine Runde, die neueste gewinnt.
  - Zähler, Auszeichnungen und Tallies werden bereinigt: leere, doppelte oder zu lange IDs fallen weg, Werte werden begrenzt, höchstens 128 / 256 / 64 Einträge.
  - Die Uhr wird begrenzt.
- Abgelehnt wird ein Profil nur in einem Fall: Die ausgezahlte XP aller Runden und neuen Stufen übersteigt `periodRewardXp`.
- Die Prüfung ist rein, also ohne Unity, Einstellungen oder Uhr, und läuft deshalb auch im Hintergrund-Speicherthread und für `.bak` und `.tmp`.

**Alte Profile (alle Formate):**

- Jeder alte Tages- oder Wochenstand wird mit den bisherigen Regeln geprüft. Abgedeckt sind:
  - 6 bis 52 Zähler
  - Ziel-Versionen 0–2
  - fehlende Auszahlungsliste (wird aus den historischen Belohnungen rekonstruiert)
  - Varianten
- Ein Stand, der die Prüfung nicht besteht, wird verworfen. Seine XP steckt bereits in `periodRewardXp` und bleibt.
- **Der laufende Tag und die laufende Woche werden als Format-1-Runde weitergeführt** und erst beim nächsten Wechsel neu gezogen:
  - Der Tag behält seine 8 Aufgaben in der alten Reihenfolge, die Woche alle 16.
  - Name, Einheit, Ziel, Fortschritt, „erledigt“ und ausbezahlte XP bleiben erhalten.
  - Die Stufe wird abgeleitet: tags klein = Leicht, gross = Mittel, XXL = XXL. Wochen nach XP: unter 1'000 Leicht, unter 1'500 Mittel, sonst Schwer.
  - Die Masken für besuchte Schichten und gesichtete Artefakttypen werden übernommen.
  - Diese Runden haben keinen Bonus.
- Ein Tag ohne gespeicherte Auswahl wird verworfen, ebenso ein alter Stand, wenn schon eine gleich alte oder neuere Runde existiert.
- Welche Vorlage eine umgewandelte Aufgabe bekommt, steht in Abschnitt 8. Sonderfälle:
  - Obsidian und Smaragd werden zu `legacy-impossible` (unerfüllbar).
  - Der Tiefenzuwachs wird zu `legacy-depth-gain`.
  - Gesichtete Artefakttypen werden zu `legacy-artifact-sighted`.

**Abwärtskompatibilität:** `main` und ältere Builds lesen das Profil weiter. `challengePeriods` ist leer, die XP liegt in `periodRewardXp`, und es kommen nur bekannte Meilenstein-IDs vor. Damit beim Hin- und Herwechseln nichts doppelt ausgezahlt wird, hinterlässt jede Auszahlung eine Marke im Ledger des aktuellen Runs. Ältere Builds behalten und vereinigen diese Ledger.

- `cr:<Zeitraum>:<StartUtc>:<Vorlage>/<Gegenstand>/<Index>` mit der Endung `:t` für eine Aufgabe oder `:s<n>` für eine Projektetappe. `cr:<Zeitraum>:<StartUtc>:b` steht für den Bonus.
  - Wird eine Runde neu gezogen, nachdem ein älterer Build sie gelöscht hat, gelten die markierten Aufgaben als erledigt und ausbezahlt. Neue XP gibt es dafür nicht.
- `cm:<Stufen-ID>` stellt fehlende Langzeitstufen beim Laden wieder her, ohne erneute XP.
- `cx:`/`ct:`/`cs:` beschreiben eine gezogene Runde (Kopf mit Ende, Wochentag, Bonus und Anzahl; je Aufgabe Vorlage, Gegenstand, Stufe, Ziel, XP; je Projekt die Etappen). Sie werden einmal pro Runde geschrieben, sobald darin etwas ausbezahlt wird. Fehlt die Runde später, wird sie daraus **genau so wiederhergestellt**, wie sie gezogen wurde, und erst danach greifen die `cr:`-Marken. Umgewandelte alte Runden (Format 1) schreiben keine Marken.
- `tp:<Tally>:<Höchstwert>` hält je Tally (Verkauf, Herstellen, platzierte Fackeln/Leitern) den Höchstwert fest. Beim Laden einer Welt wird der Wiederholungsschutz daraus wiederhergestellt.
- Alle Marken sind kurz (unter 160 Zeichen), eindeutig und verwenden kulturunabhängige Ziffern, damit ältere Builds den Ledger weiter akzeptieren.
- Beide Wiederherstellungen decken höchstens die XP, die in `periodRewardXp` vorhanden ist. Damit kann die Prüfung beim Laden nie fehlschlagen.

**Restrisiken beim Wechsel zwischen `main` und diesem Branch** (gemeinsames Profil):

- **Altsystem auf `main`:** `main` findet keine alten Stände mehr und zieht den laufenden Tag und die laufende Woche im Altsystem neu, mit demselben Zufall und Fortschritt 0. Am Übergangstag kann `main` dort bereits bezahlte Aufgaben ein zweites Mal auszahlen. Danach laufen beide Systeme getrennt weiter, und jedes zahlt für seine eigenen Aufgaben.
- **Neue Felder gehen verloren:** `main` löscht beim nächsten Speichern Runden, Auszeichnungen, Zähler und Tallies. Zurück auf dem Branch gilt:
  - Auszeichnungen kommen über `cm:` zurück.
  - Die Zähler-Linien beginnen wieder bei 0. Erreichte Stufen bleiben, es wird nichts doppelt bezahlt.
  - Der Wiederholungsschutz für Verkauf und Herstellen kommt über `tp:` zurück. Ein Weltstand, den ein älterer Build gespeichert hat (ohne Tallies), gilt dabei als auf dem Höchststand: neue Verkäufe zählen sofort, nur ein bewusstes Wiederverkaufen über einen solchen älteren Weltstand ist nicht geschützt. Eigene Weltstände erkennt der Branch an der Merk-Tally `tallies`.
  - Runden, in denen schon etwas bezahlt wurde, kommen über `cx:`/`ct:`/`cs:` identisch zurück; bezahlte Aufgaben, Etappen und Bonus bleiben bezahlt.
- **Runden ohne Auszahlung:** Eine Runde, in der noch nichts bezahlt wurde, hat keine Beschreibung und wird neu gezogen (bei anderer Schicht, anderem Level oder anderer Herstellbarkeit auch anders). Da nichts bezahlt war, entsteht keine doppelte Auszahlung; nicht bezahlter Fortschritt, Masken und Fleiss-Tage gehen aber verloren.
- **Alter Stand von `main`:** Hat `main` für denselben Tag einen alten Stand angelegt, wird dieser wieder umgewandelt und ersetzt die neue Runde dieses Tages. Die bereits bezahlte XP bleibt.
- **Älteres Backup:** Fällt das Profil auf ein älteres Backup zurück, werden Marken ohne XP-Deckung nicht wiederhergestellt. Die Aufgabe kann dann erneut verdient werden.

## 5. GPS-Werte

Pfad: GPS-Fenster bzw. F1 → **Upgrades → Herausforderungen → „XP, Anzahl und Mengen“** (`GpsPreferences.challenges`, Klasse `ChallengeSettings`). Jeder Verbraucher liest `Sanitized()`, das Werte ausserhalb des Bereichs begrenzt.

**Wann Änderungen wirken:** Eine Runde speichert XP, Anzahl, Mengen, XXL-Wurf, Bonus, Projekt-XP und die Fleiss-Einstellungen beim Ziehen. Änderungen daran gelten deshalb **ab dem nächsten Tages-, Wochen- bzw. Monatswechsel**. Sofort, also ab der nächsten Rückkehr oder Anrechnung, wirken nur:

- die Rückkehr-Schwellen
- die Schwellen für volle Taschen
- „Fleiss: Tagesaufgaben pro Tag“ bei der Anrechnung eines Tages

Je Zeitraum (Gruppen „Täglich“, „Wöchentlich“, „Monatlich“):

| Feld (Anzeige) | Bereich | Standard T / W / M | Wirkung |
| --- | --- | --- | --- |
| XP leicht / mittel / schwer / XXL | 0–1'000'000 | 300/600/1'200/3'000 · 1'500/3'000/5'000/10'000 · 5'000/10'000/20'000/0 | XP pro Aufgabe der Stufe. Monatlich hat XXL keine Wirkung, weil es keine monatlichen XXL-Mengen gibt. |
| Anzahl leicht / mittel / schwer | 0–10 | 3/3/1 · 3/3/2 · 2/2/2 | Plätze pro Stufe (höchstens 16 pro Runde). Wöchentlich belegt die Fleiss-Aufgabe einen Mittel-Platz. |
| XXL-Chance (%) | 0–100 | 5 · 5 · 0 | Chance, dass ein Schwer-Platz (sonst Mittel) zu XXL wird. Monatlich ohne Wirkung. |
| Bonus alle erledigt (XP) | 0–1'000'000 | 1'000 · 5'000 · 15'000 | Bonus, wenn alle Aufgaben der Runde erledigt sind. |

Allgemein:

| Feld (Anzeige) | Bereich | Standard | Wirkung |
| --- | --- | --- | --- |
| Monatsprojekt Etappe 1 / 2 / 3 (XP) | 0–1'000'000 | 5'000 / 10'000 / 20'000 | XP je Etappe |
| Fleiss: Tage pro Woche (0 = aus) | 0–7 | 5 | Ziel der Fleiss-Aufgabe. 0 schaltet sie ab. |
| Fleiss: Tagesaufgaben pro Tag | 1–10 | 3 | Erledigte Tagesaufgaben, die einen Tag zählen lassen (höchstens die Zahl der Tagesaufgaben) |
| XXL ab Schicht | 1–5 | 3 | Ab dieser erreichten Schicht ist XXL möglich |
| Mengenwachstum pro Schicht (%) | 0–500 | 10 | Wachstum der ↑-Vorlagen je Schicht über 1 |
| Mengenwachstum pro 10 Level (%) | 0–500 | 2 | Wachstum je volle 10 Meta-Level |
| Max. Mengenwachstum (%) | 0–500 | 60 | Obergrenze des Wachstums |
| Rückkehr: Mindesttiefe (Blöcke) | 1–1'200 | 30 | Tiefe für „Sicher zurück“ und die Linie „Heimkehrer“ |
| Rückkehr: neue Blöcke pro Tour | 0–200 | 10 | Neu gezählte Blöcke, ohne die eine Tour nicht als Rückkehr zählt |
| Volle Taschen: Mindesttiefe (Blöcke) | 1–1'200 | 10 | Mindesttiefe der Tour |
| Volle Taschen: Ladung (% Tragkraft) | 10–300 | 90 | Ladung bei der Rückkehr, gemessen an der Bezugs-Tragkraft |
| Volle Taschen: Zuwachs (% Tragkraft) | 0–300 | 50 | Ladung, die auf der Tour dazukommen muss |
| Volle Taschen: Bezugs-Tragkraft max. (kg) | 10–2'000 | 150 | Bezug = min(Tragkraft, dieser Wert). So bleibt das Ziel mit grossem Rucksack erreichbar. |

Die Standardwerte stehen im Code. `GpsProfile.asset` enthält den Abschnitt erst nach dem nächsten Speichern in GPS.

## 6. Oberfläche

- **Tabs:** Täglich · Wöchentlich · Monatlich · Langzeit.
- **Countdown** oben rechts: „Neu in 12:34:56“ täglich, „Neu in 3 T 12:34:56“ wöchentlich und monatlich, im Tab Langzeit ausgeblendet. Beim Wechsel baut sich die Tafel neu auf.
- **Bonusleiste** unter dem Countdown:
  - „5 / 7 erledigt → +1.000 XP“, nach der Auszahlung „Bonus erhalten · +1.000 XP“.
  - Bei Runden ohne Bonus (umgewandelte alte Runden) nur „x / y erledigt“.
  - Im Tab Langzeit „x / 74 Stufen erreicht“.
  - Je Aufgabe ein Punkt (bis 16), im Tab Langzeit keine Punkte.
- **Kacheln:** Sie sind im bestehenden V3-Stil gehalten. Rand und Plakette zeigen die Stufenfarbe, und die Plakette nennt die Stufe:
  - Leicht grün (112, 196, 96)
  - Mittel blau (86, 156, 232)
  - Schwer violett (170, 108, 230)
  - XXL und Projekt gold

  Symbol und Einheit kommen aus der Aufgabe. Eine erledigte Kachel trägt die bisherige Erledigt-Marke.
- **Monatsprojekt-Kachel:** Die breite Kachel (1312 × 276) steht im Tab Monatlich vor den normalen Kacheln. Sie zeigt:
  - Kicker „Monatsprojekt“, Name und Gesamtfortschritt
  - drei Etappen „Etappe 1–3“, jede mit Ziel, eigener Fortschrittsleiste, XP-Plakette in Grün, Blau oder Violett und eigener Erledigt-Marke
  - Pfeile zwischen den Etappen
- **Langzeit-Kacheln:** Je Linie eine Kachel mit Name, Wert / nächstem Ziel und „Stufe x/y“.
  - Der Rand hat die Farbe der erreichten Medaille: Bronze (205, 127, 50), Silber (196, 204, 212), Gold, Platin (229, 228, 226). Ohne erreichte Stufe gibt es keinen Rand.
  - Die Plakette nennt die nächste Medaille mit ihrer XP, denn diese Stufe zahlt die angezeigte XP.
- **Run-Ziele:** Die drei Ziele des laufenden Runs stehen im Tab Langzeit nach den Linien. Ihre Plakette heisst „Run-Ziel“, ohne Rand.
- Alle Texte sind auf die Kachelgrössen ausgemessen (bis 10'000'000 XP).

## 7. Tests

Prüfskripte unter `Tests/`:

| Skript | Inhalt |
| --- | --- |
| `ChallengeCoreChecks.cs` | Edit Mode, auch ohne Unity lauffähig (nur reiner Kern): Katalog, Meilenstein-Linien, Textlängen, Mengenregeln, Auswahl über 3000 Tage, 600 Wochen und 200 Monate, Auswahlvarianten, Zählmechanik, Sanitize mit Zufallsdaten, Perioden-Grenzen mit Sommerzeit |
| `ChallengeReworkChecks.cs` | Edit Mode, isolierte Profile: Runden, Zählen, Speicherung, Wiederholungsschutz, Uhr und Zeitzonen, Langzeit, Abwärtskompatibilität, Migration, Robustheit |
| `ChallengeSettingsChecks.cs` | Edit Mode: GPS-Abschnitt „Herausforderungen“ mit temporärem Profil |
| `ChallengePanelPreviewChecks.cs` | Edit Mode, auch in der Cloud: baut die Tafel aus einem isolierten Profil, rendert jeden Tab nach `Temp/ChallengePanel-<Tab>.png` und vergleicht sie mit den Daten |
| `ChallengePeriodsChecks.cs`, `ChallengeGameplayChecks.cs` | Play Mode ab MainMenu: neue Runden im Spiel |
| `ChallengeMigrationChecks.cs` | Bestehender Check (Smoke-Suite), unverändert grün |
| `MetaProgressionUiChecks.cs`, `ProgressionTimelineChecks.cs` | Play Mode: an das neue Layout angepasst; der UI-Check nutzt jetzt ein isoliertes Profil |

Suite für die Cloud: `Tests/CI/suites/challenges.json` (die fünf Edit-Mode-Checks oben plus `MetaProgressionCoreChecks`). `ChallengeSettingsChecks` vergleicht zusätzlich die im Katalog fest hinterlegten Schichttiefen (0/30/150/450/900) und ersten Erzschichten mit der GPS-Erzverteilung; ändert sich die Verteilung im GPS, wird der Check rot, statt dass Katalog und Karte still auseinanderlaufen. Start: Actions → Unity Checks → Run workflow, Suite `challenges`.

Die alten Prüfskripte `DailyChallengeSelectionChecks.cs`, `AdditionalChallengesChecks.cs` und `PrepareDailyChallengeIsolation.ps1` liegen jetzt in `Tests/Archive/`.

### Testergebnisse

Cloud-Workflow „Unity Checks“ (Unity 6000.1.12f1, Batchmode mit Xvfb), Branch `claude/challenges-rework`:

| Lauf | Stand | Suite / Checks | Ergebnis |
| --- | --- | --- | --- |
| #22 | `9c9c131` (Gerüst) | ChallengeMigrationChecks | bestanden (Aufwärmlauf, Unity-Cache) |
| #23 | `c908d86` (erste Gesamtversion) | Migration, Settings, Rework, PanelPreview, MetaProgressionCore | 4/5 bestanden; Rework 548/580 (32-mal derselbe Testfehler: alter Name „Große“ statt „Grosse Ausbeute“) |
| #24 | `177a18f` | ChallengeReworkChecks | bestanden (580) |
| #25 | `eb57f1d` (nach Prüfrunde) | Suite `challenges` | **6/6 bestanden**: Core 581'496 · Rework 599 · Settings 71 · Migration 5 · PanelPreview 2'154 (inkl. Bilder je Tab) · MetaProgressionCore 87 |
| #26 | `eb57f1d` | Suite `smoke` (bestehende Projekt-Checks) | 6/7 bestanden; einzig `CaveGenerationChecks` rot („Cave generation entered a protected area“) – identisch zu Lauf #18 und #21 auf anderen Branches, nicht durch diese Arbeit verursacht |
| #27 | `276f34a` | Suite `challenges` | **6/6 bestanden**: Core 581'496 · Rework 600 · Settings 84 · Migration 5 · PanelPreview 2'154 · MetaProgressionCore 87 (zusammen 584'426 Prüfungen) |
| #28 | `3002269` (`main` mit gemalten Erzen hineingeholt) | Suite `challenges` | **6/6 bestanden**, gleiche Zahlen wie #27 |
| #29 | `121879f` (automatischer PR-Lauf) | Suite `smoke` | 6/7; wieder nur `CaveGenerationChecks` rot |
| #30 | `34de8f6` (Endstand, Korrektur des Höhlen-Checks übernommen) | Suite `smoke` | **7/7 bestanden** |

`CaveGenerationChecks` war schon vor dieser Arbeit rot (Läufe #18, #21). Die fertige Korrektur aus Branch `claude/compassionate-brahmagupta-32gx9j` (seitliche Ränder erlaubt, Vergleich mit der tatsächlichen Kartengrösse; dort Lauf #20 grün) ist unverändert übernommen; sie wirkt nicht mehr, sobald `main` sie enthält.

`MetaProgressionCoreChecks` war in allen früheren Cloud-Läufen rot („worker owns detached snapshot“); mit dem neuen System speichert eine Rundenziehung mitten in einem Aufzeichnungsaufruf nicht mehr sofort, seither ist der Check grün.

Lokal ohne Unity: Kompilierung aller Spiel-, Editor- und 186 Prüfskripte gegen Unity-Referenzen ohne neue Fehler (nur 119 schon vorher bestehende Fehler in veralteten fremden Prüfskripten); `ChallengeCoreChecks` unter .NET 8; Laufzeit-Simulation der echten `MetaProgression*`-Dateien gegen Unity-Attrappen (600 Prüfungen, 4'500 Zufallsrunden für die Wiederherstellung, 57'660 Prüfungen für den Verkaufs-/Herstellungsschutz).

Die Play-Mode-Checks (`ChallengePeriodsChecks`, `ChallengeGameplayChecks`, `MetaProgressionUiChecks`, `ProgressionTimelineChecks`) laufen in der Cloud derzeit nicht (siehe `Docs/Cloud-Tests.md`) und sind nur kompiliert; sie sollten lokal im Editor laufen.

Lokal im Editor der Testbank `Mining Game 2` (Unity 6000.1.12f1, 20.10.2026, Stand `9dadf45`; Play-Mode-Checks jeweils frisch ab MainMenu, isolierte Profile):

| Check | Modus | Ergebnis |
| --- | --- | --- |
| ChallengeCoreChecks | Edit | **PASS 581'496** (gleiche Zahl wie unter .NET 8 und in der Cloud) |
| ChallengeReworkChecks | Edit | **PASS 600** |
| ChallengeSettingsChecks | Edit | **PASS 84** |
| ChallengeMigrationChecks | Edit | **PASS 5** |
| ChallengePanelPreviewChecks | Edit | **PASS 2'154**, Bilder `Temp/ChallengePanel-*.png` angeschaut |
| MetaProgressionCoreChecks | Edit | **PASS 87** |
| ChallengePeriodsChecks | Play | **PASS 75** (nach zwei Korrekturen, siehe unten; vorher FAIL nach 47 bzw. 58) |
| ChallengeGameplayChecks | Play | **PASS 20** (inkl. „Kamera folgt dem Spieler“ nach Reload) |
| MetaProgressionUiChecks | Play | Stand `65704ba`: alle Challenge-Prüfungen grün (4 Tabs, alle Kacheln, Texte, Scrollen, Rückweg); FAIL nach 160 in der Bauplan-Sammlung: „All essential recipes are listed from the build catalog“ (vorher FAIL nach 22 an den Upgrade-Karten, behoben) |
| ProgressionTimelineChecks | Play | FAIL nach 3: „Upcoming blueprints visible“ (Stufen 15/35 nicht auf der Zeitleiste) |

Behoben (Commits `d7876f2`, `9dadf45`):

- `ChallengePanel.UpdateCountdown` prüfte den Periodenwechsel erst nach der Abkürzung „gleiche Restsekunde“. Ein Uhrensprung um genau eine Periode (gleiche Restzeit in der neuen Runde) liess die offene Tafel deshalb veraltet stehen. Die Prüfung steht jetzt vor der Abkürzung.
- `ChallengePeriodsChecks`: Der Bildteil las nach dem Profilwechsel die Runden direkt aus dem Profil, bevor sie gezogen waren (Runden entstehen erst bei der ersten Abfrage); dadurch wurde nichts angetrieben. Der Test zieht die Runde jetzt zuerst über `GetChallenges`.
- `MetaProgressionUiChecks`: verlangte einen Scrollbereich, obwohl die 10 Upgrades genau in den sichtbaren Bereich passen (630 = 630); jetzt nur Scrollen, wenn die Liste länger ist.
- `MetaProgressionUiChecks`: verlangte für jede anklickbare Fläche den Speichern-Knopf-Stil; die ganzen Upgrade-Karten sind aber bewusst Auswahlkarten (`SelectionCardNormal`/`SelectionCardHover`). Karten werden jetzt auf ihren Kartenrahmen geprüft, die Kaufen-Knöpfe weiter auf den Speichern-Stil (Commit `65704ba`).
- `ProgressionTimelineChecks` (noch nicht angepasst): erwartet bei Level 12 die Baupläne 15 und 35 gleichzeitig auf der Zeitleiste. Seit den Vorschau-Bauplänen (10, 20, 25, 30 …) zeigt der Ausschnitt 10, 12, 13, 14, 15 und als nächsten Bauplan 20 (Seilrolle); die Erwartung ist veraltet.

Offen, nicht durch diesen Branch verursacht (Upgrade-Tafel, Bauplan-Sammlung und Zeitleiste sind auf dem Branch unverändert): `MetaProgressionUiChecks` bricht in der Bauplan-Sammlung ab, `ProgressionTimelineChecks` an der veralteten Zeitleisten-Erwartung (siehe oben).

Auffälligkeiten in den Vorschaubildern (nur Optik, keine Fehler der Prüfungen): Der aktive Tab ist kaum vom inaktiven zu unterscheiden; der Scrollbalken rechts zeigt nur die untere Hälfte als dünne Linie; das Sanduhr-Symbol im Tages-Countdown wirkt verpixelt; einige Symbole doppeln sich (Fackel bei „Unbetretene Pfade“ und „Funken im Dunkel“, Spitzhacke bei „Werkzeugschmiede“ und „Aufrüsten“); „Goldener Lauf“ kommt täglich und wöchentlich mit demselben Namen vor; „0 / 1 Exotics“ ist ein englischer Einheitenname.

## 8. Überarbeitung der bisherigen Challenges

Das alte System im Überblick:

- **Täglich:** 8 von 52 Aufgaben, gemischt nach 2–4 Abbau, 1–3 Handwerk, 1–2 Erkundung und 1–2 Handel/Spezial. Jede Aufgabe hatte zu 50 % die grosse Variante. Ab Schicht 4 wurde mit 5 % Chance eine Abbauaufgabe zu XXL.
  - XP alt: klein 500, gross 1'500, XXL 3'000. Ein Bonus fehlte.
  - Ältere Profile kennen zusätzlich die historischen Werte 150–350 XP. Sie bleiben für die Migration gültig.
- **Wöchentlich:** immer dieselben 16 Aufgaben.
- **Langzeit:** 17 Meilensteine.
- **Run:** 3 von 9 Run-Zielen.

Mengen „neu“: Katalogwerte bei Schicht 1 und Level 1. Für Varianten mit Gegenstand (Erz, Edelstein, Schicht, Artefakttyp) stehen die tatsächlichen Werte der ersten Schicht, auf der sie vorkommen, einschliesslich Faktor und Wachstum. XXL gibt es erst ab Schicht 3. ↑ markiert Wachstum mit Schicht und Level.

### 8.1 Tagespool (52)

| # | Alt: Name · Kennzahl · Menge klein/gross · XP | Neu: Vorlage (Familie) | Neue Mengen T · W · M | Freischaltung neu | Begründung |
| --- | --- | --- | --- | --- | --- |
| 0 | Schätze des Berges · resources · 50/250 · 500/1'500 | `ores` Schätze des Berges (Abbau) ↑ | T 40/100/200/750 · W 250/600/1'200/4'000 · M 1'000/2'500/5'000 | immer | Mengen in Stufen. Mit #14 zusammengelegt |
| 1 | Ruf der Tiefe · depth (Zuwachs der Run-Maximaltiefe) · 100/300 · 500/1'500 | `depth` Ruf der Tiefe (Erkundung) | T/W je Schicht (Abschnitt 2), M als Projekt „Tiefenexpedition“ | Ziele je Schicht | Neue Zählweise ab der Oberfläche: grösste Tiefe einer Tour. Der alte Zuwachs war pro Welt nach dem ersten Abstieg verbraucht. Eine laufende alte Aufgabe zählt als `legacy-depth-gain` weiter |
| 2 | Meister am Amboss · crafted · 5/15 · 500/1'500 | `crafted` Meister am Amboss (Handwerk) ↑ | T 10/25/50/150 · W 60/150/300/800 · M 250/600/1'200 | immer | Nägel, Seil, Stoff und Stahl zählen nicht mehr: 1 Eisen ergab 4 Nägel und war farmbar. Mit Wiederholungsschutz. Mit #15 zusammengelegt |
| 3 | Kupferglut · copper · 25/100 · 500/1'500 | `ore` · Kupfer (Abbau) ↑, Faktor 0,8 | T 20/50/95/320 · W 120/280/560/1'600 · M 480/1'200/2'400 | immer | Mengen in Stufen, Faktor für das geringere Vorkommen |
| 4 | Goldene Geschäfte · sales · 1'000/5'000 · 500/1'500 | `sales` Goldene Geschäfte (Handel) | S1: T 450/1'200/3'000/9'000 · W 2'400/6'000/15'000/45'000 · M 9'000/24'000/60'000. S3: T 1'500/4'000/10'000/30'000 · W 8'000/20'000/50'000/150'000 · M 30'000/80'000/200'000 | immer | Handelsfaktor je Schicht. Nur neue Erlöse zählen (Wiederholungsschutz). Mit #19 zusammengelegt |
| 5 | Funken im Dunkel · torches (hergestellt) · 3/15 · 500/1'500 | `torches-crafted` Funken im Dunkel (Handwerk) | T 8/20/40/100 · W 50/120/240/600 · M 200/500/1'000 | immer | Mengen angepasst: ein Rezept ergibt 4 Fackeln |
| 6 | Schwarze Adern · coal · 25/100 · 500/1'500 | `ore` · Kohle (Abbau) ↑ | T 25/60/120/400 · W 150/350/700/2'000 · M 600/1'500/3'000 | immer | Mengen in Stufen |
| 7 | Eisenherz · iron · 25/100 · 500/1'500 | `ore` · Eisen (Abbau) ↑ | T 25/60/120/400 · W 150/350/700/2'000 · M 600/1'500/3'000 | immer | Mengen in Stufen |
| 8 | Silbermond · silver · 25/100 · 500/1'500 | `ore` · Silber (Abbau) ↑, Faktor 0,8 | ab S2: T 20/55/110/350 · W 130/310/620/1'750 · M 530/1'300/2'650 | ab S2 | Freischaltung korrigiert: bisher ab S1, Silber gibt es erst ab S2 |
| 9 | Goldfieber · gold · 25/100 · 500/1'500 | `ore` · Gold (Abbau) ↑, Faktor 0,5 | ab S3: T 15/35/70/240 · W 90/210/420/1'200 · M 360/900/1'800 | ab S3 | Freischaltung korrigiert: bisher ab S1, Gold gibt es erst ab S3. Mengen angepasst |
| 10 | Unbetretene Pfade · regions · 6/18 · 500/1'500 | `regions` Unbetretene Pfade (Erkundung) | T 4/8/15/40 · W 25/50/90/200 · M 100/200/350 | immer | Mengen in Stufen |
| 11 | Geheimnisse im Stein · discoveries · 1/3 · 500/1'500 | `discoveries` Geheimnisse im Stein (Erkundung) | T 2/4/8/25 · W 12/25/50/120 · M 50/100/200 | immer | Mengen angepasst. Teilt die Tätigkeit mit den Artefakten, damit ein Artefakt nicht zwei Aufgaben auf einmal erfüllt |
| 12 | Fremde Künste · exotics · 1/3 · 500/1'500 | `exotics` Fremde Künste (Handwerk) | T 1/2/4/– · W 3/6/12/– · M 10/25/50 | ab Level 5 | Zählt Herstellvorgänge statt Stück, denn die Eisenleiter ergibt 2 |
| 13 | Himmelsleiter · ladders (hergestellt) · 5/25 · 500/1'500 | `ladders-crafted` Himmelsleiter (Handwerk) | T 10/25/50/150 · W 60/150/300/800 · M 250/600/1'200 | immer | Mengen angepasst: ein Rezept ergibt 5 Leitern. Eisenleitern zählen mit |
| 14 | Grosse Ausbeute · resources · 250/1'250 · 500/1'500 | → `ores` | – | – | Zusammengelegt: Doppelung von #0, wurde täglich ohnehin nie gezogen |
| 15 | Feuer der Schmiede · crafted · 15/45 · 500/1'500 | → `crafted` | – | – | Zusammengelegt: Doppelung von #2 |
| 16 | Platinglanz · platinum · 25/100 · 500/1'500 | `ore` · Platin (Abbau) ↑, Faktor 0,5 | ab S4: T 16/40/80/260 · W 100/230/460/1'300 · M 390/980/1'950 | ab S4 | Mengen angepasst |
| 17 | Titankraft · titanium · 25/100 · 500/1'500 | `ore` · Titan (Abbau) ↑, Faktor 0,8 | ab S3: T 25/60/120/380 · W 140/340/670/1'900 · M 580/1'450/2'900 | ab S3 | Freischaltung korrigiert: bisher ab S1, Titan gibt es erst ab S3 |
| 18 | Obsidianfluch · obsidian · 30/100 · 500/1'500 | entfällt (`legacy-impossible`) | – | – | Unerreichbares Material: Obsidian kommt in keiner Welt vor |
| 19 | Goldener Handschlag · sales · 2'500/7'500 · 500/1'500 | → `sales` | – | – | Zusammengelegt: Doppelung von #4. Der Gedanke lebt als „Grosser Handschlag“ (grösster Einzelverkauf, neu) weiter |
| 20 | Ruf der Wälder · trees · 10/40 · 500/1'500 | `trees` Ruf der Wälder (Abbau) | T 3/6/12/30 · W 15/35/70/150 · M 60/150/300 | immer | Mengen angepasst: höchstens 32 Bäume, sie wachsen in 100–240 s nach |
| 21 | Grüne Ernte · grass · 25/100 · 500/1'500 | `grass` Grüne Ernte (Abbau) | T 15/40/80/200 · W 100/250/500/1'200 · M 400/1'000/2'000 | immer, Sense nötig | Mengen angepasst |
| 22 | Unter der Grasnarbe · surface-blocks · 150/500 · 500/1'500 | `layer-blocks` · Schicht 1 (Abbau) | T 80/200/400/– · W 500/1'200/2'500/– · M 2'000/5'000/10'000 (XXL nur mit GPS „XXL ab Schicht“ ≤ 2) | nur bis erreichte S2 | Neue Zählweise: jeder Block der Schicht über den Zellschlüssel, auch Erz- und Artefaktfelder. Für Fortgeschrittene ist Schicht 1 meist schon ausgegraben |
| 23 | Relikte der Vorzeit · artifacts · 5/25 · 500/1'500 | `artifacts` Relikte der Vorzeit (Artefakte) | T 2/4/8/25 · W 10/20/40/80 · M 40/80/150 | immer | Mengen ans Vorkommen angepasst (rund 490 pro Welt, erreichbar ist nur, was entlang der Gänge liegt). Mit #37 zusammengelegt |
| 24 | Spuren der Vorzeit · specific-artifact · 3/10 · 500/1'500 | `artifact` · Typ (Artefakte), Faktor nach Vorkommen | z. B. Alter Kompass T 1/–/2/5 · W 2/3/6/13 · M 5/10/20 (alle Typen siehe unten) | erste Schicht des Typs | Freischaltung korrigiert: bisher zufällig alle 10 Typen ohne Rücksicht auf die Schicht. Mengen nach Vorkommen. Mit #38 zusammengelegt |
| 25 | Steinernes Erwachen · l2-blocks · 200/600 · 500/1'500 | `layer-blocks` · Schicht 2 (Abbau) | ab S2: T 80/200/400/1'500 · W 500/1'200/2'500/8'000 · M 2'000/5'000/10'000 | ab S2 | Freischaltung korrigiert, bisher ab S1. Mengen angepasst |
| 26 | In der Bergesmitte · l3-blocks · 250/750 · 500/1'500 | `layer-blocks` · Schicht 3 (Abbau), Faktor 0,8 | ab S3: T 65/160/320/1'200 · W 400/960/2'000/6'400 · M 1'600/4'000/8'000 | ab S3 | Freischaltung korrigiert, bisher ab S1 |
| 27 | XXL · Herz des Berges · l4-xxl · 2'000 · 3'000 | `layer-blocks` · Schicht 4 „Herz des Berges“, Faktor 0,6 | ab S4: T 50/120/240/900 · W 300/720/1'500/4'800 · M 1'200/3'000/6'000 | ab S4 | Aus der reinen XXL-Aufgabe wird eine Variante mit allen Stufen. Das Präfix „XXL ·“ entfällt, die Stufe zeigt sich über die Farbe |
| 28 | XXL · Ruf des Abgrunds · l5-xxl · 2'000 · 3'000 | `layer-blocks` · Schicht 5 „Ruf des Abgrunds“, Faktor 0,5 | ab S5: T 40/100/200/750 · W 250/600/1'250/4'000 · M 1'000/2'500/5'000 | ab S5 | wie #27 |
| 29 | XXL · Erzrausch · specific-ore-xxl · 500 · 3'000 | → XXL-Stufe von `ore` | z. B. Kohle XXL T 480 · W 2'400 (bei S3) | ab S3 | Zusammengelegt. Die Obsidian-Variante entfällt |
| 30 | Funkelnde Beute · gems · 30/100 · 500/1'500 | `gems` Funkelnde Beute (Abbau) | ab S3: T 5/12/25/100 · W 30/75/150/500 · M 120/300/600 | ab S3 | Freischaltung korrigiert, bisher ab S1. Mengen stark gesenkt, weil Edelsteine selten sind |
| 31 | Glut des Granats · garnet · 25/100 · 500/1'500 | `ore` · Granat (`gem`, Abbau) | ab S3: T 3/8/15/60 · W 20/50/100/300 · M 80/200/400 | ab S3 | Granat ist in S2 sehr selten (rund 107 Felder) und braucht die Stahlspitzhacke. Mengen stark gesenkt |
| 32 | Rubinfeuer · ruby · 25/100 · 500/1'500 | `ore` · Rubin (`gem`, Abbau) | ab S4: T 3/8/15/60 · W 20/50/100/300 · M 80/200/400 | ab S4 | Mengen stark gesenkt |
| 33 | Smaragdtraum · emerald · 25/100 · 500/1'500 | entfällt (`legacy-impossible`) | – | – | Unerreichbares Material: Smaragd kommt in keiner Welt vor |
| 34 | Diamantenglanz · diamond · 25/100 · 500/1'500 | `ore` · Diamant (`gem`, Abbau) | ab S4: T 3/8/15/60 · W 20/50/100/300 · M 80/200/400 | ab S4 | Mengen stark gesenkt |
| 35 | XXL · Edelsteinrausch · gems-xxl · 300 · 3'000 | → XXL-Stufe von `gems` | T 100 · W 500 | ab S3 | Zusammengelegt |
| 36 | XXL · Kristalljagd · specific-gem-xxl · 150 · 3'000 | → XXL-Stufe von `gem` | T 60 · W 300 | ab S3 bzw. S4 | Zusammengelegt. Die Smaragd-Variante entfällt |
| 37 | XXL · Reliktenjäger · artifacts-xxl · 75 · 3'000 | → XXL-Stufe von `artifacts` | T 25 · W 80 | ab S3 | Zusammengelegt |
| 38 | XXL · Reliktvermächtnis · specific-artifact-xxl · 25 · 3'000 | → XXL-Stufe von `artifact` | je Typ, z. B. Sonnenscheibe T 2 · W 5 | ab S3 | Zusammengelegt |
| 39 | XXL · Ultroniumrausch · ultronium-xxl · 100 · 3'000 | `ultronium` Ultroniumrausch (Abbau) | T –/–/3/10 · W –/5/12/25 · M –/–/30 | ab S5, täglich nur mit Härte 8 | Mengen ans Vorkommen angepasst (rund 150 Felder pro Welt, 50 beenden den Run). Täglich nur mit Diamantspitzhacke |
| 40 | Die nächste Klinge · pickaxe-upgrades · 1/2 · 500/1'500 | `upgrades` · Spitzhacke (Handwerk) | T 1/–/2/– · W (gemeinsam „Aufrüsten“) –/2/4/– · M – | herstellbare Stufen ≥ Ziel | Die Eisenspitzhacke ist ausgenommen (Rezeptfehler). Mittel entfällt, weil es nicht grösser als Leicht wäre |
| 41 | Werkzeugschmiede · tools-crafted · 2/5 · 500/1'500 | `tools` Werkzeugschmiede (Handwerk) | T 1/2/3/– · W 2/4/–/– · M – | herstellbare Werkzeuge ≥ Ziel | Die Prüfung gilt jetzt auch wöchentlich, dazu kommt der Wiederholungsschutz. Werkzeuge sind pro Run einmalig, deshalb kleinere Mengen |
| 42 | Für den Aufbruch · gear-crafted · 1/3 · 500/1'500 | `gear` Für den Aufbruch (Handwerk) | T 1/2/3/– · W 2/4/–/– · M – | herstellbare Ausrüstung ≥ Ziel | Die Prüfung gilt jetzt auch wöchentlich |
| 43 | Funken des Lebens · energy-upgrades · 1/2 · 500/1'500 | `upgrades` · Energie (Handwerk) | wie #40 | herstellbare Stufen ≥ Ziel | Der Reise-Monolith (braucht Mythril) ist ausgenommen |
| 44 | Schultern aus Stahl · carrying-upgrades · 1/2 · 500/1'500 | `upgrades` · Tragkraft (Handwerk) | wie #40 | herstellbare Stufen ≥ Ziel | wie #40 |
| 45 | Donner der Schmiede · dynamite-crafted · 5/20 · 500/1'500 | `dynamite` Donner der Schmiede (Handwerk) | T 3/8/15/40 · W 20/50/100/250 · M 80/200/400 | Dynamit-Rezept | Mengen in Stufen |
| 46 | Heilende Vorräte · medkits-crafted · 3/10 · 500/1'500 | `medkits` Heilende Vorräte (Handwerk) | T 2/4/8/– · W 10/20/35/– · M 40/80/150 | Medkit-Bauplan | Mengen angepasst |
| 47 | Grenzgänger · visited-layers · 2/4 · 500/1'500 | `layers` Grenzgänger (Erkundung) | T 2/3/–/– · W 3/4/–/– · M – | Ziel ≤ erreichte Schicht | Neue Zählweise ab der Oberfläche: Laden in der Tiefe zählt nicht mehr. Nur Leicht und Mittel, weil ein einziger Abstieg genügt. Teilt die Tätigkeit mit der Tiefe |
| 48 | Verborgene Weiten · revealed-cells · 250/1'000 · 500/1'500 | `cells` Verborgene Weiten (Erkundung) | T 200/500/1'000/3'000 · W 1'500/3'500/7'000/15'000 · M 6'000/15'000/30'000 | immer | Mengen in Stufen. Einheit jetzt „Kartenfelder aufgedeckt“ |
| 49 | Ruf des Altars · altars-found · 1/2 · 500/1'500 | `altars` Ruf des Altars (Erkundung) | T – · W –/–/1/– · M –/–/1 | ab S5 | Freischaltung korrigiert: Die Aufgabe kam bisher täglich ab S1, der Altar liegt aber ab Tiefe 1'058. Neue Zählweise: jede Annäherung auf einer Tour ab der Oberfläche statt der einmaligen Entdeckung pro Welt. „Gross 2“ war mit einem Altar pro Welt nicht machbar |
| 50 | Spuren der Alten · artifact-variety (gesichtet) · 2/5 · 500/1'500 | `artifact-variety` Spuren der Alten (Artefakte) | T 2/3/4/6 · W 3/4/6/8 · M 4/6/8 | Ziel ≤ verfügbare Typen | Neue Zählweise: verschiedene abgebaute statt nur gesichtete Typen. Eine laufende alte Aufgabe zählt als `legacy-artifact-sighted` weiter |
| 51 | Licht und Tiefe · round-trip · 1 („L3 und zurück“) / 1 („L5 und zurück“) · 500/1'500 | `round-trips` Hin und zurück (Erkundung) | T/W/M je Schicht (Abschnitt 2), z. B. S5: T 1×S3/1×S4/1×S5/2×S5 | ab S2 | Umbenannt. Zielschicht je Stufe. Eine Tour zählt nur mit mindestens 10 neuen Blöcken, damit Pendeln an der Leiter nichts bringt. Teilt die Tätigkeit mit „Sicher zurück“ |

**Artefakttypen** (`artifact`, Faktor nach Vorkommen pro Welt, kein Wachstum; T · W · M):

| Typ | ab | Faktor | Mengen |
| --- | --- | --- | --- |
| 01 Sonnenscheibe | S1 | 0,2 | T 1/–/–/2 · W 1/–/2/5 · M 2/4/8 |
| 02 Alter Kompass | S1 | 0,5 | T 1/–/2/5 · W 2/3/6/13 · M 5/10/20 |
| 03 Jademaske | S3 | 0,7 | T 1/–/3/7 · W 2/4/8/18 · M 7/14/30 |
| 04 Bernsteinskarabäus | S2 | 0,7 | T 1/–/3/7 · W 2/4/8/18 · M 7/14/30 |
| 05 Astrolabium | S4 | 0,5 | T 1/–/2/5 · W 2/3/6/13 · M 5/10/20 |
| 06 Antike Vase | S4 | 0,4 | T 1/–/2/4 · W 1/2/5/10 · M 4/8/16 |
| 07 Siegelring | S3 | 0,5 | T 1/–/2/5 · W 2/3/6/13 · M 5/10/20 |
| 08 Fossile Spirale | S4 | 0,3 | T 1/–/–/3 · W 1/2/4/8 · M 3/6/12 |
| 09 Kupfertafel | S4 | 0,25 | T 1/–/–/3 · W 1/2/3/6 · M 3/5/10 |
| 10 Kristall-Sanduhr | S5 | 0,1 | T 1/–/–/– · W 1/–/–/3 · M 1/2/4 |

### 8.2 Wochenaufgaben (16)

Bisher kamen jede Woche alle 16. Jetzt rotieren 8 aus dem ganzen Pool. Die Namen sind über alle Zeiträume vereinheitlicht.

| # | Alt: Name · Kennzahl · Menge · XP | Neu: Vorlage (Familie) | Neue Mengen W (T/M siehe 8.1) | Freischaltung neu | Begründung |
| --- | --- | --- | --- | --- | --- |
| 0 | Schätze des Berges · resources · 500 · 600 | `ores` (Abbau) ↑ | 250/600/1'200/4'000 | immer | Mengen in Stufen. Mit #14 zusammengelegt |
| 1 | Ruf der Tiefe · depth (Zuwachs) · 750 · 1'000 | `depth` (Erkundung) | je Schicht: S1 30/45/80/– … S5 700/950/1'100/1'190 | Ziele je Schicht | Neue Zählweise ab der Oberfläche. Der Zuwachs von 750 war pro Welt nur einmal zu haben |
| 2 | Meister am Amboss · crafted · 25 · 800 | `crafted` (Handwerk) ↑ | 60/150/300/800 | immer | Ohne Materialien, mit Wiederholungsschutz. Mit #15 zusammengelegt |
| 3 | Kupferglut · copper · 150 · 500 | `ore` · Kupfer ↑ | 120/280/560/1'600 | immer | Mengen in Stufen |
| 4 | Goldene Geschäfte · sales · 10'000 · 1'000 | `sales` (Handel) | S1 2'400/6'000/15'000/45'000 · S3 8'000/20'000/50'000/150'000 | immer | Handelsfaktor statt fester Menge. 10'000 waren auf S1 kaum machbar |
| 5 | Funken im Dunkel · torches · 20 · 500 | `torches-crafted` (Handwerk) | 50/120/240/600 | immer | Mengen angepasst |
| 6 | König der Kohle · coal · 300 · 400 | `ore` · Kohle „Schwarze Adern“ ↑ | 150/350/700/2'000 | immer | Umbenannt (einheitliche Namen), Mengen in Stufen |
| 7 | Eiserne Dynastie · iron · 200 · 600 | `ore` · Eisen „Eisenherz“ ↑ | 150/350/700/2'000 | immer | Umbenannt |
| 8 | Silberne Träume · silver · 80 · 750 | `ore` · Silber „Silbermond“ ↑ | ab S2: 130/310/620/1'750 | ab S2 | Umbenannt. Freischaltung korrigiert: bisher jede Woche, auch ohne Schicht 2 |
| 9 | Goldene Ära · gold · 50 · 900 | `ore` · Gold „Goldfieber“ ↑ | ab S3: 90/210/420/1'200 | ab S3 | Umbenannt, weil der Name zur Langzeitlinie Gold gehört. Freischaltung korrigiert |
| 10 | Jenseits der Karten · regions · 50 · 800 | `regions` „Unbetretene Pfade“ (Erkundung) | 25/50/90/200 | immer | Umbenannt |
| 11 | Geheimnisse des Berges · discoveries · 5 · 1'000 | `discoveries` „Geheimnisse im Stein“ (Erkundung) | 12/25/50/120 | immer | Umbenannt, weil der Name zur Langzeitlinie gehört |
| 12 | Fremde Künste · exotics · 3 · 1'400 | `exotics` (Handwerk) | 3/6/12/– | ab Level 5 | Freischaltung korrigiert: Die Aufgabe kam bisher jede Woche und war unter Level 5 unerfüllbar. Zählt Herstellvorgänge |
| 13 | Wege zum Himmel · ladders · 30 · 500 | `ladders-crafted` „Himmelsleiter“ (Handwerk) | 60/150/300/800 | immer | Umbenannt |
| 14 | Grosse Ausbeute · resources · 2'500 · 2'000 | → `ores` | Schwer 1'200, XXL 4'000, M bis 5'000 | – | Zusammengelegt: Doppelung von #0 |
| 15 | Feuer der Schmiede · crafted · 100 · 1'600 | → `crafted` | Schwer 300, XXL 800 | – | Zusammengelegt: Doppelung von #2 |

### 8.3 Langzeit-Meilensteine (17)

Alle IDs, Ziele und XP bleiben, weil die Profilprüfung die XP aus dem Katalog nachrechnet. Wer eine Stufe bereits hat, behält sie.

| Alt-ID | Alt: Name · Kennzahl · Ziel · XP | Neu: Linie · Stufe (Medaille) | Neue Folgestufen (Ziel → XP) | Begründung |
| --- | --- | --- | --- | --- |
| depth-100 | Unter der Oberfläche · beste Run-Tiefe · 100 · 250 | „Abstieg in die Tiefe“ · 1 (Bronze) | – | Unverändert, zur Linie gruppiert |
| depth-250 | Tiefer Vorstoss · 250 · 600 | „Abstieg in die Tiefe“ · 2 (Silber) | – | Schreibweise „ss“ |
| depth-500 | Im Herzen des Berges · 500 · 1'200 | „Abstieg in die Tiefe“ · 3 (Gold) | – | Unverändert |
| depth-1000 | Abgrundforscher · 1'000 · 2'400 | „Abstieg in die Tiefe“ · 4 (Platin) | – | Keine weitere Stufe, weil die tiefste Reihe 1'199 ist |
| exotic-first | Erste fremde Kunst (bisher „Fremde Künste“) · Exotics im besten Run · 1 · 400 | „Meister fremder Künste“ · 1 (Bronze) | – | Umbenannt, weil der Name mit der Tagesaufgabe kollidierte |
| lifetime-exotics | Meister fremder Künste · verschiedene Exotics insgesamt · 3 · 1'600 | „Meister fremder Künste“ · 2 (Silber) | – | Keine weitere Stufe: Es gibt nur 3 herstellbare Exotics |
| gem-variety | Edelsteinjäger · seltene Funde im besten Run · 3 · 800 | „Edelsteinjäger“ · 1 (Bronze) | 4 → 1'600 | Obergrenze 4: Granat, Rubin, Diamant, Ultronium |
| first-victory | Tiefenhalls Vermächtnis · Sieg · 1 · 3'000 | „Tiefenhalls Champion“ · 1 (Bronze) | – | Unverändert |
| lifetime-victories | Tiefenhalls Champion · Siege insgesamt · 5 · 8'000 | „Tiefenhalls Champion“ · 2 (Silber) | 15 → 12'000 | Erweitert um Gold |
| lifetime-ores | Bergbaulegende · Erze insgesamt · 5'000 · 4'000 | „Bergbaulegende“ · 1 (Bronze) | 25'000 → 8'000 · 100'000 → 16'000 | Erweitert um Silber und Gold (XP ×2, ×4) |
| lifetime-coal | Schwarzes Gold · Kohle · 2'000 · 1'500 | „Schwarzes Gold“ · 1 | 10'000 → 3'000 · 40'000 → 6'000 | wie oben |
| lifetime-copper | Herr der Kupferadern · Kupfer · 1'000 · 1'800 | „Herr der Kupferadern“ · 1 | 5'000 → 3'600 · 20'000 → 7'200 | wie oben |
| lifetime-iron | Eisenbaron · Eisen · 1'000 · 2'000 | „Eisenbaron“ · 1 | 10'000 → 4'000 · 40'000 → 8'000 | wie oben |
| lifetime-silver | Silberkönig · Silber · 500 · 2'500 | „Silberkönig“ · 1 | 5'000 → 5'000 · 20'000 → 10'000 | wie oben |
| lifetime-gold | Goldene Ära · Gold · 250 · 3'000 | „Goldene Ära“ · 1 | 2'500 → 6'000 · 10'000 → 12'000 | wie oben |
| lifetime-regions | Weltenwanderer · Gebiete · 250 · 2'000 | „Weltenwanderer“ · 1 | 1'500 → 4'000 · 5'000 → 8'000 | wie oben |
| lifetime-discoveries | Geheimnisse des Berges · Entdeckungen · 25 · 3'000 | „Geheimnisse des Berges“ · 1 | 150 → 6'000 · 500 → 12'000 | wie oben |

Die IDs der neuen Stufen lauten `ms-<linie>-<n>`, zum Beispiel `ms-ores-2`. Wie Enum-IDs sind sie dauerhaft und werden nur ergänzt.

### 8.4 Run-Ziele (9)

Je Run werden wie bisher 3 gezogen, eines pro Gruppe. IDs, Ziele, XP und Zählweise sind unverändert. Neu stehen sie im Tab Langzeit als „Run-Ziel“.

| ID | Alt: Name · Kennzahl · Ziel · XP · Gruppe | Neu | Begründung |
| --- | --- | --- | --- |
| run-ores | Adern des Reichtums · Erzarten · 3 · 180 · 0 | unverändert | passt |
| run-miner | Reiche Ausbeute · Erze · 100 · 220 · 0 | unverändert | passt |
| run-crafter | Geschmiedetes Glück · verschiedene Herstellungen · 3 · 180 · 0 | unverändert | passt |
| run-map | Unbetretene Pfade · Gebiete · 12 · 240 · 1 | „Pfadfinder“ | Umbenannt: Der alte Name war gleich wie bei der Tagesaufgabe |
| run-depth | Ruf der Tiefe · Tiefe · 150 · 280 · 1 | „Erster Abstieg“ | Umbenannt: gleicher Name wie die Tiefenaufgabe |
| run-discovery | Flüstern im Stein · Entdeckungen · 2 · 240 · 1 | unverändert | passt |
| run-deep | Mut zum Abgrund · Tiefe · 400 · 500 · 2 | unverändert | passt (Ein-Run-Tiefe aus dem Konzept) |
| run-rare | Seltene Schätze · seltene Funde · 2 · 400 · 2 | unverändert | passt |
| run-exotic | Fremde Künste · Exotics · 1 · 400 · 2 | „Exotische Werkbank“ | Umbenannt: gleicher Name wie die Tagesaufgabe. Weiterhin erst ab Level 5 |

## 9. Neue Vorlagen, Projekte und Linien ohne Vorgänger

**Vorlagen** (T · W · M):

| Vorlage | Name | Familie | Zählt | Mengen | Freischaltung |
| --- | --- | --- | --- | --- | --- |
| `blocks` | Schaufel und Hacke | Abbau | jeden neu abgebauten Block (Tätigkeit wie Schichtblöcke) | ↑ T 100/250/500/2'000 · W 600/1'500/3'000/10'000 · M 2'500/6'000/12'000 | immer |
| `ore` · Wolfram | Wolframwille | Abbau | Wolfram, Faktor 0,6 | ↑ ab S4: T 20/45/95/310 · W 120/270/550/1'550 · M 470/1'150/2'350 | ab S4 |
| `veins` | Ader um Ader | Abbau | geleerte Erzadern | T 3/6/12/40 · W 20/45/90/250 · M 80/200/400 | immer |
| `blasted` | Sprengmeister | Abbau | mit Dynamit gesprengte Blöcke | T 20/50/100/400 · W 120/300/600/2'000 · M 500/1'200/2'500 | immer |
| `ore-variety` | Bunte Ausbeute | Abbau | verschiedene Erzarten in der Runde | T 2/3/5/8 · W 3/5/7/10 · M 4/6/9 | Ziel ≤ verfügbare Erzarten |
| `returns` | Sicher zurück | Erkundung | Rückkehren ab Tiefe 30 | T 2/4/7/15 · W 10/20/35/80 · M 40/80/150 | ab S2 |
| `full-returns` | Volle Taschen | Erkundung | Rückkehren mit fast voller Tragkraft | T 1/2/4/10 · W 5/10/20/45 · M 20/40/80 | immer |
| `torches-placed` | Lichtbringer | Erkundung | platzierte Fackeln | T 5/12/25/60 · W 30/70/140/300 · M 120/300/600 | immer |
| `ladders-placed` | Kletterer | Erkundung | platzierte Leitern | T 10/25/50/120 · W 60/150/300/700 · M 250/600/1'200 | immer |
| `sale-single` | Grosser Handschlag | Handel | grösster Einzelverkauf | S1: T 40/100/250/800 · W 150/400/1'000/2'500 · M 400/1'000/2'500. S3: T 200/500/1'250/4'000 · W 750/2'000/5'000/12'500 · M 2'000/5'000/12'500. S5 ×10 gegenüber S3 | immer |
| `run-sales` | Goldener Lauf | Handel | Verkaufserlös in einer Welt (Ein-Run-Aufgabe) | S1: T 450/1'200/3'000/9'000 · W 1'800/4'500/12'000/30'000 · M 6'000/15'000/36'000. S3: T 1'500/4'000/10'000/30'000 · W 6'000/15'000/40'000/100'000 · M 20'000/50'000/120'000 | immer |
| `upgrades` (wöchentlich) | Aufrüsten | Handwerk | Upgrades aller drei Arten zusammen | W –/2/4/– | Summe herstellbarer Stufen ≥ Ziel |
| `diligence` | Fleissige Woche | Spezial | Tage mit 3 erledigten Tagesaufgaben | W Mittel, Ziel 5 | immer, wenn Fleiss aktiv |

Intern gibt es drei stillgelegte Vorlagen, die nie gezogen werden: `legacy-depth-gain`, `legacy-artifact-sighted` und `legacy-impossible`. Sie dienen nur umgewandelten alten Runden.

**Monatsprojekte** (Etappen-XP 5'000 / 10'000 / 20'000):

| Projekt | Name | Familie | Kennzahl | Etappen |
| --- | --- | --- | --- | --- |
| `project-depth` | Tiefenexpedition | Erkundung | grösste Tiefe | je Schicht: S1 40/80/150 · S2 100/200/350 · S3 200/400/600 · S4 300/600/900 · S5 300/600/1'000 |
| `project-ores` | Erzmagnat | Abbau | Erze | ↑ 1'500/4'000/10'000 (S5: 2'100/5'600/14'000) |
| `project-sales` | Handelsimperium | Handel | Verkaufserlös | S3 20'000/60'000/150'000 × Handelsfaktor (S1 6'000/18'000/45'000 · S5 100'000/300'000/750'000) |
| `project-regions` | Kartograf | Erkundung | Gebiete | 100/250/500 |
| `project-blasted` | Grosse Sprengung | Abbau | gesprengte Blöcke | 300/800/2'000 |
| `project-artifacts` | Schatzkammer | Artefakte | Artefakte | 15/40/100 |
| `project-crafted` | Meisterschmiede | Handwerk | Gegenstände (ohne Materialien) | ↑ 200/500/1'200 (S5: 280/700/1'700) |
| `project-veins` | Aderjäger | Abbau | geleerte Erzadern | 50/120/300 |
| `project-returns` | Expeditionsleiter | Erkundung | Rückkehren ab Tiefe 30 | 30/70/150, ab S2 |

**Neue Langzeit-Linien** (je 3 Stufen, XP 1'000 / 2'500 / 5'000, Stufen-IDs `ms-<linie>-1..3`):

| Linie | Name | Kennzahl | Ziele |
| --- | --- | --- | --- |
| `line-gems` | Juwelensammler | Edelsteine insgesamt (Ledger) | 100 / 1'000 / 5'000 |
| `line-ultronium` | Ultroniumfürst | Ultronium insgesamt (Ledger) | 50 / 250 / 1'000 |
| `line-blasted` | Meister der Sprengung | gesprengte Blöcke | 500 / 5'000 / 25'000 |
| `line-torches` | Lichtbringer | platzierte Fackeln | 100 / 1'000 / 5'000 |
| `line-ladders` | Himmelsstürmer | platzierte Leitern | 200 / 2'000 / 10'000 |
| `line-full-returns` | Schwer beladen | volle Rückkehren | 10 / 100 / 500 |
| `line-returns` | Heimkehrer | Rückkehren ab Tiefe 30 | 25 / 250 / 1'000 |
| `line-veins` | Aderleerer | geleerte Erzadern | 50 / 500 / 2'500 |
| `line-sales` | Handelsfürst | angerechneter Verkaufserlös ($) | 50'000 / 1'000'000 / 10'000'000 |
| `line-crafted` | Meisterhandwerk | Gegenstände (ohne Materialien) | 500 / 5'000 / 25'000 |
| `line-artifacts` | Hüter der Relikte | Artefakte | 25 / 250 / 1'000 |
| `line-trees` | Holzfäller | Bäume | 100 / 1'000 / 5'000 |
| `line-tasks` | Herausforderer | erledigte Aufgaben aller Zeiträume | 50 / 500 / 2'000 |

Abgedeckt sind damit alle Ideen aus Konzept §3: Sprengmeister, Lichtbringer, Kletterer, Volle Taschen, Ausdauer („Sicher zurück“), Erzader leeren, Vielfalt und Ein-Run-Aufgaben („Goldener Lauf“; die Ein-Run-Tiefe deckt das Run-Ziel „Mut zum Abgrund“ ab). Keine Idee musste wegfallen.

## 10. Bekannte Grenzen und offene Punkte

- **Rezept der Eisenspitzhacke:** `Assets/GameObjects/Workbench/Recipes/IronPickaxe.asset` verlangt 10 Wolframspitzhacken. Die Eisenspitzhacke ist deshalb faktisch nicht herstellbar und von den Upgrade-Aufgaben ausgenommen. Die Korrektur liegt ausserhalb dieser Aufgabe.
- **Mythril, Obsidian und Smaragd** kommen in keiner Welt vor. Es gibt keine Aufgaben dafür, und Obsidianspitzhacke und Reise-Monolith zählen nicht als herstellbar. Umgewandelte alte Obsidian- und Smaragd-Aufgaben bleiben bis zum Ende ihres Tages bzw. ihrer Woche unerfüllbar.
- **Tode** werden nicht gezählt.
- **Hochwassermarke bei Fackeln und Leitern:** Eine umgesetzte oder aufgehobene und neu platzierte Fackel zählt nie wieder. Das ist ein bewusster Kompromiss gegen Farmen.
- **Doppelte Auszahlung am Übergangstag** beim Wechsel zwischen `main` und diesem Branch, siehe Abschnitt 4 (Restrisiken). Endgültig gelöst ist das erst, wenn der Branch in `main` ist.
- **Umgewandelte alte Aufgaben** laufen nach den neuen Zählstellen. Dadurch ändert sich einiges:
  - „Ruf des Altars gross“ (Ziel 2) ist nicht mehr erfüllbar.
  - „Licht und Tiefe“ braucht jetzt 10 neue Blöcke pro Tour.
  - Exotics zählen Herstellvorgänge.
  - „Meister am Amboss“ zählt keine Materialien mehr.
  - Verkäufe zählen nur noch als angerechnete Erlöse.
  - „Unter der Grasnarbe“ zählt jetzt auch Erz- und Artefaktfelder, wird also etwas leichter.
- **Debug-Teleport** (GPS-Testaktion) sperrt die Belohnungen nicht. Ein Teleport ab der Oberfläche zählt Tiefe, Schichten und Altar. Diese Lücke bestand schon vorher.
- **Erzadern:** Erz, das bei gesperrten Belohnungen abgebaut wurde, hat keinen Ledger-Schlüssel. Eine Ader kann dadurch zerfallen und mehrfach zählen. Dynamit, das im Debugmodus gelegt wurde und danach explodiert, wird angerechnet.
- **Kurzes Fenster nach dem Laden:** Eine Leiter, die in den ersten rund 0,25 s nach dem Laden gesetzt wird, kann höchstens 1 zu viel anrechnen.
- **Runde im Hauptmenü gezogen:** Sie wird für einen frischen Run gezogen (nur Axt/Sense/Dynamit herstellbar), also ohne Ausrüstungs-, Upgrade- und Medkit-Aufgaben für diesen Tag bzw. diese Woche.
- **Schichten und Vorkommen sind fest im Katalog hinterlegt:** Erz- und Artefaktschichten, verfügbare Arten und die Schichtgrenzen 30/150/450/900 entsprechen dem GPS-Stand vom Oktober 2026. Wird die Erzverteilung oder die Schichttiefe in GPS geändert, muss `ChallengeCatalog.cs` mitgezogen werden. Das alte System las die ersten Erzschichten aus GPS.
- **Fleiss:** Die Einheit „Tage mit N Tagesaufgaben“ wird beim Ziehen der Woche gespeichert. Die Anrechnung liest „Fleiss: Tagesaufgaben pro Tag“ aber laufend. Ändert man den Wert mitten in der Woche, passt der Text nicht mehr zur Regel.
- **Monatliche XXL-Werte** in GPS haben keine Wirkung, weil die Monatsmengen nur drei Stufen kennen.
- **Überschneidungen:** Verschiedene Tätigkeiten können dasselbe Ereignis zählen, und das ist gewollt. Ein Diamant zählt etwa für Erze, Edelsteine, Vielfalt und Ader. In einer Woche können „Relikte der Vorzeit“ und „Spuren der Vorzeit“ zusammen vorkommen.
- **Unity-Laufzeit:** Die Simulation lief unter .NET 8. Ob `System.Random` mit Seed und die Zeitzonenregeln unter Mono gleich laufen, zeigen erst die Tests im Editor (Abschnitt 7).
