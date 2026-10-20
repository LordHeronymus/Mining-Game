<#
.SYNOPSIS
  Erkennt und schliesst modale Dialoge des Unity Editors der Testbank ("Mining Game 2"), die den Editor blockieren
  (z. B. "The open scene(s) have been modified externally"). Nie fuer den Editor des Benutzers ("Mining Game").
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  Tools/editor-dialog.sh list                          # offene Dialoge: Titel, Text, Knoepfe
  Tools/editor-dialog.sh press "Reload"                # Knopf klicken (& im Text egal); bei mehreren Dialogen -Title "<Teil>"
  Tools/editor-dialog.sh watch -Once                   # bekannte Dialoge sofort beantworten (wie der Waechter im Dienst)
  Tools/editor-dialog.sh watch                         # Waechter im Vordergrund (alle 3 s), falls der Dienst nicht laeuft
  Tools/editor-dialog.sh list -All                     # alle sichtbaren Fenster des Editors (Fehlersuche)

  Exit-Codes: 0 ok / kein Dialog offen, 1 Fehler, 3 Editor der Testbank laeuft nicht,
              6 list/watch: Dialog offen (der nicht automatisch beantwortet wurde); press: Knopf/Dialog nicht gefunden.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('list', 'press', 'watch', 'help')]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Button,
    [string]$Title,
    [string]$Project,
    [string]$ProjectRoot,
    [string]$QueueDir,
    [switch]$All,
    [switch]$Json,
    [switch]$Once,
    [switch]$Quiet,
    [int]$IntervalSeconds = 3,
    # So lange muss ein bekannter Dialog offen sein, bevor der Waechter klickt (ein Chat kann ihn vorher selbst schliessen).
    [double]$GraceSeconds = 2,
    # Nur fuer Tests (Tests/EditorDialogChecks.ps1): Fenster dieses Prozesses statt des Testbank-Editors. Nie ein Unity Editor.
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$inv = [Globalization.CultureInfo]::InvariantCulture
if ($Command -eq 'help') { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output; exit 0 }

# --- Ordner -------------------------------------------------------------------

