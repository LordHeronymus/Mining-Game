<!-- Einzige Quelle der Projektregeln: Codex liest diese Datei direkt, CLAUDE.md importiert sie (@AGENTS.md). -->

# Mining Game – Projektanweisungen

2D-Mining-Spiel in Unity 6 (URP 17, 2D-Renderer, Tilemaps). Hauptszene: `Assets/Scenes/SampleScene.unity`.
Die vollständige Änderungshistorie steht in `Docs/Changelog.md`. Dort nur bei Bedarf gezielt nachlesen (grep), nicht komplett laden.

## Kommunikation

- Auf Deutsch antworten (Schweizer Schreibweise, kein ß).
- Kompakt antworten, auf das Wesentliche konzentrieren. Details nur, wenn nötig oder ausdrücklich erfragt.
- Stellt der Benutzer eine Frage, zuerst nur die Frage beantworten. Auch wenn sie eine mögliche Änderung betrifft: nichts programmieren oder verändern, bis eine ausdrückliche Freigabe in einer folgenden Nachricht kommt. Eine ausdrückliche Arbeitsanweisung ohne Frage direkt ausführen.
- Nach jeder abgeschlossenen Aufgabe ein kurzes Resümee in einfacher Sprache ohne technische Details: was geändert wurde, wie es im Prinzip funktioniert und was der nächste Schritt ist.

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
- **Befehlsaufrufe:** Skripte (`Tools/editor-queue.sh`, `Tools/editor-bench.sh`, `unity command`) und Git-Befehle einzeln und direkt aufrufen, ohne `cd`-Präfix, `for`-/`while`-Schleifen, Variablen (`$rc`, `$(…)`), `&&`-/`||`-Ketten oder Pipes. Git immer im eigenen Arbeitsordner aufrufen, ohne `git -C <Ordner>` (z. B. `git show origin/main:AGENTS.md`). Wiederholtes Warten (z. B. bis der Editor antwortet) als einzelne Aufrufe nacheinander. Dateien mit den eingebauten Such- und Lesewerkzeugen durchsuchen und lesen statt mit `grep`/`cat`/`ls` in der Shell. Nur so greifen die festen Freigaben, sonst muss der Benutzer jeden Aufruf bestätigen.

## Tests im Unity Editor

- **Welcher Editor:** Der Editor von `Mining Game` gehört dem Benutzer für eigene Tests; Chats nutzen ihn nur, wenn der Benutzer es ausdrücklich sagt. Chats testen im Editor von `Mining Game 2` (Testbank, siehe „Zusammenarbeit mehrerer Agenten“ Punkt 3). Unity-Werkzeuge (MCP) nur verwenden, wenn `editor_status` als `projectPath` `Mining Game 2` meldet (festgelegt in der nicht versionierten `.mcp.json` des Ordners, in dem der Chat läuft); sonst den Editor mit `unity command --project-path "<Git Repos>/Mining Game 2" …` ausdrücklich ansprechen.
- Prüfskripte liegen unter `Tests/` und laufen im verbundenen Editor:
  `unity command --project-path "<Git Repos>/Mining Game 2" run_script --file Tests/<Name>Checks.cs`
  Viele Prüfskripte verlangen einen frischen Play-Start ab MainMenu (Rückgabe „Requires … Play …“). Solche Skripte nie im Edit Mode starten, sonst schreiben sie Testobjekte in die geöffnete Szene.
- `Tests/Archive/` enthält bereits angewendete Einmal-Skripte (Setup/Install/Bake/Preview). Nicht ungeprüft erneut ausführen: Sie können neuere Assets oder Szenenänderungen überschreiben.
- Tests nutzen temporäre Szenen bzw. isolierte Profile und verändern die Spielszene nicht.
- Vorschauen und Berichte gehen ins lokale `Temp/`-Verzeichnis.
- Abschlusszustand nach Tests: SampleScene im Edit Mode, keine offenen Play-/Szenenwechsel, Save-/Meta-TestDirectory `null`.
- Ohne lokalen Editor (Cloud-Sessions): Checks über den GitHub-Workflow „Unity Checks“ ausführen, siehe `Docs/Cloud-Tests.md`.
- **Cloud nur für Passendes:** In der Cloud nur Code-Änderungen, Logik-/Daten-Checks, CPU-Messungen (Suite `performance`, ohne Zeichnen) und Screenshot-Vergleiche von Standbildern (z. B. Gebäude, Erz-Overlays). Bewegung, Animation, Partikel, Bildraten und Spielgefühl nur lokal im Editor prüfen bzw. den Benutzer zur Abnahme bitten. Solche Checks im Skript mit `// CI: local-only` markieren; die Cloud lehnt sie dann sofort ab.
- **Zeitlimit Cloud:** Ein Lauf hat höchstens 20 Minuten. Bricht er mit „Abgebrochen: …“ ab (Zeitlimit oder `local-only`), denselben Lauf nicht erneut starten, sondern den Grund dem Benutzer melden und den Check verkleinern oder lokal ausführen.

## Zusammenarbeit mehrerer Agenten

Mehrere Agenten (Claude Code, Codex, Claude Cowork) arbeiten am selben Repo. Koordination läuft über `Docs/Arbeitsstand.md`:

