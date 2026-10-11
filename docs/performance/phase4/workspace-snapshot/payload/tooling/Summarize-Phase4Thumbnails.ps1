#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$SessionDirectory,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskPath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $Path))
    if (!$taskFull.StartsWith($taskRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Input/output must remain inside workspace.' }
    return $taskFull
}
function Get-TaskStatistics($Values) {
    $taskSorted = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if (!$taskSorted.Count) { return [ordered]@{ Count = 0 } }
    $taskQuantiles = [ordered]@{}
    foreach ($taskPercent in @(50,95,99)) {
        $taskPosition = ($taskSorted.Count-1)*$taskPercent/100.0
        $taskLo = [int][Math]::Floor($taskPosition); $taskHi = [int][Math]::Ceiling($taskPosition)
        $taskQuantiles['P'+$taskPercent] = $taskSorted[$taskLo] + ($taskSorted[$taskHi]-$taskSorted[$taskLo])*($taskPosition-$taskLo)
    }
    return [ordered]@{ Count = $taskSorted.Count; Min = $taskSorted[0]; P50 = $taskQuantiles.P50; P95 = $taskQuantiles.P95; P99 = $taskQuantiles.P99; Max = $taskSorted[-1]; Mean = ($taskSorted | Measure-Object -Average).Average }
}
$taskOutput = if ($OutputDirectory) { Resolve-TaskPath $OutputDirectory } else { Join-Path $taskRoot ('benchmarks/results/phase4/thumbnail-summary-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a fresh summary directory.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskResults = [Collections.Generic.List[object]]::new()
$taskFirstSeen = [Collections.Generic.List[object]]::new()
foreach ($taskInput in $SessionDirectory) {
    $taskDirectory = Resolve-TaskPath $taskInput
    $taskMeta = Get-Content -Raw -LiteralPath (Join-Path $taskDirectory 'session.json') | ConvertFrom-Json
    $taskCulture = [Globalization.CultureInfo]::GetCultureInfo($taskMeta.Culture)
    function Get-TaskNumber($Value) { if ([string]$Value -eq '') { return $null }; return [double]::Parse([string]$Value,[Globalization.NumberStyles]::Float,$taskCulture) }
    $taskSamples = @(Import-Csv -LiteralPath (Join-Path $taskDirectory 'samples.csv'))
    $taskPolls = @(Import-Csv -LiteralPath (Join-Path $taskDirectory 'polls.csv'))
    $taskTimeouts = @($taskSamples | Where-Object TimedOut -eq 'True').Count
    $taskExpected = @($taskSamples.VisiblePictures | Sort-Object -Unique)
    $taskPollTimes = @{}
    foreach ($taskPoll in $taskPolls) { $taskPollTimes[[string]$taskPoll.Repetition + ':' + [string]$taskPoll.Poll] = $taskPoll }
    $taskDetectionFile = Join-Path $taskDirectory 'picture-detection.csv'
    if ([IO.File]::Exists($taskDetectionFile)) {
        $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($taskDetection in Import-Csv -LiteralPath $taskDetectionFile) {
            $taskSeenKey = [string]$taskDetection.Repetition + ':' + [string]$taskDetection.Fixture
            if ($taskDetection.Detected -ne 'True' -or !$taskSeen.Add($taskSeenKey)) { continue }
            $taskPoll = $taskPollTimes[[string]$taskDetection.Repetition + ':' + [string]$taskDetection.Poll]
            $taskAt = if ($taskPoll.CaptureCompletedMs) { Get-TaskNumber $taskPoll.CaptureCompletedMs } else { $null }
            $taskFirstSeen.Add([pscustomobject]@{ Session = [IO.Path]::GetRelativePath($taskRoot,$taskDirectory); Variant = $taskMeta.Variant; Repetition = $taskDetection.Repetition; Fixture = $taskDetection.Fixture; Extension = [IO.Path]::GetExtension($taskDetection.Fixture); Poll = $taskDetection.Poll; CaptureCompletedMs = $taskAt; ViewportX = $taskDetection.X; ViewportY = $taskDetection.Y; Width = $taskDetection.Width; Height = $taskDetection.Height; MatchFraction = $taskDetection.MatchFraction; Method = 'Offline first matching poll joined to existing capture timestamp; no extra UI observation' })
        }
    }
    $taskResult = [ordered]@{
        Session = [IO.Path]::GetRelativePath($taskRoot,$taskDirectory)
        Variant = $taskMeta.Variant
        Package = $taskMeta.Package
        CaptureMode = $taskMeta.CaptureMode
        DiagnosticState = $taskMeta.DiagnosticState
        SessionPurpose = $taskMeta.SessionPurpose
        ConcurrentBuild = [bool]$taskMeta.ConcurrentBuild
        EligibleForComparison = ($taskMeta.Status -eq 'Completed' -and $taskMeta.SessionPurpose -eq 'Comparison' -and !$taskMeta.ConcurrentBuild -and !$taskMeta.ComparisonExcluded -and $taskTimeouts -eq 0)
        SampleCount = $taskSamples.Count
        Timeouts = $taskTimeouts
        VisiblePictures = $taskExpected
        FirstDetectedMs = Get-TaskStatistics @($taskSamples | ForEach-Object { Get-TaskNumber $_.FirstDetectedMs })
        ViewportAllDetectedMs = Get-TaskStatistics @($taskSamples | ForEach-Object { Get-TaskNumber $_.ViewportAllDetectedMs })
        FirstObserverIntervalMs = Get-TaskStatistics @($taskSamples | Where-Object { $_.FirstDetectedMs -ne '' } | ForEach-Object { (Get-TaskNumber $_.FirstDetectedMs) - (Get-TaskNumber $_.FirstDetectionLowerBoundMs) })
        ViewportObserverIntervalMs = Get-TaskStatistics @($taskSamples | Where-Object { $_.ViewportAllDetectedMs -ne '' } | ForEach-Object { (Get-TaskNumber $_.ViewportAllDetectedMs) - (Get-TaskNumber $_.ViewportDetectionLowerBoundMs) })
        QueryMs = Get-TaskStatistics @($taskPolls | ForEach-Object { Get-TaskNumber $_.QueryMs })
        CaptureMs = Get-TaskStatistics @($taskPolls | ForEach-Object { Get-TaskNumber $_.CaptureMs })
        DetectorMs = Get-TaskStatistics @($taskPolls | ForEach-Object { Get-TaskNumber $_.DetectorMs })
        PhysicalPresentationMeasured = $false
        Interpretation = 'Synthetic thumbnail pattern observed in captured app surface; observer-induced rendering, UIA and polling costs remain included. Warm/uncontrolled caches, no FPS or exact scanout claim.'
    }
    $taskResults.Add([pscustomobject]$taskResult)
}
[ordered]@{ GeneratedUtc = [DateTime]::UtcNow.ToString('o'); Quantiles = 'Linear interpolation of ordered samples; n=10 does not establish tail guarantees'; Sessions = @($taskResults) } | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $taskOutput 'summary.json') -Encoding utf8
$taskFirstSeen | Export-Csv -LiteralPath (Join-Path $taskOutput 'first-seen.csv') -NoTypeInformation
Write-Output $taskOutput