if (-not $ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$repoRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
$ownerMatch = [regex]::Match($repoRoot, '^(.*?)[\\/]\.claude[\\/]worktrees[\\/]')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }
# Aus jedem Arbeitsordner ("Mining Game", "Mining Game 3", Worktrees) gilt: Benutzer = "<Projekt>", Testbank = "<Projekt> 2".
$baseName = (Split-Path $ownerRoot -Leaf) -replace ' \d+$', ''
$reposDir = Split-Path $ownerRoot -Parent
if (-not $Project) { $Project = "$baseName 2" }
if ($Project -eq $baseName) {
    Write-Output "'$Project' ist der Editor des Benutzers. editor-dialog arbeitet nur mit der Testbank."
    exit 1
}
$projectPath = [IO.Path]::GetFullPath((Join-Path $reposDir $Project)).TrimEnd('\')
if (-not $QueueDir) { $QueueDir = $env:EDITOR_QUEUE_DIR }
if (-not $QueueDir) { $QueueDir = Join-Path $reposDir 'EditorQueue' }
$stateFile = Join-Path $QueueDir "$Project.dialogs.json"
$logFile = Join-Path $QueueDir "$Project.log"
$activityFile = Join-Path $QueueDir "$Project.activity.json"

# --- Bekannte Dialoge (Waechter) ----------------------------------------------
# match: Regex auf Titel + Text. press: sichere Wahl (Knopftext ohne &), $null = nur melden.

$Rules = @(
    @{ name = 'modified-externally'; match = 'modified externally'; press = 'Reload'; why = 'Szene wurde ausserhalb geaendert (git checkout): neu laden' }
    @{ name = 'scene-save'; match = 'Scene\(s\) Have Been Modified|save the changes you made in the scene'; press = "Don't Save"; why = 'die Testbank speichert keine Szenen' }
    @{ name = 'safe-mode'; match = 'Safe Mode'; press = 'Ignore'; why = 'Compilefehler: Editor normal oeffnen, die Fehler stehen in der Konsole' }
    @{ name = 'api-updater'; match = 'API Update Required|Script Updating Consent|API Updater'; press = 'No Thanks'; why = 'Code der Branches nicht automatisch umschreiben (veraltete API im eigenen Branch beheben)' }
    @{ name = 'non-matching-editor'; match = 'Non-Matching Editor|different version of the Editor|Opening Project in'; press = $null; why = 'Editor-Version passt nicht: Benutzer fragen' }
)

# --- Win32 --------------------------------------------------------------------

if (-not ('EditorDialogNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class EditorDialogNative {
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeoutText(IntPtr hwnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);

    const uint SMTO_ABORTIFHUNG = 2, WM_GETTEXT = 0x0D, WM_GETTEXTLENGTH = 0x0E, WM_COMMAND = 0x111, BM_CLICK = 0xF5;

    public static IntPtr[] TopWindows(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list.ToArray();
    }

    public static IntPtr[] Children(IntPtr parent) {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list.ToArray();
    }

    public static string ClassOf(IntPtr hwnd) { var sb = new StringBuilder(256); GetClassName(hwnd, sb, sb.Capacity); return sb.ToString(); }

    // Fenstertitel ohne Nachricht an den (evtl. blockierten) Editor.
    public static string Caption(IntPtr hwnd) { var sb = new StringBuilder(1024); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); }

    // Text eines Steuerelements in einem fremden Prozess: nur per WM_GETTEXT (mit Zeitlimit, falls der Editor haengt).
    public static string Text(IntPtr hwnd) {
        IntPtr len;
        if (SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out len) == IntPtr.Zero) return "";
        int n = Math.Min(Math.Max(len.ToInt32(), 0), 8000) + 1;
        var sb = new StringBuilder(n);
        IntPtr got;
        if (SendMessageTimeoutText(hwnd, WM_GETTEXT, (IntPtr)n, sb, SMTO_ABORTIFHUNG, 1000, out got) == IntPtr.Zero) return "";
        return sb.ToString();
    }

    public static bool Click(IntPtr button) {
        IntPtr r;
        return SendMessageTimeout(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 5000, out r) != IntPtr.Zero;
    }

    // Rueckfall, falls BM_CLICK nicht wirkt (Dialog nicht aktiv): BN_CLICKED direkt an den Dialog.
    public static bool Command(IntPtr dialog, IntPtr button) {
        IntPtr r;
        int id = GetDlgCtrlID(button) & 0xFFFF;
        return SendMessageTimeout(dialog, WM_COMMAND, (IntPtr)id, button, SMTO_ABORTIFHUNG, 5000, out r) != IntPtr.Zero;
    }
}
'@
}

# --- Hilfsfunktionen ----------------------------------------------------------

