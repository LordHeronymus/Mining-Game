<#
.SYNOPSIS
  Anmeldeliste mit Warteschlange fuer den Unity Editor eines Arbeitsordners.
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 join -Who "Claude Code: Eisen" -Purpose "Play-Tests"
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 wait -Id q1a2b3 -TimeoutMinutes 9
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue.ps1 leave -Id q1a2b3
  (Git Bash: Tools/editor-queue.sh mit denselben Argumenten)

  Benutzer (z. B. ueber PrivOrg):
  editor-queue.ps1 move -Id q1a2b3 -Position 2        # Wartende umsortieren (Platz 1 ist gesperrt)
  editor-queue.ps1 request-release                    # Inhaber bitten, den Editor in 5 min freizugeben
  editor-queue.ps1 request-release -Cancel            # Bitte zuruecknehmen
  editor-queue.ps1 notice -Session <id>               # Hinweis fuer einen Chat (Bitte / Verdraengung), sonst leer

  Exit-Codes: 0 = ok / du bist oben, 1 = Fehler, 2 = wait-Zeitlimit erreicht (noch nicht oben),
              3 = Eintrag nicht (mehr) in der Warteschlange.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('join', 'status', 'wait', 'heartbeat', 'leave', 'move', 'request-release', 'notice', 'help')]
    [string]$Command = 'help',
    [string]$Id,
    [string]$Who,
    [string]$Session,
    [string]$Purpose,
    [switch]$User,
    [switch]$Json,
    # heartbeat -Busy: ein Unity-Aufruf laeuft (Inhaber nie verdraengen); -Idle: Aufruf beendet.
    [switch]$Busy,
    [switch]$Idle,
    [string]$Project,
    [string]$QueueDir,
    [double]$TimeoutMinutes = 0,
    [int]$PollSeconds = 15,
    [double]$StaleMinutes = 0,
    [double]$UserStaleMinutes = 0,
    # move: neue Position (1-basiert, mindestens 2).
    [int]$Position = 0,
    # request-release -Cancel: Bitte zuruecknehmen.
    [switch]$Cancel,
    # Bitte abschliessen: Frist bis zur Freigabe, danach (falls noch getestet wird) letzte Frist.
    [double]$ReleaseMinutes = 0,
    [double]$FinalMinutes = 0
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$inv = [Globalization.CultureInfo]::InvariantCulture

# Ab diesem Alter gilt das Aktivitaetssignal als nicht mehr aktuell (Editor schreibt alle 30 s, im Hintergrund gedrosselt bis ~90 s).
$FreshMinutes = 5
# Haengt der Editor laenger als so lange (Prozess laeuft, aber kein Signal), wird er nicht mehr als belegt gewertet.
$BlockedMaxMinutes = 30

