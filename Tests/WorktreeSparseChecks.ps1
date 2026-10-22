# Prueft Tools/worktree-sparse.ps1 in einem isolierten Test-Repo (Temp/WorktreeSparseTest), nie am echten Repository.
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WorktreeSparseChecks.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script = Join-Path $root 'Tools\worktree-sparse.ps1'
$t = Join-Path $root 'Temp\WorktreeSparseTest'
if (Test-Path $t) { Remove-Item $t -Recurse -Force }
$repo = Join-Path $t 'repo'
$wts = Join-Path $t 'wts'
$failures = 0

# git im Test: Ausgabe als Text, Exit-Code in $script:gitCode (stderr ist kein Fehler).
function G([string]$dir) {
    $argv = $args
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $out = & git -C $dir @argv 2>&1
    $script:gitCode = $LASTEXITCODE; $ErrorActionPreference = $old
    return (($out | ForEach-Object { "$_" }) -join "`n")
}
function Must([string]$dir) { $o = G $dir @args; if ($script:gitCode -ne 0) { throw "git $($args -join ' '): $o" }; $o }
function Run([string[]]$argv) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @argv -Repo $repo
    return @{ code = $LASTEXITCODE; out = ($out -join "`n") }
}
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Output "OK    $name" } else { Write-Output "FEHLER $name $detail"; $script:failures++ }
}
function Put([string]$path, [string]$text) {
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($path, $text)
}
function Wt([string]$name) { Join-Path $wts $name }
function Has([string]$wt, [string]$rel) { Test-Path -LiteralPath (Join-Path $wt $rel) }
function Marker([string]$wt) {
    $f = Join-Path (G $wt rev-parse --absolute-git-dir).Trim() 'mining-game-sparse'
    if (Test-Path -LiteralPath $f) { return (Get-Content -LiteralPath $f -Raw).Trim() }
    return ''
}

# --- Aufbau: Repo mit Design/, AudioMastering/, einem vorhandenen Hook und einem alten Arbeitsordner ---------------
New-Item -ItemType Directory -Path $repo, $wts -Force | Out-Null
Must $t init -q -b main repo | Out-Null
Must $repo config user.name Test | Out-Null
Must $repo config user.email test@example.invalid | Out-Null
Put (Join-Path $repo 'readme.txt') 'v1'
Put (Join-Path $repo 'Assets\a.txt') 'a'
Put (Join-Path $repo 'Design\Thema\x.png') 'x'
Put (Join-Path $repo 'Design\Anderes\y.png') 'y'
Put (Join-Path $repo 'AudioMastering\z.wav') 'z'
Must $repo add -A | Out-Null
Must $repo commit -q -m init | Out-Null
$hook = Join-Path $repo '.git\hooks\post-checkout'
$oldLine = 'echo alt >> "$(git rev-parse --git-common-dir)/alt-hook.log"'
[IO.File]::WriteAllText($hook, "#!/bin/sh`n$oldLine`n")
Must $repo worktree add -q (Wt 'alt') -b t/alt main | Out-Null
Must $repo config extensions.worktreeConfig true | Out-Null
# Markierung einer frueheren Version (in config.worktree) muss install entfernen.
Must (Wt 'alt') config --worktree miningGame.sparse vorhanden | Out-Null

# 0) Sicherung: das Skript muss den Hook des Test-Repos meinen, nie den echten (sonst sofort abbrechen).
$r = Run @('status')
if ($r.out -notmatch [regex]::Escape("($hook)")) { Write-Output "ABBRUCH: Skript meint nicht das Test-Repo:`n$($r.out)"; exit 1 }