function Now { [DateTime]::UtcNow }
function Iso([DateTime]$t) { $t.ToUniversalTime().ToString('o', $inv) }
function AgeSeconds($value) {
    if (-not $value) { return 0 }
    return ((Now) - [DateTime]::Parse([string]$value, $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()).TotalSeconds
}
# Write-Host: auch aus Funktionen mit Rueckgabewert sichtbar.
function Say([string]$text) { if (-not $Quiet) { Write-Host $text } }

# Knopftexte vergleichbar machen: & (Tastenkuerzel) weg, typografische Apostrophe, Gross/Klein, Leerraum.
$apostrophes = '[' + [char]0x2018 + [char]0x2019 + [char]0x60 + [char]0xB4 + ']'
function Norm([string]$s) { (($s -replace '&', '' -replace $apostrophes, "'") -replace '\s+', ' ').Trim().ToLowerInvariant() }

function Write-Log([string]$text) {
    if (-not (Test-Path -LiteralPath $QueueDir)) { return }
    $line = '{0}  {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $inv), $text
    for ($i = 0; $i -lt 20; $i++) {
        try { [IO.File]::AppendAllText($logFile, $line + "`r`n", $utf8); return } catch { Start-Sleep -Milliseconds 50 }
    }
}

# Editor-Prozess der Testbank: Unity.exe (Editor, nicht die Unity-CLI) mit -projectpath genau auf den Testbank-Ordner.
function Find-Editor {
    if ($ProcessId) {
        $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if (-not $p -or $p.ProcessName -ieq 'Unity') { return $null }
        return $ProcessId
    }
    $hint = 0
    if (Test-Path -LiteralPath $activityFile) {
        try { $hint = [int](([IO.File]::ReadAllText($activityFile, $utf8) | ConvertFrom-Json).processId) } catch { }
    }
    try { $procs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction Stop) } catch { return $null }
    $found = @(foreach ($p in $procs) {
            if ([string]$p.ExecutablePath -notmatch '\\Editor\\Unity\.exe$') { continue }
            $m = [regex]::Match([string]$p.CommandLine, '-projectpath\s+(?:"([^"]+)"|(\S+))', 'IgnoreCase')
            if (-not $m.Success) { continue }
            $path = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
            try { $path = [IO.Path]::GetFullPath($path).TrimEnd('\', '/') } catch { continue }
            if ($path -ieq $projectPath) { [int]$p.ProcessId }
        })
    if ($found.Count -eq 0) { return $null }
    if ($found -contains $hint) { return $hint }
    return $found[0]
}

function Get-PrevState {
    if (-not (Test-Path -LiteralPath $stateFile)) { return $null }
    try { return [IO.File]::ReadAllText($stateFile, $utf8) | ConvertFrom-Json } catch { return $null }
}

# Sichtbare Fenster des Editors. Dialog = hat Druckknoepfe; Fortschrittsfenster (Importieren, "Hold on") nur zur Info.
function Get-Windows([int]$editorPid, $prev) {
    $since = @{}; $logged = @{}
    if ($prev -and $prev.dialogs) { foreach ($d in @($prev.dialogs)) { $since["$($d.hwnd)"] = $d.since; $logged["$($d.hwnd)"] = [bool]$d.logged } }
    $result = New-Object System.Collections.ArrayList
    foreach ($h in [EditorDialogNative]::TopWindows([uint32]$editorPid)) {
        $cls = [EditorDialogNative]::ClassOf($h)
        $title = [EditorDialogNative]::Caption($h)
        $buttons = New-Object System.Collections.ArrayList
        $options = New-Object System.Collections.ArrayList
        $texts = New-Object System.Collections.ArrayList
        $progress = $false
        foreach ($c in [EditorDialogNative]::Children($h)) {
            if (-not [EditorDialogNative]::IsWindowVisible($c)) { continue }
            $cc = [EditorDialogNative]::ClassOf($c)
            if ($cc -like '*progress32*') { $progress = $true; continue }
            # Native Knoepfe (Unity, MessageBox) und WinForms-Knoepfe (WindowsForms10.BUTTON.*, nur in Tests/EditorDialogChecks.ps1)
            if ($cc -eq 'Button' -or $cc -like 'WindowsForms*.BUTTON.*') {
                $t = [EditorDialogNative]::Text($c)
                if (-not $t) { continue }
                # Untere 4 Stil-Bits: 0/1 Druckknopf, B Owner-Draw, C-F Split-/Befehlsknopf; sonst Kontrollkaestchen/Optionsfeld/Gruppe.
                $type = [EditorDialogNative]::GetWindowLong($c, -16) -band 0xF
                if (@(0, 1, 0xB, 0xC, 0xD, 0xE, 0xF) -contains $type) {
                    [void]$buttons.Add([ordered]@{ text = ($t -replace '&', ''); hwnd = [int64]$c; enabled = [EditorDialogNative]::IsWindowEnabled($c) })
                }
                elseif ($type -ne 7) { [void]$options.Add(($t -replace '&', '')) }
                continue
            }
            if ($cc -eq 'Static' -or $cc -eq 'Edit' -or $cc -like 'RichEdit*' -or $cc -like 'WindowsForms*.STATIC.*' -or $cc -like 'WindowsForms*.EDIT.*') {
                $t = [EditorDialogNative]::Text($c)
                if ($t -and $t.Trim()) { [void]$texts.Add($t.Trim()) }
            }
        }
        $isUnity = $cls -like 'Unity*'
        $kind = if ($buttons.Count -gt 0 -and -not $progress) { 'dialog' } elseif ($progress -or ($cls -eq '#32770')) { 'progress' } else { 'window' }
        if (-not $All -and ($kind -eq 'window' -or ($isUnity -and $kind -ne 'dialog'))) { continue }
        $text = ($texts -join "`n")
        if ($text.Length -gt 2000) { $text = $text.Substring(0, 2000) + ' ...' }
        $rule = $null
        if ($kind -eq 'dialog') {
            foreach ($r in $Rules) { if (("$title`n$text") -match $r.match) { $rule = $r; break } }
        }
        $key = "$([int64]$h)"
        [void]$result.Add([ordered]@{
                hwnd = [int64]$h; title = $title; class = $cls; kind = $kind; text = $text
                buttons = @($buttons); options = @($options)
                since = $(if ($since.ContainsKey($key) -and $since[$key]) { $since[$key] } else { Iso (Now) })
                rule = $(if ($rule) { $rule.name } else { $null })
                press = $(if ($rule) { $rule.press } else { $null })
                why = $(if ($rule) { $rule.why } else { $null })
                logged = ($logged.ContainsKey($key) -and $logged[$key])
            })
    }
    return , $result
}

function Save-State([int]$editorPid, $windows, $prev, $handled) {
    if (-not (Test-Path -LiteralPath $QueueDir)) { return }
    $recent = New-Object System.Collections.ArrayList
    foreach ($x in @($handled)) { if ($x) { [void]$recent.Add($x) } }
    if ($prev -and $prev.recent) { foreach ($x in @($prev.recent)) { if ($recent.Count -lt 10) { [void]$recent.Add($x) } } }
    $obj = [ordered]@{
        project = $Project; updated = (Iso (Now)); processId = $editorPid
        dialogs = @($windows | Where-Object { $_.kind -ne 'window' } | ForEach-Object {
                $d = [ordered]@{}; foreach ($k in $_.Keys) { $d[$k] = $_[$k] }
                $d.buttons = @($_.buttons | ForEach-Object { $_.text }); $d
            })
        recent = @($recent)
    }
    $json = ConvertTo-Json -InputObject $obj -Depth 6
    $tmp = "$stateFile.$PID.tmp"
    try {
        [IO.File]::WriteAllText($tmp, $json, $utf8)
        for ($i = 0; $i -lt 20; $i++) {
            try {
                if (Test-Path -LiteralPath $stateFile) { [IO.File]::Replace($tmp, $stateFile, [NullString]::Value) } else { [IO.File]::Move($tmp, $stateFile) }
                return
            }
            catch { Start-Sleep -Milliseconds 50 }
        }
    }
    finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } }
}

