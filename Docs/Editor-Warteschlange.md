# Editor-Warteschlange

Anmeldeliste mit Warteschlange für den Unity Editor, damit sich Chats (Claude Code, Codex, Cowork) und der Benutzer beim Testen nicht in die Quere kommen. Jeder Arbeitsordner (`Mining Game`, `Mining Game 2`) hat seinen eigenen Editor und damit seine eigene Warteschlange.

## Dateien (ausserhalb von Git)

Alle unter `C:\IT\GameDev\Git Repos\EditorQueue\` (gleich für alle Branches und Worktrees, Ordnername = Projektordner des Editors):

| Datei | Inhalt |
| --- | --- |
| `<Ordner>.json` | Warteschlange: Id, Wer, Sitzung, Zweck, angemeldet seit, letztes Lebenszeichen, oben seit |
| `<Ordner>.log` | Protokoll: Anmeldung, Nachrücken, Abmeldung, Verdrängung mit Grund |
| `<Ordner>.activity.json` | Aktivitätssignal des Editors (schreibt `Assets/Editor/EditorQueueActivity.cs`) |
| `<Ordner>.lock` | Sperre für atomares Schreiben (exklusiv geöffnet, wird beim Prozessende automatisch frei) |
| `<Ordner>.dialogs.json` | Nur Testbank: offene modale Dialoge des Editors und die zuletzt beantworteten (schreibt `Tools/editor-dialog.ps1`) |
| `<Ordner>.focus-guard-off` | Nur Testbank: vorhanden = Fokus-Wächter aus (`Tools/editor-focus.ps1 off/on`) |

Claude-Worktrees unter `Mining Game/.claude/worktrees/…` zählen zum Editor von `Mining Game`. Andere Ordner/Projekte mit `-Project`, anderer Queue-Ordner mit `-QueueDir` oder `EDITOR_QUEUE_DIR`.

## Ablauf für Chats

```bash
Tools/editor-queue.sh join -Who "Claude Code: Eisen-Kacheln" -Session "<Sitzungs-ID>" -Purpose "Play-Tests"
Tools/editor-queue.sh wait -Id <id> -TimeoutMinutes 9     # blockiert, bis man oben ist
#   ... Editor nutzen ...
Tools/editor-queue.sh heartbeat -Id <id>                  # nur ohne Claude-Code-Hook (Codex, Cowork): während des Tests jede Minute
Tools/editor-queue.sh leave -Id <id>                      # sofort danach
```

PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 <Befehl> …` mit denselben Argumenten.

- Nur der **oberste** Eintrag darf den Editor nutzen. Alle anderen warten.
- `join` gibt die Id aus (oder `-Id` selbst wählen). Erneutes `join` mit derselben Id frischt nur das Lebenszeichen auf.
- `wait` prüft alle 15 s (`-PollSeconds`), setzt dabei das eigene Lebenszeichen und räumt verwaiste Einträge ab. Exit-Codes: `0` oben, `2` Zeitlimit erreicht (Eintrag bleibt, erneut `wait`), `3` Eintrag nicht mehr vorhanden (neu `join`). Im Bash-Tool wegen des 10-Minuten-Limits `-TimeoutMinutes 9` verwenden oder im Hintergrund laufen lassen.
- `status` zeigt Warteschlange und Editorzustand.

## Testbank „Mining Game 2“ (Editor der Chats)

Der Editor von `Mining Game` gehört dem Benutzer. Chats testen im Editor von `Mining Game 2`, der **Testbank**: Dort ist kein fester Branch ausgecheckt, jeder Chat holt seinen eigenen Stand losgelöst (`git switch --detach`) hinein, testet und gibt sie wieder frei. Der Branch darf dabei im eigenen Arbeitsordner ausgecheckt bleiben.