1. **Vor dem Start** `Docs/Arbeitsstand.md` lesen. Überschneidet sich die geplante Arbeit mit einem aktiven Eintrag (gleiche Dateien/Systeme): nicht loslegen, sondern den Benutzer fragen.
2. **Eintragen** unter „Aktiv“: Agent, Aufgabe, Branch, Arbeitsordner, betroffene Dateien/Systeme, Editor ja/nein, Startzeit.
3. **Editor-Warteschlange und Testbank:** Jeder Arbeitsordner hat seinen eigenen Unity Editor, den immer nur einer nutzt (Play-Mode, `run_script`, Szenenänderungen, Recompile). Chats testen in `Mining Game 2`, der Testbank; der Editor von `Mining Game` bleibt dem Benutzer. Die Belegung läuft über die Warteschlange ausserhalb von Git (`Git Repos/EditorQueue/<Ordner>.json`), Details in `Docs/Editor-Warteschlange.md`. Ablauf: eigenen Stand committen (auch als WIP), `Tools/editor-queue.sh join -Project "Mining Game 2" -Who "<Agent>: <Aufgabe>" -Purpose "<Zweck>"` (PowerShell: `Tools/editor-queue.ps1`), dann `wait -Project "Mining Game 2" -Id <id> -TimeoutMinutes 9` bis „Du bist oben“ (Exit 2: erneut aufrufen). Dann `Tools/editor-bench.sh checkout <branch> -Id <id> -Discard` (holt den Stand losgelöst in die Testbank, verwirft Reste), Unity neu laden lassen und testen. Danach sofort `Tools/editor-bench.sh release` (`-Discard` verwirft Testspuren; auf der Testbank entstandene Commits zeigt es zum Übernehmen an) und `leave -Project "Mining Game 2" -Id <id>`. Die Testbank läuft stumm; nur für Audio-Tests `Tools/editor-bench.sh audio-on -Id <id>` und danach `audio-off` (release schaltet ebenfalls stumm). Der Editor ist nur fürs Testen reserviert, nicht fürs Programmieren: Wer 2 Minuten lang weder Unity aufruft noch Editor-Aktivität (Play-Mode, Kompilieren) erzeugt, wird verdrängt. Zwischen zwei Tests Code ändern heisst also: `leave`, danach für den nächsten Test neu anmelden. Claude Code meldet Unity-Aufrufe automatisch per Hook (`.claude/settings.json`, nur Unity-MCP-Werkzeuge, `unity command`, `editor-bench`; ein laufender Aufruf schützt bis 15 min); Agenten ohne diesen Hook (Codex, Cowork) rufen während eines Tests mindestens jede Minute `heartbeat -Id <id>` auf (bei langen Aufrufen `heartbeat -Busy` vorher, `-Idle` danach). Für den Benutzer gilt eine Frist von 10 Minuten. Möchte der Benutzer den Editor (er sagt es im Chat), `join -User -Purpose "<Zweck>"` ausführen: Er kommt direkt hinter den aktuellen Inhaber, der nicht verdrängt wird.
4. **Branches und Arbeitsordner:** `main` ist der stabile Stand und wird nie direkt bearbeitet. Jede Aufgabe läuft auf einem eigenen Branch von `main` (`claude/<thema>`, `codex/<thema>`). Gleichzeitig laufende Chats arbeiten je in einem eigenen Arbeitsordner (git worktree), z. B. unter `Mining Game/.claude/worktrees/` oder mit `git worktree add "../Mining Game <n>" -b <agent>/<thema> main` (n ≥ 3). `Mining Game` ist der Ordner des Benutzers, `Mining Game 2` die Testbank ohne festen Branch: dort nicht dauerhaft arbeiten, nur über `Tools/editor-bench.sh` Stände zum Testen holen. Ein Branch ist immer nur in einem Ordner ausgecheckt; einen neuen Branch im eigenen Ordner mit `git switch -c <agent>/<thema> main` beginnen (vorher `git fetch`). Ein neuer Ordner importiert beim ersten Öffnen in Unity die komplette Library (etwa 12 GB, dauert) und braucht eine eigene, nicht versionierte `.mcp.json` mit seinem Projektpfad. Vor dem Zusammenführen `main` in den Branch holen und Konflikte dort lösen; nach `main` erst nach Freigabe durch den Benutzer.
5. **Abschluss:** Eigenen Eintrag aus „Aktiv“ entfernen, einen Satz unter „Zuletzt abgeschlossen“ ergänzen (max. 10 Einträge, älteste löschen), Changelog-Eintrag in `Docs/Changelog.md`.
6. Veraltete Einträge (älter als 24 h) nicht selbst löschen, sondern den Benutzer darauf hinweisen.
7. Gibt es keinen aktiven Konflikt, keine Rückfragen oder Abschlussmeldungen an andere Agenten.

## Dokumentation

- Nach abgeschlossenen Änderungen einen kurzen Eintrag **oben** in `Docs/Changelog.md` ergänzen: `- Titel (TT.MM.JJJJ): Was, wo, wie geprüft.` Diese Datei (`AGENTS.md`) nur für dauerhafte Regeln verwenden, nicht für das Protokoll.
- Feature-Dokus unter `Docs/` (z. B. `Ore-Sparkles.md`, `Parallax-Hintergrund.md`).
