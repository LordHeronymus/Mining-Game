# Prueft Tools/editor-queue.ps1 in einem isolierten Testordner (Temp/EditorQueueTest), nie im echten Queue-Ordner.
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorQueueChecks.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script = Join-Path $root 'Tools\editor-queue.ps1'
$dir = Join-Path $root 'Temp\EditorQueueTest'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory $dir | Out-Null
$project = 'QueueTest'
$queueFile = Join-Path $dir "$project.json"
$activityFile = Join-Path $dir "$project.activity.json"
$logFile = Join-Path $dir "$project.log"
$failures = 0

function Q([string[]]$argv) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @argv -Project $project -QueueDir $dir
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}
function StartQ([string]$name, [string[]]$argv) {
    $o = Join-Path $dir "$name.out"
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"") + ($argv | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) + @('-Project', $project, '-QueueDir', "`"$dir`"")
    $p = Start-Process powershell.exe -ArgumentList $all -PassThru -NoNewWindow -RedirectStandardOutput $o -RedirectStandardError "$o.err"
    $null = $p.Handle  # sonst bleibt ExitCode leer
    return $p
}
function Ids { if (-not (Test-Path $queueFile)) { return @() }; @((Get-Content $queueFile -Raw | ConvertFrom-Json).entries | ForEach-Object { $_.id }) }
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Output "OK    $name" } else { Write-Output "FEHLER $name $detail"; $script:failures++ }
}
function Reset { Remove-Item (Join-Path $dir "$project.*") -Force -ErrorAction SilentlyContinue }
function SetActivity([bool]$playing, [double]$lastActivityMinutesAgo, [double]$updatedMinutesAgo = 0) {
    $now = [DateTime]::UtcNow
    $obj = [ordered]@{ project = $project; updated = $now.AddMinutes(-$updatedMinutesAgo).ToString('o'); lastActivity = $now.AddMinutes(-$lastActivityMinutesAgo).ToString('o'); lastEvent = 'Test'; isPlaying = $playing; isCompiling = $false; closed = $false }
    [IO.File]::WriteAllText($activityFile, (ConvertTo-Json $obj), (New-Object Text.UTF8Encoding $false))
}

# 1) Gleichzeitige Anmeldungen: alle Eintraege kommen an, keiner geht verloren.
Reset
$procs = 1..8 | ForEach-Object { StartQ "join$_" @('join', '-Id', "p$_", '-Who', "Chat $_", '-Purpose', 'parallel') }
$procs | ForEach-Object { $_.WaitForExit() }
$ids = Ids
Check '8 gleichzeitige joins' (($ids.Count -eq 8) -and (($ids | Sort-Object -Unique).Count -eq 8)) "ids=$($ids -join ',')"

# 2) wait blockiert, bis man oben ist; leave laesst den Naechsten nachruecken.
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Q @('join', '-Id', 'b', '-Who', 'Chat B') | Out-Null
$w = StartQ 'waitB' @('wait', '-Id', 'b', '-PollSeconds', '1', '-TimeoutMinutes', '1')
Start-Sleep -Seconds 3
Check 'wait blockiert solange A oben ist' (-not $w.HasExited)
$r = Q @('leave', '-Id', 'a')
Check 'leave meldet Nachfolger' ($r.out -match 'Jetzt oben: \[b\]') $r.out
$w.WaitForExit(15000) | Out-Null
Check 'wait endet nach Nachruecken mit 0' ($w.HasExited -and $w.ExitCode -eq 0) "exit=$($w.ExitCode)"
$r = Q @('wait', '-Id', 'x')
Check 'wait ohne Eintrag -> 3' ($r.code -eq 3)

# 3) Verdraengung: Inhaber ohne Lebenszeichen, Editor ungenutzt (Stale-Zeit 3 s).
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Q @('join', '-Id', 'b', '-Who', 'Chat B') | Out-Null
$r = Q @('wait', '-Id', 'b', '-PollSeconds', '1', '-StaleMinutes', '0.05', '-TimeoutMinutes', '0.5')
Check 'veralteter Inhaber wird verdraengt, B rueckt nach' (($r.code -eq 0) -and ((Ids) -join ',') -eq 'b') "code=$($r.code) ids=$((Ids) -join ',')"
Check 'Verdraengung im Log vermerkt' ((Get-Content $logFile -Raw) -match 'VERDRAENGT \(Inhaber\) \[a\]')

# 4) Nie verdraengen, solange Play-Mode laeuft.
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Q @('join', '-Id', 'b', '-Who', 'Chat B') | Out-Null
SetActivity $true 10
$r = Q @('wait', '-Id', 'b', '-PollSeconds', '1', '-StaleMinutes', '0.05', '-TimeoutMinutes', '0.15')
Check 'Play-Mode schuetzt Inhaber' (($r.code -eq 2) -and ((Ids) -join ',') -eq 'a,b') "code=$($r.code) ids=$((Ids) -join ',')"

# 5) Nie verdraengen bei frischer Editor-Aktivitaet; danach (Aktivitaet alt) schon.
SetActivity $false 0
$r = Q @('wait', '-Id', 'b', '-PollSeconds', '1', '-StaleMinutes', '0.05', '-TimeoutMinutes', '0.02')
Check 'frische Editor-Aktivitaet schuetzt Inhaber' (($r.code -eq 2) -and ((Ids) -join ',') -eq 'a,b') "code=$($r.code)"
SetActivity $false 10
$r = Q @('wait', '-Id', 'b', '-PollSeconds', '1', '-StaleMinutes', '0.05', '-TimeoutMinutes', '0.3')
Check 'alte Editor-Aktivitaet: Inhaber wird verdraengt' (($r.code -eq 0) -and ((Ids) -join ',') -eq 'b') "code=$($r.code)"

# 6) Wartende ohne Lebenszeichen fallen weg, wartender Benutzer bleibt.
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Q @('join', '-Id', 'c', '-Who', 'Chat C') | Out-Null
Q @('join', '-User') | Out-Null
Start-Sleep -Seconds 4
Q @('heartbeat', '-Id', 'a') | Out-Null
$r = Q @('status', '-StaleMinutes', '0.05')
Check 'wartender Chat ohne Lebenszeichen entfernt, Benutzer bleibt' (((Ids) -join ',') -eq 'a,benutzer') "ids=$((Ids) -join ',')"

# 7) Wer gerade nach oben rueckt, bekommt die volle Frist (kein sofortiges Verdraengen).
Reset
Q @('join', '-User') | Out-Null
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Start-Sleep -Seconds 4
Q @('heartbeat', '-Id', 'a') | Out-Null
Q @('leave', '-User') | Out-Null
$r = Q @('status', '-StaleMinutes', '0.05')
Check 'Nachruecker hat volle Frist' (((Ids) -join ',') -eq 'a') "ids=$((Ids) -join ',')"

# 8) Benutzer als Inhaber bleibt durch Editor-Aktivitaet am Leben, ohne eigenes Lebenszeichen.
Reset
Q @('join', '-User', '-Purpose', 'selbst testen') | Out-Null
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Start-Sleep -Seconds 4
SetActivity $false 0
$r = Q @('wait', '-Id', 'a', '-PollSeconds', '1', '-StaleMinutes', '0.05', '-TimeoutMinutes', '0.02')
Check 'Benutzer durch Editor-Aktivitaet geschuetzt' (($r.code -eq 2) -and ((Ids) -join ',') -eq 'benutzer,a') "code=$($r.code) ids=$((Ids) -join ',')"

# 9) Gehaltene Sperre blockiert Schreiber, bis sie frei wird.
Reset
$lock = [IO.File]::Open((Join-Path $dir "$project.lock"), 'OpenOrCreate', 'ReadWrite', 'None')
$p = StartQ 'locked' @('join', '-Id', 'z', '-Who', 'Chat Z')
Start-Sleep -Seconds 2
Check 'join wartet auf Sperre' (-not $p.HasExited)
$lock.Dispose()
$p.WaitForExit(15000) | Out-Null
Check 'join nach Freigabe erfolgreich' ($p.HasExited -and $p.ExitCode -eq 0 -and ((Ids) -join ',') -eq 'z')

# 10) Benutzer reiht sich direkt hinter dem Inhaber ein, ohne ihn zu verdraengen.
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Q @('join', '-Id', 'b', '-Who', 'Chat B') | Out-Null
$r = Q @('join', '-User', '-Purpose', 'selbst testen')
Check 'Benutzer an Position 2' ((((Ids) -join ',') -eq 'a,benutzer,b') -and $r.out -match 'Position 2') "ids=$((Ids) -join ',')"
Reset
Q @('join', '-User') | Out-Null
Check 'Benutzer in leerer Liste oben' (((Ids) -join ',') -eq 'benutzer')

# 11) Claude-Code-Hook setzt das Lebenszeichen ueber die Sitzungs-ID (join merkt sie aus CLAUDE_CODE_SESSION_ID).
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
$env:CLAUDE_CODE_SESSION_ID = 'sess-test-1'
Q @('join', '-Id', 'c', '-Who', 'Chat C') | Out-Null
Remove-Item Env:CLAUDE_CODE_SESSION_ID
Start-Sleep -Seconds 4
$bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
$env:EDITOR_QUEUE_DIR = $dir; $env:EDITOR_QUEUE_HOOK_SECONDS = '0'
'{"session_id":"sess-test-1","hook_event_name":"PostToolUse"}' | & $bash (Join-Path $root 'Tools/editor-queue-hook.sh')
'{"session_id":"unbekannt"}' | & $bash (Join-Path $root 'Tools/editor-queue-hook.sh')
Remove-Item Env:EDITOR_QUEUE_DIR, Env:EDITOR_QUEUE_HOOK_SECONDS
Q @('heartbeat', '-Id', 'a') | Out-Null
$r = Q @('status', '-StaleMinutes', '0.05')
Check 'Hook haelt wartenden Chat am Leben' (((Ids) -join ',') -eq 'a,c') "ids=$((Ids) -join ',')"
$r = Q @('heartbeat', '-Session', 'sess-test-1')
Check 'heartbeat per -Session' ($r.code -eq 0 -and $r.out -match 'Position 2') $r.out

Write-Output ''
if ($failures -eq 0) { Write-Output 'Alle Editor-Warteschlangen-Checks bestanden.'; exit 0 }
Write-Output "$failures Check(s) fehlgeschlagen."
exit 1
