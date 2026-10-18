<#
.SYNOPSIS
  Testbank fuer Chats: holt einen Branch/Commit in den Arbeitsordner "Mining Game 2", dessen Unity Editor
  den Chats gehoert (der Editor von "Mining Game" bleibt fuer den Benutzer frei).
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  Tools/editor-bench.sh status
  Tools/editor-bench.sh checkout claude/mein-thema -Id q1a2b3     # nur als oberster Eintrag; -Discard verwirft Reste
  Tools/editor-bench.sh release -Id q1a2b3                         # danach; -Discard verwirft Testspuren

  Exit-Codes: 0 ok, 1 Fehler, 4 nicht oben in der Warteschlange, 5 Testbank hat Aenderungen.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('status', 'checkout', 'release', 'help')]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Ref,
    [string]$Id,
    [string]$Session,
    [string]$Bench,
    [string]$QueueDir,
    [switch]$Discard,
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

function Show-Status {
    $head = Git @('rev-parse', '--short', 'HEAD')
    $branch = (Git @('symbolic-ref', '-q', '--short', 'HEAD')).out
    $state = Get-State
    Write-Output "Testbank: $Bench"
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
    'checkout' {
        if (-not $Ref) { Write-Output 'checkout braucht einen Branch oder Commit.'; exit 1 }
        if (-not $Force -and -not (Test-Holder)) {
            Write-Output "Nicht oben in der Warteschlange '$benchName'. Zuerst: editor-queue join -Project `"$benchName`" ... und wait (oder -Id angeben)."
            exit 4
        }
        $dirty = Get-Dirty
        if ($dirty -and $Discard) {
            $r = Git @('restore', '--staged', '--worktree', '--', '.')
            if ($r.code -ne 0) { Write-Output "Verwerfen fehlgeschlagen: $($r.err)"; exit 1 }
            Write-Output 'Testspuren verworfen.'
            $dirty = Get-Dirty
        }
        if ($dirty) {
            Write-Output "Die Testbank hat noch Aenderungen (Testspuren oder Unity-Importspuren wie Sprite-IDs in .meta). Pruefen mit status, verwerfen mit checkout -Discard."
            $dirty -split "`n" | ForEach-Object { Write-Output "  $_" }
            exit 5
        }
        $sha = Git @('rev-parse', '--verify', "$Ref^{commit}")
        if ($sha.code -ne 0) { Write-Output "Unbekannter Branch/Commit: $Ref"; exit 1 }
        # Losgeloest auschecken: der Branch darf gleichzeitig im eigenen Arbeitsordner ausgecheckt bleiben.
        $r = Git @('switch', '--detach', $sha.out)
        if ($r.code -ne 0) { Write-Output "git switch fehlgeschlagen: $($r.err)"; exit 1 }
        $state = [ordered]@{ ref = $Ref; sha = $sha.out; by = $(if ($Id) { $Id } else { $Session }); session = $Session; since = [DateTime]::UtcNow.ToString('o') }
        if (-not (Test-Path -LiteralPath $QueueDir)) { New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null }
        [IO.File]::WriteAllText($stateFile, (ConvertTo-Json -InputObject $state), $utf8)
        Write-Output "Testbank steht auf $Ref ($($sha.out.Substring(0, 9)))."
        Write-Output "Unity: Assets neu laden lassen, dann testen mit"
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
        if ($dirty) { $r = Git @('restore', '--staged', '--worktree', '--', '.'); if ($r.code -ne 0) { Write-Output "Verwerfen fehlgeschlagen: $($r.err)"; exit 1 } ; Write-Output 'Testspuren verworfen.' }
        if ($state) {
            $ahead = (Git @('log', '--oneline', "$($state.sha)..HEAD")).out
            if ($ahead) {
                $head = (Git @('rev-parse', 'HEAD')).out
                Write-Output 'Auf der Testbank sind neue Commits entstanden. Im eigenen Arbeitsordner uebernehmen mit:'
                Write-Output "  git merge --ff-only $head"
            }
            Remove-Item -LiteralPath $stateFile -Force
        }
        Write-Output "Testbank freigegeben. Jetzt aus der Warteschlange: editor-queue leave -Project `"$benchName`" -Id <id>"
        exit 0
    }
}
