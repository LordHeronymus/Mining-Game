# Cloud-Tests (GitHub Actions)

Die Prüfskripte aus `Tests/*Checks.cs` laufen auch ohne lokalen Editor: GitHub startet Unity im Batchmode in einem GameCI-Docker-Image (`unityci/editor:ubuntu-<Version>-base-3`, Version aus `ProjectSettings/ProjectVersion.txt`). So können Agenten in der Cloud Änderungen prüfen, während der eigene Computer ausgeschaltet ist.

## Bestandteile

- `.github/workflows/unity-checks.yml`: Workflow „Unity Checks“. Startet manuell (Actions → Unity Checks → Run workflow) und bei Pull Requests nach `main`.
- `Tests/CI/prepare.py`: wählt die Checks (Suite oder Liste), kopiert sie nach `Assets/Editor/CIChecks/` (per `.gitignore` ausgeschlossen) und schreibt `Artifacts/plan.json`.
- `Tests/CI/suites/*.json`: Suiten. `smoke` = schnelle Edit-Mode-Checks, `play` = Play-Mode ab MainMenu.
- `Tests/CI/run-in-container.sh`: Lizenzaktivierung und Unity-Start im Container.
- `Assets/Editor/CI/CiCheckRunner.cs` (eigene Editor-Assembly `MiningGame.CI.Editor`): führt die Checks aus wie `unity command run_script`. Edit-Checks laufen in der geöffneten SampleScene, Play-Checks mit frischem Play-Start ab MainMenu. `Main()`-Rückgaben mit „FAIL“/„Requires“, Ausnahmen, Timeouts und `FAIL`-Zeilen in `Temp/<Name>.txt` gelten als Fehler. Checks mit `EditorApplication.update` werden bis zu ihrer Ergebnisdatei abgewartet, optional folgt `afterStop` im Edit Mode.

Ergebnisse: Zusammenfassung im Workflow-Lauf, Artefakt `unity-checks-<Nr>` mit `ci-results.md`, `ci-results.xml` (JUnit), `editor.log` und den Temp-Berichten/Vorschauen der Checks.

## Live-Dashboard

`Tools/CloudDashboard/index.html` im Browser öffnen (Doppelklick genügt). Es zeigt die letzten Läufe, den Ablauf der Workflow-Schritte, die Unity-Phase (Lizenz, Import mit Fortschritt, Kompilieren, Checks), die Checks mit Status, die letzten Log-Zeilen und Notizen von Claude. Aktualisierung alle 5 s während eines Laufs, sonst alle 30 s. Läufe lassen sich dort auch starten, abbrechen und wiederholen.

- Datenquelle: GitHub-API direkt aus dem Browser. Den Live-Status während „Run Unity“ schreibt `Tests/CI/progress.py` alle 15 s als Kommentar in das Issue mit Label `cloud-status` („Cloud-Tests Live-Status“). Kommentare dort ohne Status-Markierung sind Notizen von Claude (Lauf mit `#<Nummer>` referenziert).
- Zugang: Fine-grained Token (github.com → Settings → Developer settings → Fine-grained tokens), nur dieses Repository, Rechte **Actions: Read and write** (Read reicht zum Zuschauen) und **Issues: Read**. Der Schlüssel bleibt im localStorage des Browsers.
- Modul: `cloud-dashboard.js` + `cloud-dashboard.css` sind eigenständig (alles unter `.cd`). Einbinden mit `CloudDashboard.mount(element, { owner, repo, workflow, token })`; ohne `token` fragt das Dashboard selbst danach. `destroy()` auf dem Rückgabewert räumt auf.

## Einmalige Einrichtung (Benutzer)

1. **Unity-Konto:** Für Unity Personal braucht es nur E-Mail und Passwort des Unity-Kontos. Der Workflow aktiviert die Personal-Lizenz damit über Unitys Licensing Client (`--activate-all --include-personal`, wie GameCI) und gibt den Platz nach dem Lauf wieder frei (`--return-ulf`). Eine `.ulf`-Datei funktioniert für Personal nicht mehr: Unity stellt sie nur noch für Enterprise/Industry aus. Wer sich bei Unity mit Google anmeldet, setzt unter id.unity.com zuerst ein eigenes Unity-Passwort (nie das Google-Passwort verwenden).
2. **Secrets anlegen:** GitHub → Repository → Settings → Secrets and variables → Actions → New repository secret:
   - `UNITY_EMAIL`: E-Mail des Unity-Kontos
   - `UNITY_PASSWORD`: Passwort des Unity-Kontos
   - Nur bei Pro/Plus-Lizenz zusätzlich: `UNITY_SERIAL`
   - `UNITY_LICENSE` (Inhalt einer `.ulf`) wird nur noch verwendet, wenn kein Passwort gesetzt ist.
3. **Kostenlimit:** GitHub → Settings (Konto) → Billing and licensing → Budgets and alerts: Budget für Actions auf 0 $ setzen, dann gibt es keine Kosten über das Gratis-Kontingent hinaus.
4. **Actions erlauben:** Repository → Settings → Actions → General: „Allow all actions and reusable workflows“ (benötigt werden `actions/*`). Unter „Workflow permissions“ genügt „Read“.
5. **Ersten Lauf starten:** Actions → Unity Checks → Run workflow, Branch wählen, Suite `smoke`. Der erste Lauf importiert die komplette Library (lange); danach kommt sie aus dem Cache.