# 1) install: Block nach der Shebang-Zeile, alter Hook bleibt, bestehender Arbeitsordner markiert.
$r = Run @('install')
$lines = @([IO.File]::ReadAllText($hook) -split "`n")
Check 'install endet mit 0' ($r.code -eq 0) $r.out
Check 'Shebang bleibt erste Zeile, Block direkt danach' (($lines[0] -eq '#!/bin/sh') -and ($lines[1] -like '# >>> Mining Game*')) ($lines[0..2] -join ' | ')
Check 'alte Hook-Zeile bleibt (nach dem Block)' ($lines -contains $oldLine)
Check 'kein CR im Hook' (-not ([IO.File]::ReadAllText($hook).Contains("`r")))
Check 'bestehender Arbeitsordner markiert' ((Marker (Wt 'alt')) -eq 'vorhanden') (Marker (Wt 'alt'))
Check 'alte Markierung in config.worktree entfernt' (-not (G (Wt 'alt') config --worktree --get miningGame.sparse))
$r = Run @('install')
Check 'install zweimal: Block nur einmal' (([regex]::Matches([IO.File]::ReadAllText($hook), 'Mining Game: Arbeitsordner ohne Design \(Tools')).Count -eq 1)

# 2) Bestehender Arbeitsordner und Hauptordner bleiben bei Branchwechseln vollstaendig.
Must (Wt 'alt') switch -q -c t/alt2 | Out-Null
Check 'alter Arbeitsordner behaelt Design/ nach Branchwechsel' (Has (Wt 'alt') 'Design\Thema\x.png')
Must $repo switch -q -c t/main2 | Out-Null
Must $repo switch -q main | Out-Null
Check 'Hauptordner behaelt Design/' (Has $repo 'Design\Anderes\y.png')

# 3) Neuer Arbeitsordner: ohne Design/ und AudioMastering/, sonst vollstaendig und sauber.
Remove-Item (Join-Path $repo '.git\alt-hook.log') -ErrorAction SilentlyContinue
$out = G $repo worktree add (Wt 'neu') -b t/neu main
Check 'git worktree add endet mit 0' ($script:gitCode -eq 0) $out
$neu = Wt 'neu'
Check 'neu: ohne Design/ und AudioMastering/' ((-not (Has $neu 'Design')) -and (-not (Has $neu 'AudioMastering'))) (Get-ChildItem $neu -Name -Force | Out-String)
Check 'neu: Assets/ und Dateien der Wurzel da' ((Has $neu 'Assets\a.txt') -and (Has $neu 'readme.txt'))
Check 'neu: git status sauber' (-not (G $neu status --porcelain)) (G $neu status --porcelain)
Check 'neu: markiert ohne-design' ((Marker $neu) -eq 'ohne-design') (Marker $neu)
Check 'alter Hook lief weiter' (Test-Path (Join-Path $repo '.git\alt-hook.log'))

# 3b) Aus einem anderen Arbeitsordner heraus angelegt (Git kopiert dessen config.worktree): trotzdem ohne Design/.
Must (Wt 'alt') worktree add -q (Wt 'ausalt') -b t/ausalt main | Out-Null
Check 'aus vollstaendigem Arbeitsordner angelegt: ohne Design/' ((Has (Wt 'ausalt') 'Assets\a.txt') -and (-not (Has (Wt 'ausalt') 'Design'))) (Marker (Wt 'ausalt'))
Must $neu worktree add -q (Wt 'aussparse') -b t/aussparse main | Out-Null
Check 'aus Sparse-Arbeitsordner angelegt: ohne Design/' ((Has (Wt 'aussparse') 'Assets\a.txt') -and (-not (Has (Wt 'aussparse') 'Design')))

# 4) Design/ bei Bedarf nachholen: ein Thema oder alles; danach kein erneutes Ausblenden.
Must $neu sparse-checkout add 'Design/Thema/' | Out-Null
Check 'sparse-checkout add holt nur das Thema' ((Has $neu 'Design\Thema\x.png') -and (-not (Has $neu 'Design\Anderes')))
Must $neu sparse-checkout disable | Out-Null
Check 'sparse-checkout disable holt alles' ((Has $neu 'Design\Anderes\y.png') -and (Has $neu 'AudioMastering\z.wav'))
Must $neu switch -q -c t/neu2 | Out-Null
Check 'nach disable bleibt Design/ auch nach Branchwechsel' (Has $neu 'Design\Anderes\y.png')

