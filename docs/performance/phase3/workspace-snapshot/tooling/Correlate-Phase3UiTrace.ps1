#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TraceSessionDirectory,
    [Parameter(Mandatory)][string[]]$UiSessionDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskInput([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($Path)
    if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Inputs and outputs must remain inside the workspace.' }
    return $taskFull
}
$taskTraceDirectory = Resolve-TaskInput $TraceSessionDirectory
$taskTraceMeta = Get-Content -Raw -LiteralPath (Join-Path $taskTraceDirectory 'session.json') | ConvertFrom-Json
if (!$taskTraceMeta.TraceFound) { throw 'A verified diagnostic trace is required; TraceFound=false cannot be correlated.' }
$taskEvents = [Collections.Generic.List[object]]::new()
foreach ($taskLine in [IO.File]::ReadLines((Join-Path $taskTraceDirectory 'trace.jsonl'))) {
    if ($taskEvents.Count -ge 100001) { throw 'Trace exceeds the bounded logger contract.' }
    try { $taskEvents.Add(($taskLine | ConvertFrom-Json)) } catch { Write-Warning 'Partial trailing JSONL record ignored.' }
}
$taskAnchor = $taskEvents | Where-Object { $_.Milestone -eq 'SessionStarted' -and $_.UtcTicks -gt 0 } | Select-Object -First 1
if (!$taskAnchor) { throw 'A numeric SessionStarted UTC anchor is required.' }
$taskAnchorUtc = [DateTime]::new([long]$taskAnchor.UtcTicks, [DateTimeKind]::Utc)
$taskOperations = [Collections.Generic.List[object]]::new()
foreach ($taskGroup in $taskEvents | Where-Object OperationId -gt 0 | Group-Object OperationId) {
    $taskRequested = $taskGroup.Group | Where-Object Milestone -eq 'Requested' | Select-Object -First 1
    if (!$taskRequested) { continue }
    $taskPublications = @($taskGroup.Group | Where-Object Milestone -eq 'DataPublished' | Sort-Object ElapsedMs)
    $taskFirstPublished = $taskPublications | Where-Object Count -gt 0 | Select-Object -First 1
    $taskFirstNative = $taskGroup.Group | Where-Object Milestone -eq 'FirstItemAvailable' | Select-Object -First 1
    $taskFirstVisible = $taskGroup.Group | Where-Object Milestone -eq 'RenderCallbackWithVisibleContainer' | Select-Object -First 1
    $taskListing = $taskGroup.Group | Where-Object Milestone -eq 'ListingCompleted' | Select-Object -Last 1
    $taskAdaptive = $taskGroup.Group | Where-Object Milestone -eq 'AdaptiveLayoutCompleted' | Select-Object -Last 1
    $taskOperations.Add([pscustomobject]@{
        OperationId = $taskGroup.Name
        Kind = $taskRequested.Kind
        RequestedUtc = $taskAnchorUtc.AddMilliseconds($taskRequested.SessionMs - $taskAnchor.SessionMs - $taskRequested.ElapsedMs)
        RequestedMarkerUtc = $taskAnchorUtc.AddMilliseconds($taskRequested.SessionMs - $taskAnchor.SessionMs)
        MaximumPublishedCount = ($taskPublications | Measure-Object Count -Maximum).Maximum
        FirstNative = $taskFirstNative
        FirstPublished = $taskFirstPublished
        FirstVisible = $taskFirstVisible
        FinalPublished = if ($taskPublications.Count) { $taskPublications[-1] } else { $null }
        Listing = $taskListing
        Adaptive = $taskAdaptive
        JobsStartedBeforeVisible = if ($taskFirstVisible) { @($taskGroup.Group | Where-Object { $_.Milestone -eq 'ItemJobStarted' -and $_.ElapsedMs -le $taskFirstVisible.ElapsedMs }).Count } else { $null }
        Frames = @($taskGroup.Group | Where-Object Milestone -eq 'NavigationXamlFrameDuration')
    })
}
$taskOutput = if ($OutputDirectory) { Resolve-TaskInput $OutputDirectory } else { Join-Path $taskRoot ('benchmarks/results/phase3/correlation-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if ([IO.Directory]::Exists($taskOutput)) { throw 'Use a fresh output directory.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskRows = [Collections.Generic.List[object]]::new()
$taskFrames = [Collections.Generic.List[object]]::new()
foreach ($taskDirectory in $UiSessionDirectory) {
    $taskUiDirectory = Resolve-TaskInput $taskDirectory
    $taskMeta = Get-Content -Raw -LiteralPath (Join-Path $taskUiDirectory 'session.json') | ConvertFrom-Json
    if ($taskMeta.ProcessId -ne $taskTraceMeta.ProcessId) { throw 'UIA and trace process IDs differ.' }
    if ($taskMeta.Action -ne 'Navigation') { throw 'This correlation accepts ordinary navigation sessions only; selection/rapid sessions remain separate.' }
    foreach ($taskSample in Import-Csv -LiteralPath (Join-Path $taskUiDirectory 'samples.csv')) {
        if (!$taskSample.StartedUtc -or !$taskSample.CompletedUtc) { throw 'Per-target UTC boundaries are absent; preserve the older record without inventing correlation.' }
        $taskStart = [DateTime]::Parse($taskSample.StartedUtc).ToUniversalTime()
        $taskEnd = [DateTime]::Parse($taskSample.CompletedUtc).ToUniversalTime()
        $taskMatches = @($taskOperations | Where-Object { $_.Kind -eq 'Navigation' -and $_.MaximumPublishedCount -eq [int]$taskSample.Scenario -and $_.RequestedUtc -ge $taskStart -and $_.RequestedUtc -le $taskEnd })
        $taskOperation = if ($taskMatches.Count -eq 1) { $taskMatches[0] } else { $null }
        $taskRows.Add([pscustomobject]@{
            UiSource = $taskUiDirectory
            Scenario = $taskSample.Scenario
            Repetition = $taskSample.Repetition
            SetterStartedUtc = $taskStart.ToString('o')
            ProviderCompletedUtc = $taskEnd.ToString('o')
            CandidateCount = $taskMatches.Count
            Matched = ($null -ne $taskOperation)
            OperationId = $taskOperation.OperationId
            RequestedUtc = if ($taskOperation) { $taskOperation.RequestedUtc.ToString('o') } else { $null }
            RequestedMarkerUtc = if ($taskOperation) { $taskOperation.RequestedMarkerUtc.ToString('o') } else { $null }
            SetterToRequestedMs = if ($taskOperation) { ($taskOperation.RequestedUtc - $taskStart).TotalMilliseconds } else { $null }
            RequestedToFirstNativeMs = $taskOperation.FirstNative.ElapsedMs
            RequestedToFirstNonEmptyPublicationMs = $taskOperation.FirstPublished.ElapsedMs
            RequestedToVisibleRenderedMs = $taskOperation.FirstVisible.ElapsedMs
            PublicationToVisibleRenderedMs = if ($taskOperation.FirstVisible -and $taskOperation.FirstPublished) { $taskOperation.FirstVisible.ElapsedMs - $taskOperation.FirstPublished.ElapsedMs } else { $null }
            SetterToVisibleRenderedMs = if ($taskOperation.FirstVisible) { ($taskOperation.RequestedUtc - $taskStart).TotalMilliseconds + $taskOperation.FirstVisible.ElapsedMs } else { $null }
            FinalPublicationMs = $taskOperation.FinalPublished.ElapsedMs
            ListingCompletedMs = $taskOperation.Listing.ElapsedMs
            AdaptiveLayoutCompletedMs = $taskOperation.Adaptive.ElapsedMs
            AdaptiveLayoutDurationMs = $taskOperation.Adaptive.DurationMs
            PhaseZeroCallbacksAtFirstVisible = $taskOperation.FirstVisible.Count
            JobsStartedBeforeFirstVisible = $taskOperation.JobsStartedBeforeVisible
            NavigationFrames = $taskOperation.Frames.Count
            PhysicalPresentationMeasured = $false
        })
        foreach ($taskFrame in $taskOperation.Frames) { $taskFrames.Add([pscustomobject]@{ Scenario = $taskSample.Scenario; Repetition = $taskSample.Repetition; OperationId = $taskOperation.OperationId; DurationMs = $taskFrame.DurationMs }) }
    }
}
$taskRows | Export-Csv -LiteralPath (Join-Path $taskOutput 'correlation.csv') -NoTypeInformation
$taskFrames | Export-Csv -LiteralPath (Join-Path $taskOutput 'navigation-frames.csv') -NoTypeInformation
[ordered]@{ TraceSource = $taskTraceDirectory; ProcessId = $taskTraceMeta.ProcessId; UtcAnchor = $taskAnchorUtc.ToString('o'); AnchorSampleDurationMs = $taskAnchor.DurationMs; Rows = $taskRows.Count; MatchedRows = @($taskRows | Where-Object Matched).Count; Scope = 'Exactly one actual navigation Requested operation of the matching final published count inside each observed setter-to-provider-completion UTC interval; ambiguous/unmatched rows remain explicit.'; RequestedUtcInterpretation = 'Operation origin reconstructed as SessionMs minus ElapsedMs for the Requested marker; RequestedMarkerUtc retains the marker timestamp. Relative phase times share the operation origin.'; SetterToRequestedInterpretation = 'Includes toolbar path/root validation and working-directory/Git work before the enumeration operation begins; UTC clock uncertainty applies.'; PhysicalPresentationMeasured = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOutput 'session.json')
Write-Output $taskOutput