function Describe-Dialog($d) {
    $b = ($d.buttons | ForEach-Object { "[$($_.text)]" }) -join ' '
    $t = ($d.text -replace '\s+', ' ')
    if ($t.Length -gt 200) { $t = $t.Substring(0, 200) + ' ...' }
    return "'$($d.title)' | $t | Knoepfe: $b"
}

function Find-Button($d, [string]$label) {
    $want = Norm $label
    $exact = @($d.buttons | Where-Object { (Norm $_.text) -eq $want })
    if ($exact.Count -ge 1) { return $exact[0] }
    $prefix = @($d.buttons | Where-Object { (Norm $_.text).StartsWith($want) })
    if ($prefix.Count -eq 1) { return $prefix[0] }
    return $null
}

function Test-Open([int64]$hwnd) {
    $h = [IntPtr]$hwnd
    return ([EditorDialogNative]::IsWindow($h) -and [EditorDialogNative]::IsWindowVisible($h))
}

# Klickt einen Knopf; wirkt BM_CLICK nicht (Dialog im Hintergrund), BN_CLICKED direkt an den Dialog.
function Invoke-Press($d, $btn) {
    [void][EditorDialogNative]::Click([IntPtr]$btn.hwnd)
    for ($i = 0; $i -lt 10 -and (Test-Open $d.hwnd); $i++) { Start-Sleep -Milliseconds 100 }
    if (Test-Open $d.hwnd) {
        [void][EditorDialogNative]::Command([IntPtr]$d.hwnd, [IntPtr]$btn.hwnd)
        for ($i = 0; $i -lt 10 -and (Test-Open $d.hwnd); $i++) { Start-Sleep -Milliseconds 100 }
    }
    return -not (Test-Open $d.hwnd)
}

