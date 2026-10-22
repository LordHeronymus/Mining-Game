# Prueft Tools/disk-cleanup.ps1 in einem isolierten Testordner (Temp/DiskCleanupTest), nie an den echten Mining-Game-Ordnern.
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tests/DiskCleanupChecks.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script = Join-Path $root 'Tools\disk-cleanup.ps1'
$t = Join-Path $root 'Temp\DiskCleanupTest'
if (Test-Path $t) { Remove-Item $t -Recurse -Force }
$repos = Join-Path $t 'Repos'
$project = Join-Path $repos 'Mining Game'
$wts = Join-Path $project '.claude\worktrees'
$dataDir = Join-Path $repos 'DiskCleanup'
$failures = 0

function G([string]$dir) {
    $argv = $args
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $out = & git -C $dir @argv 2>&1
    $code = $LASTEXITCODE; $ErrorActionPreference = $old
    if ($code -ne 0) { throw "git $($argv -join ' '): $out" }
}
function Run([string[]]$argv) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @argv -ProjectRoot $project
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Output "OK    $name" } else { Write-Output "FEHLER $name $detail"; $script:failures++ }
}
function NewFile([string]$path, [int]$kb = 1) {
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllBytes($path, (New-Object byte[] ($kb * 1024)))
}
function Backdate([string]$path, [double]$days) {
    $when = (Get-Date).AddDays(-$days)
    Get-ChildItem -LiteralPath $path -Recurse -Force | ForEach-Object { $_.LastWriteTime = $when }
    (Get-Item -LiteralPath $path -Force).LastWriteTime = $when
}
function AddWorktree([string]$name, [string]$branch) { G $project worktree add (Join-Path $wts $name) -b $branch main }

# --- Aufbau: Repo mit Remote, Arbeitsordner in verschiedenen Zustaenden, Geschwisterordner ----------------------
New-Item -ItemType Directory -Path $project -Force | Out-Null
G $t init --bare -b main origin.git
G $project init -b main
G $project config user.name Test
G $project config user.email test@example.invalid
[IO.File]::WriteAllText((Join-Path $project '.gitignore'), "/Library/`n/Temp/`n/Build Tests/`n/SceneBackups/`n/.claude/worktrees/`n")
[IO.File]::WriteAllText((Join-Path $project 'readme.txt'), 'v1')
G $project add -A
G $project commit -q -m init
G $project remote add origin (Join-Path $t 'origin.git')
G $project push -q -u origin main

AddWorktree 'merged-old' 't/merged-old'
NewFile (Join-Path $wts 'merged-old\Library\big.bin') 2048
AddWorktree 'merged-new' 't/merged-new'
AddWorktree 'unmerged-old' 't/unmerged-old'
[IO.File]::WriteAllText((Join-Path $wts 'unmerged-old\readme.txt'), 'v2')
G (Join-Path $wts 'unmerged-old') commit -q -am 'nur lokal'
AddWorktree 'dirty-old' 't/dirty-old'
[IO.File]::WriteAllText((Join-Path $wts 'dirty-old\readme.txt'), 'geaendert')
AddWorktree 'untracked-old' 't/untracked-old'
[IO.File]::WriteAllText((Join-Path $wts 'untracked-old\neu.txt'), 'neu')
foreach ($n in 'merged-old', 'unmerged-old', 'dirty-old', 'untracked-old') {
    Backdate (Join-Path $wts $n) 5
    Backdate (Join-Path $project ".git\worktrees\$n") 5
}

NewFile (Join-Path $project 'Build Tests\alt\a.bin') 512
Backdate (Join-Path $project 'Build Tests\alt') 5
NewFile (Join-Path $project 'Build Tests\neu\b.bin') 512
$bench = Join-Path $repos 'Mining Game 2'
NewFile (Join-Path $bench 'SceneBackups\alt.unity') 256
NewFile (Join-Path $bench 'SceneBackups\sub\alt2.unity') 256
Backdate (Join-Path $bench 'SceneBackups') 20
NewFile (Join-Path $bench 'SceneBackups\neu.unity') 256
NewFile (Join-Path $repos 'Other Game\Build Tests\alt\c.bin') 512
Backdate (Join-Path $repos 'Other Game\Build Tests') 5
NewFile (Join-Path $repos 'Mining Game Extra\x.bin') 64

# 0) Sicherung: das Skript muss den Testordner meinen, nie die echten Ordner (sonst sofort abbrechen).
$r = Run @('status', '-LimitGB', '100')
if ($r.out -notmatch [regex]::Escape("Ordner: $repos\Mining Game*")) { Write-Output "ABBRUCH: Skript meint nicht den Testordner:`n$($r.out)"; exit 1 }

