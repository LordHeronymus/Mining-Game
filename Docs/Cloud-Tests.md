# Cloud-Tests (GitHub Actions)

Die Prüfskripte aus `Tests/*Checks.cs` laufen auch ohne lokalen Editor: GitHub startet Unity im Batchmode in einem GameCI-Docker-Image (`unityci/editor:ubuntu-<Version>-base-3`, Version aus `ProjectSettings/ProjectVersion.txt`). So können Agenten in der Cloud Änderungen prüfen, während der eigene Computer ausgeschaltet ist.

## Bestandteile

- `.github/workflows/unity-checks.yml`: Workflow „Unity Checks“. Startet manuell (Actions → Unity Checks → Run workflow) und bei Pull Requests nach `main` (gedrosselt, siehe „Drosselung“).
- `Tests/CI/prepare.py`: wählt die Checks (Suite oder Liste), kopiert sie nach `Assets/Editor/CIChecks/` (per `.gitignore` ausgeschlossen) und schreibt `Artifacts/plan.json`.
- `Tests/CI/suites/*.json`: Suiten. `smoke` = schnelle Edit-Mode-Checks, `play` = Play-Mode ab MainMenu (GPS zur Laufzeit, GPS-Oberfläche, Ladeübergänge, Spielstand-Slots), `visual` = Standbilder (Erz-Vorschauen), `performance` = Benchmark-Player.
- `Tests/CI/run-in-container.sh`: Lizenzaktivierung und Unity-Start im Container.
- `Assets/Editor/CI/CiCheckRunner.cs` (eigene Editor-Assembly `MiningGame.CI.Editor`): führt die Checks aus wie `unity command run_script`. Edit-Checks laufen in der geöffneten SampleScene, Play-Checks mit frischem Play-Start ab MainMenu. `Main()`-Rückgaben mit „FAIL“/„Requires“, Ausnahmen, Timeouts und `FAIL`-Zeilen in `Temp/<Name>.txt` gelten als Fehler. Checks mit `EditorApplication.update` werden bis zu ihrer Ergebnisdatei abgewartet, optional folgt `afterStop` im Edit Mode.

Ergebnisse: Zusammenfassung im Workflow-Lauf, Artefakt `unity-checks-<Nr>` mit `ci-results.md`, `ci-results.xml` (JUnit), `editor.log` und den Temp-Berichten/Vorschauen der Checks.

## Live-Dashboard

`Tools/CloudDashboard/index.html` im Browser öffnen (Doppelklick genügt). Es zeigt die letzten Läufe, den Ablauf der Workflow-Schritte, die Unity-Phase (Lizenz, Import mit Fortschritt, Kompilieren, Checks), die Checks mit Status, die letzten Log-Zeilen und Notizen von Claude. Aktualisierung alle 5 s während eines Laufs, sonst alle 30 s. Läufe lassen sich dort auch starten, abbrechen und wiederholen.

- Datenquelle: GitHub-API direkt aus dem Browser. Den Live-Status während „Run Unity“ schreibt `Tests/CI/progress.py` alle 15 s als Kommentar in das Issue mit Label `cloud-status` („Cloud-Tests Live-Status“). Kommentare dort ohne Status-Markierung sind Notizen von Claude (Lauf mit `#<Nummer>` referenziert).
- Zugang: Fine-grained Token (github.com → Settings → Developer settings → Fine-grained tokens), nur dieses Repository, Rechte **Actions: Read and write** (Read reicht zum Zuschauen), **Issues: Read** und für die Vorschaubilder **Contents: Read**. Der Schlüssel bleibt im localStorage des Browsers.
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

