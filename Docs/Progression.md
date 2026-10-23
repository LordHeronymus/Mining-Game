# Progression (Meta-Level und XP)

Technische Beschreibung des Level- und Fortschrittssystems und seiner Speicherregeln. Inhalt, Mengen und XP der Challenges stehen in `Docs/Challenges.md` und `Docs/Challenges-Konzept.md`. Massgebend ist der Code unter `Assets/GameObjects/Progression/` (`MetaProgression*.cs`, `MetaChallengeRounds.cs`, `MetaChallengePeriods.cs`, `ChallengeEngine.cs`, `ChallengeCatalog.cs`).

## Soll-Verhalten (vom Benutzer festgelegt)

1. **Eine Haupt-Progression:** Es gibt genau ein Level und eine XP-Summe über alle Spielstände. Welten haben keine eigene Level-Progression; jede Welt füllt dieselbe Haupt-Progression.
2. **Sofort dauerhaft:** Level-Ups sind im Spiel möglich und stehen sofort auf der Platte, auch wenn die Welt danach nicht gespeichert, das Spiel beendet oder abgewürgt wird. Kein Verlust und keine Doppelzählung beim erneuten Laden eines älteren Spielstands.
3. **Challenges:** Tages-, Wochen- und Monatsrunden, Monatsprojekt, Meilensteine und Run-Ziele vergeben XP auf die Haupt-Progression, jede Belohnung genau einmal.

Geprüft wird das mit `Tests/ProgressionChecks.cs` (Edit Mode, läuft in der Cloud in den Suiten `smoke` und `challenges`).

## Überblick

- **Ein Profil pro Benutzer:** `<persistentDataPath>/Tiefenhall/Profile/meta-progression.json` (`MetaProgression.cs`). Nur dort liegen XP, Ausbauten, Challenge-Runden und Meilensteine.
- **Das Level wird nie gespeichert,** sondern immer aus `profile.totalXp` berechnet (`MetaProgression.Level` → `MetaProgressionCatalog.LevelForXp`). Spielstände enthalten kein Level.
- **Welten:** Jeder Spielstand hat eine `runId` (neue Welt: GUID in `GameSaveSystem.BeginNewRun`; alte Stände ohne Fortschrittsdaten: `legacy-<createdUtc>-<seed>-<breite>-<höhe>`). `profile.runs` führt pro `runId` ein Ledger (`MetaRunState`). Der Spielstand trägt nur eine Kopie davon (`RunSaveState.progression`, über `CaptureRunState`).
- **Ledger bleiben erhalten,** wenn ein Slot gelöscht wird. Eine wiederhergestellte Kopie einer Welt kann so keine Belohnung doppelt holen.

## Datenmodell (`MetaProgressionData.cs`)

| `MetaProfile` | Inhalt |
| --- | --- |
| `version`, `revision` | Format und Schreibzähler (steigt bei jedem Schreiben) |
| `totalXp`, `upgrades` | Gesamt-XP und gekaufte Ränge |
| `runs` | Ledger pro Welt |
| `completedMilestones` | alte permanente Meilensteine (Katalog-XP) |
| `periodRewardXp` | XP aus Runden, Projekt, Boni und neuen Meilensteinstufen |
| `challengeRounds`, `milestoneAwards`, `lifetimeCounters`, `challengeClockUtc` | Challenge-Daten |
| `reachedMiningLayer` | tiefste je erreichte Schicht |

| `MetaRunState` (pro Welt) | Inhalt |
| --- | --- |
| `loadout`, `settings` | Ausbauten und GPS-Erfahrungswerte der Welt, beim Start eingefroren |
| `progressXp`, `efficiencyXp` | XP aus Tiefe, Erz, Gebieten und Entdeckungen |
| `challengeXp`, `milestoneXp` | Run-Ziele bzw. alte Meilensteine dieser Welt |
| `rewardXp` | Challenge-XP, die in dieser Welt verdient wurde (nur Anzeige, steckt schon in `periodRewardXp`) |
| `earnedXp` | Anzeige der Run-XP: Summe der vier Felder oben |
| `maxDepth`, `regions`, `discoveries`, `resources`, `resourceSources`, `crafts`, `exoticCrafts` | Wiederholungsschutz (Replay-Listen); `resourceSources` enthält auch die Challenge-Marker und die Zellen-Blöcke (`#1…`, siehe „Grösse des Profils“) |
| `objectiveIds`, `completedObjectives`, `milestones` | Run-Ziele und Meilensteine |
| `tallies` | Zähler mit Wert und Höchststand (Verkauf, Herstellen, Platzieren) |
| `ended`, `won` | Run beendet bzw. gewonnen |

