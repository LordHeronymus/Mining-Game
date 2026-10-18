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
Tools/editor-queue.sh heartbeat -Id <id>                  # bei langen Arbeiten ohne Editor-Aktivität, mind. alle 4 min
Tools/editor-queue.sh leave -Id <id>                      # sofort danach
```

PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 <Befehl> …` mit denselben Argumenten.

- Nur der **oberste** Eintrag darf den Editor nutzen. Alle anderen warten.
- `join` gibt die Id aus (oder `-Id` selbst wählen). Erneutes `join` mit derselben Id frischt nur das Lebenszeichen auf.
- `wait` prüft alle 15 s (`-PollSeconds`), setzt dabei das eigene Lebenszeichen und räumt verwaiste Einträge ab. Exit-Codes: `0` oben, `2` Zeitlimit erreicht (Eintrag bleibt, erneut `wait`), `3` Eintrag nicht mehr vorhanden (neu `join`). Im Bash-Tool wegen des 10-Minuten-Limits `-TimeoutMinutes 9` verwenden oder im Hintergrund laufen lassen.
- `status` zeigt Warteschlange und Editorzustand.

## Benutzer

Im Unity-Menü **Tools → Editor-Warteschlange**: „Als Benutzer anmelden“, „Lebenszeichen (Benutzer)“, „Als Benutzer abmelden“, „Status anzeigen“. Oder im Terminal:

```bash
Tools/editor-queue.sh join -User -Purpose "selbst testen"
```

```bash
Tools/editor-queue.sh leave -User
```

Der Benutzereintrag hat die feste Id `benutzer`. Solange er wartet, wird er nie entfernt. Ist er oben, hält ihn jede Editor-Aktivität am Leben (Play-Mode, Auswahl, Änderungen, Speichern); erst nach 5 Minuten ohne Lebenszeichen **und** ohne Editor-Aktivität rückt der Nächste nach.

## Verdrängung verwaister Belegungen

Bei jedem Aufruf (praktisch: durch wartende `wait`-Befehle) wird geprüft:

- **Inhaber (oben):** verdrängt, wenn sein letztes Lebenszeichen und sein Nachrücken älter als 5 Minuten sind (`-StaleMinutes`) **und** der Editor objektiv ungenutzt ist. Wer frisch nach oben rückt, hat also immer volle 5 Minuten.
- **Editor gilt als genutzt** (nie verdrängen), wenn das Aktivitätssignal frisch ist (≤ 2 min alt) und Play-Mode läuft, kompiliert wird oder die letzte Aktivität jünger als 5 Minuten ist; oder wenn der Editor-Prozess läuft, aber seit höchstens 30 Minuten kein Signal mehr schreibt (blockiert/beschäftigt).
- **Editor gilt als ungenutzt**, wenn er geschlossen ist, länger ruhig war oder kein Aktivitätssignal hat (z. B. Branch ohne `EditorQueueActivity.cs`); dann entscheidet allein das Lebenszeichen.
- **Wartende Chats** ohne Lebenszeichen seit 5 Minuten fallen weg (ihr `wait` setzt es laufend).

Jede Verdrängung steht mit Grund in `<Ordner>.log`.

## Aktivitätssignal im Editor

`Assets/Editor/EditorQueueActivity.cs` (nur im Editor, nicht im Batchmode/CI) schreibt alle 30 s sowie nach Ereignissen (höchstens alle 5 s) `<Ordner>.activity.json`: Zeitpunkt, letzte Aktivität mit Ereignis, Play-Mode, Pause, Kompilieren, Prozess-Id, beim Beenden `closed`. Als Aktivität zählen Play-Mode-/Pause-Wechsel, laufende Play-Session, Kompilierung/Import, Szene geöffnet/geändert/gespeichert, Undo/Redo, Eigenschaftsänderungen, Auswahlwechsel und Projektänderungen. `run_script`-Aufrufe ohne solche Ereignisse zählen nicht: dafür `heartbeat`.

## Prüfen

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorQueueChecks.ps1
```

Läuft ohne Editor in `Temp/EditorQueueTest` (nie im echten Queue-Ordner): gleichzeitige Anmeldungen, `wait`/Nachrücken, Verdrängung bei veraltetem Lebenszeichen, Schutz durch Play-Mode und frische Aktivität, Benutzereintrag, Sperre.
