<!-- Einzige Quelle der Projektregeln: Codex liest diese Datei direkt, CLAUDE.md importiert sie (@AGENTS.md). -->

# Mining Game – Projektanweisungen

2D-Mining-Spiel in Unity 6 (URP 17, 2D-Renderer, Tilemaps). Hauptszene: `Assets/Scenes/SampleScene.unity`.
**Orientierung:** `Docs/Systemuebersicht.md` zeigt, welches System in welchen Dateien steckt; dort zuerst nachsehen, dann gezielt suchen. Bei neuen Systemen oder Umzügen nachführen.

**Grosse Dateien nie komplett laden, nur gezielt suchen (grep) und Ausschnitte lesen:** `Docs/Changelog.md` (vollständige Änderungshistorie), `Assets/Resources/Gameplay/GpsProfile.asset` (alle F1/GPS-Werte, mehrere MB), Szenen (`*.unity`), Prefabs und Unity-Logs.

## Kommunikation

- Auf Deutsch antworten (Schweizer Schreibweise, kein ß), kompakt, Details nur wenn nötig oder erfragt.
- Stellt der Benutzer eine Frage, nur die Frage beantworten. Auch wenn sie eine mögliche Änderung betrifft: nichts programmieren oder verändern, bis eine ausdrückliche Freigabe in einer folgenden Nachricht kommt. Eine ausdrückliche Arbeitsanweisung ohne Frage direkt ausführen.
- Nach jeder abgeschlossenen Aufgabe ein kurzes Resümee in einfacher Sprache ohne technische Details: was geändert wurde, wie es im Prinzip funktioniert, was der nächste Schritt ist. Zwischenstände (z. B. Warten auf Tests) in 2–3 Sätzen: Stand, Hindernis, nächster Schritt.

## Arbeitsregeln

- **Kamera:** Nach jeder Aufgabe folgt die Main Camera dem Spieler: `CameraFollow` und `CameraWorldBorderClamp` in der SampleScene aktiviert, Debug → Gameplay → Camera Follow eingeschaltet, temporäre Kameraänderungen nach Play-Tests zurückgesetzt. Zum Abschluss einen frischen Play-Start auf aktive Nachführung prüfen.
- **Neue Assets:** `Assets/ZZZ New Assets` ist der Eingangsordner für Assets des Benutzers. Bei passenden Aufgaben dort nachsehen, sinnvoll benennen und einsortieren, immer mit `.meta`/GUIDs verschieben. Nicht Zugeordnetes dort lassen, nichts ungefragt löschen.
- **Szenen-Backups:** AutoSave legt sie in `SceneBackups/` an der Projektwurzel ab (per `.gitignore` ausgeschlossen), nicht zu Git/LFS hinzufügen. `SampleScene.unity` wird über LFS versioniert.
- **Enum-IDs:** Bestehende `BlockType`-/`Item`-IDs nie verschieben, neue nur anhängen (Save-Kompatibilität). Ebenso Asset-Schlüssel, die Spielstände referenzieren (z. B. Bild-Verweise im GPS-Profil), nicht ersatzlos entfernen.
- **Erze:** Neue Erzarten erhalten im `Block` die Gruppen `smallOre`, `mediumOre`, `richOre`. Overlays: beleuchtetes Material wie Stein (kein selbstleuchtendes Glitzern), einheitlicher Mittelpunkt, 0,5 Welteinheiten pro Kachel, unkomprimiertes RGBA, Mipmaps, trilinear. Erzverteilung und Grafikvariation bleiben seedstabil; Effekte dürfen Unitys Zufallszustand der Mapgenerierung nicht verändern.
- **Bestehendes bewahren:** Grafikpfade, GUIDs und Sprite-IDs beim Ersetzen erhalten. Originale nicht überschreiben, sondern Varianten anlegen, ausser der Benutzer gibt es ausdrücklich frei.
- **Generierte Grafiken:** Quellen, Prompts und Vergleichsbilder unter `Design/<Thema>/` an der Projektwurzel (z. B. `Prompts.json`), nicht unter `Assets/` (Unity würde sie importieren). Nach `Assets/` kommt nur, was das Spiel verwendet. In Arbeitsordnern ohne `Design/` vorher `git sparse-checkout add Design/<Thema>/`.
- **Gameplay-Werte** zentral über GPS bzw. das F1-Debugfenster einstellbar machen, nicht hart codieren.
- **Ordner `Mining Game` (Benutzer):** Chats committen dort nie und ändern keine versionierten Dateien. Er folgt automatisch `origin/main` (`Docs/Main-Auto-Update.md`); eigene F1/GPS-Werte des Benutzers liegen unversioniert in `UserSettings/GpsOverrides.json` (`Docs/GPS-Eigene-Werte.md`). GPS-Standardwerte (`GpsProfile.asset`) ändern sich nur über Branch/PR.
- **Benutzerprofile/Spielstände** bei Tests nie verändern und keine echten Spielstände anlegen (die Saves sind für alle Editoren gemeinsam); isolierte Testprofile/-verzeichnisse verwenden.
- **Befehlsaufrufe:** Skripte (`Tools/editor-queue.sh`, `Tools/editor-bench.sh`, `unity command`) und Git einzeln und direkt aufrufen, ohne `cd`-Präfix, Schleifen, Variablen (`$rc`, `$(…)`), `&&`/`||`-Ketten oder Pipes. Git immer im eigenen Arbeitsordner, ohne `git -C <Ordner>` (z. B. `git show origin/main:AGENTS.md`). Wiederholtes Warten als einzelne Aufrufe nacheinander. Dateien mit den eingebauten Such- und Lesewerkzeugen statt `grep`/`cat`/`ls` in der Shell. Nur so greifen die festen Freigaben, sonst muss der Benutzer jeden Aufruf bestätigen.