if ($Command -eq 'help') {
    Get-Help $PSCommandPath -Detailed | Out-String | Write-Output
    Write-Output 'Befehle: join | status | wait | heartbeat | leave | move | request-release | notice   (Details: Docs/Editor-Warteschlange.md)'
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
# Kurz: wer zwischen Tests Code aendert, gibt den Editor frei (Lebenszeichen kommen nur von Editor-Nutzung).
if ($StaleMinutes -le 0) { $StaleMinutes = 4 }
if ($UserStaleMinutes -le 0 -and $env:EDITOR_QUEUE_USER_STALE_MINUTES) { $UserStaleMinutes = [double]::Parse($env:EDITOR_QUEUE_USER_STALE_MINUTES, $inv) }
# Der Benutzer arbeitet von Hand im Editor und bekommt mehr Zeit.
if ($UserStaleMinutes -le 0) { $UserStaleMinutes = 10 }
# Ein laufender Unity-Aufruf (heartbeat -Busy) schuetzt hoechstens so lange, falls das Ende nie gemeldet wird.
$BusyMaxMinutes = 15
if ($ReleaseMinutes -le 0 -and $env:EDITOR_QUEUE_RELEASE_MINUTES) { $ReleaseMinutes = [double]::Parse($env:EDITOR_QUEUE_RELEASE_MINUTES, $inv) }
if ($ReleaseMinutes -le 0) { $ReleaseMinutes = 5 }
if ($FinalMinutes -le 0 -and $env:EDITOR_QUEUE_FINAL_MINUTES) { $FinalMinutes = [double]::Parse($env:EDITOR_QUEUE_FINAL_MINUTES, $inv) }
if ($FinalMinutes -le 0) { $FinalMinutes = 2 }
if ($PollSeconds -lt 1) { $PollSeconds = 1 }

if (-not (Test-Path -LiteralPath $QueueDir)) { New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null }
$queueFile = Join-Path $QueueDir "$Project.json"
$lockFile = Join-Path $QueueDir "$Project.lock"
$logFile = Join-Path $QueueDir "$Project.log"
$activityFile = Join-Path $QueueDir "$Project.activity.json"
# Offene modale Dialoge des Editors (schreibt Tools/editor-dialog.ps1: Waechter im Dienst, wait, list/press).
$dialogFile = Join-Path $QueueDir "$Project.dialogs.json"
$dialogScript = Join-Path $PSScriptRoot 'editor-dialog.ps1'
# Verdraengte Eintraege der letzten Stunde, damit der Hook dem betroffenen Chat Bescheid geben kann.
$evictedFile = Join-Path $QueueDir "$Project.evicted.json"
$EntryFields = 'id', 'who', 'session', 'purpose', 'kind', 'joined', 'heartbeat', 'topSince', 'busySince', 'releaseRequestedAt', 'releaseDeadline', 'finalDeadline'

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
    # Der Dialog-Waechter (Tools/editor-dialog.ps1) schreibt ohne Sperre ins selbe Protokoll: bei Kollision kurz erneut.
    for ($i = 0; ; $i++) {
        try { [IO.File]::AppendAllText($logFile, $line + "`r`n", $utf8); return }
        catch { if ($i -ge 20) { throw }; Start-Sleep -Milliseconds 50 }
    }
}

function Describe($e) {
    $s = "[$($e.id)] $($e.who)"
    if ($e.session) { $s += " (Sitzung $($e.session))" }
    return $s
}

# Offene Dialoge laut Tools/editor-dialog.ps1 (hoechstens 2 min alt) als Text, sonst $null.
function Get-DialogText {
    if (-not (Test-Path -LiteralPath $dialogFile)) { return $null }
    try { $st = [IO.File]::ReadAllText($dialogFile, $utf8) | ConvertFrom-Json } catch { return $null }
    if ((AgeMinutes (Get-Prop $st 'updated')) -gt 2) { return $null }
    $open = @(@(Get-Prop $st 'dialogs') | Where-Object { $_ -and $_.kind -eq 'dialog' })
    if ($open.Count -eq 0) { return $null }
    $d = $open[0]
    $buttons = (@($d.buttons) | ForEach-Object { "[$_]" }) -join ' '
    $hint = if ($d.press) { "Waechter klickt '$($d.press)'" } else { 'nicht automatisch: Tools/editor-dialog.sh list, dann press "<Knopf>" oder Benutzer melden' }
    return "Dialog offen (geoeffnet $(FormatAge (AgeMinutes $d.since))): '$($d.title)' $buttons - $hint"
}

# Ein Durchgang des Dialog-Waechters fuer die Testbank (wartende Chats helfen mit, falls der Dienst nicht laeuft).
function Invoke-DialogWatch {
    if (-not (Test-Path -LiteralPath $dialogScript)) { return }
    if ($Project -ne (((Split-Path $ownerRoot -Leaf) -replace ' \d+$', '') + ' 2')) { return }   # nur die Testbank, nie der Editor des Benutzers
    try { & $dialogScript watch -Once -Quiet -Project $Project -ProjectRoot $ownerRoot -QueueDir $QueueDir | Out-Null } catch { }
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
    foreach ($e in $list) { foreach ($p in $EntryFields) { if (-not $e.PSObject.Properties[$p]) { Set-Prop $e $p $null } } }
    return , $list
}

