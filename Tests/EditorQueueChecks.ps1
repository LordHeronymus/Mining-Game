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

# 11) Claude-Code-Hook: nur Unity-Aufrufe zaehlen; ein laufender Aufruf schuetzt, Code-Arbeit nicht.
Reset
$env:CLAUDE_CODE_SESSION_ID = 'sess-test-1'
Q @('join', '-Id', 'c', '-Who', 'Chat C') | Out-Null
Remove-Item Env:CLAUDE_CODE_SESSION_ID
Q @('join', '-Id', 'w', '-Who', 'Chat W') | Out-Null
$bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
$hook = Join-Path $root 'Tools/editor-queue-hook.sh'
function Hook([string]$json) { $env:EDITOR_QUEUE_DIR = $dir; $json | & $bash $hook; Remove-Item Env:EDITOR_QUEUE_DIR }
function Busy { (Get-Content $queueFile -Raw | ConvertFrom-Json).entries | Where-Object { $_.id -eq 'c' } | ForEach-Object { $_.busySince } }
Hook '{"session_id":"sess-test-1","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"x.cs"}}'
Hook '{"session_id":"sess-test-1","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git status"}}'
Check 'Hook ignoriert Nicht-Unity-Aufrufe' (-not (Busy))
Hook '{"session_id":"sess-test-1","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"unity command --project-path X run_script --file Tests/A.cs"}}'
Check 'Hook: Unity-Befehl markiert laufenden Aufruf' ([bool](Busy))
Start-Sleep -Seconds 4
Q @('heartbeat', '-Id', 'w') | Out-Null
Q @('status', '-StaleMinutes', '0.05') | Out-Null
Check 'laufender Unity-Aufruf schuetzt Inhaber' (((Ids) -join ',') -eq 'c,w') "ids=$((Ids) -join ',')"
Hook '{"session_id":"sess-test-1","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"command":"unity command --project-path X run_script --file Tests/A.cs"}}'
Check 'Hook: Ende des Aufrufs hebt Schutz auf' (-not (Busy))
Hook '{"session_id":"sess-test-1","hook_event_name":"PreToolUse","tool_name":"mcp__unity__editor_play","tool_input":{}}'
Check 'Hook: Unity-MCP-Werkzeug zaehlt' ([bool](Busy))
Hook '{"session_id":"sess-test-1","hook_event_name":"PostToolUse","tool_name":"mcp__unity__editor_play","tool_input":{}}'
Start-Sleep -Seconds 4
Q @('heartbeat', '-Id', 'w') | Out-Null
Hook '{"session_id":"sess-test-1","hook_event_name":"PostToolUse","tool_name":"Edit","tool_input":{}}'
Q @('status', '-StaleMinutes', '0.05') | Out-Null
Check 'Code aendern gibt den Editor frei (Inhaber verdraengt)' (((Ids) -join ',') -eq 'w') "ids=$((Ids) -join ',')"
$r = Q @('heartbeat', '-Session', 'unbekannt')
Check 'heartbeat per unbekannter Sitzung -> 3' ($r.code -eq 3)

# 11b) Der Benutzer als Inhaber hat die laengere Frist (Standard 10 min), Chats die kurze.
Reset
Q @('join', '-User') | Out-Null
Q @('join', '-Id', 'a', '-Who', 'Chat A') | Out-Null
Start-Sleep -Seconds 4
Q @('heartbeat', '-Id', 'a') | Out-Null
Q @('status', '-StaleMinutes', '0.05') | Out-Null
Check 'Benutzer als Inhaber bekommt laengere Frist' (((Ids) -join ',') -eq 'benutzer,a') "ids=$((Ids) -join ',')"
Q @('status', '-StaleMinutes', '0.05', '-UserStaleMinutes', '0.05') | Out-Null
Check 'Benutzer-Frist einstellbar' (((Ids) -join ',') -eq 'a') "ids=$((Ids) -join ',')"
$j = (Q @('status', '-Json')).out | ConvertFrom-Json
Check 'Standardfristen 2 / 10 min' (($j.staleMinutes -eq 2) -and ($j.userStaleMinutes -eq 10)) "$($j.staleMinutes)/$($j.userStaleMinutes)"