# 5) Commit im Arbeitsordner ohne Design/ laesst Design/ im Branch unveraendert.
Must $repo worktree add -q (Wt 'commit') -b t/commit main | Out-Null
Put (Join-Path (Wt 'commit') 'Assets\a.txt') 'a2'
Must (Wt 'commit') commit -q -am 'nur Assets' | Out-Null
$tree = G $repo ls-tree -r --name-only t/commit
Check 'Commit behaelt Design/ und AudioMastering/ im Branch' (($tree -match 'Design/Anderes/y.png') -and ($tree -match 'AudioMastering/z.wav')) $tree

# 6) Arbeitsordner ohne Auschecken angelegt, danach ausgecheckt (z. B. Werkzeuge mit --no-checkout).
Must $repo worktree add -q --no-checkout (Wt 'nc') -b t/nc main | Out-Null
Must (Wt 'nc') checkout -q -f t/nc | Out-Null
Check '--no-checkout + checkout: ohne Design/' ((Has (Wt 'nc') 'Assets\a.txt') -and (-not (Has (Wt 'nc') 'Design')))

# 7) MG_FULL_CHECKOUT=1: vollstaendig, bleibt es.
$env:MG_FULL_CHECKOUT = '1'
Must $repo worktree add -q (Wt 'voll') -b t/voll main | Out-Null
Remove-Item Env:\MG_FULL_CHECKOUT
Check 'MG_FULL_CHECKOUT: mit Design/, markiert voll' ((Has (Wt 'voll') 'Design\Thema\x.png') -and ((Marker (Wt 'voll')) -eq 'voll'))

# 8) status und apply.
$r = Run @('status')
Check 'status: Hook eingetragen, Design/ je Arbeitsordner' (($r.out -match 'Hook: eingetragen') -and ($r.out -match '(?m)^\s+nc\s+Design/ nein') -and ($r.out -match '(?m)^\s+alt\s+Design/ ja')) $r.out
Put (Join-Path (Wt 'voll') 'Design\Thema\x.png') 'geaendert'
$r = Run @('apply', '-Path', (Wt 'voll'))
Check 'apply verweigert bei Aenderungen in Design/' (($r.code -eq 5) -and (Has (Wt 'voll') 'Design\Thema\x.png')) $r.out
Must (Wt 'voll') checkout -q -- Design | Out-Null
$r = Run @('apply', '-Path', (Wt 'voll'))
Check 'apply blendet Design/ aus' (($r.code -eq 0) -and (-not (Has (Wt 'voll') 'Design')) -and (-not (G (Wt 'voll') status --porcelain))) $r.out
$r = Run @('apply', '-Path', $repo)
Check 'apply verweigert den Hauptordner' (($r.code -eq 1) -and (Has $repo 'Design'))

# 9) uninstall: Block weg, alter Hook bleibt, neue Arbeitsordner wieder vollstaendig.
$r = Run @('uninstall')
$text = [IO.File]::ReadAllText($hook)
Check 'uninstall entfernt nur den Block' (($text -notmatch 'Mining Game: Arbeitsordner') -and ($text.Contains($oldLine)) -and ($text.StartsWith('#!/bin/sh'))) $text
Must $repo worktree add -q (Wt 'nachher') -b t/nachher main | Out-Null
Check 'nach uninstall: neuer Arbeitsordner mit Design/' (Has (Wt 'nachher') 'Design\Thema\x.png')

Remove-Item $t -Recurse -Force
if ($failures) { Write-Output "$failures Fehler"; exit 1 }
Write-Output 'Alle Pruefungen bestanden.'
exit 0