# 1) status: nur "Mining Game" und "Mining Game N" zaehlen.
Check 'status zaehlt Mining Game und Mining Game 2' (($r.out -match 'Mining Game 2') -and ($r.out -match '(?m)^\s+Mining Game\s')) $r.out
Check 'status ignoriert andere Ordner' (($r.out -notmatch 'Other Game') -and ($r.out -notmatch 'Mining Game Extra')) $r.out

# 2) Unter dem Limit passiert nichts.
$r = Run @('run', '-LimitGB', '100')
Check 'unter dem Limit: nichts zu tun' (($r.code -eq 0) -and ($r.out -match 'nichts zu tun') -and (Test-Path (Join-Path $wts 'merged-old'))) $r.out

# 3) Probelauf: meldet, loescht nichts, schreibt kein Protokoll.
$logBefore = @(Get-Content (Join-Path $dataDir 'disk-cleanup.log') -ErrorAction SilentlyContinue).Count
$r = Run @('run', '-LimitGB', '0', '-DryRun')
Check 'Probelauf nennt merged-old' ($r.out -match 'wuerde entfernen merged-old') $r.out
Check 'Probelauf loescht nichts' ((Test-Path (Join-Path $wts 'merged-old')) -and (Test-Path (Join-Path $project 'Build Tests\alt')) -and (Test-Path (Join-Path $bench 'SceneBackups\alt.unity')))
Check 'Probelauf schreibt kein Protokoll' (@(Get-Content (Join-Path $dataDir 'disk-cleanup.log') -ErrorAction SilentlyContinue).Count -eq $logBefore)

# 4) Laeuft bereits: zweite Instanz bricht mit 3 ab.
$lock = [IO.File]::Open((Join-Path $dataDir 'run.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$r = Run @('run', '-LimitGB', '0')
$lock.Dispose()
Check 'zweite Instanz -> Exit 3' ($r.code -eq 3) "code=$($r.code) $($r.out)"

# 5) Echter Lauf ueber dem Limit (0 GB): alle Schritte.
$r = Run @('run', '-LimitGB', '0')
Check 'Lauf endet mit 0' ($r.code -eq 0) $r.out
Check 'erledigter, alter Arbeitsordner entfernt (inkl. Library)' (-not (Test-Path (Join-Path $wts 'merged-old'))) $r.out
Check 'Branch des entfernten Arbeitsordners bleibt' ((& git -C $project branch --list 't/merged-old') -match 'merged-old')
Check 'frisch benutzter Arbeitsordner bleibt' (Test-Path (Join-Path $wts 'merged-new\readme.txt'))
Check 'Arbeitsordner mit nicht gepushtem Commit bleibt' (Test-Path (Join-Path $wts 'unmerged-old\readme.txt'))
Check 'Arbeitsordner mit Aenderungen bleibt' ((Get-Content (Join-Path $wts 'dirty-old\readme.txt')) -eq 'geaendert')
Check 'Arbeitsordner mit neuer Datei bleibt' (Test-Path (Join-Path $wts 'untracked-old\neu.txt'))
Check 'Gruende im Protokoll' ($r.out -match 'unmerged-old \(nicht vollstaendig' -and $r.out -match 'dirty-old \(ungespeicherte' -and $r.out -match 'merged-new \(zuletzt benutzt') $r.out
Check 'alter Build geloescht, neuer bleibt' ((-not (Test-Path (Join-Path $project 'Build Tests\alt'))) -and (Test-Path (Join-Path $project 'Build Tests\neu\b.bin')))
Check 'alte Szenen-Backups geloescht, neue bleiben' ((-not (Test-Path (Join-Path $bench 'SceneBackups\alt.unity'))) -and (Test-Path (Join-Path $bench 'SceneBackups\neu.unity')))
Check 'leerer Backup-Unterordner entfernt' (-not (Test-Path (Join-Path $bench 'SceneBackups\sub')))
Check 'andere Spiele unberuehrt' ((Test-Path (Join-Path $repos 'Other Game\Build Tests\alt\c.bin')) -and (Test-Path (Join-Path $repos 'Mining Game Extra\x.bin')))
Check 'git gc gelaufen' ($r.out -match '5 git gc: [\d.,]+ GB frei') $r.out
$log = Get-Content (Join-Path $dataDir 'disk-cleanup.log') -Raw -Encoding UTF8
Check 'Protokoll mit Ergebnis' ($log -match 'Ergebnis: ') $log

Remove-Item $t -Recurse -Force
if ($failures) { Write-Output "$failures Fehler"; exit 1 }
Write-Output 'Alle Pruefungen bestanden.'
exit 0