function Show-Windows([int]$editorPid, $windows) {
    $dialogs = @($windows | Where-Object { $_.kind -eq 'dialog' })
    Write-Output ("Testbank-Editor '{0}' (Prozess {1}): {2}" -f $Project, $editorPid, $(if ($dialogs.Count) { "$($dialogs.Count) Dialog(e) offen" } else { 'kein Dialog offen' }))
    $n = 0
    foreach ($d in $windows) {
        $n++
        Write-Output ("[{0}] {1} '{2}'  (Klasse {3}, seit {4:0} s, Fenster 0x{5:X})" -f $n, $d.kind, $d.title, $d.class, (AgeSeconds $d.since), $d.hwnd)
        if ($d.text) { ($d.text -split "`n") | ForEach-Object { Write-Output "    $_" } }
        if ($d.options.Count) { Write-Output ("    Optionen: {0}" -f (($d.options | ForEach-Object { "[ ] $_" }) -join '  ')) }
        if ($d.buttons.Count) { Write-Output ("    Knoepfe: {0}" -f (($d.buttons | ForEach-Object { "[$($_.text)]" + $(if (-not $_.enabled) { '(inaktiv)' } else { '' }) }) -join ' ')) }
        if ($d.rule) {
            if ($d.press) { Write-Output "    Bekannt ($($d.rule)): Waechter klickt '$($d.press)' - $($d.why)" }
            else { Write-Output "    Bekannt ($($d.rule)): nicht automatisch - $($d.why)" }
        }
        elseif ($d.kind -eq 'dialog') { Write-Output '    Unbekannt: nicht automatisch geklickt, dem Benutzer melden oder bewusst mit press schliessen.' }
    }
    if ($dialogs.Count) { Write-Output 'Schliessen: Tools/editor-dialog.sh press "<Knopf>"  (mehrere Dialoge: -Title "<Teil des Titels>")' }
}

