# Build an independent copy of the real static progression implementation for
# Pipeline run_script. This preserves a concurrently active Editor test session.
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProgression = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'Assets/GameObjects/Progression/MetaProgression.cs')
$taskPeriods = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'Assets/GameObjects/Progression/MetaChallengePeriods.cs')
$taskChecks = Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'Tests/DailyChallengeSelectionChecks.cs')
# Use the production period data types so MetaProfile keeps its normal serialization.
$taskPeriods = $taskPeriods.Substring($taskPeriods.IndexOf('public static partial class MetaProgression'))
$taskImplementation = ($taskProgression + "`n" + $taskPeriods).Replace(
    'public static partial class MetaProgression', 'public static partial class DailySelectionIsolatedProgression')
$taskChecks = $taskChecks.Replace('MetaProgression.', 'DailySelectionIsolatedProgression.').Replace(
    'typeof(MetaProgression)', 'typeof(DailySelectionIsolatedProgression)')
# The independent static instance can safely run during another test's Play Mode.
$taskChecks = $taskChecks.Replace('Application.isPlaying || DailySelectionIsolatedProgression.CurrentRun != null',
    'DailySelectionIsolatedProgression.CurrentRun != null')
$taskSource = $taskImplementation + "`n" + $taskChecks
$taskUsings = [regex]::Matches($taskSource, '(?m)^using [^\r\n]+;') | ForEach-Object { $_.Value } | Select-Object -Unique
$taskBody = [regex]::Replace($taskSource, '(?m)^using [^\r\n]+;\r?\n', '')
$taskOutput = Join-Path $taskRoot 'Temp/DailyChallengeIsolatedChecks.cs'
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'Temp') -Force | Out-Null
Set-Content -LiteralPath $taskOutput -Value (($taskUsings -join "`n") + "`n" + $taskBody) -Encoding utf8
Write-Output $taskOutput
