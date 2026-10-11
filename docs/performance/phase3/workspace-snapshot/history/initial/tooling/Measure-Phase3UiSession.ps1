#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Inspect','Navigation','Scroll','Selection','SelectionDuringLoad','RapidNavigation')][string]$Action = 'Inspect',
    [ValidateSet('Original','PhaseII','PhaseIII')][string]$Variant = 'PhaseII',
    [ValidateSet('1000','10000','50000','100000')][string[]]$Scenario = @('1000','10000','50000','100000'),
    [ValidateRange(1,20)][int]$Repetitions = 5,
    [ValidateRange(5,120)][int]$TimeoutSeconds = 30,
    [int]$ProcessId,
    [switch]$RestoreWindow,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskPackage = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPackage) { throw 'The experimental package must already be registered; this script does not install or launch it.' }
if (!$taskPackage.InstallLocation.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Experimental package location must be in this workspace.' }
$taskProcesses = @(Get-Process -Name Files -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($taskPackage.InstallLocation.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (!$ProcessId -or $_.Id -eq $ProcessId)
})
if ($taskProcesses.Count -ne 1) { throw 'Specify -ProcessId for exactly one running experimental Files process.' }
$taskProcess = $taskProcesses[0]
if (!$taskProcess.MainWindowHandle) { throw 'The experimental process has no main window handle.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskProcess.MainWindowHandle)
if ($taskWindow.Current.ProcessId -ne $taskProcess.Id) { throw 'UI Automation window ownership mismatch.' }
$taskWindowPattern = [Windows.Automation.WindowPattern]$taskWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
if ($RestoreWindow -and $taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) {
    $taskWindowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 500
}
if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized -or $taskWindow.Current.BoundingRectangle.IsEmpty) { throw 'Restore the experimental window before measurement; minimized providers can expose stale offscreen bounds.' }
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot ('benchmarks/results/phase3/ui-' + $Variant + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (!$taskOutput.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside the workspace.' }
if ([IO.Directory]::Exists($taskOutput)) { throw 'Use a fresh output directory to preserve historical samples.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskSamples = [Collections.Generic.List[object]]::new()
$taskTransitions = [Collections.Generic.List[object]]::new()
$taskSessionStartedUtc = [DateTime]::UtcNow
$script:taskPathGetter = $null
$script:taskPathSetter = $null
$script:taskCountGetter = $null
$script:taskPathGetterPattern = $null
$script:taskPathSetterPattern = $null
function Get-TaskElement([string]$Id) {
    $taskCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $taskWindow.FindFirst([Windows.Automation.TreeScope]::Descendants, $taskCondition)
}
function Get-TaskPath {
    $taskElement = if ($script:taskPathGetter) { $script:taskPathGetter } else { Get-TaskElement 'CurrentPathGet' }
    if (!$taskElement) { throw 'CurrentPathGet is unavailable; cannot verify synthetic navigation scope.' }
    $script:taskPathGetter = $taskElement
    if (!$script:taskPathGetterPattern) { $script:taskPathGetterPattern = [Windows.Automation.ValuePattern]$taskElement.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern) }
    return $script:taskPathGetterPattern.Current.Value
}
function Get-TaskVisibleNames {
    $taskCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'ItemName'),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsOffscreenProperty, $false))
    $taskFirstItem = $taskWindow.FindFirst([Windows.Automation.TreeScope]::Descendants, $taskCondition)
    $taskBounds = $taskWindow.Current.BoundingRectangle
    $taskVisible = [Collections.Generic.List[object]]::new()
    foreach ($taskItem in @($taskFirstItem | Where-Object { $_ })) {
        $taskCurrent = $taskItem.Current
        $taskRect = $taskCurrent.BoundingRectangle
        if (!$taskCurrent.IsOffscreen -and $taskRect.Width -gt 0 -and $taskRect.Height -gt 0 -and $taskRect.IntersectsWith($taskBounds)) { $taskVisible.Add($taskItem) }
        if ($taskVisible.Count -ge 128) { break }
    }
    return ,$taskVisible
}
function Set-TaskPath([string]$Path, [bool]$RequireEmpty = $false, [int]$ExpectedCount = -1) {
    if (!$Path.StartsWith((Join-Path $taskRoot 'benchmarks') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Navigation is limited to benchmark fixtures.' }
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    $taskSetter = if ($script:taskPathSetter) { $script:taskPathSetter } else { Get-TaskElement 'CurrentPathSet' }
    if (!$taskSetter) { throw 'CurrentPathSet is unavailable.' }
    $script:taskPathSetter = $taskSetter
    $taskPatternStarted = [Diagnostics.Stopwatch]::GetTimestamp()
    if (!$script:taskPathSetterPattern) { $script:taskPathSetterPattern = [Windows.Automation.ValuePattern]$taskSetter.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern) }
    $taskPatternQueryMs = [Diagnostics.Stopwatch]::GetElapsedTime($taskPatternStarted).TotalMilliseconds
    $taskValue = $script:taskPathSetterPattern
    $taskSetStarted = [Diagnostics.Stopwatch]::GetTimestamp()
    $taskValue.SetValue($Path)
    $taskSetMs = [Diagnostics.Stopwatch]::GetElapsedTime($taskSetStarted).TotalMilliseconds
    $taskFirstNames = $null
    $taskFullCountMs = $null
    $taskPathReadMaxMs = 0.0
    $taskNamesQueryMaxMs = 0.0
    $taskStatusReadMaxMs = 0.0
    $taskPathMs = $null
    $taskPolls = 0
    while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $taskPolls++
        if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { throw 'Experimental window was minimized during measurement; session is incomplete.' }
        $taskObserveStarted = [Diagnostics.Stopwatch]::GetTimestamp()
        $taskObservedPath = Get-TaskPath
        $taskPathReadMaxMs = [Math]::Max($taskPathReadMaxMs, [Diagnostics.Stopwatch]::GetElapsedTime($taskObserveStarted).TotalMilliseconds)
        if ($taskObservedPath.Equals($Path, [StringComparison]::OrdinalIgnoreCase)) {
            if ($null -eq $taskPathMs) { $taskPathMs = $taskWatch.Elapsed.TotalMilliseconds }
            if ($null -eq $taskFirstNames) {
                $taskObserveStarted = [Diagnostics.Stopwatch]::GetTimestamp()
                $taskNames = Get-TaskVisibleNames
                $taskNamesQueryMaxMs = [Math]::Max($taskNamesQueryMaxMs, [Diagnostics.Stopwatch]::GetElapsedTime($taskObserveStarted).TotalMilliseconds)
                if (($RequireEmpty -and $taskNames.Count -eq 0) -or (!$RequireEmpty -and $taskNames.Count -gt 0)) {
                    $taskFirstNames = $taskWatch.Elapsed.TotalMilliseconds
                    if ($ExpectedCount -lt 0) { break }
                }
            }
            if ($null -ne $taskFirstNames -and $ExpectedCount -ge 0) {
                $taskObserveStarted = [Diagnostics.Stopwatch]::GetTimestamp()
                $taskCountElement = $script:taskCountGetter
                $taskCountDigits = if ($taskCountElement) { [regex]::Replace($taskCountElement.Current.Name, '\D', '') } else { '' }
                $taskStatusReadMaxMs = [Math]::Max($taskStatusReadMaxMs, [Diagnostics.Stopwatch]::GetElapsedTime($taskObserveStarted).TotalMilliseconds)
                if ($taskCountDigits -and [long]$taskCountDigits -eq $ExpectedCount) {
                    $taskFullCountMs = $taskWatch.Elapsed.TotalMilliseconds
                    break
                }
            }
        }
        Start-Sleep -Milliseconds 10
    }
    [pscustomobject]@{ AccessibleNamesMs = $taskFirstNames; StatusFullCountMs = $taskFullCountMs; PathUpdatedMs = $taskPathMs; SetValueCallMs = $taskSetMs; PatternQueryMs = $taskPatternQueryMs; PathReadMaxMs = $taskPathReadMaxMs; NamesQueryMaxMs = $taskNamesQueryMaxMs; StatusReadMaxMs = $taskStatusReadMaxMs; Polls = $taskPolls; TimedOut = ($null -eq $taskFirstNames -or ($ExpectedCount -ge 0 -and $null -eq $taskFullCountMs)) }
}
function Wait-TaskScrollPosition([Windows.Automation.ScrollPattern]$Pattern, [string]$Mode, [double]$Before) {
    $taskScrollWait = [Diagnostics.Stopwatch]::StartNew()
    $taskPosition = $Before
    $taskMatched = $false
    do {
        $taskPosition = $Pattern.Current.VerticalScrollPercent
        $taskMatched = if ($Mode -eq 'JumpEnd') { [Math]::Abs($taskPosition - 100) -lt 0.001 } elseif ($Mode -eq 'JumpStart') { [Math]::Abs($taskPosition) -lt 0.001 } else { [Math]::Abs($taskPosition - $Before) -gt 1e-10 }
        if ($taskMatched) { break }
        Start-Sleep -Milliseconds 5
    } while ($taskScrollWait.Elapsed.TotalSeconds -lt 2)
    return [pscustomobject]@{ Percent = $taskPosition; TimedOut = !$taskMatched }
}
if ($Action -eq 'Inspect') {
    $taskWalker = [Windows.Automation.TreeWalker]::RawViewWalker
    $taskQueue = [Collections.Generic.Queue[object]]::new()
    $taskQueue.Enqueue(@($taskWindow,0))
    $taskTree = [Collections.Generic.List[object]]::new()
    while ($taskQueue.Count -and $taskTree.Count -lt 300) {
        $taskEntry = $taskQueue.Dequeue(); $taskElement = $taskEntry[0]; $taskDepth = $taskEntry[1]
        $taskCurrent = $taskElement.Current
        $taskTree.Add([pscustomobject]@{ Depth = $taskDepth; Id = $taskCurrent.AutomationId; Type = $taskCurrent.ControlType.ProgrammaticName; Class = $taskCurrent.ClassName; Offscreen = $taskCurrent.IsOffscreen; Enabled = $taskCurrent.IsEnabled; Patterns = @($taskElement.GetSupportedPatterns() | ForEach-Object ProgrammaticName) })
        if ($taskDepth -lt 12) {
            $taskChild = $taskWalker.GetFirstChild($taskElement)
            while ($taskChild) { $taskQueue.Enqueue(@($taskChild,($taskDepth+1))); $taskChild = $taskWalker.GetNextSibling($taskChild) }
        }
    }
    $taskTree | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'automation-tree.json')
}
elseif ($Action -eq 'Navigation') {
    $taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
    $null = [IO.Directory]::CreateDirectory($taskEmpty)
    if (@([IO.Directory]::EnumerateFileSystemEntries($taskEmpty)).Count) { throw 'Empty transition fixture contains entries.' }
    foreach ($taskScenario in $Scenario) {
        $taskFolder = Join-Path $taskRoot ('benchmarks/synthetic/' + $taskScenario)
        if (![IO.Directory]::Exists($taskFolder)) { throw "Missing fixture: $taskScenario" }
        for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
            $taskTransitionStartedUtc = [DateTime]::UtcNow
            $taskAway = Set-TaskPath $taskEmpty $true
            $taskTransitions.Add([pscustomobject]@{ Scenario = $taskScenario; Repetition = $taskRepeat; StartedUtc = $taskTransitionStartedUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); EmptyNamesObservedMs = $taskAway.AccessibleNamesMs; PathUpdatedMs = $taskAway.PathUpdatedMs; SetValueCallMs = $taskAway.SetValueCallMs; PatternQueryMs = $taskAway.PatternQueryMs; PathReadMaxMs = $taskAway.PathReadMaxMs; NamesQueryMaxMs = $taskAway.NamesQueryMaxMs; TimedOut = $taskAway.TimedOut })
            $taskTransitions | Export-Csv -LiteralPath (Join-Path $taskOutput 'transitions.csv') -NoTypeInformation
            if ($taskAway.TimedOut) { throw 'Empty fixture did not become accessible; refusing stale-list measurement.' }
            $script:taskCountGetter = Get-TaskElement 'DirectoryItemCount'
            if (!$script:taskCountGetter) { throw 'DirectoryItemCount unavailable in empty fixture; cannot observe full status count without traversal.' }
            Start-Sleep -Milliseconds 250
            $taskProcess.Refresh()
            $taskCpuBefore = $taskProcess.TotalProcessorTime.TotalMilliseconds
            $taskResult = Set-TaskPath $taskFolder $false ([int]$taskScenario)
            $taskProcess.Refresh()
            $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Scenario = $taskScenario; Repetition = $taskRepeat; Metric = 'UIA_FirstViewportName'; AccessibleNamesMs = $taskResult.AccessibleNamesMs; StatusFullCountMs = $taskResult.StatusFullCountMs; PathUpdatedMs = $taskResult.PathUpdatedMs; SetValueCallMs = $taskResult.SetValueCallMs; PatternQueryMs = $taskResult.PatternQueryMs; PathReadMaxMs = $taskResult.PathReadMaxMs; NamesQueryMaxMs = $taskResult.NamesQueryMaxMs; StatusReadMaxMs = $taskResult.StatusReadMaxMs; Polls = $taskResult.Polls; TimedOut = $taskResult.TimedOut; CPUms = $taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpuBefore; PrivateBytes = $taskProcess.PrivateMemorySize64; WorkingSetBytes = $taskProcess.WorkingSet64; WindowBounds = $taskWindow.Current.BoundingRectangle.ToString() })
            $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
            Start-Sleep -Milliseconds 750
        }
    }
}
elseif ($Action -eq 'RapidNavigation') {
    $taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
    $null = [IO.Directory]::CreateDirectory($taskEmpty)
    if (@([IO.Directory]::EnumerateFileSystemEntries($taskEmpty)).Count) { throw 'Empty transition fixture contains entries.' }
    $taskAway = Set-TaskPath $taskEmpty $true
    if ($taskAway.TimedOut) { throw 'Empty fixture did not become accessible.' }
    $script:taskCountGetter = Get-TaskElement 'DirectoryItemCount'
    $taskTargets = @('100000','10000','50000','empty','100000','empty','1000')
    for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
        $taskProcess.Refresh()
        $taskBeforePrivate = $taskProcess.PrivateMemorySize64
        $taskCpuBefore = $taskProcess.TotalProcessorTime.TotalMilliseconds
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        foreach ($taskTarget in $taskTargets) {
            $taskFolder = if ($taskTarget -eq 'empty') { $taskEmpty } else { Join-Path $taskRoot ('benchmarks/synthetic/' + $taskTarget) }
            if (![IO.Directory]::Exists($taskFolder)) { throw 'Missing synthetic target.' }
            $script:taskPathSetterPattern.SetValue($taskFolder)
            Start-Sleep -Milliseconds 20
        }
        $taskBurstMs = $taskWatch.Elapsed.TotalMilliseconds
        $taskExpected = Join-Path $taskRoot 'benchmarks/synthetic/1000'
        $taskConfirmed = $false
        while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { throw 'Window minimized during stress session.' }
            $taskCountDigits = [regex]::Replace($script:taskCountGetter.Current.Name, '\D', '')
            if ((Get-TaskPath).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase) -and $taskCountDigits -eq '1000') { $taskConfirmed = $true; break }
            Start-Sleep -Milliseconds 20
        }
        $taskReadyMs = $taskWatch.Elapsed.TotalMilliseconds
        Start-Sleep -Milliseconds 1000
        $taskProcess.Refresh()
        $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Scenario = 'RapidNavigation'; Repetition = $taskRepeat; Metric = 'UIA_RapidNavigationFinal'; BurstCallsMs = $taskBurstMs; StatusFullCountMs = $taskReadyMs; TimedOut = !$taskConfirmed; SubmittedNavigations = $taskTargets.Count; CPUms = $taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpuBefore; StartPrivateBytes = $taskBeforePrivate; PrivateBytes = $taskProcess.PrivateMemorySize64; WorkingSetBytes = $taskProcess.WorkingSet64; WindowBounds = $taskWindow.Current.BoundingRectangle.ToString() })
        $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
        if (!$taskConfirmed) { throw 'Final synthetic target/count did not stabilize; session is incomplete.' }
    }
}
elseif ($Action -eq 'SelectionDuringLoad') {
    $taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
    $null = [IO.Directory]::CreateDirectory($taskEmpty)
    if (@([IO.Directory]::EnumerateFileSystemEntries($taskEmpty)).Count) { throw 'Empty transition fixture contains entries.' }
    $taskExpected = Join-Path $taskRoot ('benchmarks/synthetic/' + $Scenario[0])
    if (![IO.Directory]::Exists($taskExpected)) { throw 'Missing selected synthetic fixture.' }
    for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
        $taskAway = Set-TaskPath $taskEmpty $true
        if ($taskAway.TimedOut) { throw 'Empty fixture did not become accessible.' }
        $script:taskCountGetter = Get-TaskElement 'DirectoryItemCount'
        if (!$script:taskCountGetter) { throw 'Status count unavailable in empty fixture.' }
        Start-Sleep -Milliseconds 250
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $script:taskPathSetterPattern.SetValue($taskExpected)
        $taskSetMs = $taskWatch.Elapsed.TotalMilliseconds
        $taskNames = $null
        while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { throw 'Window minimized during selection probe.' }
            if ((Get-TaskPath).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase)) {
                $taskCandidateNames = Get-TaskVisibleNames
                if ($taskCandidateNames.Count) { $taskNames = $taskCandidateNames; break }
            }
            Start-Sleep -Milliseconds 10
        }
        $taskNamesMs = if ($taskNames) { $taskWatch.Elapsed.TotalMilliseconds } else { $null }
        $taskSelectRequestMs = $null; $taskSelectConfirmedMs = $null; $taskSelectCallMs = $null
        $taskSelected = $false; $taskCountBefore = $null; $taskCountAfter = $null
        $taskCapturedRowName = $null; $taskSelection = $null; $taskItem = $null
        if ($taskNames) {
            $taskWalker = [Windows.Automation.TreeWalker]::RawViewWalker
            $taskItem = $taskNames[0]; $taskSelection = $null
            while ($taskItem -and $taskItem.Current.ProcessId -eq $taskProcess.Id) {
                $taskCandidate = $null
                if ($taskItem.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$taskCandidate)) { $taskSelection = [Windows.Automation.SelectionItemPattern]$taskCandidate; break }
                if ($taskItem -eq $taskWindow) { break }
                $taskItem = $taskWalker.GetParent($taskItem)
            }
            if (!$taskSelection) { throw 'The first viewport row exposes no SelectionItemPattern.' }
            $taskCapturedRowName = $taskItem.Current.Name
            $taskCountBefore = [regex]::Replace($script:taskCountGetter.Current.Name, '\D', '')
            $taskSelectRequestMs = $taskWatch.Elapsed.TotalMilliseconds
            $taskSelection.Select()
            $taskSelectCallMs = $taskWatch.Elapsed.TotalMilliseconds - $taskSelectRequestMs
            $taskSelected = $taskSelection.Current.IsSelected
            if ($taskSelected) { $taskSelectConfirmedMs = $taskWatch.Elapsed.TotalMilliseconds }
            $taskCountAfter = [regex]::Replace($script:taskCountGetter.Current.Name, '\D', '')
        }
        $taskFullMs = $null
        while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            if ((Get-TaskPath).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase) -and [regex]::Replace($script:taskCountGetter.Current.Name, '\D', '') -eq $Scenario[0]) { $taskFullMs = $taskWatch.Elapsed.TotalMilliseconds; break }
            Start-Sleep -Milliseconds 10
        }
        $taskDuringLoad = $taskSelected -and $taskCountBefore -and $taskCountAfter -and [long]$taskCountBefore -gt 0 -and [long]$taskCountBefore -lt [long]$Scenario[0] -and [long]$taskCountAfter -gt 0 -and [long]$taskCountAfter -lt [long]$Scenario[0]
        $taskRetainedAfterFull = $null; $taskSameNameAfterFull = $null
        if ($null -ne $taskFullMs -and $taskSelection) {
            try { $taskRetainedAfterFull = $taskSelection.Current.IsSelected; $taskSameNameAfterFull = $taskItem.Current.Name -eq $taskCapturedRowName } catch [Windows.Automation.ElementNotAvailableException] { }
        }
        $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Scenario = $Scenario[0]; Repetition = $taskRepeat; Metric = 'UIA_SelectionDuringLoad'; AccessibleNamesMs = $taskNamesMs; SetValueCallMs = $taskSetMs; SelectionRequestedMs = $taskSelectRequestMs; SelectionConfirmedMs = $taskSelectConfirmedMs; CallMs = $taskSelectCallMs; CountBeforeSelect = $taskCountBefore; CountAfterSelect = $taskCountAfter; Selected = $taskSelected; ConfirmedBeforeFullCount = [bool]$taskDuringLoad; CapturedProviderSelectedAfterFull = $taskRetainedAfterFull; CapturedProviderNameSameAfterFull = $taskSameNameAfterFull; StatusFullCountMs = $taskFullMs; TimedOut = ($null -eq $taskFullMs -or !$taskSelected); WindowBounds = $taskWindow.Current.BoundingRectangle.ToString() })
        $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
        Start-Sleep -Milliseconds 750
    }
}
elseif ($Action -eq 'Selection') {
    $taskExpected = Join-Path $taskRoot ('benchmarks/synthetic/' + $Scenario[0])
    if (!(Get-TaskPath).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Open the selected synthetic fixture first.' }
    $taskNames = Get-TaskVisibleNames
    if (!$taskNames.Count) { throw 'No accessible viewport file names.' }
    $taskWalker = [Windows.Automation.TreeWalker]::RawViewWalker
    $taskItem = $taskNames[0]
    $taskSelection = $null
    while ($taskItem -and $taskItem.Current.ProcessId -eq $taskProcess.Id) {
        $taskCandidate = $null
        if ($taskItem.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$taskCandidate)) { $taskSelection = [Windows.Automation.SelectionItemPattern]$taskCandidate; break }
        if ($taskItem -eq $taskWindow) { break }
        $taskItem = $taskWalker.GetParent($taskItem)
    }
    if (!$taskSelection) { throw 'The viewport row exposes no SelectionItemPattern.' }
    for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
        if ($taskSelection.Current.IsSelected) { $taskSelection.RemoveFromSelection() }
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $taskSelection.Select()
        $taskCallMs = $taskWatch.Elapsed.TotalMilliseconds
        $taskConfirmed = $taskSelection.Current.IsSelected
        $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Scenario = $Scenario[0]; Repetition = $taskRepeat; Metric = 'UIA_SelectionConfirm'; CallMs = $taskCallMs; ObservationMs = $taskWatch.Elapsed.TotalMilliseconds; Selected = $taskConfirmed })
        if (!$taskConfirmed) { throw 'Scoped selection was not confirmed.' }
        Start-Sleep -Milliseconds 100
    }
}
else {
    $taskExpected = Join-Path $taskRoot ('benchmarks/synthetic/' + $Scenario[0])
    if (!(Get-TaskPath).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Open the selected synthetic fixture first.' }
    $taskNames = Get-TaskVisibleNames
    if (!$taskNames.Count) { throw 'No accessible viewport file names.' }
    $taskWalker = [Windows.Automation.TreeWalker]::RawViewWalker
    $taskScroller = $taskNames[0]
    $taskScrollPattern = $null
    while ($taskScroller -and $taskScroller.Current.ProcessId -eq $taskProcess.Id) {
        $taskCandidate = $null
        if ($taskScroller.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern, [ref]$taskCandidate)) { $taskScrollPattern = [Windows.Automation.ScrollPattern]$taskCandidate; break }
        if ($taskScroller -eq $taskWindow) { break }
        $taskScroller = $taskWalker.GetParent($taskScroller)
    }
    if (!$taskScrollPattern) { throw 'The scoped file list exposes no ScrollPattern; use the integrated manual probe.' }
    foreach ($taskMode in @('SmallIncrement','LargeIncrement','JumpEnd','JumpStart')) {
        for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
            # A repeated jump to the same end is an idempotent call, not another movement.
            if ($taskMode -eq 'JumpEnd') {
                $taskScrollPattern.SetScrollPercent(-1,0)
                $taskPreparation = Wait-TaskScrollPosition $taskScrollPattern 'JumpStart' -1
                if ($taskPreparation.TimedOut) { throw 'Jump preparation did not reach the starting endpoint.' }
                Start-Sleep -Milliseconds 100
            }
            elseif ($taskMode -eq 'JumpStart') {
                $taskScrollPattern.SetScrollPercent(-1,100)
                $taskPreparation = Wait-TaskScrollPosition $taskScrollPattern 'JumpEnd' -1
                if ($taskPreparation.TimedOut) { throw 'Jump preparation did not reach the ending endpoint.' }
                Start-Sleep -Milliseconds 100
            }
            $taskBefore = $taskScrollPattern.Current.VerticalScrollPercent
            $taskScrollStartedUtc = [DateTime]::UtcNow
            $taskWatch = [Diagnostics.Stopwatch]::StartNew()
            if ($taskMode -eq 'JumpEnd') { $taskScrollPattern.SetScrollPercent(-1,100) }
            elseif ($taskMode -eq 'JumpStart') { $taskScrollPattern.SetScrollPercent(-1,0) }
            else { $taskScrollPattern.Scroll([Windows.Automation.ScrollAmount]::NoAmount, [Windows.Automation.ScrollAmount]::$taskMode) }
            $taskCallMs = $taskWatch.Elapsed.TotalMilliseconds
            $taskPosition = Wait-TaskScrollPosition $taskScrollPattern $taskMode $taskBefore
            $taskAfter = $taskPosition.Percent
            $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Scenario = $Scenario[0]; Repetition = $taskRepeat; Metric = 'UIA_ScrollPatternCall'; Mode = $taskMode; StartedUtc = $taskScrollStartedUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); CallMs = $taskCallMs; BeforePercent = $taskBefore; AfterPercent = $taskAfter; Moved = ($taskAfter -ne $taskBefore); TimedOut = $taskPosition.TimedOut; ObservationMs = $taskWatch.Elapsed.TotalMilliseconds })
            Start-Sleep -Milliseconds 100
        }
    }
}
$taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
[ordered]@{ Variant = $Variant; Action = $Action; Package = $taskPackage.PackageFullName; InstallLocation = $taskPackage.InstallLocation; ProcessId = $taskProcess.Id; StartedUtc = $taskSessionStartedUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); Scenarios = $Scenario; Repetitions = $Repetitions; Culture = [Globalization.CultureInfo]::CurrentCulture.Name; CacheState = 'Warm/uncontrolled; no global cache manipulation'; ScrollJumpPreparation = 'Opposite end is set before each timed jump; preparation generates additional untimed ViewChanged/render events'; Clock = 'Stopwatch'; PollIntervalMs = 10; Observer = 'Path/status elements and ValuePatterns cached in empty fixture; first ItemName FindFirst; max observed query durations retained; Stopwatch starts before setter lookup'; Scope = 'UIA calls and provider-accessible viewport text/status count in the verified experimental process; not physical pixels or click-to-display'; ListCompletionMetric = 'Expected full count exposed by status-bar provider; does not assert final layout completion'; PhysicalPresentationMeasured = $false; Samples = $taskSamples.Count } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'session.json')
Write-Output $taskOutput