## Benutzung

- Suite: Eingabe `suite` (`smoke`, `play` oder eine neue Datei unter `Tests/CI/suites/`).
- Einzelne Checks: Eingabe `checks`, z. B. `CaveGenerationChecks, play:GpsUiChecks`. Ohne Präfix wird der Modus aus dem Skript erkannt („Requires fresh Play“, `MainMenuController.IsVisible` → play).
- `nographics`: Unity ohne Grafikgerät (schneller, aber Kameras rendern nicht; nur für reine Logik-Checks).
- Lokal vorbereiten/prüfen: `python3 Tests/CI/prepare.py --suite smoke` (danach `Assets/Editor/CIChecks/` und `Artifacts/` wieder löschen, solange der Editor offen ist, sonst importiert er die Kopien).

## Arbeitsteilung Cloud / lokal

Cloud-Sessions (Claude Code im Browser/App) und der Workflow „Unity Checks“ brauchen keinen lokalen Editor. Faustregel:

| In der Cloud | Lokal im Editor |
| --- | --- |
| Code-Änderungen, Refactorings, Fehlerbehebungen | Grafik, Optik, Shader, Licht, Animation beurteilen |
| Logik-Checks (Suite `smoke`, einzelne Checks) | Szenen-/Prefab-Arbeit, die man sehen muss |
| CPU-Messungen vorher/nachher (Suite `performance`) | Echte FPS-Abnahme (GPU), Build-Messung |
| Analyse von Messdaten und Logs | Spielgefühl, Steuerung |

- Die Cloud-Rechner haben keine GPU (Software-Rendering). FPS-Werte dort sind nur untereinander vergleichbar, nicht mit dem lokalen PC. Aussagekräftig sind Framezeiten von Spiellogik, Ladestufen, Spitzen und GC-Allokationen im Vergleich vorher/nachher auf dem gleichen Runner-Typ. Schwankungen von einigen Prozent sind normal.
- Eine Cloud-Aufgabe läuft auf ihrem eigenen Branch und trägt sich in `Docs/Arbeitsstand.md` mit Ordner „Cloud“ ein. Sie belegt keinen lokalen Editor.
- Abschluss einer Cloud-Aufgabe mit Optik- oder FPS-Bezug: kurze lokale Abnahme durch den Benutzer (Branch holen, Play starten bzw. Build-Benchmark).

## Play-Mode in der Cloud

Im `-batchmode` rendert der Editor kein Game-Fenster, daher kehren Coroutinen mit `WaitForEndOfFrame` (Blenden, Ladebildschirm) nie zurück und Play-Checks hängen (Lauf #7). Der Editor mit Oberfläche (ohne `-batchmode`, Xvfb + Mesa, `-automated`) beendet sich ohne Unity-Hub-Anmeldung direkt nach der Lizenzprüfung („Access token is unavailable“, Läufe #8–#12); diese Variante ist nur noch manuell mit `editorui = 1` aktivierbar. Play-Checks (`play:`) laufen deshalb in der Cloud derzeit nicht.

## Suite `performance` (Benchmark-Player)

Statt im Editor misst die Cloud mit einem eigenständigen Linux-Player: `Assets/Editor/CI/CiBenchmarkBuild.cs` baut ihn (Mono, Define `MINING_BENCHMARK`, eigener Produktname „Tiefenhall Benchmark“ wie `Tests/BuildPerformanceBenchmark.cs`), danach startet `run-in-container.sh` ihn auf Xvfb mit Mesa-Software-OpenGL (`-performanceBenchmark`). `PerformanceBenchmarkDriver` spielt Laden, Stillstand, Abbau normal/schnell und Erkunden durch und schreibt `PerformanceBenchmark.txt`/`.csv` (Artefakt, Zusammenfassung im Dashboard). Der erste Build kompiliert alle Shader und dauert entsprechend; danach hilft der Library-Cache. Werte sind nur untereinander vergleichbar (kein GPU, Mono statt IL2CPP). Software-Rendering kostet rund 100 ms pro Bild (Lauf #14), daher läuft der Player in einem kleinen virtuellen Bildschirm (640×360) mit Ladegrenze `-benchmarkLoadLimit 2100` statt 300 s. `-nographics` stürzt den Player sofort ab (Lauf #15).

## Grenzen

- Checks, die Dateien aus dem lokalen `Temp/` erwarten (z. B. `LegacyMetaSerializationChecks`), oder Archiv-/Install-Skripte gehören nicht in Suiten.
- Grafik läuft über Software-Rendering (Xvfb). Visuelle Checks funktionieren, sind aber langsam und können sich von der lokalen GPU unterscheiden.
- Kopierte Checks werden zusammen kompiliert: Gleichnamige Top-Level-Klassen in zwei Checks derselben Auswahl führen zu Compilerfehlern (stehen in der Lauf-Zusammenfassung).
- Kosten: siehe Gratis-Kontingent von GitHub Actions (2'000 Min./Monat privat) und Git LFS (10 GiB Übertragung/Monat). LFS-Dateien unter `Assets/` (~390 MB) werden gecacht, `AudioMastering/` wird nicht geladen.
