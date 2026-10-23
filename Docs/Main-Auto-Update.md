# main automatisch aktualisieren

`Assets/Editor/MainAutoUpdate.cs` hält den Ordner des Benutzers (`Git Repos/Mining Game`) auf dem neuesten `origin/main`. Das Skript ist nur in diesem Ordner aktiv, nicht in der Testbank `Mining Game 2` und nicht in Worktrees. Grundsatz: **main folgt immer origin/main, verworfen wird nichts ohne Sicherung.** Eigene Einstellungen (F1/GPS) blockieren die Aktualisierung nie, sie liegen in einer nicht versionierten Überschreibungs-Schicht (siehe `Docs/GPS-Eigene-Werte.md`).

## Ablauf

- Jede Minute (erstmals 20 s nach dem Laden, nach dem Play-Stopp nach 2 s): `git fetch origin main` und Bestandsaufnahme im Hintergrund (`MainSyncGit.Inspect`): neue Commits, eigene Commits auf `main`, lokal geänderte Dateien, welche davon `main` ebenfalls ändert.
- **Während Play-Mode oder Kompilierung** wird nur geprüft. Neue Commits erscheinen als Hinweis im Game-Fenster („Neuer Stand auf main … Play beenden zum Aktualisieren“) und werden direkt nach dem Play-Stopp geholt. Ein Play-Start während der Git-Schritte wird abgebrochen (Hinweis im Game-/Scene-Fenster).
- **Im Edit Mode** wird aktualisiert (`MainSyncGit.Update`, Hintergrund):
  1. **Sicherung** auf den Branch `backup/mining-game-auto`, falls nötig: lokale Commits auf `main` (z. B. ein Commit „Update gameplay settings profile“) und lokal geänderte oder unversionierte Dateien, die `main` ebenfalls ändert bzw. neu bringt. Jede Sicherung ist ein Commit mit HEAD und der vorherigen Sicherung als Eltern (eine Kette, nichts geht verloren); Arbeitsordner, Index und Stash bleiben dabei unberührt (eigener Index).
  2. Diese Dateien auf den Stand von HEAD setzen, dann `git merge --ff-only origin/main` bzw. bei eigenen Commits `git reset --keep origin/main`.
  3. Andere lokale Änderungen (die `main` nicht betrifft) bleiben stehen.
  4. `AssetDatabase.Refresh()` (Skripte kompilieren, Assets neu laden), GPS neu einlesen.
- **GpsProfile.asset**: Sind darin lokale Werte (geänderte Datei oder eigene Commits), werden die Abweichungen gegenüber dem Stand vor diesen Änderungen in die Überschreibungs-Schicht übernommen; danach wird das Profil gesichert und auf den Stand von `main` gesetzt. Rundungsunterschiede in der letzten Kommastelle zählen nicht als eigene Werte.
- **Offene Szenen**: Ändert `main` eine offene, gespeicherte Szene, wird sie für die Aktualisierung kurz geschlossen und danach neu geöffnet (kein Dialog „modified externally“). Eine Szene mit **ungespeicherten** Änderungen, die `main` ändert, blockiert (speichern oder verwerfen); die Meldung steht im Statusfenster und im Game-/Scene-Fenster.
- Ausgesetzt wird sonst nur, wenn ein anderer Branch als `main` ausgecheckt ist, ein Merge/Rebase/Cherry-Pick offen ist oder `git fetch` scheitert (z. B. offline).

## Stand und Protokoll

- **Fenstertitel** des Editors: z. B. „main aktuell @ 14b352b“, „main: 3 neue Commits wartet“ oder „main blockiert – Tools → main-Sync“.
- **Tools → main-Sync**: Stand mit Grund, letzte Prüfung, Knöpfe „Jetzt aktualisieren“, „Automatisch“ (an/aus), „Protokoll öffnen“, Anzahl eigener GPS-Werte mit „Als Standard vorschlagen“, Liste der lokal geänderten Dateien, die letzten Protokollzeilen.
- **Protokoll**: `Logs/main-sync.log` im Projektordner (nicht versioniert, ab 1 MB nach `.1`): Aktualisierungen (alt → neu, Commit-Titel), Sicherungen (Commit, Dateien), übernommene GPS-Werte, Gründe fürs Aussetzen.
- **Editor-Warteschlange/PrivOrg**: Der Stand steht wie bisher in der Aktivitätsdatei (`mainState`, `mainText`, `mainBehind`, `mainChecked`, geschrieben von `EditorQueueActivity`); `editor-queue status` zeigt ihn als „Stand: …“.
- Menü: **Tools → main automatisch aktualisieren** (an/aus, EditorPrefs, Standard an) und **Tools → main jetzt aktualisieren**.

## Sicherungen ansehen und zurückholen

```bash
git log --oneline backup/mining-game-auto
```

Eine gesicherte Datei zurückholen (z. B. eine Szene): `git show backup/mining-game-auto:<Pfad> > <Datei>` oder `git checkout backup/mining-game-auto -- <Pfad>`. Gesicherte lokale Commits sind die ersten Eltern des jeweiligen Sicherungs-Commits (`git log backup/mining-game-auto^1`).

## Regel für Chats

Chats committen nie im Ordner `Mining Game` und ändern dort keine versionierten Dateien (AGENTS.md). Änderungen laufen über eigene Branches/Worktrees und PRs nach `main`; der Ordner des Benutzers holt sie dann von selbst.

## Prüfung

- `Tests/MainSyncGitChecks.cs` (Edit Mode, temporäre Repositories unter `Temp/`): Fast-Forward, fremde lokale Änderung bleibt, Konfliktdatei gesichert und ersetzt, lokaler Commit gesichert und `main` = `origin/main`, unversionierte Datei, GPS-Profil zurückgesetzt, offener Merge blockiert, Sicherungskette, GPS-Vorschlag als Branch.
- Die Unity-Seite (Szene schliessen/öffnen, Refresh, GPS neu einlesen, Fenstertitel) wird im Editor des Benutzers abgenommen.