```bash
git commit -am "WIP: Stand zum Testen"                                       # im eigenen Arbeitsordner
Tools/editor-queue.sh join -Project "Mining Game 2" -Who "Claude Code: <Aufgabe>" -Purpose "<Zweck>"
Tools/editor-queue.sh wait -Project "Mining Game 2" -Id <id> -TimeoutMinutes 9
Tools/editor-bench.sh checkout <branch> -Id <id> -Discard                    # nur als oberster Eintrag; -Discard verwirft Reste
unity command --project-path "C:\IT\GameDev\Git Repos\Mining Game 2" run_script --file Tests/<Name>Checks.cs
Tools/editor-bench.sh release                                                # -Discard verwirft Testspuren
Tools/editor-queue.sh leave -Project "Mining Game 2" -Id <id>
```

- `checkout` verweigert, wenn der Aufrufer nicht oben in der Warteschlange von `Mining Game 2` steht (Exit 4) oder die Testbank noch Änderungen hat (Exit 5); `-Discard` verwirft solche Reste vorher. Unity schreibt beim Laden selbst Importspuren (Sprite-IDs in `.meta`, neu erzeugtes `OreVeins/CopperRockTiles/Combined.asset`, Zeilenenden), die gelten als Testspuren. `status` zeigt Stand, Herkunft und Änderungen.
- `release` verweigert bei Änderungen (Exit 5): Gewollte Änderungen (z. B. Szenen) auf der Testbank committen, dann zeigt `release` den Befehl `git merge --ff-only <commit>` zum Übernehmen in den eigenen Branch; Testspuren mit `-Discard` verwerfen. Nicht versionierte Dateien (`Library`, `.mcp.json`) bleiben unberührt.
- Die Unity-Werkzeuge (MCP) eines Chats zeigen auf den Editor aus der nicht versionierten `.mcp.json` seines Ordners (heute in `Mining Game` noch auf den Editor des Benutzers). Deshalb vor MCP-Nutzung `editor_status` prüfen (`projectPath` muss `Mining Game 2` sein), sonst `unity command --project-path "…Mining Game 2"` verwenden.
- Zustand der Testbank: `Git Repos/EditorQueue/Mining Game 2.bench.json`.
- `checkout` und `release -Discard` schliessen vor jeder Dateiänderung die offene Szene im Testbank-Editor ohne Speichern (beenden dabei auch den Play-Mode, per `unity command eval_file`) und öffnen danach `Assets/Scenes/SampleScene.unity` frisch (mit `AssetDatabase.Refresh`, asynchron, weil Unity einzelne Befehle nach 5 s Hauptthread-Zeit abbricht; `checkout` wartet bis zu 3 min, bis die Szene offen ist). So erscheint kein Dialog „The open scene(s) have been modified externally“. Ist der Editor beschäftigt (Import, Kompilieren), wartet es und setzt dabei alle 30 s ein Lebenszeichen; rückt währenddessen jemand anderes nach oben, bricht es ab (Exit 4) und fasst den Editor nicht mehr an. Läuft der Editor nicht, geht es ohne; blockiert ihn ein unbekannter Dialog, bricht `checkout` mit Exit 6 ab (siehe unten). `-NoEditor` lässt den Editor ganz in Ruhe. `release` fasst den Editor nur als oberster Eintrag an.
- **Ton:** Der Editor der Testbank läuft immer stumm (`Assets/Editor/TestBenchAudioMute.cs` setzt „Mute Audio“, nur im Ordner `<Projekt> 2`; der Editor des Benutzers bleibt unberührt). Unity speichert „Mute Audio“ als gemeinsame EditorPrefs-Einstellung `AudioMasterMute` aller Editoren; das Skript schaltet nur den laufenden Testbank-Editor stumm und hält die gespeicherte Einstellung einige Sekunden auf ihrem vorherigen Wert, weil Unity sie verzögert schreibt. Nur für Audio-Tests als oberster Eintrag `Tools/editor-bench.sh audio-on -Id <id>` (oder Unity-Menü **Tools → Testbank → Ton an**), danach `audio-off`; `checkout` und `release` schalten ebenfalls wieder stumm. Schalter: `Git Repos/EditorQueue/Mining Game 2.audio-on`.

