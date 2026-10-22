<#
.SYNOPSIS
  Neue Arbeitsordner (git worktree add) ohne Design/ und AudioMastering/ auschecken. Quellen generierter Grafiken und
  Audio-Mastering-Dateien braucht das Spiel nicht, sie kosten aber rund 2,9 GB pro Arbeitsordner.
  Doku: Docs/Arbeitsordner-ohne-Design.md

.EXAMPLE
  Tools/worktree-sparse.sh install                       # Hook eintragen; bestehende Arbeitsordner bleiben, wie sie sind
  Tools/worktree-sparse.sh status                        # Hook da? Welche Arbeitsordner haben Design/?
  Tools/worktree-sparse.sh apply -Path <Arbeitsordner>   # bestehenden Arbeitsordner nachtraeglich ohne Design/
  Tools/worktree-sparse.sh uninstall

  Der Hook steht in .git/hooks/post-checkout (gemeinsam fuer alle Arbeitsordner, nicht in Git versioniert) und wirkt beim
  ersten Auschecken eines neuen verknuepften Arbeitsordners, egal welcher Agent ihn anlegt. Der Hauptordner und
  Arbeitsordner, die beim install schon da waren, bleiben unveraendert.

  Design/ im Arbeitsordner doch brauchen (z. B. Grafikquellen ablegen):
    git sparse-checkout add Design/<Thema>/       # nur dieses Thema (ohne fuehrenden /, sonst macht Git Bash einen Pfad daraus)
    git sparse-checkout disable                   # alles zurueck
  Einen neuen Arbeitsordner gleich vollstaendig anlegen: MG_FULL_CHECKOUT=1 git worktree add ...
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('install', 'uninstall', 'status', 'apply', 'help')]
    [string]$Command = 'help',
    # Arbeitsordner fuer apply.
    [string]$Path,
    # Repository (Standard: das, aus dem dieses Skript stammt).
    [string]$Repo
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
if ($Command -eq 'help') { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output; exit 0 }
if (-not $Repo) { $Repo = Split-Path $PSScriptRoot -Parent }
$Repo = [IO.Path]::GetFullPath($Repo).TrimEnd('\', '/')

$excluded = @('Design', 'AudioMastering')
$patterns = @('/*') + @($excluded | ForEach-Object { "!/$_/" })
$blockStart = '# >>> Mining Game: Arbeitsordner ohne Design (Tools/worktree-sparse.ps1) >>>'
$blockEnd = '# <<< Mining Game: Arbeitsordner ohne Design <<<'
$patternArgs = ($patterns | ForEach-Object { "'$_'" }) -join ' '
$markerName = 'mining-game-sparse'
$block = @"
$blockStart
# Neue verknuepfte Arbeitsordner beim ersten Auschecken ohne $($excluded -join '/ und ')/ (Doku: Docs/Arbeitsordner-ohne-Design.md).
# Markierung $markerName im Git-Verwaltungsordner des Arbeitsordners: danach wird er nie wieder angefasst.
# (Nicht in config.worktree: die kopiert git worktree add in neue Arbeitsordner.)
mg_sparse() {
  [ "`$3" = "1" ] || return 0
  top=`$(git rev-parse --show-toplevel 2>/dev/null) || return 0
  [ -f "`$top/.git" ] || return 0
  marker="`$(git rev-parse --absolute-git-dir 2>/dev/null)/$markerName" || return 0
  [ ! -e "`$marker" ] || return 0
  if [ -n "`$MG_FULL_CHECKOUT" ]; then
    echo voll > "`$marker"
    return 0
  fi
  # Aus einem Sparse-Arbeitsordner angelegt: Git hat dessen Muster uebernommen, so lassen.
  if [ "`$(git config --worktree --get core.sparseCheckout 2>/dev/null)" = "true" ]; then
    echo eigenes-muster > "`$marker"
    return 0
  fi
  # Muster ueber stdin: als Argument wuerde Git Bash /Design/ in einen Windows-Pfad umwandeln.
  if printf '%s\n' $patternArgs | git sparse-checkout set --no-cone --stdin >/dev/null 2>&1; then
    echo ohne-design > "`$marker"
  else
    echo "Hinweis: Arbeitsordner konnte nicht ohne Design/ ausgecheckt werden (Tools/worktree-sparse.sh apply)." >&2
  fi
  return 0
}
mg_sparse "`$@"
$blockEnd
"@ -replace "`r", ''

# Git ohne PowerShell-Fehlerumleitung (stderr eines nativen Befehls wuerde sonst als Fehler gelten).
function Git([string]$dir, [string[]]$argv) {
    $psi = New-Object Diagnostics.ProcessStartInfo 'git'
    $psi.Arguments = (@('-C', $dir) + $argv | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = $utf8
    $p = [Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()
    $out = $p.StandardOutput.ReadToEnd(); $p.WaitForExit()
    return @{ code = $p.ExitCode; out = $out.TrimEnd(); err = $errTask.Result.TrimEnd() }
}

function Get-HookFile {
    $r = Git $Repo @('rev-parse', '--git-path', 'hooks')
    if ($r.code -ne 0) { Write-Output "Kein Git-Repository: $Repo"; exit 1 }
    $dir = $r.out.Trim()
    if (-not [IO.Path]::IsPathRooted($dir)) { $dir = Join-Path $Repo $dir }
    return Join-Path ([IO.Path]::GetFullPath($dir)) 'post-checkout'
}

# Hook-Datei ohne unseren Block (Zeilen ohne CR).
function Get-HookLines([string]$file) {
    if (-not (Test-Path -LiteralPath $file)) { return @() }
    $lines = @([IO.File]::ReadAllText($file, $utf8) -replace "`r", '' -split "`n")
    $out = @(); $skip = $false
    foreach ($l in $lines) {
        if ($l -eq $blockStart) { $skip = $true; continue }
        if ($l -eq $blockEnd) { $skip = $false; continue }
        if (-not $skip) { $out += $l }
    }
    $end = $out.Count - 1
    while ($end -ge 0 -and $out[$end] -eq '') { $end-- }
    if ($end -lt 0) { return @() }
    return @($out[0..$end])
}

function Write-Hook([string]$file, [string[]]$lines) {
    [IO.File]::WriteAllText($file, (($lines -join "`n") + "`n"), $utf8)
}

# Verknuepfte Arbeitsordner (ohne Hauptordner), die noch existieren.
function Get-LinkedWorktrees {
    $list = (Git $Repo @('worktree', 'list', '--porcelain')).out
    $paths = @(); $first = $true
    foreach ($line in ($list -replace "`r", '' -split "`n")) {
        if ($line -notlike 'worktree *') { continue }
        $p = [IO.Path]::GetFullPath($line.Substring(9))
        if ($first) { $first = $false; continue }
        if (Test-Path -LiteralPath (Join-Path $p '.git')) { $paths += $p }
    }
    return $paths
}

# Markierung als Datei im Git-Verwaltungsordner des Arbeitsordners (.git/worktrees/<Name>/), siehe Hook.
function Get-MarkerFile([string]$wt) { Join-Path (Git $wt @('rev-parse', '--absolute-git-dir')).out.Trim() $markerName }
function Get-Marker([string]$wt) {
    $f = Get-MarkerFile $wt
    if (Test-Path -LiteralPath $f) { return ([IO.File]::ReadAllText($f)).Trim() }
    return ''
}
function Set-Marker([string]$wt, [string]$state) { [IO.File]::WriteAllText((Get-MarkerFile $wt), "$state`n") }

switch ($Command) {
    'install' {
        $file = Get-HookFile
        $lines = @(Get-HookLines $file)
        if (-not $lines.Count -or $lines[0] -notlike '#!*') { $lines = @('#!/bin/sh') + $lines }
        # Gleich nach der Shebang-Zeile: der Rest (z. B. git lfs post-checkout) laeuft unveraendert danach und bestimmt den Exit-Code.
        $new = @($lines[0]) + @($block -split "`n") + @($lines | Select-Object -Skip 1)
        New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
        Write-Hook $file $new
        if ((Git $Repo @('config', '--get', 'extensions.worktreeConfig')).out.Trim() -ne 'true') { Git $Repo @('config', 'extensions.worktreeConfig', 'true') | Out-Null }
        # Bestehende Arbeitsordner markieren, damit der Hook sie beim naechsten Branchwechsel nicht umstellt.
        $marked = 0
        foreach ($wt in Get-LinkedWorktrees) {
            # Fruehere Markierung in config.worktree entfernen (wuerde in neue Arbeitsordner kopiert).
            Git $wt @('config', '--worktree', '--unset-all', 'miningGame.sparse') | Out-Null
            if (Get-Marker $wt) { continue }
            $own = (Git $wt @('config', '--worktree', '--get', 'core.sparseCheckout')).out.Trim() -eq 'true'
            Set-Marker $wt $(if ($own) { 'eigenes-muster' } else { 'vorhanden' })
            $marked++
        }
        Git $Repo @('config', '--worktree', '--unset-all', 'miningGame.sparse') | Out-Null
        Write-Output "Hook eingetragen: $file"
        Write-Output "Neue Arbeitsordner entstehen ohne $($excluded -join '/, ')/. $marked bestehende Arbeitsordner bleiben unveraendert (markiert)."
        exit 0
    }
    'uninstall' {
        $file = Get-HookFile
        if (-not (Test-Path -LiteralPath $file)) { Write-Output 'Kein Hook vorhanden.'; exit 0 }
        Write-Hook $file (Get-HookLines $file)
        Write-Output "Block aus $file entfernt (uebrige Hook-Zeilen unveraendert). Sparse-Arbeitsordner bleiben, wie sie sind."
        exit 0
    }
    'status' {
        $file = Get-HookFile
        $installed = (Test-Path -LiteralPath $file) -and ([IO.File]::ReadAllText($file, $utf8) -match [regex]::Escape($blockStart))
        Write-Output ("Hook: {0} ({1})" -f $(if ($installed) { 'eingetragen' } else { 'nicht eingetragen, einrichten mit Tools/worktree-sparse.sh install' }), $file)
        foreach ($wt in Get-LinkedWorktrees) {
            $hasDesign = Test-Path -LiteralPath (Join-Path $wt 'Design')
            $marker = Get-Marker $wt
            Write-Output ('  {0,-28} Design/ {1,-5} {2}' -f (Split-Path $wt -Leaf), $(if ($hasDesign) { 'ja' } else { 'nein' }), $(if ($marker) { "($marker)" } else { '(ohne Markierung)' }))
        }
        exit 0
    }
    'apply' {
        if (-not $Path) { Write-Output 'apply braucht -Path <Arbeitsordner>.'; exit 1 }
        $wt = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
        if (-not (Test-Path -LiteralPath (Join-Path $wt '.git') -PathType Leaf)) { Write-Output "Kein verknuepfter Arbeitsordner (Hauptordner bleibt immer vollstaendig): $wt"; exit 1 }
        $dirty = (Git $wt (@('status', '--porcelain', '--') + $excluded)).out
        if ($dirty) { Write-Output "Abgebrochen: Aenderungen in $($excluded -join '/, ')/:"; Write-Output $dirty; exit 5 }
        $r = Git $wt (@('sparse-checkout', 'set', '--no-cone') + $patterns)
        if ($r.code -ne 0) { Write-Output "sparse-checkout fehlgeschlagen: $($r.err)"; exit 1 }
        Set-Marker $wt 'ohne-design'
        Write-Output "$(Split-Path $wt -Leaf): jetzt ohne $($excluded -join '/, ')/ (zurueck mit: git sparse-checkout disable)."
        exit 0
    }
}
