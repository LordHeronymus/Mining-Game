<#
.SYNOPSIS
  Anmeldeliste mit Warteschlange fuer den Unity Editor eines Arbeitsordners.
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 join -Who "Claude Code: Eisen" -Purpose "Play-Tests"
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 wait -Id q1a2b3 -TimeoutMinutes 9
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 leave -Id q1a2b3
  (Git Bash: Tools/editor-queue.sh mit denselben Argumenten)

  Exit-Codes: 0 = ok / du bist oben, 1 = Fehler, 2 = wait-Zeitlimit erreicht (noch nicht oben),
              3 = Eintrag nicht (mehr) in der Warteschlange.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('join', 'status', 'wait', 'heartbeat', 'leave', 'help')]
    [string]$Command = 'help',
    [string]$Id,
    [string]$Who,
    [string]$Session,
    [string]$Purpose,
    [switch]$User,
    [string]$Project,
    [string]$QueueDir,
    [double]$TimeoutMinutes = 0,
    [int]$PollSeconds = 15,
    [double]$StaleMinutes = 0
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$inv = [Globalization.CultureInfo]::InvariantCulture

# Ab diesem Alter gilt das Aktivitaetssignal des Editors als nicht mehr aktuell (Editor schreibt alle 30 s).
$FreshMinutes = 2
# Haengt der Editor laenger als so lange (Prozess laeuft, aber kein Signal), wird er nicht mehr als belegt gewertet.
$BlockedMaxMinutes = 30

if ($Command -eq 'help') {
    Get-Help $PSCommandPath -Detailed | Out-String | Write-Output
    Write-Output 'Befehle: join | status | wait | heartbeat | leave   (Details: Docs/Editor-Warteschlange.md)'
    exit 0
}

# --- Ordner und Dateien -------------------------------------------------------

$repoRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
$ownerMatch = [regex]::Match($repoRoot, '^(.*?)[\\/]\.claude[\\/]worktrees[\\/]')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }

if (-not $Project) { $Project = Split-Path $ownerRoot -Leaf }
if (-not $QueueDir) { $QueueDir = $env:EDITOR_QUEUE_DIR }
if (-not $QueueDir) { $QueueDir = Join-Path (Split-Path $ownerRoot -Parent) 'EditorQueue' }
if ($StaleMinutes -le 0 -and $env:EDITOR_QUEUE_STALE_MINUTES) { $StaleMinutes = [double]::Parse($env:EDITOR_QUEUE_STALE_MINUTES, $inv) }
if ($StaleMinutes -le 0) { $StaleMinutes = 10 }
if ($PollSeconds -lt 1) { $PollSeconds = 1 }

if (-not (Test-Path -LiteralPath $QueueDir)) { New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null }
$queueFile = Join-Path $QueueDir "$Project.json"
$lockFile = Join-Path $QueueDir "$Project.lock"
$logFile = Join-Path $QueueDir "$Project.log"
$activityFile = Join-Path $QueueDir "$Project.activity.json"

# --- Hilfsfunktionen ----------------------------------------------------------

function Now { [DateTime]::UtcNow }
function Iso([DateTime]$t) { $t.ToUniversalTime().ToString('o', $inv) }

