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
- **Ton:** Der Editor der Testbank läuft immer stumm (`Assets/Editor/TestBenchAudioMute.cs` setzt „Mute Audio“, nur im Ordner `<Projekt> 2`; der Editor des Benutzers bleibt unberührt). Nur für Audio-Tests als oberster Eintrag `Tools/editor-bench.sh audio-on -Id <id>` (oder Unity-Menü **Tools → Testbank → Ton an**), danach `audio-off`; `checkout` und `release` schalten ebenfalls wieder stumm. Schalter: `Git Repos/EditorQueue/Mining Game 2.audio-on`.

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

- **Autostart:** `-Install` kopiert `editor-queue.ps1` und den Dienst nach `Git Repos/EditorQueue/bin` (unabhängig von Arbeitsordnern und Testbank) und legt eine Verknüpfung in den Windows-Autostart (startet unsichtbar, mit `-ProjectRoot` auf `Mining Game`). Nach Änderungen an den Skripten `-Install` erneut ausführen; entfernen mit `-Uninstall`.
- **Gehostetes PrivOrg:** dessen Adresse freigeben, z. B. `-Install -AllowOrigin https://<projekt>.vercel.app` (mehrere mit Komma). `localhost`-Seiten sind immer erlaubt, alle anderen Herkünfte bekommen 403.
- **Schnittstelle:** `GET /api/queue` (alle Arbeitsordner, je `status -Json`), `POST /api/join|heartbeat|leave` mit `{ "project": "<Ordner>", "purpose": "…" }`, immer als Benutzer (`-User`); `POST /api/move` mit `{ "project", "id", "position" }` (409 für Platz 1) und `POST /api/request-release` mit `{ "project", "cancel": false|true }`. Arbeitsordner sind der Projektordner, Geschwister `<Projekt> <n>` mit `ProjectSettings` und alle mit Warteschlange/Aktivitätssignal.
- Doku auf PrivOrg-Seite: `docs/editor-queue.md` im PrivOrg-Repository.

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