# Ein Durchgang des Waechters: bekannte Dialoge beantworten, unbekannte einmal protokollieren. Rueckgabe: offene Dialoge.
function Invoke-Watch([int]$editorPid, [double]$grace) {
    $prev = Get-PrevState
    if ($prev -and $prev.processId -ne $editorPid) { $prev = $null }
    $windows = Get-Windows $editorPid $prev
    $handled = New-Object System.Collections.ArrayList
    foreach ($d in @($windows | Where-Object { $_.kind -eq 'dialog' })) {
        # Jeder Dialog kommt nur einmal ins Protokoll (Merker in der Status-Datei).
        $isNew = -not $d.logged
        if ($d.press) {
            if ((AgeSeconds $d.since) -lt $grace) { continue }
            $d.logged = $true
            $btn = Find-Button $d $d.press
            if ($btn) {
                $ok = Invoke-Press $d $btn
                if ($ok) {
                    Write-Log "DIALOG automatisch beantwortet ($($d.rule)): $(Describe-Dialog $d) -> '$($btn.text)'"
                    Say "Dialog '$($d.title)' automatisch beantwortet: '$($btn.text)' ($($d.why))."
                    [void]$handled.Add([ordered]@{ time = (Iso (Now)); title = $d.title; button = $btn.text; rule = $d.rule; by = 'Waechter' })
                    $d.kind = 'closed'
                    continue
                }
                if ($isNew) { Write-Log "DIALOG Klick auf '$($btn.text)' ohne Wirkung ($($d.rule)): $(Describe-Dialog $d)" }
                continue
            }
            if ($isNew) { Write-Log "DIALOG bekannt ($($d.rule)), aber Knopf '$($d.press)' fehlt, nicht geklickt: $(Describe-Dialog $d)" }
            continue
        }
        $d.logged = $true
        if ($isNew) {
            $what =if ($d.rule) { "bekannt ($($d.rule)), nicht automatisch: $($d.why)" } else { 'unbekannt, nicht geklickt' }
            Write-Log "DIALOG offen, $what - $(Describe-Dialog $d). Schliessen: Tools/editor-dialog.sh press `"<Knopf>`""
        }
    }
    $open = @($windows | Where-Object { $_.kind -ne 'closed' })
    Save-State $editorPid $open $prev $handled
    return , $open
}

# --- Befehle ------------------------------------------------------------------

$editorPid = Find-Editor
if (-not $editorPid -and $Command -ne 'watch') {
    if ($Json) { ConvertTo-Json -Compress -InputObject ([ordered]@{ project = $Project; running = $false; dialogs = @() }) }
    else { Say "Editor der Testbank '$Project' ($projectPath) laeuft nicht." }
    exit 3
}

switch ($Command) {
    'list' {
        $prev = Get-PrevState
        if ($prev -and $prev.processId -ne $editorPid) { $prev = $null }
        $windows = Get-Windows $editorPid $prev
        Save-State $editorPid $windows $prev @()
        $dialogs = @($windows | Where-Object { $_.kind -eq 'dialog' })
        if ($Json) {
            ConvertTo-Json -Depth 6 -Compress -InputObject ([ordered]@{ project = $Project; running = $true; processId = $editorPid; dialogs = @($windows) })
        }
        else { Show-Windows $editorPid $windows }
        if ($dialogs.Count) { exit 6 } else { exit 0 }
    }
    'press' {
        if (-not $Button) { Write-Output 'press braucht den Knopftext, z. B. press "Reload".'; exit 1 }
        $prev = Get-PrevState
        if ($prev -and $prev.processId -ne $editorPid) { $prev = $null }
        $windows = Get-Windows $editorPid $prev
        $dialogs = @($windows | Where-Object { $_.kind -eq 'dialog' })
        if ($Title) { $dialogs = @($dialogs | Where-Object { $_.title -like "*$Title*" }) }
        $hits = @(foreach ($d in $dialogs) { $b = Find-Button $d $Button; if ($b) { , @($d, $b) } })
        if ($hits.Count -eq 0) {
            Write-Output "Kein offener Dialog mit Knopf '$Button'$(if ($Title) { " und Titel '*$Title*'" })."
            if ($windows.Count) { Show-Windows $editorPid $windows }
            exit 6
        }
        if ($hits.Count -gt 1) {
            Write-Output "Mehrere Dialoge haben den Knopf '$Button'. Mit -Title `"<Teil des Titels>`" waehlen:"
            Show-Windows $editorPid $windows
            exit 6
        }
        $d = $hits[0][0]; $btn = $hits[0][1]
        $ok = Invoke-Press $d $btn
        if (-not $ok) { Write-Output "Klick auf '$($btn.text)' in '$($d.title)' hat den Dialog nicht geschlossen."; exit 1 }
        Write-Log "DIALOG per press geschlossen: $(Describe-Dialog $d) -> '$($btn.text)'"
        $by = if ($env:CLAUDE_CODE_SESSION_ID) { "press (Sitzung $($env:CLAUDE_CODE_SESSION_ID))" } else { 'press' }
        $windows = @($windows | Where-Object { $_.hwnd -ne $d.hwnd })
        Save-State $editorPid $windows $prev @([ordered]@{ time = (Iso (Now)); title = $d.title; button = $btn.text; rule = $d.rule; by = $by })
        Write-Output "Dialog '$($d.title)' geschlossen mit '$($btn.text)'."
        exit 0
    }
    'watch' {
        do {
            $editorPid = Find-Editor
            $open = @()
            if ($editorPid) { $open = Invoke-Watch $editorPid $GraceSeconds }
            elseif (-not $Once) { Start-Sleep -Seconds ([Math]::Max(10, $IntervalSeconds)); continue }
            if ($Once) {
                if (-not $editorPid) { Say "Editor der Testbank '$Project' laeuft nicht."; exit 3 }
                $dialogs = @($open | Where-Object { $_.kind -eq 'dialog' })
                if ($dialogs.Count) {
                    if (-not $Quiet) { Show-Windows $editorPid $open }
                    exit 6
                }
                exit 0
            }
            Start-Sleep -Seconds ([Math]::Max(1, $IntervalSeconds))
        } while ($true)
    }
}
