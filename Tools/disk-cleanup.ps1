<#
.SYNOPSIS
  Speicher-Waechter fuer die Mining-Game-Ordner: Sind "Mining Game", "Mining Game 2", "Mining Game 3" ... zusammen
  groesser als das Limit (Standard 100 GB), raeumt er Schritt fuer Schritt auf, bis die Summe wieder darunter liegt.
  Doku: Docs/Speicher-Bereinigung.md

.EXAMPLE
  Tools/disk-cleanup.sh status            # Groesse je Ordner, Limit, geplanter Task, letzte Laeufe
  Tools/disk-cleanup.sh run -DryRun       # zeigt nur, was passieren wuerde (-Force: auch unter dem Limit)
  Tools/disk-cleanup.sh run               # raeumt nur auf, wenn das Limit ueberschritten ist
  Tools/disk-cleanup.sh install           # geplanter Windows-Task: taeglich 12:00 und 15 min nach der Anmeldung
  Tools/disk-cleanup.sh uninstall

  Schritte (danach wird neu gerechnet; liegt die Summe unter dem Limit, ist Schluss):
    1. Alte Git-LFS-Versionen, nur was nachweislich auf GitHub liegt
    2. Arbeitsordner unter .claude/worktrees: Stand vollstaendig in origin/main, keine Aenderungen, Unity zu,
       mindestens 2 Tage unbenutzt (git worktree remove ohne --force; Branch bleibt)
    3. Build Tests: Eintraege, die mindestens 2 Tage alt sind
    4. SceneBackups: Dateien, die mindestens 14 Tage alt sind
    5. git gc
  Nie angefasst: Library, Assets, Design, Spielstaende, Branches, nicht gepushte Commits und die Ordner "Mining Game N".

  Exit-Codes: 0 ok, 1 Fehler, 3 laeuft bereits
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'status', 'install', 'uninstall', 'help')]
    [string]$Command = 'help',
    [double]$LimitGB = 100,
    # Ein Mining-Game-Ordner (Standard: der Ordner, aus dem dieses Skript stammt); gemessen werden er und seine Geschwister "<Name> N".
    [string]$ProjectRoot,
    # Protokoll, Sperre und installierte Kopie (Standard: Git Repos/DiskCleanup, ausserhalb von Git).
    [string]$DataDir,
    [string]$MainRef = 'origin/main',
    [double]$WorktreeIdleDays = 2,
    [double]$BuildAgeDays = 2,
    [double]$BackupAgeDays = 14,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
if ($Command -eq 'help') { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output; exit 0 }

if (-not $ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$repoRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
# Nur ein Arbeitsordner selbst (<Projekt>\.claude\worktrees\<Name>) zaehlt zum Projekt darueber, kein tieferer Pfad darin.
$ownerMatch = [regex]::Match($repoRoot, '^(.*)[\\/]\.claude[\\/]worktrees[\\/][^\\/]+$')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }
$ownerName = Split-Path $ownerRoot -Leaf
$reposDir = Split-Path $ownerRoot -Parent
$worktreesDir = Join-Path $ownerRoot '.claude\worktrees'
if (-not $DataDir) { $DataDir = Join-Path $reposDir 'DiskCleanup' }
$logFile = Join-Path $DataDir 'disk-cleanup.log'
$lockFile = Join-Path $DataDir 'run.lock'
$taskName = "$ownerName Speicher-Bereinigung"

