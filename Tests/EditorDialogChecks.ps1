# Prueft Tools/editor-dialog.ps1 ohne Unity: Testdialoge (Fenster mit Text und Knoepfen) in einem eigenen PowerShell-Prozess,
# ausserhalb des Bildschirms und ohne Fokus (nimmt dem Benutzer keine Tastatureingaben weg).
# Queue-Ordner isoliert in Temp/EditorDialogTest (nie der echte). Echte Unity-Dialoge: siehe Docs/Editor-Warteschlange.md.
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EditorDialogChecks.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script = Join-Path $root 'Tools\editor-dialog.ps1'
$queueScript = Join-Path $root 'Tools\editor-queue.ps1'
$dir = Join-Path $root 'Temp\EditorDialogTest'
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory $dir | Out-Null
$project = 'DialogTest 2'
$stateFile = Join-Path $dir "$project.dialogs.json"
$logFile = Join-Path $dir "$project.log"
$failures = 0
$boxes = New-Object System.Collections.ArrayList

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Output "OK    $name" } else { Write-Output "FEHLER $name $detail"; $script:failures++ }
}

$boxScript = Join-Path $dir 'box.ps1'
Set-Content -LiteralPath $boxScript -Encoding UTF8 -Value @'
param([string]$Title, [string]$Text, [string]$Buttons, [string]$Result)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition 'public class NoActivateForm : System.Windows.Forms.Form { protected override bool ShowWithoutActivation { get { return true; } } }'
$form = New-Object NoActivateForm
$form.Text = $Title; $form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000)
$label = New-Object System.Windows.Forms.Label; $label.Text = $Text; $label.AutoSize = $true
$form.Controls.Add($label)
$x = 0
foreach ($b in $Buttons -split ',') {
    $btn = New-Object System.Windows.Forms.Button
    $btn.Text = $b; $btn.Top = 40; $btn.Left = $x; $btn.Width = 110; $x += 120
    $btn.add_Click({ param($s, $e) [IO.File]::WriteAllText($Result, ($s.Text -replace '&', '')); $form.Close() })
    $form.Controls.Add($btn)
}
[System.Windows.Forms.Application]::Run($form)
'@

function D([string[]]$argv) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @argv -Project $project -QueueDir $dir
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}

# Testdialog starten; Ergebnis (Text des geklickten Knopfs) landet in <name>.result.
function Start-Box([string]$name, [string]$text, [string]$title, [string]$buttons) {
    $result = Join-Path $dir "$name.result"
    $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$boxScript`"", '-Title', "`"$title`"", '-Text', "`"$text`"", '-Buttons', "`"$buttons`"", '-Result', "`"$result`"")
    $p = Start-Process powershell.exe -ArgumentList $argv -PassThru -WindowStyle Hidden
    $null = $p.Handle
    [void]$script:boxes.Add($p)
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 300
        if ((D @('list', '-Json', '-ProcessId', $p.Id)).out -match '"kind":"dialog"') { break }
    }
    return @{ proc = $p; result = $result }
}
function Result($box) {
    $box.proc.WaitForExit(5000) | Out-Null
    if (Test-Path $box.result) { return (Get-Content $box.result -Raw).Trim() }
    return ''
}
function LogText { if (Test-Path $logFile) { Get-Content $logFile -Raw } else { '' } }

