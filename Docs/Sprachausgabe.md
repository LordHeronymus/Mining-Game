# Sprachausgabe des Charakters

Der Charakter kommentiert Spielereignisse mit kurzen, deutsch gesprochenen Sätzen (ElevenLabs-Aufnahmen des Benutzers).

## Ereignisse und Clips

Clips: `Assets/AC Audio/Voice/<Anlass>/NN_<Satz>_V<k>.(mp3|wav)`. `NN` ist die Satznummer, `V1`/`V2`/… sind Sprechvarianten desselben Satzes. Herkunft, Lautheit und Pegelausgleich jedes Clips stehen in `Design/Sprachausgabe/Voice-Manifest.json`.

| Ereignis (`VoiceLineEvent`) | Ordner | Sätze / Clips | Auslöser |
| --- | --- | --- | --- |
| EnergyLow | `Energie_33` | 3 / 6 | Energie fällt unter 33 % |
| EnergyCritical | `Energie_15` | 4 / 8 | Energie fällt unter 15 % |
| EnergyRefilled | `Energie_Aufgefuellt` | 6 / 11 | Aufladen am Monolithen: mindestens 10 % dazu und danach mindestens 80 % |
| CarryStage1 | `Traglast_Stufe1` | 5 / 10 | Gewicht über der Tragkraft (gelbe Anzeige) |
| CarryStage2 | `Traglast_Stufe2` | 6 / 12 | Gewicht über der doppelten Tragkraft (rote Anzeige) |
| FallSmall | `Sturz_Klein` | 5 / 9 | Fallschaden ab 5, aber nicht gross |
| FallBig | `Sturz_Gross` | 6 / 12 | Fallschaden über `bigFallDamageThreshold` (30, wie der Knochenbruch-Sound) |
| OreSold | `Shop_Verkauf` | 4 / 9 | „Alles verkaufen“ oder ein Verkauf, der mindestens 30 % der Tragkraft leert |
| BlueprintBought | `Shop_Bauplan` | 9 / 9 | Bauplan im Shop gekauft |
| CraftedAxe / CraftedScythe / CraftedMedkit / CraftedTorches | `Werkbank_*` | je 4–6 Sätze | entsprechendes Rezept an der Werkbank gebaut |
| CraftedPickaxe | `Werkbank_Spitzhacke` | 6 / 12 | Spitzhacke gebaut |
| UpgradeEnergy / UpgradeCarry | `Upgrade_Energie`, `Upgrade_Traglast` | je 6 / 12 | Energie- bzw. Traglast-Upgrade gebaut (geht vor der Item-Zuordnung) |
| NewWorld | `Neue_Welt` | 3 / 4 | Neue Welt betreten (nach dem Ladebildschirm) |
| SaveLoaded | – | 0 | Spielstand geladen: Ereignis vorbereitet, noch ohne Clips |

Neue Clips einbauen: Datei in den passenden Ordner legen und in `Assets/Resources/Audio/VoiceLineLibrary.asset` (Liste `sets` → `lines` → `takes`) eintragen. Ein neuer Satz ist ein neuer `lines`-Eintrag, eine weitere Variante ein weiterer `takes`-Eintrag. Für „Spielstand geladen“ einen Set mit `trigger: 17` anlegen. Import wie die übrigen Sprachclips: Mono, Compressed in Memory, Load in Background, kein Preload, Normalize aus.

## Aufbau

- `VoiceLineDirector` (reine Logik, ohne Unity): entscheidet, was wann gesprochen wird. Pro Ereignis ein Mischbeutel der Sätze, so kommt jeder Satz einmal pro Durchgang und nie zweimal hintereinander. Die Variante (V1/V2) wird zufällig gewählt, nicht zweimal hintereinander dieselbe. Eigener `System.Random`, der Zufallszustand von Unity bleibt unberührt.
- Regeln je Ereignis (`VoiceLineEventRule`): Priorität, Chance, Abklingzeit (ab Satzbeginn), Verzögerung (damit Sturz-, Kauf- oder Bastelgeräusche zuerst kommen), maximale Wartezeit, „ohne globale Pause“.
- Globale Regeln: nach jedem Satz eine Pause (`globalCooldownSeconds`, 20 s). Ereignisse mit „ohne globale Pause“ (Warnungen, Stürze, neue Welt, Upgrades) brauchen nur die Reaktionspause (4 s). Nie zwei Sätze gleichzeitig: Es gibt eine Warteschlange mit einem Platz, die höhere Priorität gewinnt, bei Gleichstand bleibt der Wartende. Ein Satz mit mindestens 25 Punkten mehr Priorität blendet den laufenden kurz aus und übernimmt (z. B. grosser Sturz vor Traglast-Kommentar).
- `VoiceThresholdLatch`, `VoiceEnergyWatcher`, `VoiceCarryWatcher`: Schwellen lösen nur beim Überschreiten aus und werden erst wieder scharf, wenn der Wert mit Abstand zurück ist (Energie +5 %, Traglast −10 % der Tragkraft). Eine Warnung, die gerade nicht gesprochen werden kann, bleibt offen und wird erneut angefragt, solange der Zustand gilt.
- `VoiceLinePlayer`: entsteht zur Laufzeit (keine Szenenänderung) mit eigener AudioSource. Er wird erst nach dem Ladebildschirm aktiv, damit Laden und Neustart keine falschen Auslöser liefern (Energie und Gewicht werden dann als Ausgangslage übernommen). Beim Szenenwechsel, Endbildschirm, Game Over oder Sieg verstummt er. Im Pausemenü hält der Satz an und wird nach 1,5 s verworfen. Während ein Satz läuft, werden Effekte über `AudioManager.TunedVolume` leicht abgesenkt (70 %); Musik und Ambiente bleiben gleich.
- Hooks im Spielcode (statische Events): `PlayerMovement.FallDamaged`, `ShopManager.Sold` / `RecipeBought`, `CraftingService.Crafted`, `EnergyMonolyth.Recharged`, `LoadingScreen.GameplayRevealed`, `InventoryManager.CarryStage` (gemeinsame Stufenregel für Anzeige und Stimme).

