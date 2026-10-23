<#
.SYNOPSIS
  Lokaler Dienst fuer die Editor-Warteschlange: liefert sie an PrivOrg (Modul Editor-Warteschlange)
  und nimmt An-/Abmeldungen des Benutzers entgegen. Nur auf diesem PC erreichbar (http://localhost:47615).
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue-server.ps1            # starten
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue-server.ps1 -Install   # Start mit dem ersten Unity Editor
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/editor-queue-server.ps1 -Uninstall

  GET  /api/queue                      -> { projects: [ <status -Json je Projekt> ] }
  POST /api/join      { project, purpose }   Benutzer direkt hinter dem Inhaber eintragen
  POST /api/heartbeat { project }            Lebenszeichen des Benutzers
  POST /api/leave     { project }            Benutzer abmelden
#>
[CmdletBinding()]
param(
    [int]$Port = 47615,
    [string[]]$AllowOrigin = @(),
    [string]$QueueDir,
    # Projektordner, dessen Editoren gemeint sind (Standard: der Ordner, aus dem dieses Skript stammt).
    [string]$ProjectRoot,
    # Dialog-Waechter fuer den Editor der Testbank ("<Projekt> 2"): Pruefintervall in Sekunden, 0 = aus (Tools/editor-dialog.ps1).
    [int]$DialogWatchSeconds = 3,
    # Ohne Fokus-Waechter (Tools/editor-focus.ps1): die Testbank darf sich nach vorne holen. Zur Laufzeit: editor-focus.ps1 off/on.
    [switch]$NoFocusGuard,
    [switch]$Install,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$queueScript = Join-Path $PSScriptRoot 'editor-queue.ps1'
$dialogScript = Join-Path $PSScriptRoot 'editor-dialog.ps1'
$focusScript = Join-Path $PSScriptRoot 'editor-focus.ps1'
# Frueher Autostart mit Windows; wird bei -Install/-Uninstall entfernt.
$legacyShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Mining Game Editor-Warteschlange.lnk'

# --- Projekte -------------------------------------------------------------------

if (-not $ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$repoRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
$ownerMatch = [regex]::Match($repoRoot, '^(.*?)[\\/]\.claude[\\/]worktrees[\\/]')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }
$ownerName = Split-Path $ownerRoot -Leaf
$reposDir = Split-Path $ownerRoot -Parent
if (-not $QueueDir) { $QueueDir = $env:EDITOR_QUEUE_DIR }
if (-not $QueueDir) { $QueueDir = Join-Path $reposDir 'EditorQueue' }

# Startverknuepfung im festen Ordner: Assets/Editor/EditorQueueActivity.cs oeffnet sie, wenn ein Unity Editor
# startet und der Dienst noch nicht laeuft (kein Autostart mit Windows mehr).
$bin = Join-Path $QueueDir 'bin'
$shortcut = Join-Path $bin 'editor-queue-server.lnk'

if ($Uninstall) {
    foreach ($s in $shortcut, $legacyShortcut) { if (Test-Path -LiteralPath $s) { Remove-Item -LiteralPath $s; Write-Output "Entfernt: $s" } }
    Write-Output 'Der Dienst startet nicht mehr automatisch.'
    exit 0
}
if ($Install) {
    # Kopie in einen festen Ordner ausserhalb von Git: Arbeitsordner und Testbank wechseln ihren Stand.
    if (Test-Path -LiteralPath $legacyShortcut) { Remove-Item -LiteralPath $legacyShortcut; Write-Output "Windows-Autostart entfernt: $legacyShortcut" }
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    # Gleiche Liste wie InstalledScripts in Assets/Editor/EditorQueueActivity.cs (frischt die Kopien auf main auf).
    foreach ($name in 'editor-queue.ps1', 'editor-queue-server.ps1', 'editor-dialog.ps1', 'editor-focus.ps1') { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $bin $name) -Force }
    $target = Join-Path $bin 'editor-queue-server.ps1'
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($shortcut)
    $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $allowed = @($AllowOrigin | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    $originArgs = if ($allowed.Count) { " -AllowOrigin `"$($allowed -join ',')`"" } else { '' }
    $lnk.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$target`" -Port $Port -ProjectRoot `"$ownerRoot`" -QueueDir `"$QueueDir`"$originArgs"
    $lnk.WorkingDirectory = $bin
    $lnk.Description = 'Lokaler Dienst der Unity-Editor-Warteschlange fuer PrivOrg'
    $lnk.Save()
    Write-Output "Startverknuepfung angelegt: $shortcut"
    Write-Output "Skripte kopiert nach $bin (nach Aenderungen -Install erneut ausfuehren)."
    Write-Output "Startet mit dem ersten Unity Editor; jetzt sofort starten mit: Start-Process '$shortcut'"
    exit 0
}

# --- Erlaubte Herkunft ----------------------------------------------------------

$origins = @($AllowOrigin | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().TrimEnd('/') } | Where-Object { $_ }) + @(($env:EDITOR_QUEUE_ORIGINS -split ',') | ForEach-Object { $_.Trim().TrimEnd('/') } | Where-Object { $_ })

function Test-Origin([string]$origin) {
    if (-not $origin) { return $true }   # kein Browser (lokales Werkzeug)
    if ($origin -match '^https?://(localhost|127\.0\.0\.1|\[::1\]|[a-z0-9-]+\.localhost)(:\d+)?$') { return $true }
    return $origins -contains $origin.TrimEnd('/')
}

# Arbeitsordner: Projektordner und Geschwister "<Projekt> <n>", dazu alles, was schon eine Warteschlange hat.
function Get-Projects {
    $names = New-Object System.Collections.Generic.List[string]
    $names.Add($ownerName)
    Get-ChildItem -LiteralPath $reposDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "$ownerName *" -and (Test-Path -LiteralPath (Join-Path $_.FullName 'ProjectSettings')) } |
        Sort-Object Name | ForEach-Object { if (-not $names.Contains($_.Name)) { $names.Add($_.Name) } }
    if (Test-Path -LiteralPath $QueueDir) {
        Get-ChildItem -LiteralPath $QueueDir -Filter '*.json' | ForEach-Object {
            $n = $_.Name -replace '\.activity\.json$', '' -replace '\.json$', ''
            # Nur echte Warteschlangen (mit Log) oder Editoren mit Aktivitaetssignal, keine fremden JSON-Dateien.
            $known = (Test-Path -LiteralPath (Join-Path $QueueDir "$n.log")) -or (Test-Path -LiteralPath (Join-Path $QueueDir "$n.activity.json"))
            if ($n -and $known -and -not $names.Contains($n)) { $names.Add($n) }
        }
    }
    return , $names
}

function Invoke-Queue([string]$command, [hashtable]$named) {
    $out = & $queueScript $command @named -QueueDir $QueueDir 2>&1 | ForEach-Object { "$_" }
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}

function Get-QueueJson {
    $parts = foreach ($p in (Get-Projects)) {
        $r = Invoke-Queue 'status' @{ Json = $true; Project = $p }
        $line = ($r.out -split "`n" | Where-Object { $_.StartsWith('{') } | Select-Object -Last 1)
        # Rolle: der Projektordner gehoert dem Benutzer, "<Projekt> 2" ist die Testbank der Chats (Tools/editor-bench.ps1).
        $role = if ($p -eq $ownerName) { 'user' } elseif ($p -eq "$ownerName 2") { 'bench' } else { 'other' }
        if ($line) { $line.Substring(0, $line.Length - 1) + ',"role":"' + $role + '"}' }
        else { (ConvertTo-Json -Compress -InputObject ([ordered]@{ project = $p; role = $role; error = $r.out })) }
    }
    return '{"service":"editor-queue","version":1,"queueDir":' + (ConvertTo-Json -InputObject $QueueDir) + ',"projects":[' + ($parts -join ',') + ']}'
}

function Send([System.Net.HttpListenerContext]$ctx, [int]$status, [string]$body) {
    $res = $ctx.Response
    $origin = $ctx.Request.Headers['Origin']
    if ($origin -and (Test-Origin $origin)) {
        $res.Headers['Access-Control-Allow-Origin'] = $origin
        $res.Headers['Vary'] = 'Origin'
        $res.Headers['Access-Control-Allow-Methods'] = 'GET, POST, OPTIONS'
        $res.Headers['Access-Control-Allow-Headers'] = 'Content-Type'
        $res.Headers['Access-Control-Allow-Private-Network'] = 'true'
        $res.Headers['Access-Control-Max-Age'] = '600'
    }
    $res.Headers['Cache-Control'] = 'no-store'
    $res.StatusCode = $status
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $res.ContentType = 'application/json; charset=utf-8'
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
    $res.Close()
}

function SendError($ctx, [int]$status, [string]$message) {
    Send $ctx $status (ConvertTo-Json -Compress -InputObject ([ordered]@{ error = $message }))
}

# --- Server ---------------------------------------------------------------------

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
try { $listener.Start() }
catch { Write-Output "Port $Port ist belegt oder nicht freigegeben (laeuft der Dienst schon?): $($_.Exception.Message)"; exit 1 }
Write-Output "Editor-Warteschlange: Dienst laeuft auf http://localhost:$Port (Queue-Ordner $QueueDir)"

# Dialog-Waechter: beantwortet bekannte modale Dialoge der Testbank, meldet unbekannte (Protokoll, status, wait).
# Nur fuer eine Testbank, die in diesen Queue-Ordner meldet (Aktivitaetssignal vorhanden), nie fuer den Editor des Benutzers.
$benchName = "$ownerName 2"
function Watch-Dialogs {
    if ($DialogWatchSeconds -le 0 -or -not (Test-Path -LiteralPath $dialogScript)) { return }
    if (-not (Test-Path -LiteralPath (Join-Path $QueueDir "$benchName.activity.json"))) { return }
    try { & $dialogScript watch -Once -Quiet -Project $benchName -ProjectRoot $ownerRoot -QueueDir $QueueDir | Out-Null } catch { }
}
if ($DialogWatchSeconds -gt 0) { Write-Output "Dialog-Waechter fuer '$benchName' alle $DialogWatchSeconds s." }

# Fokus-Waechter: holt das Fenster des Benutzers zurueck, wenn sich die Testbank von selbst nach vorne holt (eigener Thread
# in diesem Prozess, reagiert sofort auf Fensterwechsel). Protokoll mit "FOKUS" in <Testbank>.log.
if (-not $NoFocusGuard -and (Test-Path -LiteralPath $focusScript)) {
    try { & $focusScript start -Project $benchName -ProjectRoot $ownerRoot -QueueDir $QueueDir | ForEach-Object { Write-Output $_ } }
    catch { Write-Output "Fokus-Waechter nicht gestartet: $($_.Exception.Message)" }
}

try {
    while ($listener.IsListening) {
        # Auf Anfragen warten und dazwischen den Dialog-Waechter laufen lassen.
        $pending = $listener.BeginGetContext($null, $null)
        while (-not $pending.AsyncWaitHandle.WaitOne([Math]::Max(1, $DialogWatchSeconds) * 1000)) { Watch-Dialogs }
        $ctx = $listener.EndGetContext($pending)
        try {
            $req = $ctx.Request
            $origin = $req.Headers['Origin']
            if (-not (Test-Origin $origin)) { SendError $ctx 403 "Herkunft nicht erlaubt: $origin (mit -AllowOrigin oder EDITOR_QUEUE_ORIGINS freigeben)"; continue }
            $path = $req.Url.AbsolutePath.TrimEnd('/')
            if ($req.HttpMethod -eq 'OPTIONS') { Send $ctx 204 ''; continue }
            if ($req.HttpMethod -eq 'GET' -and ($path -eq '/api/queue' -or $path -eq '')) { Send $ctx 200 (Get-QueueJson); continue }
            if ($req.HttpMethod -eq 'POST' -and $path -match '^/api/(join|heartbeat|leave|move|request-release)$') {
                $action = $Matches[1]
                if ($req.ContentType -notlike 'application/json*') { SendError $ctx 415 'Content-Type application/json erwartet'; continue }
                $reader = New-Object IO.StreamReader($req.InputStream, [Text.Encoding]::UTF8)
                $raw = $reader.ReadToEnd()
                $body = if ($raw.Trim()) { $raw | ConvertFrom-Json } else { $null }
                $project = [string]$body.project
                if (-not ((Get-Projects) -contains $project)) { SendError $ctx 400 "Unbekannter Arbeitsordner: $project"; continue }
                # join/heartbeat/leave immer als Benutzer; move und request-release ordnen fremde Eintraege.
                if ($action -eq 'move') {
                    $entryId = [string]$body.id
                    $position = 0
                    if ($entryId -notmatch '^[A-Za-z0-9_-]{1,64}$' -or -not [int]::TryParse([string]$body.position, [ref]$position) -or $position -lt 1 -or $position -gt 1000) {
                        SendError $ctx 400 'move braucht id und position (ab 2)'; continue
                    }
                    $named = @{ Project = $project; Id = $entryId; Position = $position }
                }
                elseif ($action -eq 'request-release') {
                    $named = @{ Project = $project; Cancel = ($body.cancel -eq $true) }
                }
                else {
                    $named = @{ User = $true; Project = $project }
                    $purpose = [string]$body.purpose
                    if ($action -eq 'join' -and $purpose) {
                        if ($purpose.Length -gt 200) { $purpose = $purpose.Substring(0, 200) }
                        $named.Purpose = $purpose
                    }
                }
                $r = Invoke-Queue $action $named
                # move/request-release: Exit 1 = nicht erlaubt (Inhaber verschieben, Benutzer bitten), 3 = Eintrag weg.
                if ($action -in 'move', 'request-release' -and $r.code -eq 1) { SendError $ctx 409 $r.out; continue }
                if ($action -eq 'move' -and $r.code -eq 3) { SendError $ctx 404 $r.out; continue }
                if ($r.code -ne 0 -and $r.code -ne 3) { SendError $ctx 500 $r.out; continue }
                Send $ctx 200 ('{"ok":' + $(if ($r.code -eq 0) { 'true' } else { 'false' }) + ',"message":' + (ConvertTo-Json -InputObject $r.out) + ',"queue":' + (Get-QueueJson) + '}')
                continue
            }
            SendError $ctx 404 'Unbekannter Pfad'
        }
        catch {
            try { SendError $ctx 500 $_.Exception.Message } catch { }
        }
    }
}
finally { $listener.Close() }