if (-not ('DiskScan' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class DiskScan {
    // Groesse in Bytes; Verknuepfungen (Junctions/Symlinks) werden nicht verfolgt.
    public static long Size(string path) {
        long total = 0;
        DirectoryInfo dir;
        try { dir = new DirectoryInfo(path); if (!dir.Exists) return 0; } catch { return 0; }
        try { foreach (var f in dir.GetFiles()) { try { total += f.Length; } catch { } } } catch { }
        try {
            foreach (var d in dir.GetDirectories()) {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                total += Size(d.FullName);
            }
        } catch { }
        return total;
    }
    // Neueste Schreibzeit (UTC) unter path; Unterordner mit einem Namen aus skipTop werden auf oberster Ebene ausgelassen.
    public static DateTime Newest(string path, string[] skipTop) { return Newest(path, skipTop, true); }
    static DateTime Newest(string path, string[] skipTop, bool top) {
        DateTime newest = DateTime.MinValue;
        DirectoryInfo dir;
        try { dir = new DirectoryInfo(path); if (!dir.Exists) return newest; newest = dir.LastWriteTimeUtc; } catch { return newest; }
        try { foreach (var f in dir.GetFiles()) { try { if (f.LastWriteTimeUtc > newest) newest = f.LastWriteTimeUtc; } catch { } } } catch { }
        try {
            foreach (var d in dir.GetDirectories()) {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (top && skipTop != null && Array.IndexOf(skipTop, d.Name.ToLowerInvariant()) >= 0) continue;
                var t = Newest(d.FullName, skipTop, false);
                if (t > newest) newest = t;
            }
        } catch { }
        return newest;
    }
}
'@
}

# --- Hilfen ---------------------------------------------------------------------

function Format-Size([double]$bytes) { '{0:N1} GB' -f ($bytes / 1GB) }

function Log([string]$msg) {
    Write-Host $msg
    if ($DryRun) { return }
    $line = '{0} {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $msg
    [IO.File]::AppendAllText($logFile, $line + "`r`n", $utf8)
}

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

function Get-LastLine([string]$text) { @($text -split "`n" | Where-Object { $_.Trim() })[-1] }

# "Mining Game", "Mining Game 2", "Mining Game 3" ... (keine anderen Spiele).
function Get-ProjectDirs {
    $pattern = '^' + [regex]::Escape($ownerName) + '( \d+)?$'
    @(Get-ChildItem -LiteralPath $reposDir -Directory -Force |
        Where-Object { $_.Name -match $pattern -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
        Sort-Object Name | ForEach-Object { $_.FullName })
}

function Measure-Projects {
    $sizes = [ordered]@{}
    foreach ($d in Get-ProjectDirs) { $sizes[$d] = [DiskScan]::Size($d) }
    return $sizes
}

function Format-Sizes($sizes) { (@($sizes.Keys | ForEach-Object { '{0} {1}' -f (Split-Path $_ -Leaf), (Format-Size $sizes[$_]) }) -join ', ') }

function Get-CommonGitDir {
    $r = Git $ownerRoot @('rev-parse', '--git-common-dir')
    if ($r.code -ne 0) { return $null }
    $dir = $r.out.Trim()
    if (-not [IO.Path]::IsPathRooted($dir)) { $dir = Join-Path $ownerRoot $dir }
    return [IO.Path]::GetFullPath($dir)
}

# Unity haelt Temp/UnityLockfile exklusiv offen, solange das Projekt geoeffnet ist.
function Test-UnityOpen([string]$root) {
    $lock = Join-Path $root 'Temp\UnityLockfile'
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    try { $s = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $s.Dispose(); return $false } catch { return $true }
}

function Remove-Entry([string]$path) {
    try { Remove-Item -LiteralPath $path -Recurse -Force; return $true }
    catch { Log "    Fehler beim Loeschen von $path`: $($_.Exception.Message)"; return $false }
}

function Get-AgeDays([DateTime]$utc) { ([DateTime]::UtcNow - $utc).TotalDays }

# --- Schritte (Rueckgabe: frei gewordene bzw. bei -DryRun geschaetzte Bytes) ------

function Step-Lfs {
    $common = Get-CommonGitDir
    $lfsDir = if ($common) { Join-Path $common 'lfs\objects' } else { $null }
    if (-not $lfsDir -or -not (Test-Path -LiteralPath $lfsDir)) { Log '1 LFS: kein LFS-Speicher gefunden.'; return 0 }
    $before = [DiskScan]::Size($lfsDir)
    $argv = @('lfs', 'prune', '--verify-remote', '--when-unverified=continue')
    if ($DryRun) { $argv += '--dry-run' }
    $r = Git $ownerRoot $argv
    $text = $r.out + "`n" + $r.err
    if ($r.code -ne 0) { Log "1 LFS: fehlgeschlagen ($(Get-LastLine $text))"; return 0 }
    $missing = [regex]::Match($text, '(\d+) not on remote')
    $kept = if ($missing.Success) { ", $($missing.Groups[1].Value) nicht auf GitHub (bleiben)" } else { '' }
    if ($DryRun) {
        $m = [regex]::Match($text, '(\d+) files? would be pruned \(([\d.,]+)\s*(\w+)\)')
        if (-not $m.Success) { Log "1 LFS: nichts zu entfernen ($(Format-Size $before) LFS-Speicher$kept)."; return 0 }
        $units = @{ 'B' = 1; 'KB' = 1KB; 'MB' = 1MB; 'GB' = 1GB; 'TB' = 1TB }
        $unit = $m.Groups[3].Value.ToUpperInvariant()
        $est = [double]::Parse($m.Groups[2].Value.Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture) * $(if ($units.ContainsKey($unit)) { $units[$unit] } else { 1 })
        Log "1 LFS: wuerde $($m.Groups[1].Value) alte Versionen entfernen (ca. $(Format-Size $est) von $(Format-Size $before)$kept)."
        return [long][math]::Min($est, $before)
    }
    $freed = $before - [DiskScan]::Size($lfsDir)
    $del = [regex]::Match($text, 'Deleting objects: \d+% \(\d+/(\d+)\)')
    $count = if ($del.Success) { $del.Groups[1].Value } else { '0' }
    Log "1 LFS: $count alte Versionen entfernt, $(Format-Size $freed) frei$kept."
    return [long][math]::Max(0, $freed)
}

function Get-Worktrees {
    $list = (Git $ownerRoot @('worktree', 'list', '--porcelain')).out
    $entries = @(); $cur = $null
    foreach ($line in ($list -split "`n")) {
        $line = $line.TrimEnd("`r")
        if ($line -like 'worktree *') { if ($cur) { $entries += $cur }; $cur = @{ path = [IO.Path]::GetFullPath($line.Substring(9)); head = ''; branch = ''; locked = $false; prunable = $false } }
        elseif (-not $cur) { continue }
        elseif ($line -like 'HEAD *') { $cur.head = $line.Substring(5) }
        elseif ($line -like 'branch *') { $cur.branch = $line.Substring(7) -replace '^refs/heads/', '' }
        elseif ($line -like 'locked*') { $cur.locked = $true }
        elseif ($line -like 'prunable*') { $cur.prunable = $true }
    }
    if ($cur) { $entries += $cur }
    return $entries
}

function Step-Worktrees {
    if (-not (Test-Path -LiteralPath $worktreesDir)) { Log '2 Arbeitsordner: keine unter .claude/worktrees.'; return 0 }
    $f = Git $ownerRoot @('fetch', '--quiet', 'origin')
    if ($f.code -ne 0) { Log "2 Arbeitsordner: git fetch fehlgeschlagen, pruefe gegen den letzten bekannten Stand von $MainRef." }
    $main = Git $ownerRoot @('rev-parse', '--verify', "$MainRef^{commit}")
    if ($main.code -ne 0) { Log "2 Arbeitsordner: $MainRef unbekannt, uebersprungen."; return 0 }
    $prefix = $worktreesDir.TrimEnd('\') + '\'
    $freed = 0; $removed = @(); $kept = @()
    foreach ($wt in Get-Worktrees) {
        if (-not $wt.path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $name = Split-Path $wt.path -Leaf
        $why = $null
        if ($wt.prunable -or -not (Test-Path -LiteralPath $wt.path)) { continue }
        if ($wt.locked) { $why = 'gesperrt' }
        elseif ($repoRoot -ieq $wt.path) { $why = 'Skript laeuft von hier' }
        elseif ((Git $ownerRoot @('merge-base', '--is-ancestor', $wt.head, $main.out)).code -ne 0) { $why = "nicht vollstaendig in $MainRef" }
        else {
            $st = Git $wt.path @('--no-optional-locks', 'status', '--porcelain')
            if ($st.code -ne 0) { $why = 'git status fehlgeschlagen' }
            elseif ($st.out) { $why = 'ungespeicherte Aenderungen' }
            elseif (Test-UnityOpen $wt.path) { $why = 'Unity offen' }
            else {
                $newest = [DiskScan]::Newest($wt.path, @('library'))
                $gitFile = Join-Path $wt.path '.git'
                if (Test-Path -LiteralPath $gitFile -PathType Leaf) {
                    $admin = ([IO.File]::ReadAllText($gitFile) -replace '^gitdir:\s*', '').Trim()
                    if (-not [IO.Path]::IsPathRooted($admin)) { $admin = Join-Path $wt.path $admin }
                    $t = [DiskScan]::Newest($admin, @())
                    if ($t -gt $newest) { $newest = $t }
                }
                $age = Get-AgeDays $newest
                if ($age -lt $WorktreeIdleDays) { $why = 'zuletzt benutzt vor {0:N0} h' -f ($age * 24) }
            }
        }
        if ($why) { $kept += "$name ($why)"; continue }
        $size = [DiskScan]::Size($wt.path)
        if ($DryRun) { $freed += $size; $removed += "$name $(Format-Size $size)"; continue }
        $r = Git $ownerRoot @('worktree', 'remove', $wt.path)
        if ($r.code -ne 0) { $kept += "$name (Entfernen fehlgeschlagen: $(Get-LastLine ($r.out + "`n" + $r.err)))"; continue }
        $rest = if (Test-Path -LiteralPath $wt.path) { [DiskScan]::Size($wt.path) } else { 0 }
        $freed += $size - $rest; $removed += "$name $(Format-Size $size)"
    }
    if (-not $DryRun) { Git $ownerRoot @('worktree', 'prune') | Out-Null }
    $verb = if ($DryRun) { 'wuerde entfernen' } else { 'entfernt' }
    $list = if ($removed.Count) { $removed -join ', ' } else { 'keine' }
    Log "2 Arbeitsordner: $verb $list ($(Format-Size $freed))."
    if ($kept.Count) { Log "  behalten: $($kept -join ', ')" }
    return $freed
}

# Ordner, in denen Build Tests / SceneBackups liegen koennen: alle Mining-Game-Ordner und ihre Arbeitsordner.
function Get-ProjectRoots {
    $roots = @(Get-ProjectDirs)
    if (Test-Path -LiteralPath $worktreesDir) { $roots += @(Get-ChildItem -LiteralPath $worktreesDir -Directory -Force | ForEach-Object { $_.FullName }) }
    return $roots
}

function Step-Builds {
    $freed = 0; $done = @()
    foreach ($root in Get-ProjectRoots) {
        $dir = Join-Path $root 'Build Tests'
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($e in @(Get-ChildItem -LiteralPath $dir -Force)) {
            $newest = if ($e.PSIsContainer) { [DiskScan]::Newest($e.FullName, @()) } else { $e.LastWriteTimeUtc }
            if ((Get-AgeDays $newest) -lt $BuildAgeDays) { continue }
            $size = if ($e.PSIsContainer) { [DiskScan]::Size($e.FullName) } else { $e.Length }
            if (-not $DryRun -and -not (Remove-Entry $e.FullName)) { continue }
            $freed += $size; $done += "$(Split-Path $root -Leaf)/Build Tests/$($e.Name)"
        }
    }
    $verb = if ($DryRun) { 'wuerde loeschen' } else { 'geloescht' }
    if ($done.Count) { Log "3 Build Tests: $verb $($done.Count) Eintraege ($(Format-Size $freed)): $($done -join ', ')" }
    else { Log "3 Build Tests: nichts aelter als $BuildAgeDays Tage." }
    return $freed
}

function Step-Backups {
    $freed = 0; $count = 0
    foreach ($root in Get-ProjectRoots) {
        $dir = Join-Path $root 'SceneBackups'
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -File -Recurse -Force)) {
            if ((Get-AgeDays $f.LastWriteTimeUtc) -lt $BackupAgeDays) { continue }
            if (-not $DryRun -and -not (Remove-Entry $f.FullName)) { continue }
            $freed += $f.Length; $count++
        }
        if (-not $DryRun) {
            # Leer gewordene Unterordner entfernen (tiefste zuerst), SceneBackups selbst bleibt.
            Get-ChildItem -LiteralPath $dir -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending |
                Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | ForEach-Object { Remove-Entry $_.FullName | Out-Null }
        }
    }
    $verb = if ($DryRun) { 'wuerde loeschen' } else { 'geloescht' }
    Log ("4 Szenen-Backups: {0} {1} Dateien aelter als {2} Tage ({3})." -f $verb, $count, $BackupAgeDays, (Format-Size $freed))
    return $freed
}

function Step-Gc {
    $common = Get-CommonGitDir
    if (-not $common) { Log '5 git gc: kein Git-Ordner gefunden.'; return 0 }
    $objects = Join-Path $common 'objects'
    $before = [DiskScan]::Size($objects)
    if ($DryRun) { Log "5 git gc: wuerde die Git-Daten verdichten (derzeit $(Format-Size $before))."; return 0 }
    $r = Git $ownerRoot @('gc', '--quiet')
    if ($r.code -ne 0) { Log "5 git gc: fehlgeschlagen ($(Get-LastLine ($r.out + "`n" + $r.err)))"; return 0 }
    $freed = [math]::Max(0, $before - [DiskScan]::Size($objects))
    Log "5 git gc: $(Format-Size $freed) frei."
    return $freed
}

# --- Befehle --------------------------------------------------------------------

function Invoke-Run {
    $limit = [long]($LimitGB * 1GB)
    $sizes = Measure-Projects
    $total = [long]0; foreach ($v in $sizes.Values) { $total += $v }
    $mode = if ($DryRun) { 'Probelauf' } else { 'Lauf' }
    Log "$mode`: $(Format-Size $total) von $(Format-Size $limit) ($(Format-Sizes $sizes))"
    if ($total -le $limit -and -not $Force) { Log 'Unter dem Limit, nichts zu tun.'; return }
    $start = $total
    foreach ($step in 'Step-Lfs', 'Step-Worktrees', 'Step-Builds', 'Step-Backups', 'Step-Gc') {
        $freed = & $step
        $total -= [long]$freed
        if ($total -le $limit -and -not $Force) { break }
    }
    if ($DryRun) { Log "Ergebnis (geschaetzt): $(Format-Size ($start - $total)) frei, danach ca. $(Format-Size $total)."; return }
    $sizes = Measure-Projects
    $after = [long]0; foreach ($v in $sizes.Values) { $after += $v }
    Log "Ergebnis: $(Format-Size ($start - $after)) frei, jetzt $(Format-Size $after) ($(Format-Sizes $sizes))."
    if ($after -gt $limit) { Log 'Warnung: weiterhin ueber dem Limit. Den Rest (Library, Assets, Design, Arbeitsordner mit Aenderungen) prueft der Benutzer selbst.' }
}

New-Item -ItemType Directory -Path $DataDir -Force | Out-Null

switch ($Command) {
    'run' {
        try { $lock = [IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'None') }
        catch { Write-Output 'Die Bereinigung laeuft bereits.'; exit 3 }
        try {
            try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' } catch { }
            Invoke-Run
            # Protokoll kurz halten.
            if ((Test-Path -LiteralPath $logFile) -and (Get-Item -LiteralPath $logFile).Length -gt 1MB) {
                $lines = [IO.File]::ReadAllLines($logFile, $utf8)
                [IO.File]::WriteAllLines($logFile, [string[]]$lines[[math]::Max(0, $lines.Count - 2000)..($lines.Count - 1)], $utf8)
            }
        }
        catch { Log "Fehler: $($_.Exception.Message)"; exit 1 }
        finally { $lock.Dispose() }
        exit 0
    }
    'status' {
        $sizes = Measure-Projects
        $total = [long]0; foreach ($v in $sizes.Values) { $total += $v }
        Write-Output "Ordner: $reposDir\$ownerName*"
        Write-Output "Zusammen: $(Format-Size $total) von $(Format-Size ($LimitGB * 1GB))"
        foreach ($k in $sizes.Keys) { Write-Output ('  {0,-20} {1,10}' -f (Split-Path $k -Leaf), (Format-Size $sizes[$k])) }
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) {
            $info = $task | Get-ScheduledTaskInfo
            $last = if ($info.LastRunTime -and $info.LastRunTime.Year -gt 2000) { '{0:dd.MM.yyyy HH:mm} (Ergebnis {1})' -f $info.LastRunTime, $info.LastTaskResult } else { 'noch nie' }
            Write-Output "Geplanter Task '$taskName': $($task.State), zuletzt $last, naechster Lauf $('{0:dd.MM.yyyy HH:mm}' -f $info.NextRunTime)"
        }
        else { Write-Output "Kein geplanter Task (einrichten mit: Tools/disk-cleanup.sh install)." }
        if (Test-Path -LiteralPath $logFile) {
            Write-Output "Letzte Eintraege ($logFile):"
            Get-Content -LiteralPath $logFile -Tail 15 -Encoding UTF8 | ForEach-Object { Write-Output "  $_" }
        }
        exit 0
    }
    'install' {
        # Kopie in einen festen Ordner ausserhalb von Git: Arbeitsordner wechseln ihren Stand.
        $bin = Join-Path $DataDir 'bin'
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        $target = Join-Path $bin 'disk-cleanup.ps1'
        Copy-Item -LiteralPath $PSCommandPath -Destination $target -Force
        $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $limitArg = $LimitGB.ToString([Globalization.CultureInfo]::InvariantCulture)
        $runArgs = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$target`" run -ProjectRoot `"$ownerRoot`" -DataDir `"$DataDir`" -LimitGB $limitArg"
        # conhost --headless: ohne aufblitzendes Konsolenfenster (z. B. mitten im Spiel).
        $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\conhost.exe') -Argument "--headless `"$ps`" $runArgs" -WorkingDirectory $bin
        $user = "$env:USERDOMAIN\$env:USERNAME"
        $daily = New-ScheduledTaskTrigger -Daily -At '12:00'
        $logon = New-ScheduledTaskTrigger -AtLogOn -User $user
        $logon.Delay = 'PT15M'
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($daily, $logon) -Settings $settings -Principal $principal `
            -Description "Raeumt die Mining-Game-Ordner auf, sobald sie zusammen groesser als $limitArg GB sind. Doku: Docs/Speicher-Bereinigung.md" -Force | Out-Null
        Write-Output "Geplanter Task '$taskName' eingerichtet: taeglich 12:00 (nachgeholt, falls der PC aus war) und 15 min nach der Anmeldung, Limit $limitArg GB."
        Write-Output "Skript kopiert nach $target (nach Aenderungen install erneut ausfuehren)."
        Write-Output "Protokoll: $logFile"
        exit 0
    }
    'uninstall' {
        if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
            Write-Output "Geplanter Task '$taskName' entfernt."
        }
        else { Write-Output 'Kein geplanter Task eingerichtet.' }
        exit 0
    }
}