## Einstellungen

- Spieler: Einstellungen → Audio → Regler **Stimme** (0 % schaltet die Stimme ab).
- GPS bzw. F1 → Audio → Ablauf und Mischung → **Stimme**: Sprachausgabe an/aus, Lautstärke, Pausen, Schwellen (33 %/15 %, Aufladen, Verkauf, kleiner Sturz), Absenken anderer Sounds, Debug-Log und die Tabelle „Ereignisse“ mit allen Regeln je Ereignis. Grosser Sturz: Health → „Grosser Sturz ab Schaden“.
- F1 → Tests → **Sprachausgabe**: jedes Ereignis direkt abspielen (ignoriert Pausen, Chance und den Schalter) und „Stimme stoppen“.
- Pegel: Jeder Clip ist per `gain` auf etwa −24 LUFS angeglichen. Feinabgleich einzelner Clips im GPS-Clipkatalog (Kategorie „Stimme“).

Hinweis: Den GPS-Abschnitt „Stimme“, den Wert „Grosser Sturz ab Schaden“ und die 169 Katalogeinträge schreibt erst der Editor in `Assets/Resources/Gameplay/GpsProfile.asset` (beim Öffnen automatisch oder über Mining Game → GPS-Profil aktualisieren). Bis dahin gelten die Werte aus `VoiceLineSettings.asset`. Diese Änderung am Profil einmal committen. Bis sie committet ist, meldet `GpsAudioChecks` fehlende Katalogeinträge.

## Tests

- `Tests/VoiceLineChecks.cs` (Edit Mode, Suite `smoke`): Auswahl ohne Wiederholung, Abklingzeiten, Pausen, Priorität und Unterbrechen, Warteschlange, Schwellen, Bastel-Zuordnung, Vollständigkeit der Bibliothek, Import-Einstellungen, GPS-Schema, Unity-Zufallszustand, Regler-Wert.
- Die reine Logik lässt sich auch ohne Unity mit .NET prüfen (die drei Dateien `VoiceLineEvent.cs`, `VoiceLineDirector.cs`, `VoiceLineTriggers.cs` enthalten keine Unity-Abhängigkeit ausserhalb von `#if UNITY_5_3_OR_NEWER`).

## Abnahme (lokal, Hören und Spielgefühl)

1. Branch holen, Unity öffnen, warten bis das GPS-Profil aktualisiert ist; `GpsProfile.asset` committen.
2. `SettingsStyleChecks` (Play) laufen lassen: fünf Audio-Regler.
3. F1 → Tests → Sprachausgabe: jedes Ereignis einmal abspielen. Klingt jeder Clip sauber, ohne Knacken oder Abschneiden? Ist die Lautstärke zwischen den Sätzen gleichmässig?
4. Neues Spiel: Nach dem Ladebildschirm kommt ein Satz zur neuen Welt. Spielstand laden: kein Satz (noch keine Clips).
5. Energie: Ohne Aufladen bis unter 33 % und unter 15 % spielen → je ein Satz, nicht wiederholt. Am Monolithen ganz aufladen → Aufgefüllt-Satz nach dem Ladegeräusch.
6. Traglast: Rucksack über die gelbe und rote Stufe füllen → je ein Satz.
7. Stürze: kleiner Sturz (ca. 5 Blöcke) und grosser Sturz (ca. 9 Blöcke) → passender Satz nach Schmerz- und Knochengeräusch, nicht gleichzeitig.
8. Shop: „Alles verkaufen“ mit vollem Rucksack (Satz in etwa jedem zweiten Fall), Bauplan kaufen.
9. Werkbank: Axt, Sense, MedKit, Fackeln, Spitzhacke, Energie- und Traglast-Upgrade bauen. Bei mehreren Bauten kurz hintereinander redet der Charakter nicht pausenlos.
10. Regler „Stimme“ auf 0 % → stumm; GPS „Sprachausgabe aktiv“ aus → stumm.
11. Pausemenü während eines Satzes → Satz hält an; Game Over → Satz bricht ab.
12. Gefühl: Redet der Charakter zu oft oder zu selten? Werte in F1 → Audio → Stimme anpassen (Abklingzeiten, Chancen, globale Pause).