**Formel** (`RecalculateTotal`, beim Laden exakt geprüft in `ValidateProfile`):

```
totalXp = periodRewardXp
        + Σ Welten ( floor(progressXp + efficiencyXp) + challengeXp )
        + Σ completedMilestones Katalog-XP
```

`run.milestoneXp` und `run.rewardXp` zählen nur für die Anzeige der Run-XP, nicht ein zweites Mal in `totalXp`.

## XP-Quellen und Wiederholungsschutz

- Welt-Ereignisse kommen über `MetaProgressionRuntime` oder Aufrufer, die `MetaProgressionRuntime.RewardsAllowed` prüfen. Ausgeschaltet beim Laden, beim Szenenwechsel und in den Debugmodi (Fly, NoClip, God, NoEnergy, InfiniteMoney, NoWeight, Abbau- oder Bewegungsfaktor über 1).
- Ein beendeter Run (`ended`) nimmt nichts mehr an.

| Ereignis | XP (Standard) | Schutz vor Doppelzählung |
| --- | --- | --- |
| Neue Maximaltiefe | je 25 Blöcke min(150, 30 + 5·Stufe) | nur Zuwachs über `maxDepth` |
| Gebiet erkundet (12×12 Zellen, ab 24 aufgedeckten) | 16·(1 + 0,6·min(1, Tiefe/1000)) | `regions` |
| Erz | Gewicht·(n_neu^0,8 − n_alt^0,8) pro Erzart | Erz-Zelle im Zellen-Ledger (früher Schlüssel `item:x:y`); andere Quellen-IDs als Schlüssel `item:quelle` |
| Entdeckung (Altar, Artefakt, Findling, Exotic) | 180 | `discoveries` |
| Run-Ziele (3 pro Welt, „run-exotic“ ab Level 5) | 180–500 | `completedObjectives` |
| Alte permanente Meilensteine | 250–8000 | `completedMilestones` |
| Runden, Projekt, Boni, neue Meilensteinstufen | siehe `Docs/Challenges.md` | Rundenstatus (`done`, `paid`, `stagesPaid`, `bonusPaid`), Marker |

- **Mengen ohne Quellen-ID** (Verkauf, Herstellen, platzierte Fackeln und Leitern) schützt ein Tally: gutgeschrieben wird nur Wachstum über den Höchststand, der nie sinkt. Jeder Höchststand steht zusätzlich als `tp:`-Marker im Ledger.
- **Zellen** (aufgedeckt, abgebaut, Erz) prüft das Zellen-Ledger der Welt (`MetaCellLedger`): ein Bit pro Zelle, ein Eintrag pro Block von 32×16 Zellen und Art. Bis Schritt 2 schreibt das Spiel neue Zellen zusätzlich als alte Schlüssel, siehe „Grösse des Profils“.

## Level und Punkte (`MetaProgressionCatalog.cs`)

- **Kurve:** Ein Level kostet `round(1000 + 5000·(1 − e^(−(L−1)/25)))` XP, also 1000 XP für Level 1→2 und gegen 6000 XP später. Höchstes Level 1000 (5'866'493 XP).
- **Punkte:** ein Powerup-Punkt pro Level bis Level 200 (höchstens 199), ab Level 200 ein Komfortpunkt je 5 Level.
- **Ausgeben nur im Hauptmenü** (`Spend`, `RefundAll`); synchron gespeichert, bei einem Fehler zurückgerollt.
- **Loadout pro Welt:** Eine neue Welt übernimmt die aktuellen Ränge. Eine geladene Welt behält ihr gespeichertes Loadout (bewusst: Ausgeben verändert keine laufende Welt).
- **Anzeige:** `GameplayLevelUpPresentation` zeigt mehrere Level-Ups als eine Medaille (von → bis), nur im laufenden Run.
- **Katalog eingefroren:** Profile und Weltstände werden beim Laden gegen den Katalog geprüft (Meilenstein- und Run-Ziel-XP, IDs, Ränge, Schritte, Kosten, Levelkurve, Erzgewichte). Wer davon etwas senkt, entfernt oder umbenennt, macht bestehende Profile ungültig. Änderungen nur mit Migration; `ProgressionChecks` („catalog freeze“) schlägt sonst an. Neue Werte nur anhängen.