## Lebenszeichen: nur echte Editor-Nutzung zählt

Der Editor ist **nur fürs Testen** reserviert, nicht fürs Programmieren. Lebenszeichen entstehen deshalb nur durch tatsächliche Editor-Nutzung. `join` merkt sich die Claude-Code-Sitzung (`CLAUDE_CODE_SESSION_ID`). Der Hook in `.claude/settings.json` (PreToolUse/PostToolUse/PostToolUseFailure) ruft `Tools/editor-queue-hook.sh` nur bei Unity-Aufrufen auf (Unity-MCP-Werkzeuge, `unity command …`, `Tools/editor-bench`): vorher `heartbeat -Busy` (ein Aufruf läuft, der Inhaber wird bis 15 min nicht verdrängt, auch bei langen `run_script`), danach `heartbeat -Idle`. Lesen, Code schreiben oder Suchen setzen kein Lebenszeichen. Wer zwischen zwei Tests Code ändert, meldet sich mit `leave` ab und für den nächsten Test neu an; wer es vergisst, wird nach 4 Minuten ohne Editor-Nutzung verdrängt. Codex und Cowork haben diesen Hook nicht und rufen während eines Tests mindestens jede Minute `heartbeat` auf (lange Aufrufe mit `-Busy`/`-Idle` einrahmen).

## Benutzer

Am einfachsten im Chat sagen („trag mich für den Editor ein“); der Chat führt dann `join -User` aus. Der Benutzer kommt **direkt hinter den aktuellen Inhaber** (Position 2, bei leerer Liste Position 1): Wer gerade testet, wird nie hinausgeworfen, aber alle anderen Wartenden kommen danach. Alternativ im Unity-Menü **Tools → Editor-Warteschlange** („Als Benutzer anmelden“, „Lebenszeichen (Benutzer)“, „Als Benutzer abmelden“, „Status anzeigen“) oder im Terminal:

```bash
Tools/editor-queue.sh join -User -Purpose "selbst testen"
```

```bash
Tools/editor-queue.sh leave -User
```

Der Benutzereintrag hat die feste Id `benutzer`. Solange er wartet, wird er nie entfernt. Ist er oben, hält ihn jede Editor-Aktivität am Leben (Play-Mode, Auswahl, Änderungen, Speichern); erst nach 10 Minuten (`-UserStaleMinutes`, länger als bei Chats) ohne Lebenszeichen **und** ohne Editor-Aktivität rückt der Nächste nach.

## Reihenfolge ändern und um Freigabe bitten (Benutzer)

- **Umsortieren:** In PrivOrg die Wartenden per Drag & Drop (oder Pfeiltasten-Knöpfe) verschieben, bei allen Arbeitsordnern. Platz 1 (wer den Editor gerade nutzt) ist gesperrt und lässt sich weder verschieben noch überholen. Terminal: `Tools/editor-queue.sh move -Project "<Ordner>" -Id <id> -Position <n>` (n ≥ 2).
- **Bitte abschliessen:** Knopf beim Inhaber in PrivOrg bzw. `Tools/editor-queue.sh request-release -Project "<Ordner>"` (`-Cancel` nimmt die Bitte zurück; nicht für den Benutzer selbst). Der Inhaber hat dann **5 Minuten** (`-ReleaseMinutes`). Läuft bei Ablauf noch ein Test (Unity-Aufruf, Play-Mode, Kompilieren), gilt einmalig eine **letzte Frist von 2 Minuten** (`-FinalMinutes`), danach wird er entfernt, auch mitten im Test. Testet er nicht, wird er sofort bei Ablauf entfernt. Der Nächste rückt nach; der Chat kann sich hinten neu anmelden.
- **Benachrichtigung des Chats:** Der Claude-Code-Hook gibt nach Unity-/Bash-Aufrufen höchstens einmal pro Minute eine sachliche Zustandsmeldung mit Uhrzeit als Zusatzinfo an Claude (`hookSpecificOutput.additionalContext`): „Der Benutzer hat um Freigabe des Editors gebeten … endet um HH:MM Uhr“, später „… ist überfällig, letzte Frist … endet um HH:MM Uhr“. Wurde die Sitzung entfernt (wegen der Bitte oder fehlender Nutzung), meldet der Hook das einmal (`<Ordner>.evicted.json`, eine Stunde lang). Codex/Cowork sehen die Bitte als `HINWEIS:` in der Ausgabe von `heartbeat`. Abfrage für Skripte: `editor-queue.ps1 notice -Session <id>`.

