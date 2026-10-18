# Cloud-Tests (GitHub Actions)

Die Prüfskripte aus `Tests/*Checks.cs` laufen auch ohne lokalen Editor: GitHub startet Unity im Batchmode in einem GameCI-Docker-Image (`unityci/editor:ubuntu-<Version>-base-3`, Version aus `ProjectSettings/ProjectVersion.txt`). So können Agenten in der Cloud Änderungen prüfen, während der eigene Computer ausgeschaltet ist.

## Bestandteile

- `.github/workflows/unity-checks.yml`: Workflow „Unity Checks“. Startet manuell (Actions → Unity Checks → Run workflow) und bei Pull Requests nach `main`.
- `Tests/CI/prepare.py`: wählt die Checks (Suite oder Liste), kopiert sie nach `Assets/Editor/CIChecks/` (per `.gitignore` ausgeschlossen) und schreibt `Artifacts/plan.json`.
- `Tests/CI/suites/*.json`: Suiten. `smoke` = schnelle Edit-Mode-Checks, `play` = Play-Mode ab MainMenu.
- `Tests/CI/run-in-container.sh`: Lizenzaktivierung und Unity-Start im Container.
- `Assets/Editor/CI/CiCheckRunner.cs` (eigene Editor-Assembly `MiningGame.CI.Editor`): führt die Checks aus wie `unity command run_script`. Edit-Checks laufen in der geöffneten SampleScene, Play-Checks mit frischem Play-Start ab MainMenu. `Main()`-Rückgaben mit „FAIL“/„Requires“, Ausnahmen, Timeouts und `FAIL`-Zeilen in `Temp/<Name>.txt` gelten als Fehler. Checks mit `EditorApplication.update` werden bis zu ihrer Ergebnisdatei abgewartet, optional folgt `afterStop` im Edit Mode.

Ergebnisse: Zusammenfassung im Workflow-Lauf, Artefakt `unity-checks-<Nr>` mit `ci-results.md`, `ci-results.xml` (JUnit), `editor.log` und den Temp-Berichten/Vorschauen der Checks.

## Einmalige Einrichtung (Benutzer)

1. **Lizenzdatei holen:** Unity Hub öffnen, mit dem Unity-Konto angemeldet sein und eine aktive Personal-Lizenz haben (Hub → Einstellungen → Lizenzen). Die Datei liegt unter Windows in `C:\ProgramData\Unity\Unity_lic.ulf` (macOS: `/Library/Application Support/Unity/Unity_lic.ulf`). Mit einem Texteditor öffnen und den ganzen Inhalt kopieren.
2. **Secrets anlegen:** GitHub → Repository → Settings → Secrets and variables → Actions → New repository secret:
   - `UNITY_LICENSE`: kompletter Inhalt der `.ulf`-Datei
   - `UNITY_EMAIL`: E-Mail des Unity-Kontos
   - `UNITY_PASSWORD`: Passwort des Unity-Kontos
   - Nur bei Pro/Plus-Lizenz statt `UNITY_LICENSE`: `UNITY_SERIAL`
3. **Kostenlimit:** GitHub → Settings (Konto) → Billing and licensing → Budgets and alerts: Budget für Actions auf 0 $ setzen, dann gibt es keine Kosten über das Gratis-Kontingent hinaus.
4. **Actions erlauben:** Repository → Settings → Actions → General: „Allow all actions and reusable workflows“ (benötigt werden `actions/*`). Unter „Workflow permissions“ genügt „Read“.
5. **Ersten Lauf starten:** Actions → Unity Checks → Run workflow, Branch wählen, Suite `smoke`. Der erste Lauf importiert die komplette Library (lange); danach kommt sie aus dem Cache.

## Benutzung

- Suite: Eingabe `suite` (`smoke`, `play` oder eine neue Datei unter `Tests/CI/suites/`).
- Einzelne Checks: Eingabe `checks`, z. B. `CaveGenerationChecks, play:GpsUiChecks`. Ohne Präfix wird der Modus aus dem Skript erkannt („Requires fresh Play“, `MainMenuController.IsVisible` → play).
- `nographics`: Unity ohne Grafikgerät (schneller, aber Kameras rendern nicht; nur für reine Logik-Checks).
- Lokal vorbereiten/prüfen: `python3 Tests/CI/prepare.py --suite smoke` (danach `Assets/Editor/CIChecks/` und `Artifacts/` wieder löschen, solange der Editor offen ist, sonst importiert er die Kopien).

## Grenzen

- Checks, die Dateien aus dem lokalen `Temp/` erwarten (z. B. `LegacyMetaSerializationChecks`), oder Archiv-/Install-Skripte gehören nicht in Suiten.
- Grafik läuft über Software-Rendering (Xvfb). Visuelle Checks funktionieren, sind aber langsam und können sich von der lokalen GPU unterscheiden.
- Kopierte Checks werden zusammen kompiliert: Gleichnamige Top-Level-Klassen in zwei Checks derselben Auswahl führen zu Compilerfehlern (stehen in der Lauf-Zusammenfassung).
- Kosten: siehe Gratis-Kontingent von GitHub Actions (2'000 Min./Monat privat) und Git LFS (10 GiB Übertragung/Monat). LFS-Dateien unter `Assets/` (~390 MB) werden gecacht, `AudioMastering/` wird nicht geladen.
