<#
.SYNOPSIS
  Testbank fuer Chats: holt einen Branch/Commit in den Arbeitsordner "Mining Game 2", dessen Unity Editor
  den Chats gehoert (der Editor von "Mining Game" bleibt fuer den Benutzer frei).
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  Tools/editor-bench.sh status
  Tools/editor-bench.sh checkout claude/mein-thema -Id q1a2b3     # nur als oberster Eintrag; -Discard verwirft Reste
  Tools/editor-bench.sh release -Id q1a2b3                         # danach; -Discard verwirft Testspuren
  Tools/editor-bench.sh audio-on -Id q1a2b3                        # Ton nur fuer Audio-Tests (sonst stumm)
  Tools/editor-bench.sh audio-off                                  # wieder stumm (checkout/release tun das auch)

  checkout/release schliessen vorher die offene Szene im Editor der Testbank ohne Speichern und oeffnen danach
  Assets/Scenes/SampleScene.unity neu (kein Dialog "modified externally"). -NoEditor laesst den Editor in Ruhe.

  Exit-Codes: 0 ok, 1 Fehler, 4 nicht oben in der Warteschlange, 5 Testbank hat Aenderungen,
              6 Editor durch einen unbekannten Dialog blockiert (Tools/editor-dialog.sh list / press).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('status', 'checkout', 'release', 'audio-on', 'audio-off', 'help')]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Ref,
    [string]$Id,
    [string]$Session,
    [string]$Bench,
    [string]$QueueDir,
    [switch]$Discard,
    [switch]$NoEditor,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
if ($Command -eq 'help') { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output; exit 0 }

$repoRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
$ownerMatch = [regex]::Match($repoRoot, '^(.*?)[\\/]\.claude[\\/]worktrees[\\/]')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }
if (-not $Bench) { $Bench = $env:EDITOR_BENCH_DIR }
if (-not $Bench) { $Bench = Join-Path (Split-Path $ownerRoot -Parent) ((Split-Path $ownerRoot -Leaf) + ' 2') }
$Bench = [IO.Path]::GetFullPath($Bench).TrimEnd('\', '/')
$benchName = Split-Path $Bench -Leaf
if (-not $QueueDir) { $QueueDir = $env:EDITOR_QUEUE_DIR }
if (-not $QueueDir) { $QueueDir = Join-Path (Split-Path $ownerRoot -Parent) 'EditorQueue' }
$stateFile = Join-Path $QueueDir "$benchName.bench.json"
# Solange diese Datei fehlt, haelt Assets/Editor/TestBenchAudioMute.cs den Editor der Testbank stumm.
$audioFile = Join-Path $QueueDir "$benchName.audio-on"
if (-not $Session -and $env:CLAUDE_CODE_SESSION_ID) { $Session = $env:CLAUDE_CODE_SESSION_ID }

if (-not (Test-Path -LiteralPath (Join-Path $Bench '.git'))) { Write-Output "Testbank $Bench ist kein Git-Arbeitsordner."; exit 1 }

# Git ohne PowerShell-Fehlerumleitung (stderr eines nativen Befehls wuerde sonst als Fehler gelten).
function Git([string[]]$argv) {
    $psi = New-Object Diagnostics.ProcessStartInfo 'git'
    $psi.Arguments = (@('-C', $Bench) + $argv | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = $utf8
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd(); $err = $p.StandardError.ReadToEnd(); $p.WaitForExit()
    return @{ code = $p.ExitCode; out = $out.TrimEnd(); err = $err.TrimEnd() }
}

function Get-State {
    if (-not (Test-Path -LiteralPath $stateFile)) { return $null }
    try { return [IO.File]::ReadAllText($stateFile, $utf8) | ConvertFrom-Json } catch { return $null }
}

function Get-Dirty { (Git @('status', '--porcelain', '--untracked-files=no')).out }

# Nur der oberste Eintrag der Warteschlange dieses Editors darf die Testbank umstellen.
function Test-Holder {
    $queueFile = Join-Path $QueueDir "$benchName.json"
    if (-not (Test-Path -LiteralPath $queueFile)) { return $false }
    $top = @(([IO.File]::ReadAllText($queueFile, $utf8) | ConvertFrom-Json).entries)[0]
    if ($null -eq $top) { return $false }
    return (($Id -and $top.id -eq $Id) -or ($Session -and $top.session -eq $Session))
}

function Set-Muted { if (Test-Path -LiteralPath $audioFile) { Remove-Item -LiteralPath $audioFile -Force } }

# --- Editor der Testbank: Szene vor Git-Aenderungen schliessen, danach wieder oeffnen ---------------------------
# Sonst fragt Unity nach dem Wechsel modal "The open scene(s) have been modified externally" und blockiert den Editor.

$dialogScript = Join-Path $PSScriptRoot 'editor-dialog.ps1'
$scenePath = 'Assets/Scenes/SampleScene.unity'
$closeSceneCode = @'
if (UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.isPlaying = false; return "playing"; }
UnityEditor.SceneManagement.StageUtility.GoToMainStage();
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
return "closed";
'@
$openSceneCode = @"
UnityEditor.AssetDatabase.Refresh();
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("$scenePath", UnityEditor.SceneManagement.OpenSceneMode.Single);
return "opened";
"@

# Ein Durchgang des Dialog-Waechters ohne Schonfrist. Exit: 0 kein Dialog (mehr), 3 Editor laeuft nicht, 6 unbekannter Dialog offen.
function Invoke-DialogCheck {
    if (-not (Test-Path -LiteralPath $dialogScript)) { return @{ code = 0; out = '' } }
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dialogScript watch -Once -GraceSeconds 0 -Project $benchName -ProjectRoot $Bench -QueueDir $QueueDir
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}

# C# im Editor der Testbank ausfuehren (unity command eval_file), mit Zeitlimit.
function Invoke-UnityEval([string]$code, [int]$timeoutSeconds = 60) {
    $unity = Get-Command unity -ErrorAction SilentlyContinue
    if (-not $unity) { return @{ ok = $false; out = 'Unity-CLI (unity) nicht gefunden' } }
    $file = Join-Path ([IO.Path]::GetTempPath()) ("editor-bench-{0}.cs" -f [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($file, $code, $utf8)
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo $unity.Source
        $psi.Arguments = (@('command', '--project-path', $Bench, '--timeout', "$timeoutSeconds", '--no-banner', '--non-interactive', 'eval_file', '--file', $file) |
                ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
        $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
        $psi.StandardOutputEncoding = $utf8
        $p = [Diagnostics.Process]::Start($psi)
        $errTask = $p.StandardError.ReadToEndAsync()
        $outTask = $p.StandardOutput.ReadToEndAsync()
        if (-not $p.WaitForExit(($timeoutSeconds + 30) * 1000)) { try { $p.Kill() } catch { }; return @{ ok = $false; out = 'Zeitlimit' } }
        $text = ($outTask.Result + "`n" + $errTask.Result).Trim()
        return @{ ok = ($p.ExitCode -eq 0); out = $text }
    }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
}

# Fuehrt C# im Testbank-Editor aus; blockiert ein Dialog, beantwortet der Waechter bekannte und es folgt ein zweiter Versuch.
# Rueckgabe: ok | not-running | blocked (unbekannter Dialog, Text in .out) | failed
function Invoke-BenchEditor([string]$code, [string]$what, [int]$timeoutSeconds = 60) {
    $check = Invoke-DialogCheck
    if ($check.code -eq 3) { return @{ state = 'not-running'; out = '' } }
    if ($check.code -eq 6) { return @{ state = 'blocked'; out = $check.out } }
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $r = Invoke-UnityEval $code $timeoutSeconds
        if ($r.ok -and $r.out -match 'playing') { Write-Host "  Play-Mode wird beendet ..."; Start-Sleep -Seconds 3; continue }
        if ($r.ok) { return @{ state = 'ok'; out = $r.out } }
        $check = Invoke-DialogCheck
        if ($check.code -eq 3) { return @{ state = 'not-running'; out = '' } }
        if ($check.code -eq 6) { return @{ state = 'blocked'; out = $check.out } }
        if ($attempt -ge 2) { return @{ state = 'failed'; out = $r.out } }
        Write-Host "  Editor antwortet nicht ($what), zweiter Versuch ..."
    }
    return @{ state = 'failed'; out = 'Play-Mode endet nicht' }
}

# Vor einer Git-Aenderung: Szene ohne Speichern schliessen. $false = abbrechen (unbekannter Dialog blockiert den Editor).
function Close-BenchScene {
    if ($NoEditor) { return 'skipped' }
    $r = Invoke-BenchEditor $closeSceneCode 'Szene schliessen'
    switch ($r.state) {
        'ok' { Write-Host 'Szene im Testbank-Editor ohne Speichern geschlossen.' }
        'not-running' { Write-Host 'Testbank-Editor laeuft nicht (Szene nicht geschlossen).' }
        'blocked' {
            Write-Host 'Der Testbank-Editor ist durch einen unbekannten Dialog blockiert:'
            Write-Host $r.out
            Write-Host 'Bewusst schliessen mit Tools/editor-dialog.sh press "<Knopf>" oder dem Benutzer melden; danach erneut.'
        }
        'failed' {
            Write-Host "Warnung: Szene konnte nicht geschlossen werden (Editor antwortet nicht: $($r.out))."
            Write-Host '  Erscheint danach "modified externally", beantwortet ihn der Dialog-Waechter (sonst: Tools/editor-dialog.sh press Reload).'
        }
    }
    return $r.state
}

# Nach einer Git-Aenderung: Assets neu einlesen und SampleScene wieder oeffnen.
function Open-BenchScene([string]$closed) {
    if ($closed -ne 'ok') { return }
    # Import nach dem Wechsel kann dauern.
    $r = Invoke-BenchEditor $openSceneCode 'Szene oeffnen' 240
    if ($r.state -eq 'ok') { Write-Host "Unity: Assets neu eingelesen, $scenePath geoeffnet (Skripte kompilieren ggf. noch)." }
    elseif ($r.state -eq 'blocked') { Write-Host "Testbank-Editor durch Dialog blockiert:`n$($r.out)" }
    else { Write-Host "Warnung: $scenePath nicht geoeffnet ($($r.state): $($r.out)). Im Editor selbst oeffnen oder spaeter erneut." }
}

function Show-Status {
    $head = Git @('rev-parse', '--short', 'HEAD')
    $branch = (Git @('symbolic-ref', '-q', '--short', 'HEAD')).out
    $state = Get-State
    Write-Output "Testbank: $Bench"
    Write-Output ("Ton:      {0}" -f $(if (Test-Path -LiteralPath $audioFile) { 'an (Audio-Test)' } else { 'stumm' }))
    Write-Output ("Stand:    {0}{1}" -f $head.out, $(if ($branch) { " (Branch $branch)" } else { ' (losgeloest)' }))
    if ($state) {
        Write-Output "Geholt:   $($state.ref) von $($state.by) am $(([DateTime]::Parse($state.since)).ToLocalTime().ToString('dd.MM. HH:mm'))"
        $ahead = (Git @('log', '--oneline', "$($state.sha)..HEAD")).out
        if ($ahead) { Write-Output "Neue Commits auf der Testbank:"; $ahead -split "`n" | ForEach-Object { Write-Output "  $_" } }
    }
    $dirty = Get-Dirty
    if ($dirty) { Write-Output 'Geaenderte Dateien:'; $dirty -split "`n" | ForEach-Object { Write-Output "  $_" } }
    else { Write-Output 'Keine Aenderungen.' }
}

switch ($Command) {
    'status' { Show-Status; exit 0 }
    'audio-on' {
        if (-not $Force -and -not (Test-Holder)) {
            Write-Output "Nicht oben in der Warteschlange '$benchName'. Ton nur fuer den aktuellen Inhaber (oder -Force)."
            exit 4
        }
        if (-not (Test-Path -LiteralPath $QueueDir)) { New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null }
        [IO.File]::WriteAllText($audioFile, [DateTime]::UtcNow.ToString('o'), $utf8)
        Write-Output 'Ton der Testbank an (Audio-Test). Danach: audio-off, release schaltet ihn ebenfalls stumm.'
        exit 0
    }
    'audio-off' { Set-Muted; Write-Output 'Testbank stumm.'; exit 0 }
    'checkout' {
        if (-not $Ref) { Write-Output 'checkout braucht einen Branch oder Commit.'; exit 1 }
        if (-not $Force -and -not (Test-Holder)) {
            Write-Output "Nicht oben in der Warteschlange '$benchName'. Zuerst: editor-queue join -Project `"$benchName`" ... und wait (oder -Id angeben)."
            exit 4
        }
        $dirty = Get-Dirty
        if ($dirty -and -not $Discard) {
            Write-Output "Die Testbank hat noch Aenderungen (Testspuren oder Unity-Importspuren wie Sprite-IDs in .meta). Pruefen mit status, verwerfen mit checkout -Discard."
            $dirty -split "`n" | ForEach-Object { Write-Output "  $_" }
            exit 5
        }
        $sha = Git @('rev-parse', '--verify', "$Ref^{commit}")
        if ($sha.code -ne 0) { Write-Output "Unbekannter Branch/Commit: $Ref"; exit 1 }
        # Szene vor jeder Dateiaenderung schliessen (auch vor dem Verwerfen), sonst meldet Unity "modified externally".
        $closed = Close-BenchScene
        if ($closed -eq 'blocked') { exit 6 }
        if ($dirty) {
            $r = Git @('restore', '--staged', '--worktree', '--', '.')
            if ($r.code -ne 0) { Write-Output "Verwerfen fehlgeschlagen: $($r.err)"; Open-BenchScene $closed; exit 1 }
            Write-Output 'Testspuren verworfen.'
            $dirty = Get-Dirty
        }
        if ($dirty) {
            Write-Output "Die Testbank hat noch Aenderungen, die sich nicht verwerfen liessen:"
            $dirty -split "`n" | ForEach-Object { Write-Output "  $_" }
            Open-BenchScene $closed
            exit 5
        }
        # Losgeloest auschecken: der Branch darf gleichzeitig im eigenen Arbeitsordner ausgecheckt bleiben.
        $r = Git @('switch', '--detach', $sha.out)
        if ($r.code -ne 0) { Write-Output "git switch fehlgeschlagen: $($r.err)"; Open-BenchScene $closed; exit 1 }
        $state = [ordered]@{ ref = $Ref; sha = $sha.out; by = $(if ($Id) { $Id } else { $Session }); session = $Session; since = [DateTime]::UtcNow.ToString('o') }
        if (-not (Test-Path -LiteralPath $QueueDir)) { New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null }
        [IO.File]::WriteAllText($stateFile, (ConvertTo-Json -InputObject $state), $utf8)
        Set-Muted
        Write-Output "Testbank steht auf $Ref ($($sha.out.Substring(0, 9))), Ton stumm (fuer Audio-Tests: audio-on)."
        Open-BenchScene $closed
        if ($closed -ne 'ok') { Write-Output "Unity: Assets neu laden lassen (Fenster anklicken oder AssetDatabase.Refresh)." }
        Write-Output "Testen mit:"
        Write-Output "  unity command --project-path `"$Bench`" run_script --file Tests/<Name>Checks.cs"
        exit 0
    }
    'release' {
        $state = Get-State
        $dirty = Get-Dirty
        if ($dirty -and -not $Discard) {
            Write-Output 'Die Testbank hat Aenderungen. Gewollte Aenderungen auf der Testbank committen, Testspuren mit release -Discard verwerfen:'
            $dirty -split "`n" | ForEach-Object { Write-Output "  $_" }
            exit 5
        }
        if ($dirty) {
            # Szene vor dem Verwerfen schliessen, danach frisch oeffnen (kein "modified externally").
            $closed = Close-BenchScene
            if ($closed -eq 'blocked') { exit 6 }
            $r = Git @('restore', '--staged', '--worktree', '--', '.')
            if ($r.code -ne 0) { Write-Output "Verwerfen fehlgeschlagen: $($r.err)"; Open-BenchScene $closed; exit 1 }
            Write-Output 'Testspuren verworfen.'
            Open-BenchScene $closed
        }
        if ($state) {
            $ahead = (Git @('log', '--oneline', "$($state.sha)..HEAD")).out
            if ($ahead) {
                $head = (Git @('rev-parse', 'HEAD')).out
                Write-Output 'Auf der Testbank sind neue Commits entstanden. Im eigenen Arbeitsordner uebernehmen mit:'
                Write-Output "  git merge --ff-only $head"
            }
            Remove-Item -LiteralPath $stateFile -Force
        }
        Set-Muted
        Write-Output "Testbank freigegeben. Jetzt aus der Warteschlange: editor-queue leave -Project `"$benchName`" -Id <id>"
        exit 0
    }
}