## PrivOrg-Modul und lokaler Dienst

Im Admin-Tool PrivOrg zeigt das Modul **Editor-Warteschlange** (Dev/Owner) alle Arbeitsordner mit Inhaber, Zweck, Wartenden und Editorzustand; dort kann sich der Benutzer auch eintragen, ein Lebenszeichen senden und austragen. Die Daten liefert der lokale Dienst `Tools/editor-queue-server.ps1` unter `http://localhost:47615`; die Warteschlange verlässt den PC nicht.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue-server.ps1
```

- **Start mit dem ersten Editor:** `-Install` kopiert `editor-queue.ps1`, `editor-dialog.ps1`, `editor-focus.ps1` und den Dienst nach `Git Repos/EditorQueue/bin` (unabhängig von Arbeitsordnern und Testbank) und legt dort die Startverknüpfung `editor-queue-server.lnk` an (startet unsichtbar, mit `-ProjectRoot` auf `Mining Game`). Kein Windows-Autostart mehr (ein alter Eintrag wird entfernt): `Assets/Editor/EditorQueueActivity.cs` prüft in jedem offenen Unity Editor jede Minute, ob auf Port 47615 etwas antwortet, und öffnet sonst die Verknüpfung. So läuft der Dienst, sobald der erste Editor offen ist, und startet nach einem Absturz von selbst wieder; ein doppelter Start beendet sich, weil der Port belegt ist. Ist kein Editor offen, zeigt PrivOrg „Dienst nicht erreichbar“. Nach Änderungen an den Skripten `-Install` erneut ausführen; `-Uninstall` entfernt die Verknüpfung (dann startet der Dienst nicht mehr automatisch).
- **Gehostetes PrivOrg:** dessen Adresse freigeben, z. B. `-Install -AllowOrigin https://<projekt>.vercel.app` (mehrere mit Komma). `localhost`-Seiten sind immer erlaubt, alle anderen Herkünfte bekommen 403.
- **Schnittstelle:** `GET /api/queue` (alle Arbeitsordner, je `status -Json`), `POST /api/join|heartbeat|leave` mit `{ "project": "<Ordner>", "purpose": "…" }`, immer als Benutzer (`-User`); `POST /api/move` mit `{ "project", "id", "position" }` (409 für Platz 1) und `POST /api/request-release` mit `{ "project", "cancel": false|true }`. Arbeitsordner sind der Projektordner, Geschwister `<Projekt> <n>` mit `ProjectSettings` und alle mit Warteschlange/Aktivitätssignal.
- **Code-Stand je Editor** (alle Arbeitsordner, auch bei geschlossenem Editor): `status` zeigt „Code: …“, `status -Json` liefert `code = { folder, branch, ref, testbench, sha, subject, remote, behind, ahead, changed, current, text, fetched, checked }`. Verglichen wird der Ordner `Mining Game` mit `origin/main`, die Testbank mit dem geholten Branch (`<Ordner>.bench.json`, ab dem geholten Commit) und andere Ordner mit `origin/<Branch>`. `current` ist `true` (aktuell), `false` (neue Commits auf GitHub) oder `null` (losgelöst bzw. nur lokal). Texte z. B. „main (69c7482), aktuell“, „Testbank: claude/x (2bb54f1), 1 neuer Commit auf origin/claude/x“, „claude/y (d2ed911), aktuell, 1 eigener Commit nicht auf GitHub“, dazu „N Dateien lokal geändert“. Zwischengespeichert in `<Ordner>.code.json` (30 s), `git fetch` des Branches höchstens alle 3 min.
- **Stand gegenüber main** (nur Ordner `Mining Game`, siehe `Docs/Main-Auto-Update.md`): `status -Json` liefert zusätzlich `main = { state, text, behind, checked, current }` vom offenen Editor (`state`: `aktuell`, `veraltet`, `ausgesetzt`, `aus`; `null` bei anderen Ordnern oder geschlossenem Editor); er sagt genauer, wann geholt wird (z. B. „nach dem Play-Stopp“).
- `editor.text` endet mit „| Stand: …“ (bei veraltetem `main` dessen Text, sonst der Code-Stand), so zeigt das PrivOrg-Modul den Stand schon ohne eigene Anpassung. Für eine eigene Anzeige (z. B. grüner/gelber Punkt je Editor) `code.current` und `code.text` verwenden.
- Doku auf PrivOrg-Seite: `docs/editor-queue.md` im PrivOrg-Repository.
- **Dialog-Wächter:** Der Dienst prüft alle 3 s (`-DialogWatchSeconds`, 0 = aus) die Dialoge des Testbank-Editors, siehe nächster Abschnitt.
- **Fokus-Wächter:** Der Dienst holt das Fenster des Benutzers zurück, wenn sich die Testbank selbst nach vorne holt, siehe „Testbank auf eigenem Desktop“.