function ToUtc($value) {
    if ($null -eq $value -or "$value" -eq '') { return $null }
    if ($value -is [DateTime]) { return $value.ToUniversalTime() }
    return [DateTime]::Parse([string]$value, $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

function AgeMinutes($value) {
    $t = ToUtc $value
    if ($null -eq $t) { return [double]::MaxValue }
    return ((Now) - $t).TotalMinutes
}

function FormatAge([double]$minutes) {
    if ($minutes -ge 1e6) { return 'nie' }
    $s = [int][Math]::Max(0, [Math]::Round($minutes * 60))
    if ($s -lt 60) { return "vor $s s" }
    if ($s -lt 3600) { return ('vor {0}:{1:00} min' -f [int][Math]::Floor($s / 60), ($s % 60)) }
    return ('vor {0:0.0} h' -f ($s / 3600.0))
}

function LocalTime($value) {
    $t = ToUtc $value
    if ($null -eq $t) { return '-' }
    return $t.ToLocalTime().ToString('dd.MM. HH:mm:ss', $inv)
}

function Set-Prop($obj, [string]$name, $value) {
    if ($obj.PSObject.Properties[$name]) { $obj.$name = $value }
    else { $obj | Add-Member -NotePropertyName $name -NotePropertyValue $value }
}

function Get-Prop($obj, [string]$name) {
    if ($null -ne $obj -and $obj.PSObject.Properties[$name]) { return $obj.$name }
    return $null
}

function Write-Log([string]$text) {
    $line = '{0}  {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $inv), $text
    [IO.File]::AppendAllText($logFile, $line + "`r`n", $utf8)
}

function Describe($e) {
    $s = "[$($e.id)] $($e.who)"
    if ($e.session) { $s += " (Sitzung $($e.session))" }
    return $s
}

# Exklusive Sperre ueber den Freigabemodus: wird beim Prozessende vom System freigegeben, nie verwaist.
function Enter-Lock {
    $deadline = (Get-Date).AddSeconds(30)
    while ($true) {
        try { return [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [System.IO.IOException] {
            if ((Get-Date) -gt $deadline) { throw "Sperre $lockFile nach 30 s nicht erhalten." }
            Start-Sleep -Milliseconds (Get-Random -Minimum 40 -Maximum 160)
        }
    }
}

function Read-Queue {
    $list = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath $queueFile) {
        $text = [IO.File]::ReadAllText($queueFile, $utf8)
        if ($text.Trim()) {
            $data = $text | ConvertFrom-Json
            foreach ($e in @($data.entries)) { if ($null -ne $e) { [void]$list.Add($e) } }
        }
    }
    foreach ($e in $list) { foreach ($p in 'id', 'who', 'session', 'purpose', 'kind', 'joined', 'heartbeat', 'topSince') { if (-not $e.PSObject.Properties[$p]) { Set-Prop $e $p $null } } }
    return , $list
}

function Write-Queue($list) {
    $obj = [ordered]@{ project = $Project; updated = (Iso (Now)); entries = @($list) }
    $json = ConvertTo-Json -InputObject $obj -Depth 5
    $tmp = "$queueFile.tmp"
    [IO.File]::WriteAllText($tmp, $json, $utf8)
    if (-not (Test-Path -LiteralPath $queueFile)) { [IO.File]::Move($tmp, $queueFile); return }
    # Ein Leser ohne Sperre (Editor, Texteditor) kann das Ersetzen kurz verhindern: erneut versuchen.
    for ($i = 0; ; $i++) {
        try { [IO.File]::Replace($tmp, $queueFile, [NullString]::Value); return }
        catch {
            if ($i -ge 30) { throw }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Test-UnityRunning {
    try { $procs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction Stop) }
    catch { return $false }
    foreach ($p in $procs) {
        $m = [regex]::Match([string]$p.CommandLine, '-projectpath\s+(?:"([^"]+)"|(\S+))', 'IgnoreCase')
        if (-not $m.Success) { continue }
        $path = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        if ((Split-Path $path.TrimEnd('\', '/') -Leaf) -eq $Project) { return $true }
    }
    return $false
}

# Objektives Signal aus Assets/Editor/EditorQueueActivity.cs. busy = Editor wird nachweislich genutzt.
function Get-EditorState {
    $act = $null
    if (Test-Path -LiteralPath $activityFile) {
        for ($i = 0; $i -lt 3 -and $null -eq $act; $i++) {
            try { $act = [IO.File]::ReadAllText($activityFile, $utf8) | ConvertFrom-Json }
            catch { Start-Sleep -Milliseconds 100 }
        }
    }
    if ($null -ne $act) {
        $updAge = AgeMinutes (Get-Prop $act 'updated')
        $lastAge = AgeMinutes (Get-Prop $act 'lastActivity')
        $evt = Get-Prop $act 'lastEvent'
        if (Get-Prop $act 'closed') {
            return @{ busy = $false; text = "Editor geschlossen ($(FormatAge $updAge))" }
        }
        if ($updAge -le $FreshMinutes) {
            if (Get-Prop $act 'isPlaying') { return @{ busy = $true; text = 'Play-Mode laeuft' } }
            if (Get-Prop $act 'isCompiling') { return @{ busy = $true; text = 'Editor kompiliert/importiert' } }
            $txt = "letzte Aktivitaet $(FormatAge $lastAge): $evt"
            return @{ busy = ($lastAge -lt $StaleMinutes); text = $txt }
        }
        if (Test-UnityRunning) {
            if ($updAge -le $BlockedMaxMinutes) { return @{ busy = $true; text = "Editor offen, antwortet seit $(FormatAge $updAge) nicht (beschaeftigt?)" } }
            return @{ busy = $false; text = "Editor offen, ohne Aktivitaetssignal seit $(FormatAge $updAge)" }
        }
        return @{ busy = $false; text = "Editor nicht offen (letztes Signal $(FormatAge $updAge))" }
    }
    if (Test-UnityRunning) { return @{ busy = $false; text = 'Editor offen, aber ohne Aktivitaetssignal (EditorQueueActivity.cs fehlt?)' } }
    return @{ busy = $false; text = 'Editor nicht offen, kein Aktivitaetssignal' }
}

# Raeumt verwaiste Eintraege ab und setzt den Startzeitpunkt des obersten Eintrags.
function Update-Queue($list) {
    $now = Now
    for ($i = $list.Count - 1; $i -ge 1; $i--) {
        $e = $list[$i]
        if ($e.kind -eq 'user') { continue }
        $age = AgeMinutes $e.heartbeat
        if ($age -gt $StaleMinutes) {
            Write-Log "VERDRAENGT (wartend) $(Describe $e): kein Lebenszeichen seit $(FormatAge $age)"
            Write-Host "Verdraengt (wartend, kein Lebenszeichen $(FormatAge $age)): $(Describe $e)"
            $list.RemoveAt($i)
        }
    }
    if ($list.Count -gt 0) {
        $top = $list[0]
        if ($top.topSince) {
            $age = [Math]::Min((AgeMinutes $top.heartbeat), (AgeMinutes $top.topSince))
            if ($age -gt $StaleMinutes) {
                $ed = Get-EditorState
                if (-not $ed.busy) {
                    Write-Log "VERDRAENGT (Inhaber) $(Describe $top): kein Lebenszeichen seit $(FormatAge $age), Editor ungenutzt ($($ed.text))"
                    Write-Host "Verdraengt (Inhaber inaktiv, $($ed.text)): $(Describe $top)"
                    $list.RemoveAt(0)
                }
            }
        }
    }
    if ($list.Count -gt 0 -and -not $list[0].topSince) {
        $list[0].topSince = Iso $now
        Write-Log "OBEN $(Describe $list[0]) darf den Editor nutzen"
    }
}

function Find-Index($list) {
    if ($Id) {
        for ($i = 0; $i -lt $list.Count; $i++) { if ($list[$i].id -eq $Id) { return $i } }
        return -1
    }
    if ($Who) {
        $hits = @(for ($i = 0; $i -lt $list.Count; $i++) { if ($list[$i].who -eq $Who) { $i } })
        if ($hits.Count -eq 1) { return $hits[0] }
        if ($hits.Count -gt 1) { throw "Mehrere Eintraege mit -Who '$Who'. Bitte -Id angeben." }
        return -1
    }
    if ($Session) {
        for ($i = 0; $i -lt $list.Count; $i++) { if ($list[$i].session -eq $Session) { return $i } }
    }
    return -1
}

function Show-Queue($list) {
    $ed = Get-EditorState
    Write-Output "Editor-Warteschlange '$Project' ($queueFile)"
    Write-Output "Editor: $($ed.text)$(if ($ed.busy) { ' [in Benutzung]' })"
    if ($list.Count -eq 0) { Write-Output '  (leer - Editor frei)'; return }
    for ($i = 0; $i -lt $list.Count; $i++) {
        $e = $list[$i]
        $mark = if ($i -eq 0) { '  <- nutzt den Editor' } else { '' }
        $p = if ($e.purpose) { " - $($e.purpose)" } else { '' }
        Write-Output ("  {0}. {1}{2} | seit {3} | Lebenszeichen {4}{5}" -f ($i + 1), (Describe $e), $p, (LocalTime $e.joined), (FormatAge (AgeMinutes $e.heartbeat)), $mark)
    }
}

function Invoke-Locked([scriptblock]$body) {
    $lock = Enter-Lock
    try {
        $list = Read-Queue
        # Eigenes Lebenszeichen vor dem Aufraeumen, sonst verdraengt ein spaeter wait-Aufruf sich selbst.
        $own = Find-Index $list
        if ($own -ge 0) { $list[$own].heartbeat = Iso (Now) }
        Update-Queue $list
        $result = & $body $list
        Write-Queue $list
        return $result
    }
    finally { $lock.Dispose() }
}

if ($User) {
    if (-not $Id) { $Id = 'benutzer' }
    if (-not $Who) { $Who = 'Benutzer' }
}
if (-not $Id -and $env:EDITOR_QUEUE_ID) { $Id = $env:EDITOR_QUEUE_ID }

# --- Befehle ------------------------------------------------------------------

switch ($Command) {
    'join' {
        if (-not $Who) { Write-Output 'join braucht -Who "<Chat/Agent: Aufgabe>" (oder -User).'; exit 1 }
        if (-not $Id) { $Id = 'q' + ([guid]::NewGuid().ToString('N').Substring(0, 6)) }
        # Claude-Code-Sitzung merken: der Hook (Tools/editor-queue-hook.sh) setzt damit automatisch Lebenszeichen.
        if (-not $Session -and -not $User -and $env:CLAUDE_CODE_SESSION_ID) { $Session = $env:CLAUDE_CODE_SESSION_ID }
        $pos = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            $now = Iso (Now)
            if ($i -ge 0) {
                $e = $list[$i]
                $e.heartbeat = $now
                if ($Purpose) { $e.purpose = $Purpose }
                if ($Session) { $e.session = $Session }
                Write-Log "JOIN (bereits angemeldet) $(Describe $e)"
            }
            else {
                $e = [pscustomobject][ordered]@{
                    id = $Id; who = $Who; session = $Session; purpose = $Purpose
                    kind = $(if ($User) { 'user' } else { 'agent' })
                    joined = $now; heartbeat = $now; topSince = $null
                }
                # Der Benutzer reiht sich direkt hinter dem aktuellen Inhaber ein, ohne ihn zu verdraengen.
                if ($User -and $list.Count -ge 1) { $list.Insert(1, $e); $i = 1 }
                else { [void]$list.Add($e); $i = $list.Count - 1 }
                if ($list.Count -eq 1) { $e.topSince = $now; Write-Log "OBEN $(Describe $e) darf den Editor nutzen" }
                Write-Log "JOIN $(Describe $e) - $Purpose (Position $($i + 1))"
            }
            $i + 1
        }
        Write-Output "Angemeldet als Id '$Id', Position $pos."
        if ($pos -eq 1) { Write-Output "Du bist oben: Der Editor gehoert dir. Danach sofort: leave -Id $Id" }
        else { Write-Output "Warten mit: wait -Id $Id   (erst oben den Editor nutzen)" }
        exit 0
    }
    'wait' {
        if (-not $Id -and -not $Who -and -not $Session) { Write-Output 'wait braucht -Id (oder -Session).'; exit 1 }
        $start = Get-Date
        $lastPos = -1
        while ($true) {
            $pos = Invoke-Locked {
                param($list)
                $i = Find-Index $list
                if ($i -ge 0) { $list[$i].heartbeat = Iso (Now) }
                $i + 1
            }
            if ($pos -le 0) { Write-Output "Eintrag '$Id' ist nicht (mehr) in der Warteschlange. Neu anmelden mit join."; exit 3 }
            if ($pos -eq 1) { Write-Output "Du bist oben: Der Editor gehoert dir (Id '$Id'). Danach sofort: leave -Id $Id"; exit 0 }
            if ($pos -ne $lastPos) {
                Write-Output ("{0}  Position {1}, warte ..." -f (Get-Date).ToString('HH:mm:ss', $inv), $pos)
                $lastPos = $pos
            }
            if ($TimeoutMinutes -gt 0 -and ((Get-Date) - $start).TotalMinutes -ge $TimeoutMinutes) {
                Write-Output "Zeitlimit erreicht, noch Position $pos. Eintrag bleibt bestehen; wait erneut aufrufen."
                exit 2
            }
            Start-Sleep -Seconds $PollSeconds
        }
    }
    'heartbeat' {
        if (-not $Id -and -not $Who -and -not $Session) { Write-Output 'heartbeat braucht -Id (oder -Session).'; exit 1 }
        $pos = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            if ($i -ge 0) { $list[$i].heartbeat = Iso (Now) }
            $i + 1
        }
        if ($pos -le 0) { Write-Output "Eintrag '$Id' ist nicht (mehr) in der Warteschlange."; exit 3 }
        Write-Output "Lebenszeichen gesetzt, Position $pos."
        exit 0
    }
    'leave' {
        if (-not $Id -and -not $Who -and -not $Session) { Write-Output 'leave braucht -Id (oder -Session).'; exit 1 }
        $next = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            if ($i -lt 0) { return '?' }
            $e = $list[$i]
            $list.RemoveAt($i)
            Write-Log "LEAVE $(Describe $e)"
            if ($i -eq 0 -and $list.Count -gt 0) {
                $list[0].topSince = Iso (Now)
                Write-Log "OBEN $(Describe $list[0]) darf den Editor nutzen"
            }
            if ($list.Count -gt 0) { Describe $list[0] } else { '' }
        }
        if ($next -eq '?') { Write-Output "Eintrag nicht gefunden (bereits abgemeldet)."; exit 0 }
        Write-Output 'Abgemeldet.'
        if ($next) { Write-Output "Jetzt oben: $next" } else { Write-Output 'Warteschlange leer, Editor frei.' }
        exit 0
    }
    'status' {
        $lock = Enter-Lock
        try {
            $list = Read-Queue
            Update-Queue $list
            Write-Queue $list
        }
        finally { $lock.Dispose() }
        Show-Queue $list
        exit 0
    }
}
