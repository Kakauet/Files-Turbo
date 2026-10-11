#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('1000','10000','50000','100000')][string[]]$Scenario = @('10000','100000'),
    [ValidateRange(1,40)][int]$Repetitions = 10,
    [ValidateRange(5,120)][int]$TimeoutSeconds = 30,
    [switch]$KeepWindow,
    [ValidateSet('Comparison','ObserverValidation')][string]$SessionPurpose = 'Comparison',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
if (![IO.Directory]::Exists($taskEmpty) -or @([IO.Directory]::EnumerateFileSystemEntries($taskEmpty)).Count) { throw 'Expected empty workspace fixture is missing or contains entries.' }
$taskAllowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$null = $taskAllowed.Add($taskEmpty)
foreach ($taskSize in $Scenario) {
    $taskFolder = Join-Path $taskRoot ('benchmarks/synthetic/' + $taskSize)
    if (![IO.Directory]::Exists($taskFolder) -or ((Get-Item -LiteralPath $taskFolder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A scenario fixture is missing or linked.' }
    $null = $taskAllowed.Add($taskFolder)
}
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory)) } else { Join-Path $taskRoot ('benchmarks/results/phase4/explorer-navigation-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (!$taskOutput.StartsWith($taskRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskOutput)) { throw 'Use a fresh output directory inside the workspace.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskObserverHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
[IO.File]::Copy($PSCommandPath,(Join-Path $taskOutput 'observer-script.ps1'),$false)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$taskShell = New-Object -ComObject Shell.Application
$taskSamples = [Collections.Generic.List[object]]::new()
$taskTransitions = [Collections.Generic.List[object]]::new()
$taskBuildChecks = [Collections.Generic.List[object]]::new()
$taskOwned = $null; $taskHwnd = [IntPtr]::Zero; $taskStatus = 'Incomplete'; $script:taskConcurrentBuild = $false
$taskExpectedBounds = [Windows.Rect]::new(26,26,1440,753)
$taskWindowSetup = $null
$taskStarted = [DateTime]::UtcNow
function Get-TaskShellHandles {
    $taskWindows = $taskShell.Windows()
    $taskHandles = [Collections.Generic.List[long]]::new()
    for ($taskIndex = 0; $taskIndex -lt $taskWindows.Count; $taskIndex++) {
        $taskEntry = $taskWindows.Item($taskIndex)
        if ($taskEntry) { $taskHandles.Add([long]$taskEntry.HWND) }
    }
    return ,$taskHandles
}
function Assert-TaskNoBuild {
    $taskBuilds = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='MSBuild.exe'" | Where-Object {
        ($_.Name -eq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:') -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|\")(?:build|msbuild|publish)(?:\s|\")')
    })
    $taskBuildChecks.Add([pscustomobject]@{ CheckedUtc = [DateTime]::UtcNow.ToString('o'); ActiveBuildCount = $taskBuilds.Count; ProcessIds = @($taskBuilds | ForEach-Object ProcessId) })
    if ($taskBuilds.Count) { $script:taskConcurrentBuild = $true; if ($SessionPurpose -eq 'Comparison') { throw 'Active build detected; Explorer timing is incomplete.' } }
}
function Get-TaskOwnedPath {
    if (!$taskOwned -or [long]$taskOwned.HWND -ne $taskHwnd.ToInt64()) { throw 'Created Explorer window identity changed.' }
    $taskLocation = [string]$taskOwned.LocationURL
    if (!$taskLocation) { return $null }
    $taskUri = [Uri]::new($taskLocation)
    if (!$taskUri.IsFile) { throw 'Created Explorer window left the synthetic filesystem scope.' }
    $taskPath = [IO.Path]::GetFullPath($taskUri.LocalPath).TrimEnd('\')
    if (!$taskAllowed.Contains($taskPath)) { throw 'Created Explorer window left the allowlisted synthetic fixtures; no file items were read.' }
    return $taskPath
}
function Get-TaskRawMatches($Root,$Condition,[int]$Maximum = 256) {
    $taskWalker = [Windows.Automation.TreeWalker]::RawViewWalker
    $taskPending = [Collections.Generic.Stack[Windows.Automation.AutomationElement]]::new()
    $taskFirstChild = $taskWalker.GetFirstChild($Root)
    if ($taskFirstChild) { $taskPending.Push($taskFirstChild) }
    $taskVisited = 0
    while ($taskPending.Count -and $taskVisited -lt $Maximum) {
        $taskPeer = $taskPending.Pop()
        $taskVisited++
        $taskMatches = $taskPeer.FindFirst([Windows.Automation.TreeScope]::Element,$Condition)
        if ($taskMatches) { Write-Output $taskPeer }
        $taskSibling = $taskWalker.GetNextSibling($taskPeer)
        if ($taskSibling) { $taskPending.Push($taskSibling) }
        $taskChild = $taskWalker.GetFirstChild($taskPeer)
        if ($taskChild) { $taskPending.Push($taskChild) }
    }
}
function Find-TaskFileList($Root) {
    $taskCondition = [Windows.Automation.OrCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ItemsView'),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'UIItemsView'))
    $taskPeer = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskCondition)
    if (!$taskPeer) { $taskPeer = Get-TaskRawMatches $Root $taskCondition 256 | Select-Object -First 1 }
    return $taskPeer
}
function Find-TaskStatusBar($Root) {
    $taskCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::StatusBar)
    $taskPeer = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskCondition)
    if (!$taskPeer) { $taskPeer = Get-TaskRawMatches $Root $taskCondition 256 | Select-Object -First 1 }
    return $taskPeer
}
function Test-TaskWindowBounds($Bounds) {
    return [Math]::Abs($Bounds.X-$taskExpectedBounds.X) -le 1 -and
        [Math]::Abs($Bounds.Y-$taskExpectedBounds.Y) -le 1 -and
        [Math]::Abs($Bounds.Width-$taskExpectedBounds.Width) -le 1 -and
        [Math]::Abs($Bounds.Height-$taskExpectedBounds.Height) -le 1
}
function Initialize-TaskWindowBounds {
    if (!(Get-TaskOwnedPath).Equals($taskEmpty,[StringComparison]::OrdinalIgnoreCase)) { throw 'Window setup requires the confirmed empty synthetic fixture.' }
    $taskWindowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
    $taskRestoreWatch = [Diagnostics.Stopwatch]::StartNew()
    while ($taskWindowPattern.Current.WindowVisualState -ne [Windows.Automation.WindowVisualState]::Normal -and $taskRestoreWatch.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 50 }
    if ($taskWindowPattern.Current.WindowVisualState -ne [Windows.Automation.WindowVisualState]::Normal) { throw 'Created Explorer window did not restore to Normal.' }
    $taskOwned.Left = 26; $taskOwned.Top = 26; $taskOwned.Width = 1440; $taskOwned.Height = 753
    $taskBoundsWatch = [Diagnostics.Stopwatch]::StartNew()
    $taskStableReadbacks = 0
    do {
        $taskCurrentPath = Get-TaskOwnedPath
        if (!$taskCurrentPath -or !$taskCurrentPath.Equals($taskEmpty,[StringComparison]::OrdinalIgnoreCase)) { throw 'Created Explorer window changed fixture during its geometry setup.' }
        $taskBounds = $taskWindow.Current.BoundingRectangle
        $taskComMatches = [int]$taskOwned.Left -eq 26 -and [int]$taskOwned.Top -eq 26 -and [int]$taskOwned.Width -eq 1440 -and [int]$taskOwned.Height -eq 753
        if ($taskComMatches -and (Test-TaskWindowBounds $taskBounds) -and $taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Normal) { $taskStableReadbacks++ } else { $taskStableReadbacks = 0 }
        if ($taskStableReadbacks -ge 2) { break }
        Start-Sleep -Milliseconds 100
    } while ($taskBoundsWatch.Elapsed.TotalSeconds -lt 5)
    if ($taskStableReadbacks -lt 2) { throw 'Created Explorer window did not retain the requested 26,26,1440,753 bounds; comparable timing is unavailable.' }
    return [pscustomobject]@{ RequestedBounds = '26;26;1440;753'; ReadbackBounds = $taskBounds.ToString(); ComLeft = [int]$taskOwned.Left; ComTop = [int]$taskOwned.Top; ComWidth = [int]$taskOwned.Width; ComHeight = [int]$taskOwned.Height; VisualState = $taskWindowPattern.Current.WindowVisualState.ToString(); Matched = $true }
}
function Wait-TaskTarget([string]$Target,[long]$Count,[bool]$Empty) {
    if (!$taskAllowed.Contains($Target)) { throw 'Target is outside the benchmark allowlist.' }
    $taskRequestUtc = [DateTime]::UtcNow
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    $taskOwned.Navigate2($Target)
    $taskNavigateMs = $taskWatch.Elapsed.TotalMilliseconds
    $taskPathMs = $null; $taskFirst = $null; $taskFull = $null; $taskPolls = 0
    $taskPathReadMax = 0.0; $taskNamesMax = 0.0; $taskCountMax = 0.0; $taskPeersMax = 0.0; $taskStaleRetries = 0
    while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $taskPolls++
        if ($taskWindow.Current.ProcessId -ne $taskExplorerPid -or $taskWindowPattern.Current.WindowVisualState -ne [Windows.Automation.WindowVisualState]::Normal -or !(Test-TaskWindowBounds $taskWindow.Current.BoundingRectangle)) { throw 'Created Explorer window ownership, normal state or matched bounds changed.' }
        $taskRead = [Diagnostics.Stopwatch]::StartNew()
        $taskCurrentPath = Get-TaskOwnedPath
        $taskPathReadMax = [Math]::Max($taskPathReadMax,$taskRead.Elapsed.TotalMilliseconds)
        if ($taskCurrentPath -and $taskCurrentPath.Equals($Target,[StringComparison]::OrdinalIgnoreCase)) {
            if ($null -eq $taskPathMs) { $taskPathMs = $taskWatch.Elapsed.TotalMilliseconds }
            try {
            $taskRead.Restart()
            $taskCurrentWindow = [Windows.Automation.AutomationElement]::FromHandle($taskHwnd)
            if ($taskCurrentWindow.Current.ProcessId -ne $taskExplorerPid) { throw 'Created Explorer HWND changed process during peer acquisition.' }
            $taskList = Find-TaskFileList $taskCurrentWindow
            $taskStatusElement = Find-TaskStatusBar $taskCurrentWindow
            $taskPeersMax = [Math]::Max($taskPeersMax,$taskRead.Elapsed.TotalMilliseconds)
            $taskRead.Restart()
            $taskTexts = if ($taskStatusElement) { @($taskStatusElement.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)) } else { @() }
            if ($taskStatusElement) { $taskTexts = @($taskStatusElement) + $taskTexts }
            $taskDigits = ''
            foreach ($taskText in $taskTexts) {
                $taskName = $taskText.Current.Name
                if ($taskName -match '^\s*[\d\.,\s]+\s+\D') { $taskDigits = [regex]::Replace($taskName,'\D',''); break }
            }
            if ($taskDigits -eq '' -and $taskStatusElement) {
                foreach ($taskText in Get-TaskRawMatches $taskStatusElement ([Windows.Automation.Condition]::TrueCondition) 64) {
                    if ($taskText.Current.Name -match '^\s*[\d\.,\s]+\s+\D') { $taskDigits = [regex]::Replace($taskText.Current.Name,'\D',''); break }
                }
            }
            $taskCountMax = [Math]::Max($taskCountMax,$taskRead.Elapsed.TotalMilliseconds)
            if ($null -eq $taskFull -and $taskDigits -ne '' -and [long]$taskDigits -eq $Count) { $taskFull = $taskWatch.Elapsed.TotalMilliseconds }
            if ($null -eq $taskFirst -and !$Empty) {
                $taskRead.Restart()
                $taskItemCondition = [Windows.Automation.OrCondition]::new(
                    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem),
                    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::DataItem))
                $taskFirstItem = if ($taskList) { $taskList.FindFirst([Windows.Automation.TreeScope]::Descendants,$taskItemCondition) } else { $null }
                if ($taskFirstItem) {
                    $taskCurrent = $taskFirstItem.Current
                    if ($taskCurrent.Name -match '^file_\d{6}_' -and !$taskCurrent.IsOffscreen -and $taskCurrent.BoundingRectangle.Width -gt 0 -and $taskCurrent.BoundingRectangle.Height -gt 0 -and $taskCurrent.BoundingRectangle.IntersectsWith($taskWindow.Current.BoundingRectangle)) { $taskFirst = $taskWatch.Elapsed.TotalMilliseconds }
                }
                if ($null -eq $taskFirst -and $taskList) {
                    foreach ($taskItem in Get-TaskRawMatches $taskList ([Windows.Automation.Condition]::TrueCondition) 256) {
                        $taskCurrent = $taskItem.Current
                        if ($taskCurrent.Name -match '^file_\d{6}_' -and !$taskCurrent.IsOffscreen -and $taskCurrent.BoundingRectangle.Width -gt 0 -and $taskCurrent.BoundingRectangle.Height -gt 0 -and $taskCurrent.BoundingRectangle.IntersectsWith($taskWindow.Current.BoundingRectangle)) { $taskFirst = $taskWatch.Elapsed.TotalMilliseconds; break }
                    }
                }
                $taskNamesMax = [Math]::Max($taskNamesMax,$taskRead.Elapsed.TotalMilliseconds)
            }
            if ($Empty -and $null -ne $taskFull) { $taskFirst = $taskFull }
            if ($null -ne $taskFirst -and $null -ne $taskFull) { break }
            } catch [Windows.Automation.ElementNotAvailableException] { $taskStaleRetries++ }
        }
        Start-Sleep -Milliseconds 10
    }
    return [pscustomobject]@{ StartedUtc = $taskRequestUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); AccessibleNamesMs = $taskFirst; StatusFullCountMs = $taskFull; PathUpdatedMs = $taskPathMs; Navigate2CallMs = $taskNavigateMs; PathReadMaxMs = $taskPathReadMax; NamesQueryMaxMs = $taskNamesMax; StatusReadMaxMs = $taskCountMax; PeersQueryMaxMs = $taskPeersMax; StalePeerRetries = $taskStaleRetries; Polls = $taskPolls; TimedOut = ($null -eq $taskFirst -or $null -eq $taskFull) }
}
try {
    Assert-TaskNoBuild
    $taskBefore = Get-TaskShellHandles
    $taskShell.Explore($taskEmpty)
    $taskCreationWatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $taskAfter = Get-TaskShellHandles
        $taskNew = @($taskAfter | Where-Object { !$taskBefore.Contains($_) })
        if ($taskNew.Count -eq 1) { $taskHwnd = [IntPtr]::new($taskNew[0]); break }
        if ($taskNew.Count -gt 1) { throw 'Multiple new shell windows appeared; ownership is ambiguous and none will be closed.' }
        Start-Sleep -Milliseconds 100
    } while ($taskCreationWatch.Elapsed.TotalSeconds -lt 10)
    if ($taskHwnd -eq [IntPtr]::Zero) { throw 'Explore did not create a distinguishable new fixture HWND; no existing window was reused.' }
    $taskWindows = $taskShell.Windows()
    for ($taskIndex = 0; $taskIndex -lt $taskWindows.Count; $taskIndex++) {
        $taskCandidate = $taskWindows.Item($taskIndex)
        if ($taskCandidate -and [long]$taskCandidate.HWND -eq $taskHwnd.ToInt64()) { $taskOwned = $taskCandidate; break }
    }
    if (!$taskOwned) { throw 'Created Explorer HWND is absent from Shell.Application.' }
    $taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskHwnd)
    $taskExplorerPid = $taskWindow.Current.ProcessId
    $taskExplorerProcess = Get-Process -Id $taskExplorerPid
    if (!$taskExplorerProcess.Path.Equals((Join-Path $env:windir 'explorer.exe'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Created window is not the normal Windows Explorer executable.' }
    $taskWindowPattern = [Windows.Automation.WindowPattern]$taskWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    $taskPreparation = [Diagnostics.Stopwatch]::StartNew()
    do { $taskReadyPath = Get-TaskOwnedPath; if ($taskReadyPath -and $taskReadyPath.Equals($taskEmpty,[StringComparison]::OrdinalIgnoreCase)) { break }; Start-Sleep -Milliseconds 100 } while ($taskPreparation.Elapsed.TotalSeconds -lt 10)
    if (!$taskReadyPath -or !$taskReadyPath.Equals($taskEmpty,[StringComparison]::OrdinalIgnoreCase)) { throw 'Created Explorer window never exposed its empty fixture path.' }
    $taskOwned.Document.CurrentViewMode = 4
    Start-Sleep -Milliseconds 500
    $taskWindowSetup = Initialize-TaskWindowBounds
    foreach ($taskSize in $Scenario) {
        $taskTarget = Join-Path $taskRoot ('benchmarks/synthetic/' + $taskSize)
        for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
            Assert-TaskNoBuild
            $taskAway = Wait-TaskTarget $taskEmpty 0 $true
            $taskTransitions.Add([pscustomobject]@{ Scenario = $taskSize; Repetition = $taskRepeat; StartedUtc = $taskAway.StartedUtc; CompletedUtc = $taskAway.CompletedUtc; EmptyCountMs = $taskAway.StatusFullCountMs; Navigate2CallMs = $taskAway.Navigate2CallMs; TimedOut = $taskAway.TimedOut })
            if ($taskAway.TimedOut) { throw 'Explorer empty transition did not stabilize.' }
            Start-Sleep -Milliseconds 250
            $taskResult = Wait-TaskTarget $taskTarget ([long]$taskSize) $false
            $taskObservedViewMode = [int]$taskOwned.Document.CurrentViewMode
            if ($taskObservedViewMode -ne 4) { throw 'Explorer folder used a different view mode; preserve this control and configure the synthetic view before comparison.' }
            Assert-TaskNoBuild
            $taskSamples.Add([pscustomobject]@{ Variant = 'Explorer'; Scenario = $taskSize; Repetition = $taskRepeat; Metric = 'COMNavigate2_ExplorerUIA'; StartedUtc = $taskResult.StartedUtc; CompletedUtc = $taskResult.CompletedUtc; AccessibleNamesMs = $taskResult.AccessibleNamesMs; StatusFullCountMs = $taskResult.StatusFullCountMs; PathUpdatedMs = $taskResult.PathUpdatedMs; Navigate2CallMs = $taskResult.Navigate2CallMs; PathReadMaxMs = $taskResult.PathReadMaxMs; NamesQueryMaxMs = $taskResult.NamesQueryMaxMs; StatusReadMaxMs = $taskResult.StatusReadMaxMs; PeersQueryMaxMs = $taskResult.PeersQueryMaxMs; StalePeerRetries = $taskResult.StalePeerRetries; Polls = $taskResult.Polls; TimedOut = $taskResult.TimedOut; WindowBounds = $taskWindow.Current.BoundingRectangle.ToString() })
            $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
            Start-Sleep -Milliseconds 750
        }
    }
    $taskStatus = if (@($taskSamples | Where-Object TimedOut).Count) { 'CompletedWithTimeouts' } else { 'Completed' }
} catch {
    [ordered]@{ FailedUtc = [DateTime]::UtcNow.ToString('o'); Message = $_.Exception.Message; ScriptLine = $_.InvocationInfo.ScriptLineNumber } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'failure.json')
    throw
} finally {
    $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
    $taskTransitions | Export-Csv -LiteralPath (Join-Path $taskOutput 'transitions.csv') -NoTypeInformation
    $taskBuildChecks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'concurrent-build-checks.json')
    $taskClosed = $false
    if ($taskOwned -and !$KeepWindow) {
        try { $taskClosePath = Get-TaskOwnedPath; if ($taskClosePath -and $taskAllowed.Contains($taskClosePath) -and [long]$taskOwned.HWND -eq $taskHwnd.ToInt64()) { $taskOwned.Quit(); $taskClosed = $true } } catch { }
    }
    [ordered]@{ Status = $taskStatus; Variant = 'Explorer'; SessionPurpose = $SessionPurpose; StartedUtc = $taskStarted.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); Culture = [Globalization.CultureInfo]::CurrentCulture.Name; ProcessId = $taskExplorerPid; CreatedWindowHandle = $taskHwnd.ToInt64(); CreatedWindowClosed = $taskClosed; Scenarios = $Scenario; Repetitions = $Repetitions; View = 'ShellFolderView CurrentViewMode=4 (Details), scoped to created synthetic window'; WindowSetup = $taskWindowSetup; ConcurrentBuild = $script:taskConcurrentBuild; Creation = 'Shell.Explore(empty); existing/new ShellWindows compared using HWND only; existing window locations are never read'; ObserverScriptSha256 = $taskObserverHash; Navigation = 'COM Navigate2 from verified fixture window; start includes COM call, reacquisition of current list/status peers, provider names and current status count, observer costs included'; PeerObservation = 'Exact created HWND only after confirmed allowlisted fixture path; descendants with bounded raw-tree fallback, stale-peer retries retained'; CacheState = 'Warm/uncontrolled; no global cache manipulation'; PhysicalPresentationMeasured = $false; ThumbnailComparisonAvailable = $false; Limits = 'Explorer UIA and COM observer differ from Files ValuePattern. Normal window bounds must match 26,26,1440,753 throughout timing. FileList viewport dimensions and columns may still differ. Shared Explorer process CPU/RAM cannot be attributed to this fixture window; no process-wide termination or global settings changes.' } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'session.json') -Encoding utf8
    if ($taskShell) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($taskShell) }
}
Write-Output $taskOutput