## Dialoge im Testbank-Editor

Ein modaler Unity-Dialog (z. B. „The open scene(s) have been modified externally“ nach einem Git-Wechsel) hält den ganzen Editor an: `unity command`, MCP und Play-Mode-Tests laufen dann in eine Zeitüberschreitung. `Tools/editor-dialog.sh` (PowerShell: `Tools/editor-dialog.ps1`) erkennt solche Dialoge über Windows (Fenster des Unity-Prozesses der Testbank mit Titel, Text und Knöpfen) und klickt Knöpfe per `BM_CLICK`. Es arbeitet nur mit dem Editor der Testbank (`<Projekt> 2`, Prozess über das Aktivitätssignal bzw. `-projectpath`), nie mit dem Editor des Benutzers.

```bash
Tools/editor-dialog.sh list                      # offene Dialoge: Titel, Text, Knöpfe; Exit 6 = Dialog offen, 3 = Editor läuft nicht
Tools/editor-dialog.sh press "Reload"            # Knopf klicken (& und Gross/Klein egal); bei mehreren Dialogen -Title "<Teil>"
Tools/editor-dialog.sh watch -Once               # bekannte Dialoge sofort beantworten (wie der Wächter)
Tools/editor-dialog.sh list -All                 # alle sichtbaren Fenster des Editors (Fehlersuche)
```

**Regel für Chats:** Antwortet der Testbank-Editor nicht (MCP- oder `unity command`-Zeitüberschreitung), zuerst `list` prüfen. Bekannte Dialoge (Tabelle) mit `press` schliessen, unbekannte nicht raten, sondern dem Benutzer melden (Titel, Text, Knöpfe). `press` und automatische Antworten stehen im Protokoll `<Ordner>.log`.

**Wächter:** Bekannte Dialoge beantwortet er automatisch, sobald sie 2 s offen sind (ein Chat kann sie vorher selbst schliessen); unbekannte klickt er nie, sondern trägt sie einmal ins Protokoll ein. Er läuft im lokalen Dienst (`editor-queue-server.ps1`, nach Änderungen `-Install` erneut ausführen und Dienst neu starten) und zusätzlich bei jedem Durchgang eines wartenden `wait` für die Testbank, also auch ohne Dienst.