## Tests im Unity Editor

- **Welcher Editor:** Der Editor von `Mining Game` gehört dem Benutzer; Chats nutzen ihn nur, wenn er es ausdrücklich sagt. Chats testen auf den Testbänken `Mining Game 2` / `Mining Game 3` über die gemeinsame Warteschlange `Testbank` (Ablauf unter „Zusammenarbeit“ Punkt 3).
- Prüfskripte liegen unter `Tests/`: `unity command --project-path "<Git Repos>/<Testbank>" run_script --file Tests/<Name>Checks.cs`. Nur die betroffenen laufen lassen, mehrere direkt nacheinander in derselben Belegung (Tipps: `Docs/Editor-Warteschlange.md` → „Tests kürzer machen“). Skripte, die „Requires … Play …“ melden, nie im Edit Mode starten, sonst schreiben sie Testobjekte in die offene Szene.
- `Tests/Archive/` enthält bereits angewendete Einmal-Skripte (Setup/Install/Bake/Preview). Nicht ungeprüft erneut ausführen, sie können Neueres überschreiben.
- Tests nutzen temporäre Szenen bzw. isolierte Profile, Vorschauen und Berichte gehen nach `Temp/`. Abschlusszustand: SampleScene im Edit Mode, kein offener Play-/Szenenwechsel, Save-/Meta-TestDirectory `null`.
- **Cloud (GitHub Actions, Workflow „Unity Checks“, `Docs/Cloud-Tests.md`):** Läufe nur mit ausdrücklicher Freigabe des Benutzers (Actions-Minuten sind knapp), Pushes auf offene PRs mit `[skip ci]`. Geeignet nur für Code-Änderungen, Logik-/Daten-Checks, CPU-Messungen (Suite `performance`, ohne Zeichnen) und Standbild-Vergleiche: nach Änderungen an Erz-, Terrain- oder Overlay-Grafik die Suite `visual`, Bilder mit `Tools/ci-previews.sh <Nr>` holen und selbst ansehen (Vorher/Nachher; die endgültige Optik nimmt der Benutzer lokal ab). Bewegung, Animation, Partikel, Bildraten und Spielgefühl nur lokal bzw. Abnahme durch den Benutzer; solche Checks mit `// CI: local-only` markieren. Der Workflow drosselt sich nach Actions-Minuten selbst: nicht umgehen, `force` nur mit Freigabe des Benutzers, Label `cloud-check` erst für den einen Lauf am Schluss eines PR. Ein Lauf hat höchstens 20 Minuten; bricht er ab („Abgebrochen: …“), nicht wiederholen, sondern dem Benutzer melden und den Check verkleinern oder lokal ausführen.

## Zusammenarbeit mehrerer Agenten

Mehrere Agenten (Claude Code, Codex, Claude Cowork) arbeiten am selben Repo. Koordination über `Docs/Arbeitsstand.md`:

1. **Vor dem Start** `Docs/Arbeitsstand.md` lesen. Überschneidet sich die Arbeit mit einem aktiven Eintrag (gleiche Dateien/Systeme): nicht loslegen, sondern den Benutzer fragen.
2. **Eintragen** unter „Aktiv“: Agent, Aufgabe, Branch, Arbeitsordner, Dateien/Systeme, Editor ja/nein, Startzeit.
3. **Testbänke und Editor-Warteschlange** (vollständig in `Docs/Editor-Warteschlange.md`, beim ersten Test lesen): Jeden Editor nutzt immer nur einer. **Immer bei der gemeinsamen Warteschlange `Testbank` anmelden**, nie direkt bei einer Testbank. Ablauf: Stand committen (auch WIP), `Tools/editor-queue.sh join -Project Testbank …`, `wait … -TimeoutMinutes 9` bis `TESTBANK=<Ordner>`, `Tools/editor-bench.sh checkout <branch> -Id <id> -Discard`, mit `unity command --project-path "<Git Repos>/<Ordner>"` testen, sofort danach `editor-bench.sh release -Id <id>` und `leave`. Unity-MCP nur, wenn `editor_status` genau die zugeteilte Testbank meldet. Der Editor ist nur fürs Testen da: 4 Minuten ohne Unity-Aufruf oder Editor-Aktivität verdrängen; zwischen zwei Tests Code ändern heisst `leave` und neu anmelden. Codex/Cowork (ohne Claude-Code-Hook) senden während eines Tests jede Minute `heartbeat`. Bittet der Benutzer um Freigabe (Meldung „… endet um HH:MM Uhr“), Test abschliessen und freigeben. Will der Benutzer einen Editor, `join -User -Project "<Ordner>"`. Antwortet der Testbank-Editor nicht, zuerst `Tools/editor-dialog.sh list`: bekannte Dialoge schliessen, unbekannte dem Benutzer melden, nie beim Editor des Benutzers. Testbänke laufen stumm (`audio-on`/`audio-off` nur für Audio-Tests). „Editoren starten“ heisst: alle drei mit `unity open` starten, die Testbänke legen sich selbst auf Desktop 2/3.
4. **Branches und Arbeitsordner:** `main` ist der stabile Stand und wird nie direkt bearbeitet. Jede Aufgabe auf eigenem Branch von `main` (`claude/<thema>`, `codex/<thema>`) in eigenem Arbeitsordner (git worktree), z. B. unter `Mining Game/.claude/worktrees/` oder `git worktree add "../Mining Game <n>" -b <agent>/<thema> main` (n ≥ 4). `Mining Game 2`/`3` sind Testbänke ohne festen Branch: dort nicht dauerhaft arbeiten, nur über `Tools/editor-bench.sh` Stände zum Testen holen. Ein Branch ist nur in einem Ordner ausgecheckt; neuen Branch mit `git fetch` und `git switch -c <agent>/<thema> main`. Neue Arbeitsordner entstehen ohne `Design/` und `AudioMastering/` (spart rund 2,9 GB); bei Bedarf `git sparse-checkout add Design/<Thema>/` oder `disable` (`Docs/Arbeitsordner-ohne-Design.md`). Ein neuer Ordner importiert beim ersten Öffnen die komplette Library (etwa 12 GB) und braucht eine eigene, unversionierte `.mcp.json`. Vor dem Zusammenführen `main` in den Branch holen und Konflikte dort lösen; nach `main` erst nach Freigabe durch den Benutzer.
5. **Abschluss:** Eigenen Eintrag aus „Aktiv“ entfernen, einen Satz unter „Zuletzt abgeschlossen“ (max. 10, älteste löschen), Changelog-Eintrag.
6. Veraltete Einträge (älter als 24 h) nicht selbst löschen, sondern den Benutzer darauf hinweisen.
7. Ohne aktiven Konflikt keine Rückfragen oder Abschlussmeldungen an andere Agenten.

## Dokumentation

- Nach abgeschlossenen Änderungen einen kurzen Eintrag **oben** in `Docs/Changelog.md`: `- Titel (TT.MM.JJJJ): Was, wo, wie geprüft.` Diese Datei nur für dauerhafte Regeln verwenden, nicht als Protokoll.
- Feature-Dokus unter `Docs/` (z. B. `Ore-Sparkles.md`, `Parallax-Hintergrund.md`).
