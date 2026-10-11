#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TracePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string[]]$ObservationSessionDirectory = @(),
    [long[]]$CalibrationOperationId = @(),
    [long[]]$PreparationOperationId = @(),
    [long[]]$OperationId = @(),
    [string[]]$PassMilestone = @('ViewportPassStarted','ViewportThumbnailPassStarted','ThumbnailViewportPassStarted','ThumbnailPassStarted'),
    [string[]]$QueueMilestone = @('ViewportPassQueued','ViewportThumbnailQueued','ThumbnailViewportQueued','ViewportWorkQueued','ThumbnailQueued'),
    [string]$SourceCommit,
    [string]$Note
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskWorkspacePath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $taskRoot $Path }))
    if (!$taskFull.StartsWith($taskRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Input/output must remain inside workspace.' }
    return $taskFull
}
function Get-TaskNumber($Value) {
    if ($null -eq $Value) { return $null }
    $taskNumber = [double]$Value
    if (![double]::IsFinite($taskNumber)) { throw 'Trace contains a non-finite measurement.' }
    return $taskNumber
}
function Get-TaskStatistics($Values) {
    $taskSorted = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if (!$taskSorted.Count) { return [ordered]@{ Count = 0; Min = $null; P50 = $null; P95 = $null; P99 = $null; Max = $null; Mean = $null } }
    $taskQuantiles = @{}
    foreach ($taskPercent in @(50,95,99)) {
        $taskPosition = ($taskSorted.Count - 1) * $taskPercent / 100.0
        $taskLo = [int][Math]::Floor($taskPosition)
        $taskHi = [int][Math]::Ceiling($taskPosition)
        $taskQuantiles[$taskPercent] = $taskSorted[$taskLo] + ($taskSorted[$taskHi] - $taskSorted[$taskLo]) * ($taskPosition - $taskLo)
    }
    return [ordered]@{ Count = $taskSorted.Count; Min = $taskSorted[0]; P50 = $taskQuantiles[50]; P95 = $taskQuantiles[95]; P99 = $taskQuantiles[99]; Max = $taskSorted[-1]; Mean = ($taskSorted | Measure-Object -Average).Average }
}
function Get-TaskFirst($Events, [string[]]$Names) {
    return $Events | Where-Object { $_.Milestone -in $Names } | Sort-Object ElapsedMs,Sequence | Select-Object -First 1
}
function Get-TaskDelta($From, $To) {
    if ($null -eq $From -or $null -eq $To) { return $null }
    return $To.ElapsedMs - $From.ElapsedMs
}
function Get-TaskUtc($Event) {
    if ($null -eq $taskAnchorUtc -or $null -eq $Event -or $null -eq $Event.SessionMs) { return $null }
    return $taskAnchorUtc.AddMilliseconds($Event.SessionMs - $taskAnchorSessionMs)
}
$taskTracePath = Resolve-TaskWorkspacePath $TracePath
$taskOutputPath = Resolve-TaskWorkspacePath $OutputPath
if (![IO.File]::Exists($taskTracePath)) { throw 'Trace file does not exist.' }
if ([IO.File]::Exists($taskOutputPath)) { throw 'Use a fresh output path; existing evidence is preserved.' }
$taskTraceMetaPath = Join-Path (Split-Path $taskTracePath -Parent) 'session.json'
$taskTraceProcessId = if ([IO.File]::Exists($taskTraceMetaPath)) { [long](Get-Content -LiteralPath $taskTraceMetaPath -Raw | ConvertFrom-Json).ProcessId } else { 0L }
foreach ($taskMarker in @($PassMilestone) + @($QueueMilestone)) {
    if ($taskMarker -notmatch '^[A-Za-z][A-Za-z0-9]{0,79}$') { throw 'Stage aliases must be milestone names, not paths or free-form data.' }
}
$taskEvents = [Collections.Generic.List[object]]::new()
$taskAnchorUtc = $null
$taskAnchorSessionMs = $null
$taskLineNumber = 0
$taskDroppedRecords = 0L
foreach ($taskLine in [IO.File]::ReadLines($taskTracePath)) {
    $taskLineNumber++
    if ([string]::IsNullOrWhiteSpace($taskLine)) { continue }
    try { $taskRecord = $taskLine | ConvertFrom-Json } catch { throw "Invalid JSON at trace line $taskLineNumber." }
    if ($null -eq $taskRecord.OperationId -or $null -eq $taskRecord.ElapsedMs -or $null -eq $taskRecord.Milestone) { throw "Missing trace fields at line $taskLineNumber." }
    if ($taskRecord.Milestone -eq 'SessionStarted' -and $null -eq $taskAnchorUtc -and $taskRecord.UtcTicks -gt 0) {
        $taskAnchorUtc = [DateTime]::new([long]$taskRecord.UtcTicks,[DateTimeKind]::Utc)
        $taskAnchorSessionMs = Get-TaskNumber $taskRecord.SessionMs
    }
    $taskDroppedRecords = [Math]::Max($taskDroppedRecords,[long]$taskRecord.DroppedRecords)
    $taskEvents.Add([pscustomobject]@{
        Sequence = $taskLineNumber
        OperationId = [long]$taskRecord.OperationId
        IsNavigation = $taskRecord.Kind -eq 'Navigation'
        Milestone = [string]$taskRecord.Milestone
        ElapsedMs = Get-TaskNumber $taskRecord.ElapsedMs
        SessionMs = Get-TaskNumber $taskRecord.SessionMs
        DurationMs = Get-TaskNumber $taskRecord.DurationMs
        Count = [long]$taskRecord.Count
        Value = [long]$taskRecord.Value
    })
}
$taskObservations = [Collections.Generic.List[object]]::new()
$taskObservationSamples = [Collections.Generic.List[object]]::new()
$taskSessionIndex = 0
foreach ($taskSessionInput in $ObservationSessionDirectory) {
    $taskSessionPath = Resolve-TaskWorkspacePath $taskSessionInput
    $taskSessionMeta = Get-Content -LiteralPath (Join-Path $taskSessionPath 'session.json') -Raw | ConvertFrom-Json
    $taskCulture = [Globalization.CultureInfo]::GetCultureInfo($(if ($taskSessionMeta.Culture) { $taskSessionMeta.Culture } else { 'en-US' }))
    $taskCsvPath = Join-Path $taskSessionPath 'samples.csv'
    $taskCsvRows = if ([IO.File]::Exists($taskCsvPath)) { @(Import-Csv -LiteralPath $taskCsvPath) } else { @() }
    $taskObservations.Add([pscustomobject]@{
        Index = $taskSessionIndex
        Evidence = [IO.Path]::GetRelativePath($taskRoot,$taskSessionPath)
        Status = [string]$taskSessionMeta.Status
        DiagnosticState = [string]$taskSessionMeta.DiagnosticState
        ProcessId = [long]$taskSessionMeta.ProcessId
        Samples = $taskCsvRows.Count
        Timeouts = @($taskCsvRows | Where-Object TimedOut -eq 'True').Count
        TargetOpenedDuringCalibration = [bool]$taskSessionMeta.TargetOpenedDuringCalibration
        ProcessMatchesTrace = if ($taskTraceProcessId -gt 0 -and $taskSessionMeta.ProcessId -gt 0) { [long]$taskSessionMeta.ProcessId -eq $taskTraceProcessId } else { $null }
    })
    foreach ($taskRow in $taskCsvRows) {
        $taskSample = [ordered]@{
            SessionIndex = $taskSessionIndex
            Repetition = [int]$taskRow.Repetition
            StartedUtc = [DateTimeOffset]::Parse($taskRow.StartedUtc,[Globalization.CultureInfo]::InvariantCulture).UtcDateTime
            CompletedUtc = [DateTimeOffset]::Parse($taskRow.CompletedUtc,[Globalization.CultureInfo]::InvariantCulture).UtcDateTime
            TimedOut = $taskRow.TimedOut -eq 'True'
            CompatibleProcess = $taskTraceProcessId -eq 0 -or $taskSessionMeta.ProcessId -le 0 -or [long]$taskSessionMeta.ProcessId -eq $taskTraceProcessId
        }
        foreach ($taskField in @('FirstDetectedMs','FirstDetectionLowerBoundMs','ViewportAllDetectedMs','SetValueCallMs')) {
            $taskSample[$taskField] = if ([string]::IsNullOrWhiteSpace([string]$taskRow.$taskField)) { $null } else { [double]::Parse([string]$taskRow.$taskField,[Globalization.NumberStyles]::Float,$taskCulture) }
        }
        $taskObservationSamples.Add([pscustomobject]$taskSample)
    }
    $taskSessionIndex++
}
$taskStageNames = @('Requested','FirstItemAvailable','DataPublished','ContainerRealized','ListingCompleted','AdaptiveLayoutCompleted','ViewportWorkRequested','ViewportPassQueued','ViewportPassStarted','ViewportPassCompleted','ItemJobStarted','ThumbnailCacheStarted','ThumbnailCacheCompleted','ThumbnailNativeStarted','ThumbnailNativeCompleted','ThumbnailDispatcherWait','ThumbnailDecodeCompleted','ThumbnailApplied','ItemJobCompleted','RenderCallbackWithVisibleContainer','ScrollGeometryIgnored','ScrollDeferred','ScrollFinalWithoutMovement','ScrollSettleScheduled','ScrollSettled')
$taskDurationNames = @('PublicationQueueDelay','AdaptiveLayoutCompleted','ItemJobCompleted','ThumbnailCacheQueueWait','ThumbnailCacheCompleted','ThumbnailNativeQueueWait','ThumbnailNativeCompleted','ThumbnailDispatcherWait','ThumbnailDecodeCompleted','OverlayQueueWait','OverlayCompleted','DispatcherQueueDelay','ViewportPassStarted','ViewportPassCompleted')
$taskKnownNames = @($taskStageNames) + @($PassMilestone) + @($QueueMilestone) + @($taskDurationNames) + @('ScrollViewChanged','ScrollViewChanging','NavigationXamlFrameDuration','ScrollXamlFrameDuration','PointerInputDelivered','KeyInputDelivered','ScrollGeometryIgnored','ScrollDeferred','ScrollFinalWithoutMovement','ScrollSettleScheduled','ScrollSettled') | Sort-Object -Unique
$taskOperations = [Collections.Generic.List[object]]::new()
$taskExcluded = [Collections.Generic.List[object]]::new()
foreach ($taskGroup in ($taskEvents | Where-Object { $_.IsNavigation -and $_.OperationId -gt 0 } | Group-Object OperationId | Sort-Object { [long]$_.Name })) {
    $taskId = [long]$taskGroup.Name
    if ($OperationId.Count -gt 0 -and $taskId -notin $OperationId) { continue }
    $taskPublications = @($taskGroup.Group | Where-Object Milestone -eq 'DataPublished' | Sort-Object ElapsedMs,Sequence)
    $taskMaximumPublished = if ($taskPublications.Count) { [long]($taskPublications | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum } else { 0L }
    if ($taskMaximumPublished -le 0) {
        $taskExcluded.Add([pscustomobject]@{ OperationId = $taskId; MaximumPublishedCount = $taskMaximumPublished; Reason = 'No nonempty publication; excluded from thumbnail latency statistics.' })
        continue
    }
    $taskOperationEvents = @($taskGroup.Group | Sort-Object ElapsedMs,Sequence)
    $taskRequested = Get-TaskFirst $taskOperationEvents @('Requested')
    if ($null -eq $taskRequested) { throw "Operation $taskId has no Requested milestone." }
    $taskFirst = @{}
    $taskFirstStages = [ordered]@{}
    $taskStageCounts = [ordered]@{}
    foreach ($taskName in $taskKnownNames) {
        $taskStageEvents = @($taskOperationEvents | Where-Object Milestone -eq $taskName)
        $taskStageCounts[$taskName] = $taskStageEvents.Count
        $taskFirst[$taskName] = $taskStageEvents | Select-Object -First 1
    }
    foreach ($taskName in @($taskStageNames) + @($PassMilestone) + @($QueueMilestone) | Select-Object -Unique) {
        $taskEvent = $taskFirst[$taskName]
        $taskFirstStages[$taskName] = if ($null -eq $taskEvent) { $null } else {
            [ordered]@{ OperationElapsedMs = $taskEvent.ElapsedMs; SinceRequestedMarkerMs = $taskEvent.ElapsedMs - $taskRequested.ElapsedMs; DurationMs = $taskEvent.DurationMs; Count = $taskEvent.Count; Value = $taskEvent.Value }
        }
    }
    $taskFirstPass = Get-TaskFirst $taskOperationEvents $PassMilestone
    $taskFirstQueue = Get-TaskFirst $taskOperationEvents $QueueMilestone
    $taskLastApplied = $taskOperationEvents | Where-Object Milestone -eq 'ThumbnailApplied' | Select-Object -Last 1
    $taskLastJobCompleted = $taskOperationEvents | Where-Object Milestone -eq 'ItemJobCompleted' | Select-Object -Last 1
    $taskFirstPublication = $taskPublications | Where-Object Count -gt 0 | Select-Object -First 1
    $taskFinalPublication = $taskPublications | Select-Object -Last 1
    $taskDeltas = [ordered]@{
        RequestedToFirstNonemptyPublication = Get-TaskDelta $taskRequested $taskFirstPublication
        RequestedToContainerRealized = Get-TaskDelta $taskRequested $taskFirst.ContainerRealized
        ContainerRealizedToFirstWorkRequested = Get-TaskDelta $taskFirst.ContainerRealized $taskFirst.ViewportWorkRequested
        FirstWorkRequestedToFirstPassQueued = Get-TaskDelta $taskFirst.ViewportWorkRequested $taskFirst.ViewportPassQueued
        FirstPassQueuedToFirstPassStarted = Get-TaskDelta $taskFirst.ViewportPassQueued $taskFirst.ViewportPassStarted
        ContainerRealizedToFirstPass = Get-TaskDelta $taskFirst.ContainerRealized $taskFirstPass
        FirstPassToFirstJob = Get-TaskDelta $taskFirstPass $taskFirst.ItemJobStarted
        FirstQueueToFirstJob = Get-TaskDelta $taskFirstQueue $taskFirst.ItemJobStarted
        ContainerRealizedToFirstJob = Get-TaskDelta $taskFirst.ContainerRealized $taskFirst.ItemJobStarted
        FinalPublicationToFirstJob = Get-TaskDelta $taskFinalPublication $taskFirst.ItemJobStarted
        RequestedToFirstJob = Get-TaskDelta $taskRequested $taskFirst.ItemJobStarted
        FirstJobToFirstCacheStarted = Get-TaskDelta $taskFirst.ItemJobStarted $taskFirst.ThumbnailCacheStarted
        FirstCacheCompletionToFirstDispatcherMarker = Get-TaskDelta $taskFirst.ThumbnailCacheCompleted $taskFirst.ThumbnailDispatcherWait
        FirstDispatcherMarkerToFirstDecodeCompleted = Get-TaskDelta $taskFirst.ThumbnailDispatcherWait $taskFirst.ThumbnailDecodeCompleted
        FirstDecodeCompletedToFirstApplied = Get-TaskDelta $taskFirst.ThumbnailDecodeCompleted $taskFirst.ThumbnailApplied
        FirstJobToFirstApplied = Get-TaskDelta $taskFirst.ItemJobStarted $taskFirst.ThumbnailApplied
        RequestedToFirstApplied = Get-TaskDelta $taskRequested $taskFirst.ThumbnailApplied
        RequestedToLastApplied = Get-TaskDelta $taskRequested $taskLastApplied
        FirstAppliedToLastApplied = Get-TaskDelta $taskFirst.ThumbnailApplied $taskLastApplied
        ListingCompletedToAdaptiveCompleted = Get-TaskDelta $taskFirst.ListingCompleted $taskFirst.AdaptiveLayoutCompleted
        AdaptiveCompletedToFirstJob = Get-TaskDelta $taskFirst.AdaptiveLayoutCompleted $taskFirst.ItemJobStarted
        FirstScrollDeferredToFirstSettled = Get-TaskDelta $taskFirst.ScrollDeferred $taskFirst.ScrollSettled
        FirstScrollSettleScheduledToFirstSettled = Get-TaskDelta $taskFirst.ScrollSettleScheduled $taskFirst.ScrollSettled
    }
    $taskPrejobFraction = if ($null -ne $taskDeltas.RequestedToFirstApplied -and $taskDeltas.RequestedToFirstApplied -gt 0 -and $null -ne $taskDeltas.RequestedToFirstJob) { $taskDeltas.RequestedToFirstJob / $taskDeltas.RequestedToFirstApplied } else { $null }
    $taskDurations = [ordered]@{}
    foreach ($taskName in $taskDurationNames) { $taskDurations[$taskName] = Get-TaskStatistics @($taskOperationEvents | Where-Object Milestone -eq $taskName | Select-Object -ExpandProperty DurationMs) }
    $taskRequestedUtc = Get-TaskUtc $taskRequested
    $taskAppliedUtc = Get-TaskUtc $taskFirst.ThumbnailApplied
    $taskBindings = [Collections.Generic.List[object]]::new()
    if ($null -ne $taskRequestedUtc) {
        foreach ($taskSample in $taskObservationSamples | Where-Object { $_.CompatibleProcess -and $taskRequestedUtc -ge $_.StartedUtc -and $taskRequestedUtc -le $_.CompletedUtc }) {
            $taskAppliedAfterSetter = if ($null -ne $taskAppliedUtc) { ($taskAppliedUtc - $taskSample.StartedUtc).TotalMilliseconds } else { $null }
            $taskBindings.Add([pscustomobject]@{
                SessionIndex = $taskSample.SessionIndex
                Repetition = $taskSample.Repetition
                TimedOut = $taskSample.TimedOut
                RequestedAfterSetterStartMs = ($taskRequestedUtc - $taskSample.StartedUtc).TotalMilliseconds
                FirstAppliedAfterSetterStartMs = $taskAppliedAfterSetter
                FirstDetectedFromSetterStartMs = $taskSample.FirstDetectedMs
                FirstAppliedToFirstDetectorObservationMs = if ($null -ne $taskSample.FirstDetectedMs -and $null -ne $taskAppliedAfterSetter) { $taskSample.FirstDetectedMs - $taskAppliedAfterSetter } else { $null }
                FirstDetectorObservationIntervalMs = if ($null -ne $taskSample.FirstDetectedMs -and $null -ne $taskSample.FirstDetectionLowerBoundMs) { $taskSample.FirstDetectedMs - $taskSample.FirstDetectionLowerBoundMs } else { $null }
                ViewportAllDetectedFromSetterStartMs = $taskSample.ViewportAllDetectedMs
                SetValueCallMs = $taskSample.SetValueCallMs
                FirstAppliedInsideSampleWindow = $null -ne $taskAppliedUtc -and $taskAppliedUtc -ge $taskSample.StartedUtc -and $taskAppliedUtc -le $taskSample.CompletedUtc
            })
        }
    }
    $taskOperations.Add([pscustomobject]@{
        OperationId = $taskId
        MaximumPublishedCount = $taskMaximumPublished
        DeclaredCalibration = $taskId -in $CalibrationOperationId
        DeclaredPreparation = $taskId -in $PreparationOperationId
        MatchedTimedObservationCount = $taskBindings.Count
        RequestedUtcFromTraceAnchor = if ($null -ne $taskRequestedUtc) { $taskRequestedUtc.ToString('o') } else { $null }
        FirstStages = $taskFirstStages
        LastCompletionElapsedMs = [ordered]@{ ThumbnailApplied = if ($null -ne $taskLastApplied) { $taskLastApplied.ElapsedMs } else { $null }; ItemJobCompleted = if ($null -ne $taskLastJobCompleted) { $taskLastJobCompleted.ElapsedMs } else { $null } }
        DeltasMs = $taskDeltas
        PrejobFractionOfRequestedToFirstApplied = $taskPrejobFraction
        StageCounts = $taskStageCounts
        DurationDistributionsMs = $taskDurations
        ObservationBindings = @($taskBindings)
        ProbeCoverage = [ordered]@{
            ViewChangedEvents = $taskStageCounts.ScrollViewChanged + $taskStageCounts.ScrollViewChanging
            NavigationXamlFrameEvents = $taskStageCounts.NavigationXamlFrameDuration
            ScrollXamlFrameEvents = $taskStageCounts.ScrollXamlFrameDuration
            VisibleRenderMilestonePresent = $taskStageCounts.RenderCallbackWithVisibleContainer -gt 0
            MissingProbeEventsDoNotProveNoViewChangesOrRendering = $true
        }
    })
}
$taskAggregateDeltas = [ordered]@{}
if ($taskOperations.Count) {
    foreach ($taskKey in $taskOperations[0].DeltasMs.Keys) { $taskAggregateDeltas[$taskKey] = Get-TaskStatistics @($taskOperations | ForEach-Object { $_.DeltasMs[$taskKey] }) }
}
$taskPayload = [ordered]@{
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    SourceCommit = $SourceCommit
    Trace = [ordered]@{ Evidence = [IO.Path]::GetRelativePath($taskRoot,$taskTracePath); Sha256 = (Get-FileHash -LiteralPath $taskTracePath -Algorithm SHA256).Hash; ProcessIdFromCaptureMetadata = $taskTraceProcessId; Records = $taskEvents.Count; MaximumReportedDroppedRecords = $taskDroppedRecords; HasUtcAnchor = $null -ne $taskAnchorUtc }
    Counts = [ordered]@{ IncludedNonemptyNavigationOperations = $taskOperations.Count; ExcludedEmptyNavigationOperations = $taskExcluded.Count; DeclaredCalibrationOperations = @($taskOperations | Where-Object DeclaredCalibration).Count; DeclaredPreparationOperations = @($taskOperations | Where-Object DeclaredPreparation).Count; OperationsWithTimedObservation = @($taskOperations | Where-Object MatchedTimedObservationCount -gt 0).Count; ObservationSamplesProvided = $taskObservationSamples.Count }
    Clock = [ordered]@{ Trace = 'OperationElapsedMs uses the operation Stopwatch; deltas subtract its Requested marker. It begins after earlier navigation handling, not at the UIA setter.'; Observation = 'Optional UIA samples use their own Stopwatch from before SetValue to completed captured buffer. UTC anchor correlations are approximate, not shared Stopwatch or physical pixel clocks.'; Quantiles = 'Linear interpolation over ordered samples; small n does not establish tail guarantees.'; CalibrationClassification = 'Caller-specified operation IDs; the trace has no calibration milestone. A matched timed CSV observation is recorded separately.' }
    StageAliases = [ordered]@{ Pass = $PassMilestone; Queue = $QueueMilestone }
    Operations = @($taskOperations)
    ExcludedOperations = @($taskExcluded)
    ObservationSessions = @($taskObservations)
    AggregateDeltasMs = $taskAggregateDeltas
    Note = $Note
    Limitations = @(
        'The aggregate includes calibration/preparation when declared; inspect per-operation roles before comparing timed observations.',
        'Only numeric trace fields and known milestone names are retained per operation; no item names or item paths are copied.',
        'Parallel jobs share OperationId without RowId; earliest cache/decode/applied markers can belong to different rows. Their deltas are operation milestones, not a per-row critical path.',
        'ThumbnailDispatcherWait starts at the apply method; it does not include earlier async continuation scheduling after native cache completion.',
        'Missing pass/queue milestones remain null. Missing ViewChanged/render probe records do not demonstrate absence of those events; probe attachment or disposal can remove coverage.',
        'UTC sample joins require matching process IDs when both capture metadata and observation metadata provide them; absent IDs leave identity unverified.',
        'Adaptive completion correlation does not show its CPU cost or causality; DurationMs is reported separately.',
        'Diagnostics are opt-in. PrintWindow/UIA/polling can perturb rendering; warm cache and observer time remain included.',
        'No physical presentation, cold-cache guarantee, global performance verdict, or inference of unique thumbnail identity is made.'
    )
}
$null = [IO.Directory]::CreateDirectory((Split-Path $taskOutputPath -Parent))
$taskJson = $taskPayload | ConvertTo-Json -Depth 12
$taskJson = $taskJson -replace '\r?\n',"`r`n"
[IO.File]::WriteAllText($taskOutputPath,$taskJson + "`r`n",[Text.UTF8Encoding]::new($false))
Write-Output ([pscustomobject]@{ Output = [IO.Path]::GetRelativePath($taskRoot,$taskOutputPath); IncludedOperations = $taskOperations.Count; ExcludedEmptyOperations = $taskExcluded.Count; MatchedTimedOperations = @($taskOperations | Where-Object MatchedTimedObservationCount -gt 0).Count })
