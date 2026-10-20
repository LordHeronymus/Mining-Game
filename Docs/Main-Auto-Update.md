# main automatisch aktualisieren

`Assets/Editor/MainAutoUpdate.cs` hält den Ordner des Benutzers (`Git Repos/Mining Game`) auf dem neuesten `origin/main`. Das Skript ist nur in diesem Ordner aktiv, nicht in der Testbank `Mining Game 2` und nicht in Worktrees.

- Jede Minute (erstmals 20 s nach dem Laden): `git fetch origin main`, dann `git merge --ff-only origin/main` und `AssetDatabase.Refresh()`.
- Ausgesetzt (Grund steht einmal in der Konsole), solange: ein anderer Branch als `main` ausgecheckt ist (still), Play-Mode oder Kompilierung läuft, eine offene Szene ungespeichert ist, getrackte Dateien geändert sind oder das lokale `main` eigene Commits hat. Es wird nie etwas überschrieben oder verworfen.
- Menü: **Tools → main automatisch aktualisieren** (an/aus, pro Rechner in den EditorPrefs, Standard an) und **Tools → main jetzt aktualisieren** (sofort prüfen, Ergebnis in der Konsole).
