# Eigene GPS-Werte (Überschreibungs-Schicht)

Im Ordner des Benutzers (`Git Repos/Mining Game`) schreiben das F1-Debugfenster und das Editorfenster „Debug Settings“ **nicht** in das versionierte `Assets/Resources/Gameplay/GpsProfile.asset`, sondern in `UserSettings/GpsOverrides.json` (nicht versioniert). Beim Laden werden diese Werte über die Standardwerte des Profils gelegt. So bleibt das Profil sauber, `main` lässt sich immer aktualisieren (`Docs/Main-Auto-Update.md`), und GPS-Standardwerte ändern sich nur bewusst über Branch/PR.

Code: `Assets/GameObjects/GameManager/GpsUserOverrides.cs` (Schicht, Vergleich, Profiltext lesen), Anbindung in `GpsSettings` (Laden, Speichern, `ResetToDefaults`, `ReloadFromProfile`), Knöpfe in `GpsRuntimePanel` und `GameplaySettingsWindow`, Vorschlag in `Assets/Editor/GpsDefaultsProposal.cs`.

## Verhalten

- **Nur im Benutzerordner** aktiv (`GpsUserOverrides.Active`: Ordnername `Mining Game`). Testbank, Worktrees und Cloud arbeiten wie bisher direkt mit dem Profil; dort ändern Chats Standardwerte und committen sie auf ihrem Branch.
- **Gespeichert wird nur, was vom Standard abweicht**, und zwar so fein wie möglich: In Listen und Objekten (z. B. ein Feld eines Erzes in `oreSettings`) nur das einzelne Feld. Neue Standardwerte von `main` kommen also an, solange nicht genau dieser Wert selbst überschrieben ist. Für jede Liste auf dem Weg ist die Länge vermerkt; wurde die Liste auf `main` umgebaut, gilt der Eintrag als veraltet und wird mit Wert in der Konsole gemeldet statt falsch angewendet.
- Einträge, die inzwischen dem Standard entsprechen (z. B. nach einem gemergten Vorschlag), fallen beim Laden von selbst weg.
- **Lautstärke** (Master, Musik) bleibt wie bisher persönlich in den EditorPrefs und gehört nie in die Schicht.
- Vor jedem Schreiben bleibt die vorherige Datei als `GpsOverrides.json.bak`; eine unlesbare Datei wird als `GpsOverrides.json.defekt-<Zeit>` beiseitegelegt.
- Rückgängig (Strg+Z) wirkt im Benutzerordner nicht auf GPS-Werte (das Profil wird nicht mehr verändert); stattdessen zurücksetzen.

## Im F1-Fenster / Debug Settings

- Oben: „N eigene Werte (nur lokal)“.
- Eigene Felder stehen gold mit „(eigen)“; **×** davor setzt das Feld auf den Standard von `main` zurück.
- **Zurücksetzen**: alle eigenen Werte auf Standard (im F1-Fenster zweimal klicken, im Editorfenster mit Rückfrage).
- **Als Standard vorschlagen**: zeigt die Werte (Standard → eigen) zur Bestätigung, schreibt das Profil so, wie Unity es speichert, als Commit auf einen neuen Branch `gps/vorschlag-<Datum-Zeit>` (auf Basis von `origin/main`, ohne Arbeitsordner oder `main` anzufassen), pusht ihn und öffnet mit `gh` einen Pull Request gegen `main`. Ohne `gh` steht der Link zum Erstellen im Status. Voraussetzung: `main` ist aktuell. Nach dem Merge holt die main-Aktualisierung die neuen Standardwerte; gleiche eigene Werte fallen dann weg. Im PR-Diff erscheinen neben den Werten einige Zeilen mit Rundungsunterschieden in der letzten Kommastelle (Unity schreibt Zahlen nach dem Einlesen leicht anders); inhaltlich sind sie gleich.
- Dasselbe ist im Fenster **Tools → main-Sync** erreichbar.

## Übernahme bestehender Werte

Steht in `GpsProfile.asset` im Benutzerordner etwas Eigenes (lokal geänderte Datei oder eigene Commits auf `main`), übernimmt die main-Aktualisierung die Abweichungen gegenüber dem vorherigen Stand in die Schicht (bestehende eigene Werte gehen vor), sichert die Datei auf `backup/mining-game-auto` und setzt sie auf den Stand von `main`. Das Protokoll (`Logs/main-sync.log`) nennt die übernommenen Werte.

## Prüfung

`Tests/GpsUserOverridesChecks.cs` schaltet die Schicht nur für den Check ein (`TestActive`, Datei unter `Temp/`):
- Edit Mode: Profiltext lesen (gleich wie Unity), Rundungstoleranz, YAML-Faltung/Escapes, Unterschiede/Anwenden/Vereinigen, feine Listenfelder, veraltete Einträge, Speichern ohne Profiländerung, Neuladen, Hold während der Aktualisierung, Zurücksetzen (einzeln/alle), Übernahme aus einem geänderten Profiltext, Vorschlags-Profil (Kopf und Asset-Liste unverändert).
- Play Mode (frischer Start ab MainMenu): F1-Knöpfe, Änderung im Panel wird eigener Wert, Markierung „(eigen)“, × setzt zurück, Profil-Datei unverändert.