| Dialog (Titel/Text enthält) | Antwort des Wächters | Grund |
| --- | --- | --- |
| „modified externally“ | Reload | Szene wurde durch Git geändert, neu laden |
| „Scene(s) Have Been Modified“ / „save the changes you made in the scene“ | Don't Save | die Testbank speichert keine Szenen |
| „Safe Mode“ (Compilefehler beim Laden) | Ignore | Editor normal öffnen, Fehler stehen in der Konsole |
| „API Update Required“ / „Script Updating Consent“ | No Thanks | Code eines Branches nicht automatisch umschreiben |
| „Low disk space“ (beim Start des Play-Mode) | OK | reiner Hinweis; steht im Protokoll, Benutzer auf knappen Speicherplatz hinweisen |
| „Opening Project in Non-Matching Editor“ | keine (nur melden) | Editor-Version passt nicht: Benutzer fragen |

- **Status:** Offene Dialoge stehen in `<Ordner>.dialogs.json`. Solange dort (höchstens 2 min alt) ein Dialog offen ist, gilt der Editor als **in Benutzung**: Niemand wird wegen eines Dialogs verdrängt. `editor-queue status` zeigt ihn als Editorzustand („Dialog offen …: '<Titel>' [Knöpfe] – …“), wartende `wait` melden ihn einmal mit „Editor blockiert: …“, PrivOrg sieht ihn im Editortext.
- Fortschrittsfenster (Importieren, „Hold on“) gelten nicht als Dialog und werden nie geklickt.
- Prüfen ohne Unity: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorDialogChecks.ps1` (Testfenster ausserhalb des Bildschirms ohne Fokus, Queue-Ordner `Temp/EditorDialogTest`).

## Testbank auf eigenem Desktop: Fokus-Wächter

Liegt der Testbank-Editor auf einem zweiten virtuellen Windows-Desktop, springt Windows auf diesen Desktop, sobald sich die Testbank selbst nach vorne holt (z. B. Play-Start eines Chats). Zwei Gegenmittel:

- **An der Quelle:** `Assets/Editor/TestBenchPlayUnfocused.cs` stellt im Testbank-Editor (nur `<Projekt> 2`) alle Game-Fenster auf „Play Unfocused“: Der Play-Mode startet, ohne dass Unity das Game-Fenster nach vorne holt. Der Editor des Benutzers bleibt unberührt. Unsere Werkzeuge (`editor-bench`, `unity command`, `editor-dialog`) holen das Fenster nie selbst nach vorne.
- **Fokus-Wächter** (`Tools/editor-focus.ps1`, läuft im lokalen Dienst als eigener Thread): Er reagiert sofort auf jeden Fensterwechsel (`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, kein Abfragen). Kommt ein Fenster des Testbank-Editors (Prozess über das Aktivitätssignal bzw. `-projectpath`) nach vorne, ohne dass der Benutzer es wollte, aktiviert er sofort wieder das zuletzt benutzte Fenster; Windows wechselt dabei auf dessen Desktop zurück. Das Testbank-Fenster wird nie minimiert.
  - **Absicht des Benutzers** (dann bleibt die Testbank vorne): Mausklick in den letzten 1,5 s auf ein Testbank-Fenster oder auf Taskleiste/Task-Ansicht/Startmenü, oder in den letzten 2,5 s eine Wechsel-Taste (Win…, z. B. Win+Strg+Pfeil oder Win+Tab, Alt+Tab, Alt+Esc). Der Wächter liest dazu nur, wann und wohin geklickt bzw. ob eine solche Taste gedrückt wurde (Low-Level-Hooks), keine Texteingaben.
  - Windows erlaubt Hintergrundprozessen das Aktivieren nur eingeschränkt; der Wächter versucht es direkt, dann mit `AttachThreadInput`, zuletzt mit einem kurzen Alt-Tastendruck (mit unbelegter Zwischentaste, damit kein Menü aufgeht). Holt sich die Testbank den Fokus mehr als 6-mal in 20 s, pausiert er 60 s.
  - **Protokoll:** Jeder Eingriff steht mit `FOKUS` in `<Ordner>.log` (Testbank), z. B. „Testbank kam von selbst nach vorne ('Game' (Unity)), zurück zu '…' (direkt, 3 ms, Desktop des Benutzers aktiv)“ oder „Testbank bewusst geöffnet (Klick auf die Testbank vor 120 ms), bleibt vorne“.
  - **Ein-/Ausschalten:** `Tools/editor-focus.ps1 off` (legt `Git Repos/EditorQueue/Mining Game 2.focus-guard-off` an, wirkt sofort) bzw. `on`; `status` zeigt Zustand und letzte Eingriffe. Dienst ganz ohne Wächter: `-NoFocusGuard`.
  - Ohne Dienst bzw. zur Fehlersuche: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-focus.ps1 watch -Trace` (zeigt jeden Fensterwechsel; `-DryRun` nur beobachten). Läuft der Wächter schon im Dienst, verweigert `watch` (Exit 4).

## Verdrängung verwaister Belegungen

Bei jedem Aufruf (praktisch: durch wartende `wait`-Befehle und alle 5 s durch den lokalen Dienst, solange PrivOrg das Modul zeigt) wird geprüft:

- **Bitte des Benutzers:** siehe oben (5 Minuten, bei laufendem Test einmalig 2 Minuten mehr, dann immer).
- **Inhaber (oben):** verdrängt, wenn sein letztes Lebenszeichen und sein Nachrücken älter als 4 Minuten sind (`-StaleMinutes`, Benutzer 10 Minuten) **und** der Editor objektiv ungenutzt ist **und** kein Unity-Aufruf läuft (`heartbeat -Busy`, höchstens 15 min). Wer frisch nach oben rückt, hat also immer volle 4 Minuten zum Loslegen.
- **Editor gilt als genutzt** (nie verdrängen), wenn das Aktivitätssignal frisch ist (≤ 5 min alt; im Hintergrund schreibt Unity gedrosselt nur etwa alle 30–90 s) und Play-Mode läuft, kompiliert wird oder die letzte Aktivität jünger als die Frist (4 bzw. 10 Minuten) ist; oder wenn der Editor-Prozess läuft, aber seit höchstens 30 Minuten kein Signal mehr schreibt (blockiert/beschäftigt).
- **Editor gilt als ungenutzt**, wenn er geschlossen ist, länger ruhig war oder kein Aktivitätssignal hat (z. B. Branch ohne `EditorQueueActivity.cs`); dann entscheidet allein das Lebenszeichen.
- **Wartende Chats** ohne Lebenszeichen seit 4 Minuten fallen weg (ihr `wait` setzt es laufend).

Jede Verdrängung steht mit Grund in `<Ordner>.log`.

## Aktivitätssignal im Editor

`Assets/Editor/EditorQueueActivity.cs` (nur im Editor, nicht im Batchmode/CI) schreibt alle 30 s sowie nach Ereignissen (höchstens alle 5 s) `<Ordner>.activity.json`: Zeitpunkt, letzte Aktivität mit Ereignis, Play-Mode, Pause, Kompilieren, Prozess-Id, beim Beenden `closed`. Als Aktivität zählen Play-Mode-/Pause-Wechsel, laufende Play-Session, Kompilierung/Import, Szene geöffnet/geändert/gespeichert, Undo/Redo, Eigenschaftsänderungen, Auswahlwechsel und Projektänderungen. `run_script`-Aufrufe ohne solche Ereignisse zählen nicht: dafür `heartbeat`.

## Prüfen

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorQueueChecks.ps1
```

Läuft ohne Editor in `Temp/EditorQueueTest` (nie im echten Queue-Ordner): gleichzeitige Anmeldungen, `wait`/Nachrücken, Verdrängung bei veraltetem Lebenszeichen, Schutz durch Play-Mode und frische Aktivität, Benutzer an Position 2, Hook-Lebenszeichen per Sitzung, Sperre, `status -Json` und der lokale Dienst (Lesen, An-/Abmelden, Herkunftsprüfung, Preflight).
