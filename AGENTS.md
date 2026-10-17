<!-- Einzige Quelle der Projektregeln: Codex liest diese Datei direkt, CLAUDE.md importiert sie (@AGENTS.md). -->

# Mining Game – Projektanweisungen

2D-Mining-Spiel in Unity 6 (URP 17, 2D-Renderer, Tilemaps). Hauptszene: `Assets/Scenes/SampleScene.unity`.
Die vollständige Änderungshistorie steht in `Docs/Changelog.md`. Dort nur bei Bedarf gezielt nachlesen (grep), nicht komplett laden.

## Kommunikation

- Auf Deutsch antworten (Schweizer Schreibweise, kein ß).
- Kompakt antworten, auf das Wesentliche konzentrieren. Details nur, wenn nötig oder ausdrücklich erfragt.
- Stellt der Benutzer eine Frage, zuerst nur die Frage beantworten. Auch wenn sie eine mögliche Änderung betrifft: nichts programmieren oder verändern, bis eine ausdrückliche Freigabe in einer folgenden Nachricht kommt. Eine ausdrückliche Arbeitsanweisung ohne Frage direkt ausführen.

## Arbeitsregeln

- **Kamera:** Nach jeder Aufgabe muss die Main Camera dem Spieler folgen. `CameraFollow` und `CameraWorldBorderClamp` in der SampleScene aktiviert lassen, Debug → Gameplay → Camera Follow eingeschaltet lassen, temporäre Kameraänderungen nach Play-Mode-Tests zurücksetzen. Zum Abschluss einen frischen Play-Start auf aktive Nachführung prüfen.
- **Neue Assets:** `Assets/ZZZ New Assets` ist der Eingangsordner für vom Benutzer bereitgestellte Assets. Bei passenden Aufgaben dort nachsehen, sinnvoll benennen und in die Projektstruktur einsortieren. Immer mit `.meta`-Dateien/GUIDs verschieben. Nicht zugeordnete Assets dort belassen, nichts ungefragt löschen.
- **Szenen-Backups:** AutoSave legt sie in `SceneBackups/` an der Projektwurzel ab (ausserhalb von Assets, per `.gitignore` ausgeschlossen). Nicht zu Git/LFS hinzufügen. `SampleScene.unity` wird über LFS versioniert.
- **Enum-IDs:** Bestehende `BlockType`-/`Item`-IDs nie verschieben, neue nur anhängen (Save-Kompatibilität).
- **Erze:** Neue Erzarten erhalten im `Block` die Gruppen `smallOre`, `mediumOre`, `richOre`. Overlays: beleuchtetes Material wie Stein (kein selbstleuchtendes Glitzern), einheitlicher Mittelpunkt, 0,5 Welteinheiten pro Kachel, unkomprimiertes RGBA, Mipmaps, trilinear. Erzverteilung und Grafikvariation bleiben seedabhängig/seedstabil; Effekte dürfen Unitys Zufallszustand der Mapgenerierung nicht verändern.
- **Bestehendes bewahren:** Grafikpfade, GUIDs und Sprite-IDs beim Ersetzen erhalten. Originale nicht überschreiben, sondern Varianten anlegen, ausser der Benutzer gibt etwas ausdrücklich frei.
- **Generierte Grafiken:** Quellen, Prompts und Vergleichsbilder unter `Design/<Thema>/` an der Projektwurzel ablegen (z. B. `Prompts.json`), nicht unter `Assets/`: Unity würde sie sonst importieren. Nach `Assets/` kommt nur, was das Spiel tatsächlich verwendet.
- **Gameplay-Werte** werden zentral über GPS (Gameplay Settings) bzw. das F1-Debugfenster einstellbar gemacht, nicht hart codiert.
- **Benutzerprofile/Spielstände** bei Tests nicht verändern; isolierte Testprofile/-verzeichnisse verwenden.

## Tests im Unity Editor

- Prüfskripte liegen unter `Tests/` und laufen im verbundenen Editor:
  `unity command run_script --file Tests/<Name>Checks.cs`
  Viele Prüfskripte verlangen einen frischen Play-Start ab MainMenu (Rückgabe „Requires … Play …“). Solche Skripte nie im Edit Mode starten, sonst schreiben sie Testobjekte in die geöffnete Szene.
- `Tests/Archive/` enthält bereits angewendete Einmal-Skripte (Setup/Install/Bake/Preview). Nicht ungeprüft erneut ausführen: Sie können neuere Assets oder Szenenänderungen überschreiben.
- Tests nutzen temporäre Szenen bzw. isolierte Profile und verändern die Spielszene nicht.
- Vorschauen und Berichte gehen ins lokale `Temp/`-Verzeichnis.
- Abschlusszustand nach Tests: SampleScene im Edit Mode, keine offenen Play-/Szenenwechsel, Save-/Meta-TestDirectory `null`.

## Zusammenarbeit mehrerer Agenten

Mehrere Agenten (Claude Code, Codex, Claude Cowork) arbeiten am selben Repo. Koordination läuft über `Docs/Arbeitsstand.md`:

1. **Vor dem Start** `Docs/Arbeitsstand.md` lesen. Überschneidet sich die geplante Arbeit mit einem aktiven Eintrag (gleiche Dateien/Systeme) oder ist der Editor belegt: nicht loslegen, sondern den Benutzer fragen.
2. **Eintragen** unter „Aktiv“: Agent, Aufgabe, Branch, betroffene Dateien/Systeme, Editor ja/nein, Startzeit.
3. **Editor:** Nur ein Agent nutzt den Unity Editor gleichzeitig (Play-Mode, `run_script`, Szenenänderungen). Belegung in der Tabelle „Unity Editor“ eintragen und danach sofort wieder freigeben.
4. **Branch:** Jede Aufgabe auf einem eigenen Branch (`claude/<thema>`, `codex/<thema>`), nicht direkt auf `main`. Zusammenführen nur nach Freigabe durch den Benutzer.
5. **Abschluss:** Eigenen Eintrag aus „Aktiv“ entfernen, einen Satz unter „Zuletzt abgeschlossen“ ergänzen (max. 10 Einträge, älteste löschen), Changelog-Eintrag in `Docs/Changelog.md`.
6. Veraltete Einträge (älter als 24 h) nicht selbst löschen, sondern den Benutzer darauf hinweisen.
7. Gibt es keinen aktiven Konflikt, keine Rückfragen oder Abschlussmeldungen an andere Agenten.

## Dokumentation

- Nach abgeschlossenen Änderungen einen kurzen Eintrag **oben** in `Docs/Changelog.md` ergänzen: `- Titel (TT.MM.JJJJ): Was, wo, wie geprüft.` Diese Datei (`AGENTS.md`) nur für dauerhafte Regeln verwenden, nicht für das Protokoll.
- Feature-Dokus unter `Docs/` (z. B. `Ore-Sparkles.md`, `Parallax-Hintergrund.md`).