## Speicherzeitpunkte

Ein einziger Schreiber mit zwei Wegen: `RequestSave` schreibt eine Kopie des Profils im Hintergrund (`Task.Run`), `Save` schreibt synchron und wartet einen laufenden Hintergrund-Schreibvorgang zuerst ab. Geschrieben wird zuerst `.tmp` (mit Flush), dann ersetzt `File.Replace` die Hauptdatei; die alte Hauptdatei wird zu `.bak`.

| Auslöser | Weg |
| --- | --- |
| Level-Wechsel, Entdeckung, exotisches Rezept, jede Challenge-Auszahlung, jeder Meilenstein | sofort, Hintergrund (`MarkChanged`) |
| Läuft gerade ein Hintergrund-Schreibvorgang | Nachschreiben im nächsten Frame nach dessen Ende (`PumpSaves`, jeden Frame, auch in Menüs und bei gesperrter Eingabe) |
| Spielzeit | alle 5 s, Hintergrund |
| Welt speichern/Autosave (`CaptureRunState`), `BeginRun`, `EndRun`, Game Over, Sieg, Ausbau, Beenden, App-Pause | synchron |

**Absturzfenster:** Ein Level-Up oder eine Auszahlung geht nur verloren, wenn der Prozess innerhalb der wenigen Millisekunden des sofort gestarteten Schreibvorgangs endet. Gewöhnliche XP ohne Level-Wechsel sind höchstens 5 s alt.

## Laden älterer Spielstände (`BeginRun` → `MergeLedger`)

- Unbekannte `runId`: das Ledger des Spielstands wird übernommen.
- Bekannte `runId`: Maximum für Tiefe, Spielzeit, XP-Felder und Erzzähler, Vereinigung aller Replay-Listen, Run-Ziele und Meilensteine. Ein älterer Spielstand senkt nie XP, und dieselben Zellen, Gebiete, Entdeckungen und Verkäufe zählen nicht erneut.
- **Tallies:** Der Wert folgt dem Spielstand, der Höchststand bleibt. Ein Spielstand ohne Tallies (vor der Metaprogression gespeichert oder von einem älteren Build) steht dort, wo seine eigenen `tp:`-Marker sagen (ohne Marker: 0). Wiederverkauf nach erneutem Laden zahlt nichts.
- **Auszahlungs-Marker** (`cr:`, `cx:`, `ct:`, `cs:`, `cm:`) gelten nur aus dem eigenen Profil-Ledger, das in derselben Datei wie die XP steht. Ein Marker, den nur der Spielstand hat (Profil verloren, zurückgefallen oder nie gespeichert), wird verworfen: Die Belohnung bleibt offen, statt ohne XP als bezahlt zu gelten.
- Loadout und `ended` folgen dem Spielstand. Eine Welt kann nach dem Tod über einen älteren Stand weitergespielt werden.

## Challenges (Kurzfassung)

- Je eine Runde täglich, wöchentlich (ab Montag) und monatlich in `profile.challengeRounds`, Grenzen in Ortszeit. Eine neue Runde wird beim ersten Zugriff nach Ablauf der alten gezogen.
- `challengeClockUtc` läuft nur vorwärts: Die Uhr zurückzustellen öffnet keine bezahlte Runde wieder.
- Auszahlungen gehen nach `periodRewardXp` (Haupt-Progression) und hinterlassen Marker im Ledger der laufenden Welt; ältere Builds bauen damit verlorene Runden und Stufen nach, ohne erneut zu zahlen.
- Fortschritt gibt es nur während eines Runs.

## Robustheit

- **Lesen:** Von Hauptdatei, `.bak` und `.tmp` gewinnt die gültige Datei mit der höchsten `revision`. Eine kurz gesperrte Hauptdatei wird bis zu dreimal erneut gelesen, bevor auf `.bak` ausgewichen wird.
- **Abgebrochener erster Schreibvorgang:** Eine kaputte `.tmp` allein (ohne Haupt- und Backupdatei) sperrt nichts; das Profil startet neu.
- **Ersetzen scheitert:** Die vollständig geschriebene `.tmp` bleibt liegen und wird beim nächsten Start wiederhergestellt. Nach einem zurückgerollten Ausbau wird sie verworfen.
- **Gesperrtes Profil** (neuere Version oder unlesbar): Das Profil wird nie überschrieben. Der Fortschritt läuft aber im Ledger der Welt weiter, landet mit dem Weltstand auf der Platte und wird beim nächsten Laden mit lesbarem Profil übernommen. Profilweite Daten (Runden, Zähler, Meilensteinstufen) bleiben gesperrt.
- **Run-Ende:** Tod und Sieg beenden den Run immer, auch bei gesperrtem Profil oder ohne Belohnung (Debug-Sieg, `MarkRunEnded`); sonst öffnete „Weiter“ die Ergebnisanzeige nicht.
- **Warnung:** Ist das Profil gesperrt oder scheitert das Speichern dauerhaft, zeigt das Pausemenü „Fortschritt wird nicht gespeichert …“ bzw. „Fortschritt konnte nicht gespeichert werden.“ (`MetaProgression.ProfileWarning`).
- **Grösse des Profils:** siehe Abschnitt unten.

