# Speicher-Bereinigung

Ein Wächter hält die Mining-Game-Ordner klein. Sind `Mining Game`, `Mining Game 2`, `Mining Game 3` … (alle Ordner `Git Repos/Mining Game` und `Git Repos/Mining Game <Zahl>`, keine anderen Spiele) zusammen grösser als **100 GB**, räumt er automatisch auf.

- Skript: `Tools/disk-cleanup.ps1` (Git Bash: `Tools/disk-cleanup.sh`)
- Geplanter Windows-Task „Mining Game Speicher-Bereinigung“: täglich 12:00 (nachgeholt, falls der PC aus war) und 15 min nach der Anmeldung, ohne Fenster, mit niedriger Priorität
- Protokoll: `Git Repos/DiskCleanup/disk-cleanup.log` (ausserhalb von Git; jede Prüfung eine Zeile, Aufräumen mit Einzelheiten)
- Installierte Kopie: `Git Repos/DiskCleanup/bin/disk-cleanup.ps1`. Nach Änderungen am Skript `install` erneut ausführen.

## Befehle

```bash
Tools/disk-cleanup.sh status            # Grösse je Ordner, Limit, Task, letzte Protokollzeilen
Tools/disk-cleanup.sh run -DryRun       # zeigt nur, was passieren würde (-Force: auch unter dem Limit)
Tools/disk-cleanup.sh run               # räumt nur auf, wenn das Limit überschritten ist
Tools/disk-cleanup.sh install           # Task einrichten/aktualisieren (-LimitGB 100)
Tools/disk-cleanup.sh uninstall
```

## Schritte

Nach jedem Schritt wird neu gerechnet; liegt die Summe unter dem Limit, ist Schluss.

1. **Alte Git-LFS-Versionen:** `git lfs prune --verify-remote --when-unverified=continue`. Gelöscht wird nur, was nachweislich auf GitHub liegt; Versionen, die dort fehlen, bleiben lokal.
2. **Erledigte Arbeitsordner** unter `Mining Game/.claude/worktrees`: nur wenn der Stand vollständig in `origin/main` ist, keine Änderungen oder neuen Dateien vorliegen, Unity den Ordner nicht offen hat, der Arbeitsordner nicht gesperrt ist und er mindestens 2 Tage unbenutzt war (neueste Datei ausserhalb von `Library`, Git-Verwaltungsdaten). Entfernt mit `git worktree remove` ohne `--force`; der Branch bleibt. Das Protokoll nennt bei jedem behaltenen Ordner den Grund.
3. **Build Tests:** Einträge, die mindestens 2 Tage alt sind.
4. **Szenen-Backups** (`SceneBackups/`): Dateien, die mindestens 14 Tage alt sind.
5. **git gc**

Nie angefasst: `Library`, `Assets`, `Design`, Spielstände, Branches, nicht gepushte Commits und die Ordner `Mining Game <Zahl>` selbst. Bleibt die Summe trotzdem über dem Limit, steht eine Warnung im Protokoll.

## Prüfen

`powershell -NoProfile -ExecutionPolicy Bypass -File Tests/DiskCleanupChecks.ps1` baut unter `Temp/DiskCleanupTest` ein Test-Repo mit Remote, Arbeitsordnern in verschiedenen Zuständen und Geschwisterordnern auf und prüft Messung, Probelauf, Sperre gegen Doppelstart und jeden Schritt. Es bricht sofort ab, falls das Skript nicht den Testordner meint.