Im `-batchmode` rendert der Editor kein Game-Fenster, daher kehrt `WaitForEndOfFrame` nie zurück (Lauf #7). Ladebildschirm, Spielstand und Kartengenerierung warten deshalb über `LoadingProgress.EndOfFrame`: im Batchmode auf den nächsten Frame, sonst wie bisher auf das Bildende (für Spieler unverändert). Seit Lauf #154 laufen Play-Checks (`play:`) in der Cloud, Suite `play` mit vier Checks in rund 2 Minuten. Der Runner startet jeden Play-Check frisch ab MainMenu und wartet, bis der vorherige Play-Mode wirklich beendet ist.

- Geeignet: Abläufe und Logik im Play-Mode (Menüs, Laden, Speichern, GPS). Nicht geeignet: Bewegung, Animation, Partikel, Bildraten, Spielgefühl (`// CI: local-only`).
- Der Editor mit Oberfläche (ohne `-batchmode`, `editorui = 1`) beendet sich ohne Unity-Hub-Anmeldung direkt nach der Lizenzprüfung (Läufe #8–#12) und wird nicht gebraucht.

## Vorschaubilder (Suite `visual`)

- PNGs, die Checks während des Laufs unter `Temp/` oder `Design/` schreiben, sammelt `CiCheckRunner` vor dem Beenden nach `Artifacts/previews` (höchstens 60; Unity löscht `Temp/` beim Beenden). Der Workflow-Schritt „Publish previews“ (`Tests/CI/publish-previews.sh`) legt sie auf den Branch `ci-previews` unter `runs/<Laufnummer>/` mit `index.json` (die letzten 15 Läufe, ein einziger Commit).
- Abholen: `Tools/ci-previews.sh` listet die Läufe, `Tools/ci-previews.sh <Nr> [Ordner]` entpackt die Bilder (Standard `Temp/ci-previews/<Nr>`). Agenten sehen sich die Bilder danach selbst an (Read-Werkzeug).
- Dashboard: Abschnitt „Vorschaubilder“ (Contents-API); der Token braucht zusätzlich **Contents: Read**. Das Modul in PrivOrg ist eine Kopie und braucht dieselbe Erweiterung.
- Suite `visual`: `VeinRichnessPreviewChecks`, `EmbeddedOreRenderChecks`. Weitere Standbild-Checks nur aufnehmen, wenn sie aktuell kompilieren und bestehen (`OreVeinVisualChecks` und `DirtSurfaceChecks` sind veraltet).
- Bilder kommen aus Software-Rendering (Mesa) und können sich leicht von der lokalen GPU unterscheiden. Für Vorher/Nachher-Vergleiche taugen sie, die endgültige Optik nimmt der Benutzer lokal ab.

## Drosselung (Actions-Minuten)

Der erste Schritt „Budget“ (`Tests/CI/budget.py`) vergleicht den Verbrauch des Monats mit einer Budget-Linie: pro angefangenem Tag 3 % des Kontingents (am 23. also 69 %, am 30. 90 %, höchstens 100 %; Monat in UTC, Erneuerung am 1.). Daraus folgt die Stufe:

| Stufe | Bedingung | Wirkung |
| --- | --- | --- |
| 🟢 grün | Verbrauch auf oder unter der Linie | alles läuft normal |
| 🟡 gelb | bis 10 Prozentpunkte darüber | kein automatischer PR-Lauf bei jedem Push; ein PR-Lauf erst mit Label `cloud-check` (einmal am Schluss). Manuelle Läufe ohne `checks` laufen mit Suite `smoke` |
| 🔴 rot | mehr als 10 Punkte darüber oder weniger als 5 % übrig | nur manuelle Läufe mit Eingabe `force` (nur mit Freigabe des Benutzers) |

- Übersprungene Läufe brechen nach wenigen Sekunden ab (PR: grün mit Hinweis, manuell bei Rot: Fehler) und schreiben einen Kommentar „Übersprungen (Drosselung)“ ins Issue #2. Jeder Statuskommentar zeigt die Stufe, die Budget-Linie und den Grund.
- Öffentliches Repository: Läufe auf Standard-Runnern sind kostenlos, die Stufe wird nur angezeigt, nichts gedrosselt.
- Datenquelle: Mit dem Repository-Secret `ACTIONS_BILLING_TOKEN` (Fine-grained Token, Account permissions → **Plan: Read**) liest der Schritt die offizielle Abrechnung aller Repositories („genau“). Minuten öffentlicher Repositories zählen nicht (kostenlos). Ohne Secret schätzt er aus den Laufzeiten dieses Repositories (öffentlich: 0); scheitert beides, läuft alles ungedrosselt.
- Kontingent: Repository-Variable `ACTIONS_INCLUDED_MINUTES` (Standard 2000). Lokal prüfen: `GITHUB_TOKEN=… python3 Tests/CI/budget.py --print`.
- Anzeige: PrivOrg → Cloud-Tests → Kästchen „Actions-Minuten“ (Graph Verbrauch gegen Budget-Linie, gleiche Rechnung).

## Zeitlimit und Nur-lokal-Checks

- Ein Lauf hat höchstens **20 Minuten** (`timeout-minutes: 20`). Unity bzw. der Benchmark-Player bekommt nur die Zeit bis 16 Minuten nach Jobstart (`CI_DEADLINE`), danach folgen Bericht und Upload. Bei Überschreitung schreibt `run-in-container.sh` `Artifacts/abort-reason.txt` („Abgebrochen: Zeitlimit von 20 Minuten erreicht (…). Nicht erneut starten …“). Der Grund erscheint als Fehler im Job-Log, oben in der Lauf-Zusammenfassung, im Live-Status (Issue #2) und als Banner im Dashboard.
- Checks zu Bewegung, Animation, Partikeln oder Bildraten tragen im Skript den Kommentar `// CI: local-only`. `prepare.py` lehnt sie in der Cloud nach wenigen Sekunden mit demselben Abbruchgrund ab, bevor Unity startet.
- Agenten starten einen abgebrochenen Lauf nicht erneut (Regel in `AGENTS.md`).

## Suite `performance` (Benchmark-Player)

Statt im Editor misst die Cloud mit einem eigenständigen Linux-Player: `Assets/Editor/CI/CiBenchmarkBuild.cs` baut ihn (Mono, Define `MINING_BENCHMARK`, eigener Produktname „Tiefenhall Benchmark“ wie `Tests/BuildPerformanceBenchmark.cs`), danach startet `run-in-container.sh` ihn auf Xvfb mit Mesa-Software-OpenGL (`-performanceBenchmark -benchmarkNoRender`). `PerformanceBenchmarkDriver` spielt Laden, Stillstand, Abbau normal/schnell und Erkunden durch und schreibt `PerformanceBenchmark.txt`/`.csv` (Artefakt, Zusammenfassung im Dashboard). Der erste Build kompiliert alle Shader und dauert entsprechend; danach hilft der Library-Cache. Werte sind nur untereinander vergleichbar (kein GPU, Mono statt IL2CPP). Mit Zeichnen dauerte ein vollständiger Lauf 41 Minuten (Lauf #16: Software-Rendering, ~1,3 s pro Bild im Spiel); `-nographics` stürzt den Player ab (Lauf #15). Deshalb startet der Player mit `-benchmarkNoRender`: Grafikgerät vorhanden, aber alle Kameras zeichnen nichts (Culling-Maske 0), die Framezeiten messen nur die Rechenarbeit des Spiels. Ladegrenze `-benchmarkLoadLimit 480`. Mit `CI_PLAYER_RENDER=1` wird wieder gezeichnet (sprengt das Zeitlimit). Lauf #17: ganzer Lauf 12 min, Laden 92 s, Spielphasen 13–16 ms pro Bild (235–937 Bilder je Phase). Davon ~6 min Einrichtung (Speicher freigeben, Checkout, Cache).

## Grenzen

- Checks, die Dateien aus dem lokalen `Temp/` erwarten (z. B. `LegacyMetaSerializationChecks`), oder Archiv-/Install-Skripte gehören nicht in Suiten.
- Grafik läuft über Software-Rendering (Xvfb). Visuelle Checks funktionieren, sind aber langsam und können sich von der lokalen GPU unterscheiden.
- Kopierte Checks werden zusammen kompiliert: Gleichnamige Top-Level-Klassen in zwei Checks derselben Auswahl führen zu Compilerfehlern (stehen in der Lauf-Zusammenfassung).
- Kosten: siehe Gratis-Kontingent von GitHub Actions (2'000 Min./Monat privat) und Git LFS (10 GiB Übertragung/Monat). LFS-Dateien unter `Assets/` (~390 MB) werden gecacht, `AudioMastering/` wird nicht geladen.