## Grösse des Profils

Fund F-01 der Prüfung vom 23.10.2026: Bisher stand für jede aufgedeckte, abgebaute und Erz-Zelle jeder Welt ein eigener Text im Profil (`revealed:x:d`, `mined:x:y`, `item:x:y`). Eine voll erkundete Welt von 600×1200 Feldern ergab rund 1,4 Millionen Texte und etwa 28 MB. Bei mehr als 2 Millionen Einträgen lehnte das Spiel den Run und damit das ganze Profil ab.

**Zellen-Ledger** (`MetaCellLedger.cs`): Die Zellen einer Welt liegen als Bits in Blöcken von 32×16 Zellen, getrennt nach Art: `R` aufgedeckt, `M` abgebaut, `O<Item>` Erz (numerische Item-ID, `O3` = Kohle). Jeder Block ist ein Text im bestehenden `resourceSources` mit dem Präfix `#1`:

- Beispiel `#1M:2812:-3:sER`: Art, Block-x (`x >> 5`), Block-y (`y >> 4`), Inhalt. Diese Zeile steht für die abgebaute Zelle (90001, −40).
- Inhalt `f` (alle 512 Zellen), `s` mit bis zu 42 Zellnummern, `i` (alle ausser bis zu 42 Zellnummern), sonst `b` mit dem Bitfeld in Base64url (86 Zeichen). Ein Block hat höchstens 110 Zeichen.
- Kein neues Format und kein neues JSON-Feld: Ältere Builds behalten, prüfen, vereinigen und schreiben diese Texte wie jeden anderen Ledger-Eintrag. Vereinigte Texte ergeben für die Zellen ein bitweises Oder.
- Ein `#`-Text, der sich nicht lesen lässt (spätere Version, beschädigt), bleibt unverändert erhalten und führt nie zur Ablehnung.
- Alle anderen Schlüssel (`vein:`, `harvest:`, `artifact-…`, `craft-upgrade:`, `gear-crafted:`, Marker) bleiben unveränderte Texte.

**Grösse:** Eine voll erkundete Welt braucht noch höchstens etwa 7'500 Texte und 0,35–0,5 MB statt rund 1,4 Millionen Texte und 28 MB. Dasselbe gilt für die Kopie im Spielstand (`RunSaveState.progression`). Im Speicher sind es pro Welt etwa 1–2 MB statt 120–150 MB. Kopien des Runs für Spielstand und Hintergrund-Schreiben laufen über `CopyRun`, ohne JSON-Umweg.

**Wiederholungsschutz:** Für jede Zelle im Bereich ±2^20 entscheidet das Ledger, ob sie neu ist. Ausserhalb (nur bei sehr grossen Welten oder Testaufrufen) gilt weiter der alte Textschlüssel. Ein alter Zellschlüssel, der sich nicht falten lässt (unbekannte Schreibweise des Minuszeichens, führende Nullen), schaltet für diese Welt zusätzlich die alte Textsuche ein. Der Schutz ist damit nie schwächer als bisher; verschiedene Schreibweisen derselben Zelle zählen nur noch einmal.

**Migration:**

