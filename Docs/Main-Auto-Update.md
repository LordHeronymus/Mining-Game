# main automatisch aktualisieren

`Assets/Editor/MainAutoUpdate.cs` hält den Ordner des Benutzers (`Git Repos/Mining Game`) auf dem neuesten `origin/main`. Das Skript ist nur in diesem Ordner aktiv, nicht in der Testbank `Mining Game 2` und nicht in Worktrees.

- Jede Minute (erstmals 20 s nach dem Laden): `git fetch origin main`, dann `git merge --ff-only origin/main` und `AssetDatabase.Refresh()`.
- Ausgesetzt (Grund steht einmal in der Konsole), solange: ein anderer Branch als `main` ausgecheckt ist (still), Play-Mode oder Kompilierung läuft, eine offene Szene ungespeichert ist oder das lokale `main` eigene Commits hat.
- **Während Play-Mode/Kompilierung** wird trotzdem jede Minute nachgesehen (nur `git fetch`, nichts geholt). Gibt es neue Commits, erscheint im Game-Fenster der Hinweis „Neuer Stand auf main (N Commits) – Play beenden zum Aktualisieren“ (bei gleichem Stand höchstens alle 2 min).
- **Nach dem Play-Stopp** wird nach 2 s geprüft und geholt, nicht erst zur nächsten vollen Minute.
- **Stand in der Editor-Warteschlange:** Der Stand („main aktuell“, „N neue Commits auf main, wird nach dem Play-Stopp geholt“, „…, nicht geholt: Szene X ist nicht gespeichert“ usw.) steht in der Aktivitätsdatei (`mainState`, `mainText`, `mainBehind`, `mainChecked`, geschrieben von `EditorQueueActivity`). `editor-queue status` zeigt ihn als Zeile „Stand: …“, `status -Json` als Objekt `main` und zusätzlich im Editortext, so sieht ihn das PrivOrg-Modul direkt. Er überlebt das Neuladen der Skripte (SessionState).
- Auf aktuellem `main` frischt der Editor die Skriptkopien in `Git Repos/EditorQueue/bin` selbst auf, wenn sich `Tools/editor-queue.ps1`, `editor-dialog.ps1` oder `editor-queue-server.ps1` geändert haben (ein erneutes `-Install` ist dafür nicht mehr nötig; Änderungen am Dienst selbst wirken nach seinem nächsten Start).
- Lokal geänderte Dateien (z. B. Schriften, GpsProfile, Szene) blockieren nicht. Ändern die neuen Commits genau eine dieser Dateien, bricht Git den Fast-Forward ab, und der Grund steht in der Konsole. Es wird nie etwas überschrieben oder verworfen.
- Menü: **Tools → main automatisch aktualisieren** (an/aus, pro Rechner in den EditorPrefs, Standard an) und **Tools → main jetzt aktualisieren** (sofort prüfen, Ergebnis in der Konsole).
