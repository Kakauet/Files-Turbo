#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$SessionDirectory, [string]$OutputDirectory, [double]$RefreshRateHz)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot ('benchmarks/results/phase3/summary-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (!$taskOutput.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside the workspace.' }
if ([IO.Directory]::Exists($taskOutput)) { throw 'Use a fresh output directory.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskRows = [Collections.Generic.List[object]]::new()
$taskInput = [Collections.Generic.List[string]]::new()
$taskProcessRows = [Collections.Generic.List[object]]::new()
$taskOperationRows = [Collections.Generic.List[object]]::new()
$taskTraceRows = [Collections.Generic.List[object]]::new()
$taskCounterRows = [Collections.Generic.List[object]]::new()
function Get-TaskDouble($Value, [Globalization.CultureInfo]$Culture) {
    if ($null -eq $Value -or [string]$Value -eq '') { return $null }
    return [double]::Parse([string]$Value, [Globalization.NumberStyles]::Float, $Culture)
}
function Add-TaskRow([string]$Variant, [string]$Scenario, [string]$Metric, $Value) {
    if ($null -ne $Value) { $taskRows.Add([pscustomobject]@{ Variant = $Variant; Scenario = $Scenario; Metric = $Metric; Ms = [double]$Value }) }
}
foreach ($taskDirectory in $SessionDirectory) {
    $taskFull = [IO.Path]::GetFullPath($taskDirectory)
    if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Inputs must remain inside the workspace.' }
    $taskInput.Add($taskFull)
    $taskSessionFile = Join-Path $taskFull 'session.json'
    $taskMeta = if ([IO.File]::Exists($taskSessionFile)) { Get-Content -Raw -LiteralPath $taskSessionFile | ConvertFrom-Json } else { $null }
    $taskCulture = if ($taskMeta.Culture) { [Globalization.CultureInfo]::GetCultureInfo($taskMeta.Culture) } else { [Globalization.CultureInfo]::CurrentCulture }
    $taskCsv = Join-Path $taskFull 'samples.csv'
    if ([IO.File]::Exists($taskCsv)) {
        foreach ($taskSample in Import-Csv -LiteralPath $taskCsv) {
            if ($taskSample.TimedOut -eq 'True') { continue }
            foreach ($taskMetric in @('AccessibleNamesMs','StatusFullCountMs','SetValueCallMs','PatternQueryMs','PathReadMaxMs','NamesQueryMaxMs','StatusReadMaxMs','CallMs','ObservationMs','BurstCallsMs','SelectionRequestedMs','SelectionConfirmedMs')) {
                $taskMetricName = if ($taskMetric -in @('CallMs','ObservationMs') -or $taskSample.Metric -eq 'UIA_SelectionDuringLoad') { $taskSample.Metric + $(if ($taskSample.Mode) { '.' + $taskSample.Mode } else { '' }) + '.' + $taskMetric } else { $taskMetric }
                if ($taskSample.PSObject.Properties[$taskMetric]) { Add-TaskRow $taskSample.Variant $taskSample.Scenario $taskMetricName (Get-TaskDouble $taskSample.$taskMetric $taskCulture) }
            }
        }
    }
    $taskProcessCsv = Join-Path $taskFull 'process.csv'
    $taskTransitionCsv = Join-Path $taskFull 'transitions.csv'
    if ([IO.File]::Exists($taskTransitionCsv)) {
        foreach ($taskTransition in Import-Csv -LiteralPath $taskTransitionCsv) {
            if ($taskTransition.TimedOut -eq 'True') { continue }
            Add-TaskRow $taskMeta.Variant $taskTransition.Scenario 'EmptyTransitionBeforeTarget.TotalObservedMs' (([DateTime]::Parse($taskTransition.CompletedUtc) - [DateTime]::Parse($taskTransition.StartedUtc)).TotalMilliseconds)
            foreach ($taskTransitionMetric in @('EmptyNamesObservedMs','SetValueCallMs','PatternQueryMs','PathReadMaxMs','NamesQueryMaxMs')) {
                if ($taskTransition.PSObject.Properties[$taskTransitionMetric]) { Add-TaskRow $taskMeta.Variant $taskTransition.Scenario ('EmptyTransitionBeforeTarget.' + $taskTransitionMetric) (Get-TaskDouble $taskTransition.$taskTransitionMetric $taskCulture) }
            }
        }
    }
    if ([IO.File]::Exists($taskProcessCsv)) {
        $taskProcessSamples = @(Import-Csv -LiteralPath $taskProcessCsv)
        if ($taskProcessSamples.Count -gt 1) {
            $taskFirst = $taskProcessSamples[0]; $taskLast = $taskProcessSamples[-1]
            $taskProcessRows.Add([pscustomobject]@{ Source = $taskFull; CPUSeconds = (Get-TaskDouble $taskLast.CPUSeconds $taskCulture) - (Get-TaskDouble $taskFirst.CPUSeconds $taskCulture); StartPrivateBytes = [long]$taskFirst.PrivateBytes; EndPrivateBytes = [long]$taskLast.PrivateBytes; PrivateBytesChange = [long]$taskLast.PrivateBytes - [long]$taskFirst.PrivateBytes; PeakPrivateBytes = ($taskProcessSamples | Measure-Object PrivateBytes -Maximum).Maximum; StartWorkingSetBytes = [long]$taskFirst.WorkingSetBytes; EndWorkingSetBytes = [long]$taskLast.WorkingSetBytes })
        }
    }
    foreach ($taskTraceFile in Get-ChildItem -LiteralPath $taskFull -Filter '*.jsonl') {
        $taskEvents = [Collections.Generic.List[object]]::new()
        foreach ($taskLine in [IO.File]::ReadLines($taskTraceFile.FullName)) {
            if ($taskEvents.Count -ge 100001) { throw 'Trace exceeds the bounded logger contract.' }
            try { $taskEvents.Add(($taskLine | ConvertFrom-Json)) } catch { Write-Warning 'Partial trailing JSONL record ignored.' }
        }
        $taskAnchor = $taskEvents | Where-Object { $_.Milestone -eq 'SessionStarted' -and $_.UtcTicks -gt 0 } | Select-Object -First 1
        $taskAnchorUtc = if ($taskAnchor) { [DateTime]::new([long]$taskAnchor.UtcTicks, [DateTimeKind]::Utc) } else { $null }
        $taskTraceRows.Add([pscustomobject]@{ Source = $taskTraceFile.FullName; Records = $taskEvents.Count; UtcAnchor = if ($taskAnchorUtc) { $taskAnchorUtc.ToString('o') } else { $null }; AnchorSessionMs = $taskAnchor.SessionMs; AnchorSampleDurationMs = $taskAnchor.DurationMs; Operations = @($taskEvents | Where-Object OperationId -gt 0 | Group-Object OperationId).Count; DroppedRecords = ($taskEvents | Measure-Object DroppedRecords -Maximum).Maximum; MonitorReportedRefreshRateHz = @($taskEvents | Where-Object Milestone -eq 'MonitorReportedRefreshRateHz' | Select-Object -ExpandProperty Value -Unique) -join ';'; FrameDurationRecords = @($taskEvents | Where-Object Milestone -eq 'ScrollXamlFrameDuration').Count; NavigationFrameDurationRecords = @($taskEvents | Where-Object Milestone -eq 'NavigationXamlFrameDuration').Count; ScrollViewChangedRecords = @($taskEvents | Where-Object Milestone -eq 'ScrollViewChanged').Count; PointerInputRecords = @($taskEvents | Where-Object Milestone -eq 'PointerInputDelivered').Count; KeyInputRecords = @($taskEvents | Where-Object Milestone -eq 'KeyInputDelivered').Count })
        foreach ($taskCounterGroup in $taskEvents | Where-Object { $_.Milestone -in @('ItemJobStarted','RealizedContainerCallbacks','ManagedHeapBytes','AllocatedBytesTotal','GCCollectionCount') } | Group-Object Milestone,Count) {
            $taskCounterFirst = $taskCounterGroup.Group[0]; $taskCounterLast = $taskCounterGroup.Group[-1]
            $taskCounterRows.Add([pscustomobject]@{ Source = $taskTraceFile.FullName; Milestone = $taskCounterFirst.Milestone; Count = $taskCounterFirst.Count; N = $taskCounterGroup.Count; FirstValue = $taskCounterFirst.Value; LastValue = $taskCounterLast.Value; MaximumValue = ($taskCounterGroup.Group | Measure-Object Value -Maximum).Maximum })
        }
        foreach ($taskOperation in $taskEvents | Where-Object OperationId -gt 0 | Group-Object OperationId) {
            $taskCount = ($taskOperation.Group | Where-Object Milestone -eq 'DataPublished' | Measure-Object Count -Maximum).Maximum
            $taskScenario = if ($taskCount) { [string]$taskCount } else { 'unknown' }
            $taskPublications = @($taskOperation.Group | Where-Object Milestone -eq 'DataPublished' | Sort-Object ElapsedMs)
            $taskFirstNonEmpty = $taskPublications | Where-Object Count -gt 0 | Select-Object -First 1
            $taskFirstAvailable = $taskOperation.Group | Where-Object Milestone -eq 'FirstItemAvailable' | Sort-Object ElapsedMs | Select-Object -First 1
            $taskFirstVisible = $taskOperation.Group | Where-Object Milestone -eq 'RenderCallbackWithVisibleContainer' | Sort-Object ElapsedMs | Select-Object -First 1
            $taskFinalListing = $taskOperation.Group | Where-Object Milestone -eq 'ListingCompleted' | Sort-Object ElapsedMs | Select-Object -Last 1
            $taskFinalAdaptive = $taskOperation.Group | Where-Object Milestone -eq 'AdaptiveLayoutCompleted' | Sort-Object ElapsedMs | Select-Object -Last 1
            $taskOperationRows.Add([pscustomobject]@{ Source = $taskTraceFile.FullName; OperationId = $taskOperation.Name; Kind = $taskOperation.Group[0].Kind; RequestedUtc = if ($taskAnchorUtc) { $taskAnchorUtc.AddMilliseconds($taskOperation.Group[0].SessionMs - $taskAnchor.SessionMs).ToString('o') } else { $null }; MaximumPublishedCount = $taskCount; FirstItemAvailableMs = $taskFirstAvailable.ElapsedMs; FirstNonEmptyDataPublishedMs = $taskFirstNonEmpty.ElapsedMs; RenderCallbackWithVisibleContainerMs = $taskFirstVisible.ElapsedMs; FinalDataPublishedMs = if ($taskPublications.Count) { $taskPublications[-1].ElapsedMs } else { $null }; ListingCompletedMs = $taskFinalListing.ElapsedMs; AdaptiveLayoutCompletedMs = $taskFinalAdaptive.ElapsedMs; AdaptiveLayoutDurationMs = $taskFinalAdaptive.DurationMs; PublicationCallbacks = $taskPublications.Count })
            if ($taskPublications.Count) {
                Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskPublications[0].Kind + '.FirstDataPublished.Elapsed') $taskPublications[0].ElapsedMs
                if ($taskFirstNonEmpty) { Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskFirstNonEmpty.Kind + '.FirstNonEmptyDataPublished.Elapsed') $taskFirstNonEmpty.ElapsedMs }
                Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskPublications[-1].Kind + '.FinalDataPublished.Elapsed') $taskPublications[-1].ElapsedMs
                foreach ($taskPublication in $taskPublications) { Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskPublication.Kind + '.DataPublished.CallbackDuration') $taskPublication.DurationMs }
            }
            foreach ($taskSuggestionMilestone in @('SuggestionContainerLoaded','SuggestionAfterRender')) {
                $taskFirstSuggestionEvent = $taskOperation.Group | Where-Object Milestone -eq $taskSuggestionMilestone | Sort-Object ElapsedMs | Select-Object -First 1
                if ($taskFirstSuggestionEvent) { Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskFirstSuggestionEvent.Kind + '.First' + $taskSuggestionMilestone + '.Elapsed') $taskFirstSuggestionEvent.ElapsedMs }
            }
            foreach ($taskEvent in $taskOperation.Group) {
                if ($taskEvent.Milestone -eq 'AdaptiveLayoutCompleted') {
                    Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskEvent.Kind + '.AdaptiveLayoutCompleted.Elapsed') $taskEvent.ElapsedMs
                    Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskEvent.Kind + '.AdaptiveLayoutCompleted.Duration') $taskEvent.DurationMs
                }
                if ($taskEvent.Milestone -in @('ListingCompleted','ProviderStarted','ProviderCompleted','SearchCompleted','FirstItemAvailable','ContainerRealized','RenderCallbackWithVisibleContainer','PointerInputDelivered','KeyInputDelivered','QueryChanged','SuggestionNameLoaded','SuggestionRenderCallbackWithVisibleName','IconsCompleted')) { Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskEvent.Kind + '.' + $taskEvent.Milestone + '.Elapsed') $taskEvent.ElapsedMs }
                elseif ($taskEvent.Milestone -in @('DispatcherQueueDelay','PublicationQueueDelay','ScrollXamlFrameDuration','NavigationXamlFrameDuration','ScrollRenderCallbackGap','ItemJobCompleted','ItemJobCancelled')) { Add-TaskRow 'PhaseIII.Trace' $taskScenario ($taskEvent.Kind + '.' + $taskEvent.Milestone + '.Duration') $taskEvent.DurationMs }
            }
        }
    }
}
function Get-TaskPercentile([double[]]$Sorted, [double]$P) {
    if (!$Sorted.Count) { return $null }
    $taskPosition = ($Sorted.Count - 1) * $P
    $taskLow = [int][Math]::Floor($taskPosition); $taskHigh = [int][Math]::Ceiling($taskPosition)
    return $Sorted[$taskLow] + ($Sorted[$taskHigh] - $Sorted[$taskLow]) * ($taskPosition - $taskLow)
}
$taskSummary = foreach ($taskGroup in $taskRows | Group-Object Variant,Scenario,Metric) {
    $taskSorted = [double[]]@($taskGroup.Group.Ms | Sort-Object)
    $taskMean = ($taskSorted | Measure-Object -Average).Average
    $taskVariance = ($taskSorted | ForEach-Object { ($_ - $taskMean) * ($_ - $taskMean) } | Measure-Object -Sum).Sum / $taskSorted.Count
    $taskFirst = $taskGroup.Group[0]
    [pscustomobject]@{ Variant = $taskFirst.Variant; Scenario = $taskFirst.Scenario; Metric = $taskFirst.Metric; N = $taskSorted.Count; MinMs = $taskSorted[0]; P50Ms = Get-TaskPercentile $taskSorted 0.50; P95Ms = Get-TaskPercentile $taskSorted 0.95; P99Ms = Get-TaskPercentile $taskSorted 0.99; MaxMs = $taskSorted[-1]; StdDevMs = [Math]::Sqrt($taskVariance); IQRMs = (Get-TaskPercentile $taskSorted 0.75) - (Get-TaskPercentile $taskSorted 0.25); ExceedsRefreshBudget = if ($RefreshRateHz -gt 0 -and $taskFirst.Metric -match 'XamlFrameDuration') { @($taskSorted | Where-Object { $_ -gt (1000 / $RefreshRateHz) }).Count } else { $null } }
}
$taskSummary | Export-Csv -LiteralPath (Join-Path $taskOutput 'summary.csv') -NoTypeInformation
$taskProcessRows | Export-Csv -LiteralPath (Join-Path $taskOutput 'process-summary.csv') -NoTypeInformation
$taskOperationRows | Export-Csv -LiteralPath (Join-Path $taskOutput 'operations.csv') -NoTypeInformation
$taskTraceRows | Export-Csv -LiteralPath (Join-Path $taskOutput 'trace-summary.csv') -NoTypeInformation
$taskCounterRows | Export-Csv -LiteralPath (Join-Path $taskOutput 'counter-summary.csv') -NoTypeInformation
[ordered]@{ InputDirectories = $taskInput; Percentiles = 'Linear interpolation over sorted observations; small N does not establish tail reliability'; RefreshRateHz = if ($RefreshRateHz -gt 0) { $RefreshRateHz } else { $null }; RefreshBudgetSource = 'Only use measured target-monitor rate; optional explicit argument'; PhysicalPresentationMeasured = $false; Metrics = @($taskSummary); Process = @($taskProcessRows); Limitations = @('UIA provider polling includes observation overhead; viewport text availability is not physical presentation', 'Full status count is collection/provider completion, not final layout completion', 'RenderedEventArgs.FrameDuration is framework XAML render duration, not present-to-present display timing', 'Callback gaps and dispatcher queue delays are separate metrics, not FPS or GC pause duration', 'InputDelivered requires actual routed pointer/key callback; queue responsiveness is not proven click response', 'Memory snapshots without forced GC are retention observations, not leak verdicts') } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'summary.json')
Write-Output $taskOutput