- Beim Anhängen eines Runs (`BeginRun`, auch mit Spielstand) faltet das Spiel alte Zellschlüssel in das Ledger: `revealed:`, `mined:`, `l2-block:`, `l3-block:`, `xxl-layer:4:`, `xxl-layer:5:`, `surface-block:` und `item:x:y`, auch mit den Minus-Schreibweisen anderer Sprachen. Mehrere Blöcke derselben Zellen werden vereinigt.
- Mit eingeschaltetem Schreiber verdichtet `EnsureLoaded` ein beschreibbares Profil einmalig (`CompactLedgers`) und schreibt es sofort im Hintergrund. Die Hauptdatei ist danach klein, die `.bak` nach dem nächsten Schreiben. Gesperrte Profile werden nicht verdichtet.
- **Rettung:** Die Lesegrenze eines Runs steigt von 2 auf 6 Millionen Einträge (`MaxLedgerEntries`). Ein Profil, das bisher wegen mehr als 2 Millionen Einträgen abgelehnt wurde, lädt wieder und wird verdichtet. Danach lesen es auch ältere Builds.
- Spielstand-Dateien werden beim Laden nicht umgeschrieben; erst ihr nächstes Speichern ist kompakt.

**Rollout** (`MetaProgression.CellChunkWriter`):

1. **Schritt 1 (diese Version, `CellChunkWriter = false`):** Das Spiel liest Blöcke, behält sie unverändert und nutzt das Ledger für jede Abfrage. Neue Zellen schreibt es weiter als alte Schlüssel in der bisherigen Schreibweise, beim Laden wird nichts verdichtet. Format und Grösse der Dateien bleiben wie bisher.
2. **Schritt 2 (geplant, eine Zeile):** `CellChunkWriter = true`, sobald Schritt 1 in `main` ist. Dann schreibt das Spiel nur noch Blöcke und verdichtet alte Profile beim Laden. Erst damit wird das Profil klein.
3. **Schritt 3 (eigener PR, nur nach Freigabe):** Der Profil-Schreibvorgang beim Speichern der Welt läuft im Hintergrund.

**Lücke beim Downgrade:** Ein Build ohne Leser (vor Schritt 1) sieht Zellen nicht, die nur in Blöcken stehen. Lädt man dort einen älteren Spielstand, können erneut abgebaute oder aufgedeckte Zellen nochmals zählen (Erz-XP, Blöcke, Zellen). Verloren geht nichts: Der neue Build vereinigt beim nächsten Laden alle Blöcke und Schlüssel wieder. Darum kommt der Leser vor dem Schreiber.

**Tests:** `MetaCellLedgerChecks` prüft Kodierung, Ledger, Migration, Rettung, Downgrade und beide Schreibermodi. `CellChunkWriterOverride` wählt den Modus, wirkt aber nur in isolierten Testprofilen (`TestDirectory`).

## Testverzeichnisse und GPS

- **Profil isolieren:** `MetaProgression.TestDirectory`, im Editor zusätzlich `GameSaveSystem.TestDirectory/MetaProfile`. `Reload()` verwirft den Speicherzustand (ein geändertes Profil wird vorher gespeichert).
- `ChallengeTestUtc`, `ChallengeSettingsOverride` und `ChallengeTimeZoneOverride` wirken nur mit `MetaProgression.TestDirectory`; Spielstände des Benutzers werden dann nicht gelesen.
- **GPS:** `metaProgression` (XP-Werte, ab neuer Welt, eingefroren in `run.settings`), `challenges` (ab der nächsten Runde), `levelUpAnimationOffsetSeconds`.

## Prüfskripte

| Skript | Modus | Inhalt |
| --- | --- | --- |
| `ProgressionChecks` | Edit, Cloud | Soll-Punkte 1–3, Absturz ohne Speichern, älterer Weltstand, Run-Ziele, Tallies, Marker, gesperrtes Profil, Run-Ende, Journal, Warnung, Katalog |
| `MetaProgressionCoreChecks` | Edit, Cloud | Kurve, Belohnungen, Replay, Ausbau, Speichern, Validierung |
| `MetaCellLedgerChecks` | Edit, Cloud | Zellen-Ledger: Kodierung, Faltung alter Schlüssel, Verdichtung, Rettung, Downgrade, beide Schreibermodi, Grösse |
| `ChallengeReworkChecks`, `ChallengeCoreChecks`, `ChallengeSettingsChecks`, `ChallengeMigrationChecks` | Edit, Cloud | Runden, Zählstellen, Uhr, Zeitzonen, Meilensteine, Downgrade, Migration |
| `MetaProgressionIntegrationChecks`, `GameplayLevelUpChecks`, `ProgressionTimelineChecks`, `ChallengeGameplayChecks`, `ChallengePeriodsChecks`, `MetaVictoryRuntimeChecks`, `MetaProgressionUiChecks` | Play, nur lokal | Weltstand und Profil im Spiel, Level-Up-Anzeige, Ergebnisanzeige |