try {
    # 0) Nie der Editor des Benutzers.
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script list -Project 'Mining Game' -QueueDir $dir
    Check 'Editor des Benutzers abgelehnt' ($LASTEXITCODE -eq 1 -and ($out -join ' ') -match 'Benutzers')

    # 1) list zeigt Titel, Text und Knoepfe eines unbekannten Dialogs; press klickt ihn (& und Gross/Klein egal).
    $box = Start-Box 'unknown' 'Testtext fuer den Dialog' 'Unbekannter Testdialog' '&Yes,&No'
    $r = D @('list', '-ProcessId', $box.proc.Id)
    Check 'list: Exit 6 bei offenem Dialog' ($r.code -eq 6) "exit=$($r.code)"
    Check 'list: Titel, Text, Knoepfe ohne &' ($r.out -match 'Unbekannter Testdialog' -and $r.out -match 'Testtext' -and $r.out -match '\[Yes\] \[No\]') $r.out
    Check 'list: als unbekannt markiert' ($r.out -match 'Unbekannt')
    Check 'Status-Datei geschrieben' ((Test-Path $stateFile) -and ((Get-Content $stateFile -Raw) -match 'Unbekannter Testdialog'))
    $r = D @('watch', '-Once', '-GraceSeconds', '0', '-ProcessId', $box.proc.Id)
    Check 'watch: unbekannter Dialog wird nicht geklickt' ($r.code -eq 6 -and -not $box.proc.HasExited) "exit=$($r.code)"
    Check 'watch: unbekannter Dialog protokolliert' ((LogText) -match 'unbekannt, nicht geklickt')
    $r = D @('press', 'Gibtsnicht', '-ProcessId', $box.proc.Id)
    Check 'press: fehlender Knopf -> 6' ($r.code -eq 6) "exit=$($r.code)"
    $r = D @('press', 'no', '-ProcessId', $box.proc.Id)
    Check 'press: Knopf geklickt' ($r.code -eq 0 -and $r.out -match 'geschlossen') $r.out
    Check 'press: Dialog-Ergebnis No' ((Result $box) -eq 'No')
    Check 'press: protokolliert' ((LogText) -match "per press geschlossen.*'No'")

    # 2) Waechter: "modified externally" wird nach der Schonfrist automatisch mit Reload beantwortet.
    $box = Start-Box 'reload' 'The open scene(s) have been modified externally. Do you want to reload?' 'Scene(s) Modified' '&Reload,&Ignore'
    $r = D @('watch', '-Once', '-GraceSeconds', '30', '-ProcessId', $box.proc.Id)
    Check 'watch: Schonfrist, noch nicht geklickt' ($r.code -eq 6 -and -not $box.proc.HasExited) "exit=$($r.code)"
    $r = D @('watch', '-Once', '-GraceSeconds', '0', '-ProcessId', $box.proc.Id)
    Check 'watch: bekannter Dialog beantwortet' ($r.code -eq 0 -and $r.out -match 'automatisch beantwortet') $r.out
    Check 'watch: Reload gewaehlt' ((Result $box) -eq 'Reload')
    Check 'watch: protokolliert' ((LogText) -match "automatisch beantwortet \(modified-externally\).*'Reload'")
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
    Check 'Status: zuletzt beantwortet, keiner offen' (@($state.recent)[0].button -eq 'Reload' -and @($state.dialogs | Where-Object { $_.kind -eq 'dialog' }).Count -eq 0)

    # 3) Safe Mode -> Ignore.
    $box = Start-Box 'safemode' 'This project has compilation errors.' 'Enter Safe Mode?' 'Enter Safe Mode,&Ignore,&Quit'
    $r = D @('watch', '-Once', '-GraceSeconds', '0', '-ProcessId', $box.proc.Id)
    Check 'watch: Safe Mode -> Ignore' ((Result $box) -eq 'Ignore') $r.out

    # 4) Bekannte Regel, aber passender Knopf fehlt: nicht klicken, nur melden; Queue sieht den Editor als belegt.
    $box = Start-Box 'missing' 'Do you want to save the changes you made in the scenes?' 'Scene(s) Have Been Modified' '&OK,&Cancel'
    $r = D @('watch', '-Once', '-GraceSeconds', '0', '-ProcessId', $box.proc.Id)
    Check 'watch: Regel ohne Knopf klickt nicht' ($r.code -eq 6 -and -not $box.proc.HasExited) "exit=$($r.code)"
    Check 'watch: fehlender Knopf protokolliert' ((LogText) -match "Knopf 'Don't Save' fehlt")
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $queueScript status -Json -Project $project -QueueDir $dir
    $st = ($out | Where-Object { $_.StartsWith('{') } | Select-Object -Last 1) | ConvertFrom-Json
    Check 'Queue-Status: Dialog gemeldet, Editor belegt' ($st.editor.busy -and $st.editor.text -match "Dialog offen.*Scene\(s\) Have Been Modified") $st.editor.text
    $r = D @('press', 'cancel', '-Title', 'Have Been', '-ProcessId', $box.proc.Id)
    Check 'press mit -Title' ((Result $box) -eq 'Cancel') $r.out
    $r = D @('list', '-ProcessId', $box.proc.Id)
    Check 'list: Testprozess beendet -> 3' ($r.code -eq 3) "exit=$($r.code)"
}
finally {
    foreach ($p in $boxes) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
}
if ($failures) { Write-Output "$failures Fehler"; exit 1 }
Write-Output 'Alle Pruefungen OK'
