# Editor-Warteschlange

Damit sich Chats (Claude Code, Codex, Cowork) und der Benutzer beim Testen nicht in die Quere kommen, nutzt jeden Unity Editor immer nur einer. Der Editor von `Mining Game` gehört dem Benutzer. Chats testen auf den **Testbänken** `Mining Game 2` und `Mining Game 3` (ohne festen Branch, Testbank 2 auf Desktop 2, Testbank 3 auf Desktop 3). Die Warteschlangen liegen ausserhalb von Git unter `C:\IT\GameDev\Git Repos\EditorQueue\`.

Einzelheiten (Dateien, Fristen, Verdrängung, Benutzer-Funktionen, lokaler Dienst/PrivOrg, Dialog- und Fokus-Wächter, neue Testbank): `Docs/Editor-Warteschlange-Technik.md`, nur bei Bedarf gezielt nachlesen.

## Ablauf für Chats

**Immer bei der gemeinsamen Warteschlange `Testbank` anmelden**, nie direkt bei einer Testbank. Sie teilt automatisch die nächste freie Testbank zu, so verteilen sich die Chats auf beide Editoren.

```bash
git commit -am "WIP: Stand zum Testen"                                       # im eigenen Arbeitsordner
Tools/editor-queue.sh join -Project Testbank -Who "Claude Code: <Aufgabe>" -Purpose "<Zweck>"
Tools/editor-queue.sh wait -Project Testbank -Id <id> -TimeoutMinutes 9      # bis "Du bist oben: Testbank '<Ordner>'" und "TESTBANK=<Ordner>"
Tools/editor-bench.sh checkout <branch> -Id <id> -Discard                    # findet die zugeteilte Testbank selbst
unity command --project-path "C:\IT\GameDev\Git Repos\<Ordner>" run_script --file Tests/<Name>Checks.cs
Tools/editor-bench.sh release -Id <id>                                       # -Discard verwirft Testspuren
Tools/editor-queue.sh leave -Project Testbank -Id <id>
```

PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 <Befehl> …` mit denselben Argumenten.

- Nur der **oberste** Eintrag nutzt den Editor. `wait` endet mit Exit `0` (oben), `2` (Zeitlimit, erneut `wait`) oder `3` (Eintrag weg, neu `join`). Wegen des 10-Minuten-Limits im Bash-Tool `-TimeoutMinutes 9`.
- `checkout` holt den eigenen Stand losgelöst auf die Testbank (der Branch bleibt im eigenen Ordner ausgecheckt), schliesst vorher die offene Szene ohne Speichern und öffnet `SampleScene` frisch. Exit `4`: nicht oben; Exit `5`: Reste auf der Testbank (`-Discard` verwirft sie); Exit `6`: unbekannter Dialog. Einen nur remote vorhandenen Branch als `origin/<branch>` angeben.
- `release` verweigert bei Änderungen (Exit `5`): Gewollte Änderungen (z. B. Szenen) auf der Testbank committen, `release` zeigt dann `git merge --ff-only <commit>` zum Übernehmen; Testspuren (Importspuren in `.meta`, Zeilenenden) mit `-Discard` verwerfen.
- `editor-bench` wählt die Testbank selbst (über `-Id`); sonst `-Bench "C:\IT\GameDev\Git Repos\<Ordner>"` mit vollem Pfad. `status` zeigt alle Testbänke.
- **Unity-Werkzeuge (MCP)** zeigen auf den Editor aus der `.mcp.json` des eigenen Ordners. Bei Testbank-Tests deshalb `unity command --project-path "…<zugeteilte Testbank>"`; MCP nur, wenn `editor_status` genau die zugeteilte Testbank als `projectPath` meldet.
- **Ton:** Testbänke laufen stumm. Nur für Audio-Tests als oberster Eintrag `Tools/editor-bench.sh audio-on -Id <id>`, danach `audio-off` (`checkout`/`release` schalten ebenfalls stumm). Klangprüfungen über `AudioSource.isPlaying`/`volume` gehen auch stumm.