# 12) status -Json liefert maschinenlesbare Daten fuer den lokalen Dienst / PrivOrg.
Reset
Q @('join', '-Id', 'a', '-Who', 'Chat A', '-Purpose', 'Play-Tests') | Out-Null
Q @('join', '-User', '-Purpose', 'selbst testen') | Out-Null
$r = Q @('status', '-Json')
$j = $r.out | ConvertFrom-Json
Check 'status -Json' (($j.project -eq $project) -and ($j.entries.Count -eq 2) -and ($j.entries[1].kind -eq 'user') -and ($j.entries[0].position -eq 1) -and ($null -ne $j.editor.busy)) $r.out

# 13) Lokaler Dienst: Liste lesen, Benutzer an-/abmelden, fremde Herkunft abweisen.
Reset
New-Item -ItemType Directory (Join-Path $dir 'svc') -Force | Out-Null
$svcDir = Join-Path $dir 'svc'
$port = Get-Random -Minimum 48000 -Maximum 48900
$srv = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $root 'Tools\editor-queue-server.ps1')`"", '-Port', $port, '-QueueDir', "`"$svcDir`"", '-AllowOrigin', 'https://privorg.example') -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $dir 'svc.out') -RedirectStandardError (Join-Path $dir 'svc.err')
$base = "http://localhost:$port"
try {
    $ready = $false
    for ($i = 0; $i -lt 40 -and -not $ready; $i++) { try { Invoke-RestMethod "$base/api/queue" -TimeoutSec 2 | Out-Null; $ready = $true } catch { Start-Sleep -Milliseconds 250 } }
    Check 'Dienst startet' $ready
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script join -Id a -Who 'Chat A' -Project 'Mining Game' -QueueDir $svcDir | Out-Null
    $q = Invoke-RestMethod "$base/api/queue"
    $mg = @($q.projects | Where-Object { $_.project -eq 'Mining Game' })[0]
    Check 'Dienst liefert Arbeitsordner und Eintraege' (($q.service -eq 'editor-queue') -and ($mg.entries.Count -eq 1)) ($q | ConvertTo-Json -Depth 6 -Compress)
    $body = [Text.Encoding]::UTF8.GetBytes('{"project":"Mining Game","purpose":"Prüfung über PrivOrg"}')
    $res = Invoke-RestMethod "$base/api/join" -Method Post -ContentType 'application/json; charset=utf-8' -Body $body -Headers @{ Origin = 'http://localhost:3000' }
    $mg = @($res.queue.projects | Where-Object { $_.project -eq 'Mining Game' })[0]
    Check 'Dienst: Benutzer an Position 2, Umlaute erhalten' ($res.ok -and $mg.entries[1].id -eq 'benutzer' -and $mg.entries[1].purpose -eq 'Prüfung über PrivOrg') ($res | ConvertTo-Json -Depth 6 -Compress)
    $code = 0
    try { Invoke-WebRequest "$base/api/leave" -Method Post -ContentType 'application/json' -Body '{"project":"Mining Game"}' -Headers @{ Origin = 'https://evil.example' } -UseBasicParsing | Out-Null }
    catch { $code = [int]$_.Exception.Response.StatusCode }
    Check 'Dienst weist fremde Herkunft ab (403)' ($code -eq 403)
    $pre = Invoke-WebRequest "$base/api/join" -Method Options -Headers @{ Origin = 'https://privorg.example'; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Private-Network' = 'true' } -UseBasicParsing
    Check 'Dienst: Preflight fuer freigegebene Herkunft' (($pre.Headers['Access-Control-Allow-Origin'] -eq 'https://privorg.example') -and ($pre.Headers['Access-Control-Allow-Private-Network'] -eq 'true'))
    $res = Invoke-RestMethod "$base/api/leave" -Method Post -ContentType 'application/json' -Body '{"project":"Mining Game"}'
    $mg = @($res.queue.projects | Where-Object { $_.project -eq 'Mining Game' })[0]
    Check 'Dienst: Benutzer ausgetragen' ($res.ok -and $mg.entries.Count -eq 1)
}
finally { if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force } }

Write-Output ''
if ($failures -eq 0) { Write-Output 'Alle Editor-Warteschlangen-Checks bestanden.'; exit 0 }
Write-Output "$failures Check(s) fehlgeschlagen."
exit 1