function Read-Evicted {
    $list = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath $evictedFile) {
        try {
            $text = [IO.File]::ReadAllText($evictedFile, $utf8)
            # Nicht @(...) um ConvertFrom-Json legen: PowerShell 5.1 liefert ein JSON-Array als ein Objekt.
            if ($text.Trim()) { $parsed = $text | ConvertFrom-Json; foreach ($e in $parsed) { if ($null -ne $e) { [void]$list.Add($e) } } }
        }
        catch { }
    }
    return , $list
}

function Write-Evicted($list) {
    $keep = @($list | Where-Object { (AgeMinutes $_.at) -lt 60 } | Select-Object -Last 50)
    [IO.File]::WriteAllText($evictedFile, (ConvertTo-Json -InputObject $keep -Depth 3), $utf8)
}

# Entfernt einen Eintrag, protokolliert den Grund und merkt ihn fuer die Benachrichtigung des Chats vor.
function Remove-Entry($list, [int]$index, [string]$kindText, [string]$reason) {
    $e = $list[$index]
    Write-Log "VERDRAENGT ($kindText) $(Describe $e): $reason"
    Write-Host "Verdraengt ($kindText, $reason): $(Describe $e)"
    $list.RemoveAt($index)
    if ($e.session -or $e.id) {
        $ev = Read-Evicted
        [void]$ev.Add([pscustomobject][ordered]@{ id = $e.id; who = $e.who; session = $e.session; at = (Iso (Now)); reason = $reason; notified = $false })
        Write-Evicted $ev
    }
}

function Test-Running($e) { [bool]($e.busySince -and ((AgeMinutes $e.busySince) -lt $BusyMaxMinutes)) }

function FormatLeft($deadline) {
    $s = [int][Math]::Max(0, [Math]::Ceiling(((ToUtc $deadline) - (Now)).TotalSeconds))
    return ('{0}:{1:00} min' -f [int][Math]::Floor($s / 60), ($s % 60))
}

function ClockTime($value) { (ToUtc $value).ToLocalTime().ToString('HH:mm', $inv) }