## Nur testen, nicht programmieren

Der Editor ist nur fürs Testen reserviert. Wer 4 Minuten lang weder Unity aufruft noch Editor-Aktivität (Play-Mode, Kompilieren) erzeugt, wird verdrängt. Zwischen zwei Tests Code ändern heisst: `leave`, danach neu anmelden. Claude Code meldet Unity-Aufrufe automatisch per Hook (`.claude/settings.json`; ein laufender Aufruf schützt bis 15 min). Codex und Cowork rufen während eines Tests mindestens jede Minute `heartbeat -Id <id>` auf (lange Aufrufe mit `heartbeat -Busy` / `-Idle` einrahmen).

**Bitte des Benutzers um Freigabe:** Meldet der Hook bzw. `heartbeat` „Der Benutzer hat um Freigabe des Editors gebeten … endet um HH:MM Uhr“, den laufenden Test abschliessen, `release` und `leave` bis zu dieser Zeit. Danach gilt bei laufendem Test einmalig eine letzte Frist von 2 Minuten, dann wird der Chat entfernt. Wird der Editor noch gebraucht, hinten neu anmelden.

**Benutzer will einen Editor** (sagt es im Chat): `Tools/editor-queue.sh join -User -Project "<Ordner>" -Purpose "<Zweck>"`. Er kommt direkt hinter den aktuellen Inhaber, der nicht verdrängt wird.

## Editor antwortet nicht: Dialoge

Ein modaler Unity-Dialog hält den ganzen Testbank-Editor an (`unity command`, MCP und Play-Tests laufen in eine Zeitüberschreitung). Dann zuerst `Tools/editor-dialog.sh list` (Testbank 3: `-Project "Mining Game 3"`). Bekannte Dialoge schliessen, unbekannte nicht raten, sondern dem Benutzer melden (Titel, Text, Knöpfe). Nie beim Editor des Benutzers.

| Dialog (Titel/Text enthält) | Knopf |
| --- | --- |
| „modified externally“ | Reload |
| „Scene(s) Have Been Modified“ / „save the changes you made in the scene“ | Don't Save |
| „Safe Mode“ | Ignore |
| „API Update Required“ / „Script Updating Consent“ | No Thanks |
| „Low disk space“ | OK (Benutzer auf knappen Speicher hinweisen) |
| „Opening Project in Non-Matching Editor“ | keiner, Benutzer fragen |

`Tools/editor-dialog.sh press "<Knopf>"` (bei mehreren Dialogen `-Title "<Teil>"`). Ein Wächter beantwortet bekannte Dialoge nach 2 s auch selbst.

## Tests kürzer machen

- **Gezielte Prüfskripte statt vollem Lauf:** pro Aufgabe nur die betroffenen `Tests/<Name>Checks.cs` laufen lassen; mehrere Skripte direkt nacheinander in derselben Belegung, statt sich für jedes neu anzumelden.
- **Edit-Mode, wo möglich:** Logik-, Daten- und Layout-Prüfungen ohne Play-Start schreiben (kein MainMenu, kein Laden der Welt); Play-Skripte nur für Bewegung, Physik und Spielablauf. Play-Prüfskripte erst nach Ende des Ladebildschirms starten (er blockiert die Eingabe).
- **Erst fertig, dann testen:** Alles kompilierfähig committen, dann anmelden. Der grösste Teil der Wartezeit ist Import und Kompilieren nach `checkout`; jeder Zwischenstand kostet das erneut.
- **Kleinere Spielstarts:** Play-Skripte mit kleiner Testwelt bzw. festem Seed und isoliertem Profil starten statt der vollen Welt. Nie „Neues Spiel“ in den echten Spielständen anlegen (`AppData\LocalLow\…\Tiefenhall\Saves` ist für alle Editoren gemeinsam); eigenen Testordner nutzen bzw. danach löschen.
