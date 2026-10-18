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
Tools/editor-queue.sh heartbeat -Id <id>                  # nur ohne Claude-Code-Hook (Codex, Cowork): mind. alle 8 min
Tools/editor-queue.sh leave -Id <id>                      # sofort danach
```

PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 <Befehl> …` mit denselben Argumenten.

- Nur der **oberste** Eintrag darf den Editor nutzen. Alle anderen warten.
- `join` gibt die Id aus (oder `-Id` selbst wählen). Erneutes `join` mit derselben Id frischt nur das Lebenszeichen auf.
- `wait` prüft alle 15 s (`-PollSeconds`), setzt dabei das eigene Lebenszeichen und räumt verwaiste Einträge ab. Exit-Codes: `0` oben, `2` Zeitlimit erreicht (Eintrag bleibt, erneut `wait`), `3` Eintrag nicht mehr vorhanden (neu `join`). Im Bash-Tool wegen des 10-Minuten-Limits `-TimeoutMinutes 9` verwenden oder im Hintergrund laufen lassen.
- `status` zeigt Warteschlange und Editorzustand.

## Automatisches Lebenszeichen (Claude Code)

`join` merkt sich die Claude-Code-Sitzung (`CLAUDE_CODE_SESSION_ID`). Der Hook in `.claude/settings.json` ruft nach jedem Werkzeugaufruf und jeder Benutzernachricht `Tools/editor-queue-hook.sh` auf: Steht die Sitzung in einer Warteschlange, setzt er höchstens einmal pro Minute ein Lebenszeichen (`heartbeat -Session`), sonst endet er sofort (etwa 0,3 s). Ein Chat, der 15 Minuten testet, bleibt so von selbst am Leben; stürzt er ab oder vergisst er `leave`, hören die Lebenszeichen auf und nach 10 ruhigen Minuten rückt der Nächste nach. Codex und Cowork haben diesen Hook nicht und rufen `heartbeat` selbst auf.

## Benutzer

Am einfachsten im Chat sagen („trag mich für den Editor ein“); der Chat führt dann `join -User` aus. Der Benutzer kommt **direkt hinter den aktuellen Inhaber** (Position 2, bei leerer Liste Position 1): Wer gerade testet, wird nie hinausgeworfen, aber alle anderen Wartenden kommen danach. Alternativ im Unity-Menü **Tools → Editor-Warteschlange** („Als Benutzer anmelden“, „Lebenszeichen (Benutzer)“, „Als Benutzer abmelden“, „Status anzeigen“) oder im Terminal:

```bash
Tools/editor-queue.sh join -User -Purpose "selbst testen"
```

```bash
Tools/editor-queue.sh leave -User
```

Der Benutzereintrag hat die feste Id `benutzer`. Solange er wartet, wird er nie entfernt. Ist er oben, hält ihn jede Editor-Aktivität am Leben (Play-Mode, Auswahl, Änderungen, Speichern); erst nach 10 Minuten ohne Lebenszeichen **und** ohne Editor-Aktivität rückt der Nächste nach.

## PrivOrg-Modul und lokaler Dienst

Im Admin-Tool PrivOrg zeigt das Modul **Editor-Warteschlange** (Dev/Owner) alle Arbeitsordner mit Inhaber, Zweck, Wartenden und Editorzustand; dort kann sich der Benutzer auch eintragen, ein Lebenszeichen senden und austragen. Die Daten liefert der lokale Dienst `Tools/editor-queue-server.ps1` unter `http://localhost:47615`; die Warteschlange verlässt den PC nicht.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue-server.ps1
```

- **Autostart:** einmal im Ordner `Mining Game` mit `-Install` (Verknüpfung im Windows-Autostart, startet unsichtbar), entfernen mit `-Uninstall`.
- **Gehostetes PrivOrg:** dessen Adresse freigeben, z. B. `-Install -AllowOrigin https://<projekt>.vercel.app` (mehrere mit Komma). `localhost`-Seiten sind immer erlaubt, alle anderen Herkünfte bekommen 403.
- **Schnittstelle:** `GET /api/queue` (alle Arbeitsordner, je `status -Json`), `POST /api/join|heartbeat|leave` mit `{ "project": "<Ordner>", "purpose": "…" }`, immer als Benutzer (`-User`). Arbeitsordner sind der Projektordner, Geschwister `<Projekt> <n>` mit `ProjectSettings` und alle mit Warteschlange/Aktivitätssignal.
- Doku auf PrivOrg-Seite: `docs/editor-queue.md` im PrivOrg-Repository.

## Verdrängung verwaister Belegungen

Bei jedem Aufruf (praktisch: durch wartende `wait`-Befehle) wird geprüft:

- **Inhaber (oben):** verdrängt, wenn sein letztes Lebenszeichen und sein Nachrücken älter als 10 Minuten sind (`-StaleMinutes`) **und** der Editor objektiv ungenutzt ist. Wer frisch nach oben rückt, hat also immer volle 10 Minuten.
- **Editor gilt als genutzt** (nie verdrängen), wenn das Aktivitätssignal frisch ist (≤ 5 min alt; im Hintergrund schreibt Unity gedrosselt nur etwa alle 30–90 s) und Play-Mode läuft, kompiliert wird oder die letzte Aktivität jünger als 10 Minuten ist; oder wenn der Editor-Prozess läuft, aber seit höchstens 30 Minuten kein Signal mehr schreibt (blockiert/beschäftigt).
- **Editor gilt als ungenutzt**, wenn er geschlossen ist, länger ruhig war oder kein Aktivitätssignal hat (z. B. Branch ohne `EditorQueueActivity.cs`); dann entscheidet allein das Lebenszeichen.
- **Wartende Chats** ohne Lebenszeichen seit 10 Minuten fallen weg (ihr `wait` setzt es laufend).

Jede Verdrängung steht mit Grund in `<Ordner>.log`.

## Aktivitätssignal im Editor

`Assets/Editor/EditorQueueActivity.cs` (nur im Editor, nicht im Batchmode/CI) schreibt alle 30 s sowie nach Ereignissen (höchstens alle 5 s) `<Ordner>.activity.json`: Zeitpunkt, letzte Aktivität mit Ereignis, Play-Mode, Pause, Kompilieren, Prozess-Id, beim Beenden `closed`. Als Aktivität zählen Play-Mode-/Pause-Wechsel, laufende Play-Session, Kompilierung/Import, Szene geöffnet/geändert/gespeichert, Undo/Redo, Eigenschaftsänderungen, Auswahlwechsel und Projektänderungen. `run_script`-Aufrufe ohne solche Ereignisse zählen nicht: dafür `heartbeat`.

## Prüfen

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorQueueChecks.ps1
```

Läuft ohne Editor in `Temp/EditorQueueTest` (nie im echten Queue-Ordner): gleichzeitige Anmeldungen, `wait`/Nachrücken, Verdrängung bei veraltetem Lebenszeichen, Schutz durch Play-Mode und frische Aktivität, Benutzer an Position 2, Hook-Lebenszeichen per Sitzung, Sperre, `status -Json` und der lokale Dienst (Lesen, An-/Abmelden, Herkunftsprüfung, Preflight).
