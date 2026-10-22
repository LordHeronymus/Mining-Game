# Arbeitsordner ohne Design

Neue Arbeitsordner der Chats (`git worktree add`, z. B. unter `Mining Game/.claude/worktrees/` oder `Mining Game <n>`) entstehen ohne `Design/` und `AudioMastering/`. Diese Ordner enthalten Quellen, Prompts und Vergleichsbilder generierter Grafiken bzw. Audio-Mastering-Dateien. Das Spiel braucht sie nicht, sie kosten aber rund 2,9 GB pro Arbeitsordner (ein frischer Arbeitsordner hat so etwa 1,4 statt 4,6 GB).

## Wie es funktioniert

- Ein Block im gemeinsamen Git-Hook `.git/hooks/post-checkout` (gilt für alle Arbeitsordner und alle Agenten, nicht in Git versioniert) stellt einen neuen verknüpften Arbeitsordner beim ersten Auschecken auf Sparse-Checkout um: Muster `/*`, `!/Design/`, `!/AudioMastering/`. Die übrigen Hook-Zeilen (Git LFS) laufen unverändert danach.
- Danach liegt die Markierung `mining-game-sparse` im Git-Verwaltungsordner des Arbeitsordners (`.git/worktrees/<Name>/`). Ein markierter Arbeitsordner wird nie wieder angefasst, auch nicht bei Branchwechseln.
- Der Hauptordner `Mining Game` und alle Arbeitsordner, die bei der Einrichtung schon da waren (auch die Testbank `Mining Game 2`), bleiben vollständig.
- In Git ändert sich nichts: `Design/` bleibt in allen Branches und Commits erhalten, `git status` ist sauber, Commits aus einem Arbeitsordner ohne `Design/` lassen `Design/` unverändert.

## Design/ doch brauchen

Im Arbeitsordner (z. B. für Grafikquellen nach `Design/<Thema>/`):

```bash
git sparse-checkout add Design/<Thema>/
```
```bash
git sparse-checkout disable
```

Der erste Befehl holt nur ein Thema, der zweite alles zurück. Das Muster ohne führenden `/` schreiben, sonst macht Git Bash einen Windows-Pfad daraus. Neue Dateien unter einem ausgeblendeten Pfad nimmt `git add` nicht an. Deshalb vorher den passenden Ordner holen.

Einen neuen Arbeitsordner gleich vollständig anlegen: `MG_FULL_CHECKOUT=1 git worktree add …`

## Werkzeug

```bash
Tools/worktree-sparse.sh status                         # Hook eingetragen? Welche Arbeitsordner haben Design/?
Tools/worktree-sparse.sh install                        # Hook eintragen (nach Änderungen am Block erneut)
Tools/worktree-sparse.sh apply -Path <Arbeitsordner>    # bestehenden Arbeitsordner nachträglich ohne Design/
Tools/worktree-sparse.sh uninstall
```

`apply` bricht ab, wenn in `Design/` oder `AudioMastering/` Änderungen liegen, und lässt den Hauptordner nie zu.

## Prüfen

`powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WorktreeSparseChecks.ps1` prüft in einem Test-Repo unter `Temp/WorktreeSparseTest`: Einrichtung neben einem vorhandenen Hook, bestehende Arbeitsordner und Hauptordner bleiben vollständig, neuer Arbeitsordner ohne `Design/` und sauber, Anlegen aus einem anderen Arbeitsordner heraus, Nachholen eines Themas oder von allem, Commits, `--no-checkout`, `MG_FULL_CHECKOUT`, `apply` und `uninstall`.