# Zustandsmeldung an den Inhaber, wenn der Benutzer um Freigabe bittet (leer, wenn keine Bitte offen ist).
# Sachlich mit fester Uhrzeit formuliert: der Hook reicht sie als Zusatzinfo an Claude weiter.
function Get-ReleaseNotice($e, [int]$index) {
    if ($index -ne 0 -or -not $e.releaseDeadline) { return $null }
    $steps = "Freigabe laut AGENTS.md: laufenden Test abschliessen, Tools/editor-bench.sh release (bei Nutzung der Testbank), dann Tools/editor-queue.sh leave -Project '$Project' -Id $($e.id). Wird der Editor danach noch gebraucht, ist eine neue Anmeldung mit join moeglich (hinten in der Warteschlange)."
    if ($e.finalDeadline) {
        return "Zustand der Editor-Warteschlange '$Project': Die vom Benutzer erbetene Freigabe ist ueberfaellig. Die letzte Frist fuer Eintrag $($e.id) (Inhaber, diese Sitzung) endet um $(ClockTime $e.finalDeadline) Uhr (in $(FormatLeft $e.finalDeadline)), danach wird der Eintrag automatisch entfernt. $steps"
    }
    return "Zustand der Editor-Warteschlange '$Project': Der Benutzer hat um Freigabe des Editors gebeten. Die Reservierung von Eintrag $($e.id) (Inhaber, diese Sitzung) endet um $(ClockTime $e.releaseDeadline) Uhr (in $(FormatLeft $e.releaseDeadline)); laeuft dann noch ein Test, gilt einmalig eine letzte Frist von $FinalMinutes Minuten, danach wird der Eintrag entfernt. $steps"
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
function Get-EditorState([double]$recentMinutes = $StaleMinutes) {
    $act = $null
    if (Test-Path -LiteralPath $activityFile) {
        for ($i = 0; $i -lt 3 -and $null -eq $act; $i++) {
            try { $act = [IO.File]::ReadAllText($activityFile, $utf8) | ConvertFrom-Json }
            catch { Start-Sleep -Milliseconds 100 }
        }
    }
    # Ein offener Dialog blockiert den Editor: nie als ruhig werten, sonst wird der Inhaber wegen des Dialogs verdraengt.
    $dialog = Get-DialogText
    if ($dialog) { return @{ busy = $true; text = $dialog } }
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
            return @{ busy = ($lastAge -lt $recentMinutes); text = $txt }
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

# Stand gegenueber origin/main laut Assets/Editor/MainAutoUpdate.cs (nur im Ordner des Benutzers), sonst $null.
# Nur bei offenem Editor mit frischem Signal: ein geschlossener Editor prueft nicht mehr.
function Get-MainState {
    if (-not (Test-Path -LiteralPath $activityFile)) { return $null }
    try { $act = [IO.File]::ReadAllText($activityFile, $utf8) | ConvertFrom-Json } catch { return $null }
    $state = Get-Prop $act 'mainState'
    if (-not $state -or (Get-Prop $act 'closed') -or (AgeMinutes (Get-Prop $act 'updated')) -gt $FreshMinutes) { return $null }
    $behind = Get-Prop $act 'mainBehind'
    return [ordered]@{
        state = $state; text = (Get-Prop $act 'mainText'); behind = $(if ($null -eq $behind) { -1 } else { [int]$behind })
        checked = (Get-Prop $act 'mainChecked'); current = ($state -eq 'aktuell')
    }
}

# --- Code-Stand je Arbeitsordner (fuer PrivOrg: ist der Editor auf dem neuesten Stand?) -------------
# Ordner Mining Game: gegen origin/main; Testbank: gegen den geholten Branch (<Ordner>.bench.json);
# andere Ordner: gegen origin/<Branch>. Zwischengespeichert in <Ordner>.code.json (30 s), git fetch hoechstens alle 3 min.
$codeFile = Join-Path $QueueDir "$Project.code.json"
$CodeRefreshSeconds = 30
$FetchMinutes = 3

function Invoke-Git([string]$folder, [string[]]$gitArgs, [int]$timeoutMs = 15000) {
    # Als Zeichenkette (ArgumentList fehlt in Windows PowerShell 5.1, unter der der Dienst laeuft).
    $quoted = foreach ($a in (@('-C', $folder) + $gitArgs)) { if ($a -match '[\s"]') { '"' + ($a -replace '"', '\"') + '"' } else { $a } }
    $psi = New-Object System.Diagnostics.ProcessStartInfo 'git', ($quoted -join ' ')
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.Environment['LC_ALL'] = 'C'
    try {
        $p = [System.Diagnostics.Process]::Start($psi)
        $err = $p.StandardError.ReadToEndAsync(); $out = $p.StandardOutput.ReadToEndAsync()
        if (-not $p.WaitForExit($timeoutMs)) { try { $p.Kill() } catch { }; return @{ code = -1; out = 'Zeitlimit' } }
        return @{ code = $p.ExitCode; out = $out.Result.Trim(); err = $err.Result.Trim() }
    }
    catch { return @{ code = -1; out = $_.Exception.Message } }
}

function Get-ProjectFolder {
    if (Test-Path -LiteralPath $activityFile) {
        try { $path = Get-Prop ([IO.File]::ReadAllText($activityFile, $utf8) | ConvertFrom-Json) 'projectPath' } catch { $path = $null }
        if ($path -and (Test-Path -LiteralPath (Join-Path $path '.git'))) { return $path }
    }
    $sibling = Join-Path (Split-Path $QueueDir -Parent) $Project
    if (Test-Path -LiteralPath (Join-Path $sibling '.git')) { return $sibling }
    return $null
}

function Plural([int]$n, [string]$one, [string]$many) { if ($n -eq 1) { "1 $one" } else { "$n $many" } }

function Measure-CodeState($previous) {
    $folder = Get-ProjectFolder
    if (-not $folder) { return $null }
    $head = Invoke-Git $folder @('rev-parse', '--abbrev-ref', 'HEAD')
    if ($head.code -ne 0) { return [ordered]@{ folder = $folder; text = 'kein Git-Stand lesbar'; current = $null; checked = (Iso (Now)) } }
    $branch = $head.out
    $sha = (Invoke-Git $folder @('rev-parse', '--short', 'HEAD')).out
    $subject = (Invoke-Git $folder @('log', '-1', '--format=%s')).out
    $bench = $null
    $benchFile = Join-Path $QueueDir "$Project.bench.json"
    if (Test-Path -LiteralPath $benchFile) { try { $bench = [IO.File]::ReadAllText($benchFile, $utf8) | ConvertFrom-Json } catch { } }
    # Vergleichsziel: geholter Testbank-Branch, sonst der ausgecheckte Branch.
    $ref = if ($bench -and (Get-Prop $bench 'ref')) { [string](Get-Prop $bench 'ref') } elseif ($branch -ne 'HEAD') { $branch } else { $null }
    $label = if ($bench) { "Testbank: $ref" } elseif ($ref) { $ref } else { "losgeloest bei $sha" }
    $fetched = Get-Prop $previous 'fetched'
    $remote = $null; $behind = -1; $ahead = -1
    if ($ref -and $ref -notmatch '^[0-9a-f]{7,40}$') {
        if ((AgeMinutes $fetched) -ge $FetchMinutes) {
            [void](Invoke-Git $folder @('fetch', '-q', 'origin', $ref) 30000)
            $fetched = Iso (Now)
        }
        if ((Invoke-Git $folder @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/$ref")).code -eq 0) {
            $remote = "origin/$ref"
            $base = if ($bench -and (Get-Prop $bench 'sha')) { [string](Get-Prop $bench 'sha') } else { 'HEAD' }
            $behind = [int](Invoke-Git $folder @('rev-list', '--count', "$base..$remote")).out
            $ahead = [int](Invoke-Git $folder @('rev-list', '--count', "$remote..HEAD")).out
        }
    }
    $changed = @((Invoke-Git $folder @('status', '--porcelain', '--untracked-files=no')).out -split "`n" | Where-Object { $_ }).Count
    $parts = New-Object System.Collections.Generic.List[string]
    if (-not $ref) { $current = $null }
    elseif (-not $remote) { $current = $null; $parts.Add('nur lokal, nicht auf GitHub') }
    else {
        $current = ($behind -eq 0)
        if ($behind -gt 0) { $parts.Add((Plural $behind 'neuer Commit' 'neue Commits') + " auf $remote") } else { $parts.Add('aktuell') }
        if ($ahead -gt 0) { $parts.Add((Plural $ahead 'eigener Commit' 'eigene Commits') + ' nicht auf GitHub') }
    }
    if ($changed -gt 0) { $parts.Add((Plural $changed 'Datei' 'Dateien') + ' lokal geaendert') }
    $text = "$label ($sha)" + $(if ($parts.Count) { ', ' + ($parts -join ', ') } else { '' })
    return [ordered]@{
        folder = $folder; branch = $branch; ref = $ref; testbench = [bool]$bench; sha = $sha; subject = $subject
        remote = $remote; behind = $behind; ahead = $ahead; changed = $changed; current = $current
        text = $text; fetched = $fetched; checked = (Iso (Now))
    }
}

function Get-CodeState {
    $cached = $null
    if (Test-Path -LiteralPath $codeFile) { try { $cached = [IO.File]::ReadAllText($codeFile, $utf8) | ConvertFrom-Json } catch { } }
    if ($cached -and (AgeMinutes (Get-Prop $cached 'checked')) * 60 -lt $CodeRefreshSeconds) { return $cached }
    $state = Measure-CodeState $cached
    if ($state) { try { [IO.File]::WriteAllText($codeFile, (ConvertTo-Json -InputObject $state -Compress), $utf8) } catch { } }
    return $state
}

# Raeumt verwaiste Eintraege ab und setzt den Startzeitpunkt des obersten Eintrags.
function Update-Queue($list) {
    $now = Now
    for ($i = $list.Count - 1; $i -ge 1; $i--) {
        $e = $list[$i]
        if ($e.kind -eq 'user') { continue }
        $age = AgeMinutes $e.heartbeat
        if ($age -gt $StaleMinutes) { Remove-Entry $list $i 'wartend' "kein Lebenszeichen seit $(FormatAge $age)" }
    }
    if ($list.Count -gt 0) {
        $top = $list[0]
        # Ein laufender Unity-Aufruf (heartbeat -Busy, z. B. langes run_script) schuetzt den Inhaber.
        $running = Test-Running $top
        $done = $false
        # Bitte des Benutzers: nach Ablauf der Frist raus; wird dann noch getestet, einmal die letzte Frist.
        if ($top.releaseDeadline) {
            if ($top.finalDeadline) {
                if ($now -ge (ToUtc $top.finalDeadline)) {
                    Remove-Entry $list 0 'Inhaber' "Bitte des Benutzers, letzte Frist von $FinalMinutes min abgelaufen"
                    $done = $true
                }
            }
            elseif ($now -ge (ToUtc $top.releaseDeadline)) {
                $ed = Get-EditorState $StaleMinutes
                if ($running -or $ed.busy) {
                    $top.finalDeadline = Iso ($now.AddMinutes($FinalMinutes))
                    Write-Log "LETZTE FRIST $(Describe $top): testet noch ($(if ($running) { 'Unity-Aufruf laeuft' } else { $ed.text })), $FinalMinutes min bis $(LocalTime $top.finalDeadline)"
                }
                else {
                    Remove-Entry $list 0 'Inhaber' "Bitte des Benutzers, Frist abgelaufen ($($ed.text))"
                    $done = $true
                }
            }
        }
        if (-not $done -and $top.topSince -and -not $running) {
            $limit = if ($top.kind -eq 'user') { $UserStaleMinutes } else { $StaleMinutes }
            $age = [Math]::Min((AgeMinutes $top.heartbeat), (AgeMinutes $top.topSince))
            if ($age -gt $limit) {
                $ed = Get-EditorState $limit
                if (-not $ed.busy) { Remove-Entry $list 0 'Inhaber' "kein Lebenszeichen seit $(FormatAge $age), Editor ungenutzt ($($ed.text))" }
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
    $main = Get-MainState
    $code = Get-CodeState
    if ($code) { Write-Output "Code:  $($code.text)" }
    if ($main) { Write-Output "Stand: $($main.text) (geprueft $(FormatAge (AgeMinutes $main.checked)))" }
    if ($list.Count -eq 0) { Write-Output '  (leer - Editor frei)'; return }
    for ($i = 0; $i -lt $list.Count; $i++) {
        $e = $list[$i]
        $mark = if ($i -eq 0) { '  <- nutzt den Editor' } else { '' }
        if ($i -eq 0 -and $e.finalDeadline) { $mark += " (letzte Frist bis $(LocalTime $e.finalDeadline))" }
        elseif ($i -eq 0 -and $e.releaseDeadline) { $mark += " (soll freigeben bis $(LocalTime $e.releaseDeadline))" }
        $p = if ($e.purpose) { " - $($e.purpose)" } else { '' }
        Write-Output ("  {0}. {1}{2} | seit {3} | Lebenszeichen {4}{5}" -f ($i + 1), (Describe $e), $p, (LocalTime $e.joined), (FormatAge (AgeMinutes $e.heartbeat)), $mark)
    }
}

function Invoke-Locked([scriptblock]$body, [bool]$touch = $true) {
    $lock = Enter-Lock
    try {
        $list = Read-Queue
        # Eigenes Lebenszeichen vor dem Aufraeumen, sonst verdraengt ein spaeter wait-Aufruf sich selbst.
        # Nicht bei Befehlen des Benutzers ueber andere Eintraege (move) oder reinen Abfragen (notice).
        $own = if ($touch) { Find-Index $list } else { -1 }
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
                    joined = $now; heartbeat = $now; topSince = $null; busySince = $null
                    releaseRequestedAt = $null; releaseDeadline = $null; finalDeadline = $null
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
        $lastDialog = $null
        while ($true) {
            # Blockiert ein Dialog die Testbank, bekannte beantworten und unbekannte hier zeigen.
            Invoke-DialogWatch
            $dialog = Get-DialogText
            $dialogKey = if ($dialog) { $dialog -replace '^Dialog offen \(geoeffnet [^)]*\)', '' } else { $null }
            if ($dialog -and $dialogKey -ne $lastDialog) { Write-Output ("{0}  Editor blockiert: {1}" -f (Get-Date).ToString('HH:mm:ss', $inv), $dialog) }
            $lastDialog = $dialogKey
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
        $result = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            $notice = $null
            if ($i -ge 0) {
                $list[$i].heartbeat = Iso (Now)
                if ($Busy) { $list[$i].busySince = Iso (Now) }
                elseif ($Idle) { $list[$i].busySince = $null }
                $notice = Get-ReleaseNotice $list[$i] $i
            }
            @{ pos = $i + 1; notice = $notice }
        }
        if ($result.pos -le 0) { Write-Output "Eintrag '$Id' ist nicht (mehr) in der Warteschlange."; exit 3 }
        Write-Output ("Lebenszeichen gesetzt{0}, Position $($result.pos)." -f $(if ($Busy) { ' (Unity-Aufruf laeuft)' } elseif ($Idle) { ' (Unity-Aufruf beendet)' } else { '' }))
        if ($result.notice) { Write-Output "HINWEIS: $($result.notice)" }
        exit 0
    }
    'move' {
        if (-not $Id -or $Position -lt 1) { Write-Output 'move braucht -Id und -Position.'; exit 1 }
        $result = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            if ($i -lt 0) { return 'missing' }
            if ($i -eq 0) { return 'holder' }
            # Platz 1 gehoert dem aktuellen Inhaber und ist gesperrt.
            $target = [Math]::Min([Math]::Max($Position, 2), $list.Count) - 1
            $e = $list[$i]
            if ($target -ne $i) {
                $list.RemoveAt($i)
                $list.Insert($target, $e)
                Write-Log "MOVE $(Describe $e): Position $($i + 1) -> $($target + 1)"
            }
            "ok:$($target + 1)"
        } $false
        if ($result -eq 'missing') { Write-Output "Eintrag '$Id' ist nicht in der Warteschlange."; exit 3 }
        if ($result -eq 'holder') { Write-Output 'Der oberste Eintrag nutzt den Editor und kann nicht verschoben werden.'; exit 1 }
        Write-Output "Verschoben auf Position $($result.Substring(3))."
        exit 0
    }
    'request-release' {
        $result = Invoke-Locked {
            param($list)
            if ($list.Count -eq 0) { return 'empty' }
            $top = $list[0]
            if ($Id -and $top.id -ne $Id) { return 'nottop' }
            if ($Cancel) {
                if (-not $top.releaseDeadline) { return 'none' }
                $top.releaseRequestedAt = $null; $top.releaseDeadline = $null; $top.finalDeadline = $null
                Write-Log "BITTE ZURUECKGENOMMEN $(Describe $top)"
                return 'cancelled'
            }
            if ($top.kind -eq 'user') { return 'user' }
            if (-not $top.releaseDeadline) {
                $now = Now
                $top.releaseRequestedAt = Iso $now
                $top.releaseDeadline = Iso ($now.AddMinutes($ReleaseMinutes))
                $top.finalDeadline = $null
                Write-Log "BITTE ABSCHLIESSEN $(Describe $top): freigeben bis $(LocalTime $top.releaseDeadline)"
            }
            "ok:$(Describe $top)|$(LocalTime $top.releaseDeadline)"
        } $false
        switch -Wildcard ($result) {
            'empty' { Write-Output 'Warteschlange leer, niemand nutzt den Editor.'; exit 0 }
            'nottop' { Write-Output "Eintrag '$Id' nutzt den Editor nicht (mehr)."; exit 3 }
            'none' { Write-Output 'Es war keine Bitte offen.'; exit 0 }
            'cancelled' { Write-Output 'Bitte zurueckgenommen.'; exit 0 }
            'user' { Write-Output 'Der Editor wird gerade vom Benutzer genutzt.'; exit 1 }
            'ok:*' {
                $parts = $result.Substring(3).Split('|')
                Write-Output "Bitte gesendet: $($parts[0]) soll den Editor bis $($parts[1]) freigeben (danach hoechstens $FinalMinutes min letzte Frist, falls noch getestet wird)."
                exit 0
            }
        }
        exit 1
    }
    'notice' {
        # Fuer den Hook: Bitte des Benutzers oder Verdraengung melden. Setzt kein Lebenszeichen.
        if (-not $Id -and -not $Session) { exit 0 }
        $text = Invoke-Locked {
            param($list)
            $i = Find-Index $list
            if ($i -ge 0) { return (Get-ReleaseNotice $list[$i] $i) }
            $ev = Read-Evicted
            $hit = $null
            foreach ($e in $ev) {
                if ($e.notified) { continue }
                if (($Session -and $e.session -eq $Session) -or ($Id -and $e.id -eq $Id)) { $hit = $e }
            }
            if (-not $hit) { return $null }
            foreach ($e in $ev) { if (($Session -and $e.session -eq $Session) -or ($Id -and $e.id -eq $Id)) { $e.notified = $true } }
            Write-Evicted $ev
            "Zustand der Editor-Warteschlange '$Project': Eintrag $($hit.id) dieser Sitzung wurde um $(ClockTime $hit.at) Uhr entfernt ($($hit.reason)). Diese Sitzung hat den Editor nicht mehr reserviert; fuer weitere Tests ist laut AGENTS.md eine neue Anmeldung noetig (Tools/editor-queue.sh join -Project '$Project', dann wait)."
        } $false
        if ($text) { Write-Output $text }
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
        if ($Json) {
            # Maschinenlesbar fuer den lokalen Dienst (Tools/editor-queue-server.ps1) und PrivOrg.
            $ed = Get-EditorState
            # Code-Stand auch im Editortext, damit das PrivOrg-Modul ihn ohne eigene Anpassung zeigt.
            # Im Ordner des Benutzers sagt MainAutoUpdate genauer, wann geholt wird (z. B. nach dem Play-Stopp).
            $main = Get-MainState
            $code = Get-CodeState
            $standText = if ($main -and -not $main.current) { $main.text } elseif ($code) { $code.text } elseif ($main) { $main.text } else { $null }
            $edText = if ($standText) { "$($ed.text) | Stand: $standText" } else { $ed.text }
            $pos = 0
            $rows = @(foreach ($e in $list) {
                    $pos++
                    [ordered]@{
                        position = $pos; id = $e.id; who = $e.who; session = $e.session; purpose = $e.purpose
                        kind = $e.kind; joined = $e.joined; heartbeat = $e.heartbeat; topSince = $e.topSince; busySince = $e.busySince
                        releaseRequestedAt = $e.releaseRequestedAt; releaseDeadline = $e.releaseDeadline; finalDeadline = $e.finalDeadline
                    }
                })
            $obj = [ordered]@{
                project = $Project; now = (Iso (Now)); staleMinutes = $StaleMinutes; userStaleMinutes = $UserStaleMinutes
                releaseMinutes = $ReleaseMinutes; finalMinutes = $FinalMinutes
                editor = [ordered]@{ busy = [bool]$ed.busy; text = $edText }
                main = $main
                code = $code
                entries = $rows
            }
            ConvertTo-Json -InputObject $obj -Depth 5 -Compress
            exit 0
        }
        Show-Queue $list
        exit 0
    }
}
