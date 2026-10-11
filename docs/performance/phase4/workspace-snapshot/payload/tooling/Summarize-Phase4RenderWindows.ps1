#Requires -Version 7.2
[CmdletBinding()]
param([string]$OutputReport = 'reports/phase4-render-common-increments.json')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskOutput = [IO.Path]::GetFullPath($OutputReport,$taskRoot)
if (!$taskOutput.StartsWith($taskRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskOutput)) { throw 'Use a fresh workspace output.' }
function Get-TaskStats($Values) {
    $taskSorted = @($Values | Sort-Object)
    if (!$taskSorted.Count) { return @{Count=0} }
    $taskResult = [ordered]@{Count=$taskSorted.Count;Min=$taskSorted[0];Max=$taskSorted[-1]}
    foreach ($taskP in @(50,95,99)) { $taskPosition=($taskSorted.Count-1)*$taskP/100; $taskLo=[int][Math]::Floor($taskPosition); $taskHi=[int][Math]::Ceiling($taskPosition); $taskResult['P'+$taskP]=$taskSorted[$taskLo]+($taskSorted[$taskHi]-$taskSorted[$taskLo])*($taskPosition-$taskLo) }
    $taskResult.AboveReferenceBudget = @($taskSorted | Where-Object {$_ -gt (1000/144)}).Count
    return $taskResult
}
$taskResults = foreach ($taskCase in @(
    @{Variant='PhaseIII';Capture='baseline-III-profile-capture';Scroll='baseline-III-profile-scroll-b';Pid=37640},
    @{Variant='PhaseIV';Capture='profile-IV-final03-render-capture';Scroll='profile-IV-final03-scroll-increments';Pid=3800}
)) {
    $taskBase = Join-Path $taskRoot 'benchmarks/results/phase4'
    $taskMeta = Get-Content -LiteralPath (Join-Path $taskBase ($taskCase.Scroll+'/session.json')) -Raw | ConvertFrom-Json
    $taskCaptureMeta = Get-Content -LiteralPath (Join-Path $taskBase ($taskCase.Capture+'/session.json')) -Raw | ConvertFrom-Json
    if ($taskMeta.Status -ne 'Completed' -or $taskMeta.DiagnosticState -eq 'Off' -or $taskMeta.ProcessId -ne $taskCase.Pid -or !$taskCaptureMeta.DiagnosticTraceRequired -or $taskCaptureMeta.ProcessId -ne $taskCase.Pid -or $taskCaptureMeta.ProcessStartedUtc -ne $taskMeta.ProcessStartedUtc) { throw 'Wrong diagnostic session.' }
    $taskRows = @(Import-Csv -LiteralPath (Join-Path $taskBase ($taskCase.Scroll+'/samples.csv')) | Where-Object {$_.Mode -in @('SmallIncrement','LargeIncrement')})
    if ($taskRows.Count -ne 8 -or @($taskRows | Where-Object TimedOut -eq 'True').Count) { throw 'Eight complete common scroll increments are required.' }
    $taskWindows = @($taskRows | ForEach-Object {[pscustomobject]@{Start=[DateTime]::Parse($_.StartedUtc).ToUniversalTime();End=[DateTime]::Parse($_.CompletedUtc).ToUniversalTime().AddMilliseconds(250)}})
    $taskRecords = @(Get-Content -LiteralPath (Join-Path $taskBase ($taskCase.Capture+'/trace.jsonl')) | ForEach-Object {$_ | ConvertFrom-Json})
    $taskAnchor = $taskRecords[0]
    if ($taskAnchor.Milestone -ne 'SessionStarted' -or $taskAnchor.UtcTicks -le 0) { throw 'Trace UTC anchor missing.' }
    $taskAnchorUtc = [DateTime]::new([long]$taskAnchor.UtcTicks,[DateTimeKind]::Utc)
    $taskSelected = @($taskRecords | Where-Object {
        $taskUtc=$taskAnchorUtc.AddMilliseconds($_.SessionMs-$taskAnchor.SessionMs)
        @($taskWindows | Where-Object {$taskUtc -ge $_.Start -and $taskUtc -le $_.End}).Count -gt 0
    })
    $taskFrames = @($taskSelected | Where-Object Milestone -eq 'ScrollXamlFrameDuration' | ForEach-Object DurationMs)
    $taskGaps = @($taskSelected | Where-Object Milestone -eq 'ScrollRenderCallbackGap' | ForEach-Object DurationMs)
    $taskDispatcher = @($taskSelected | Where-Object Milestone -eq 'DispatcherQueueDelay' | ForEach-Object DurationMs)
    [pscustomobject]@{Variant=$taskCase.Variant;ProcessId=$taskCase.Pid;SourceSession=$taskCase.Scroll;Trace=$taskCase.Capture;CallCount=$taskRows.Count;Windows=$taskWindows;XamlRenderDurationMs=(Get-TaskStats $taskFrames);RenderCallbackGapMs=(Get-TaskStats $taskGaps);DispatcherQueueDelayMs=(Get-TaskStats $taskDispatcher);AnchorUncertaintyMs=$taskAnchor.DurationMs;TraceSha256=(Get-FileHash -LiteralPath (Join-Path $taskBase ($taskCase.Capture+'/trace.jsonl'))).Hash}
}
$taskReport = [ordered]@{GeneratedUtc=[DateTime]::UtcNow.ToString('o');Method='Union of eight equivalent UIA Small/Large increment request-to-readiness windows, each extended250ms for trailing XAML callbacks. Overlaps deduplicated; navigation and jump preparations excluded.';ReferenceHz=144;ReferenceBudgetMs=1000/144;PhysicalPresentationMeasured=$false;Limitations='One instrumented session per variant; callbacks are framework observations, not physical FPS. UIA, profiler and diagnostics overhead are included. Increments do not cover wheel input, scrollbar drag, jump ends or media scrolling.';Sessions=$taskResults}
[IO.File]::WriteAllText($taskOutput,(($taskReport | ConvertTo-Json -Depth 8) -replace '\r?\n',"`r`n")+"`r`n")
$taskResults | Select-Object Variant,XamlRenderDurationMs,RenderCallbackGapMs,DispatcherQueueDelayMs | ConvertTo-Json -Depth 5
