#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Prepare','Run')][string]$Action = 'Prepare',
    [string]$FixtureDirectory,
    [string]$ExpectedDelivery,
    [int]$ProcessId,
    [ValidateRange(2,45)][int]$TimeoutSeconds = 15,
    [string]$OutputReport = 'reports/phase4-app-interactions.json'
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskWorkspacePath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($Path, $taskRoot)
    if (!$taskFull.StartsWith($taskRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path must remain in this workspace.' }
    for ($taskCursor=$taskFull; $taskCursor.Length -gt $taskRoot.Length; $taskCursor=Split-Path $taskCursor -Parent) {
        if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked paths are not accepted.' }
    }
    return $taskFull
}
$taskOutput = Resolve-TaskWorkspacePath $OutputReport
$taskFixtureBase = Join-Path $taskRoot 'benchmarks/ui-phase4-operations'
$taskFixture = if ($FixtureDirectory) { Resolve-TaskWorkspacePath $FixtureDirectory } else { Join-Path $taskFixtureBase ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (!$taskFixture.StartsWith($taskFixtureBase+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Use a fresh child of benchmarks/ui-phase4-operations.' }
$taskScenarioNames = @('single_selection','multiselection','copy_paste','cut_paste','rename','back_forward','create_folder','preview_toggle','new_tab','sort','layout')
$taskResults = [Collections.Generic.List[object]]::new()
$taskActions = [Collections.Generic.List[object]]::new()
$taskSessionStart = [DateTime]::UtcNow
$taskPackageEvidence = $null
function Write-TaskReport([string]$Status) {
    $taskReport = [ordered]@{Status=$Status;UiExecuted=($Action -eq 'Run');StartedUtc=$taskSessionStart.ToString('o');CompletedUtc=[DateTime]::UtcNow.ToString('o');Fixture=$taskFixture;FixtureManifest=(Join-Path $taskFixture 'manifest.json');Package=$taskPackageEvidence;Results=@($taskResults);UiActions=@($taskActions);Scope='UIA patterns on one exact registered experimental PID and confirmed synthetic paths. Filesystem reads only corroborate requested app operations. Fixture creation is setup, not an app operation. No global keyboard, foreground forcing, private-name inventory, performance or broad file-operation safety claim.'}
    $null = [IO.Directory]::CreateDirectory((Split-Path $taskOutput -Parent))
    [IO.File]::WriteAllText($taskOutput,(($taskReport | ConvertTo-Json -Depth 9) -replace '\r?\n',"`r`n")+"`r`n")
}
if ($Action -eq 'Prepare') {
    if ([IO.Directory]::Exists($taskFixture) -or [IO.File]::Exists($taskOutput)) { throw 'Fixture or report already exists; preserve it and choose fresh paths.' }
    $taskSource = [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'source')).FullName
    $taskCopy = [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'copy-destination')).FullName
    $taskMove = [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'move-destination')).FullName
    $taskFiles = foreach ($taskName in @('copy-source.txt','move-source.txt','rename-source.txt','multi-a.txt','multi-b.txt')) {
        $taskFile = Join-Path $taskSource $taskName
        [IO.File]::WriteAllText($taskFile,"Phase IV synthetic app-operation fixture: $taskName`r`nUTF8 sample: alfa, beta, gamma.`r`n")
        [pscustomobject]@{Name=$taskName;Sha256=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash;Length=(Get-Item -LiteralPath $taskFile).Length}
    }
    $taskManifest = [ordered]@{Fixture=$taskFixture;Source=$taskSource;CopyDestination=$taskCopy;MoveDestination=$taskMove;Files=@($taskFiles);CreatedUtc=[DateTime]::UtcNow.ToString('o');Setup='Synthetic fixture prepared by this script; no UI action has run.'}
    [IO.File]::WriteAllText((Join-Path $taskFixture 'manifest.json'),(($taskManifest | ConvertTo-Json -Depth 5) -replace '\r?\n',"`r`n")+"`r`n")
    foreach ($taskScenario in $taskScenarioNames) { $taskResults.Add([pscustomobject]@{Name=$taskScenario;Status='Pending';Reason='Prepared; app UI has not been executed.'}) }
    Write-TaskReport 'Pending'
    Write-Output ('Prepared synthetic fixture; no UI executed. Manifest: '+(Join-Path $taskFixture 'manifest.json'))
    return
}
if (!$FixtureDirectory -or !$ExpectedDelivery -or $ProcessId -le 0) { throw 'Run requires the prepared fixture, exact expected delivery and explicit PID.' }
$taskManifest = Get-Content -LiteralPath (Join-Path $taskFixture 'manifest.json') -Raw | ConvertFrom-Json
if (!$taskManifest.Fixture.Equals($taskFixture,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture manifest mismatch.' }
$taskSource = Resolve-TaskWorkspacePath $taskManifest.Source
$taskCopy = Resolve-TaskWorkspacePath $taskManifest.CopyDestination
$taskMove = Resolve-TaskWorkspacePath $taskManifest.MoveDestination
$taskAllowedPaths = @($taskSource,$taskCopy,$taskMove)
foreach ($taskAllowed in $taskAllowedPaths) { if (!$taskAllowed.StartsWith($taskFixture+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escaped the fresh fixture.' } }
foreach ($taskFile in $taskManifest.Files) {
    if ($taskFile.Name -notin @('copy-source.txt','move-source.txt','rename-source.txt','multi-a.txt','multi-b.txt')) { throw 'Unexpected fixture leaf.' }
    $taskPreparedFile = Join-Path $taskSource $taskFile.Name
    if ((Get-FileHash -LiteralPath $taskPreparedFile -Algorithm SHA256).Hash -ne $taskFile.Sha256) { throw 'Fixture changed before the run; prepare a fresh fixture.' }
}
if (@(Get-ChildItem -LiteralPath $taskCopy -Force).Count -or @(Get-ChildItem -LiteralPath $taskMove -Force).Count) { throw 'Destination must still be fresh and empty.' }
if ([IO.File]::Exists($taskOutput)) {
    $taskPreviousReport = Get-Content -LiteralPath $taskOutput -Raw | ConvertFrom-Json
    if ($taskPreviousReport.Status -ne 'Pending' -or $taskPreviousReport.UiExecuted -or $taskPreviousReport.Fixture -ne $taskFixture) { throw 'Preserve completed evidence and choose a fresh report.' }
}
$taskDelivery = Resolve-TaskWorkspacePath $ExpectedDelivery
$taskPackage = Get-AppxPackage -Name FilesTurbo.Experimental
if (!$taskPackage -or !$taskPackage.InstallLocation.Equals($taskDelivery,[StringComparison]::OrdinalIgnoreCase)) { throw 'Registered experimental delivery is not the expected exact package.' }
$taskExecutable = Join-Path $taskDelivery 'Files.exe'
$taskInstances = @(Get-Process -Name Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase) })
if ($taskInstances.Count -ne 1 -or $taskInstances[0].Id -ne $ProcessId) { throw 'Exactly one path-verified experimental instance with the explicit PID is required.' }
$taskProcess = $taskInstances[0]
if ($taskProcess.MainWindowHandle -eq [IntPtr]::Zero -or $taskProcess.StartTime.ToUniversalTime() -lt (Get-Item -LiteralPath (Join-Path $taskDelivery 'Files.dll')).LastWriteTimeUtc) { throw 'The process must have launched after its exact application assembly was produced.' }
$taskConcurrentBuilds = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='MSBuild.exe'" | Where-Object { ($_.Name -eq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:') -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|\")(?:build|msbuild|publish)(?:\s|\")') })
if ($taskConcurrentBuilds.Count) { throw 'Finish all builds before running application interactions.' }
$taskPackageEvidence = [ordered]@{FullName=$taskPackage.PackageFullName;InstallLocation=$taskDelivery;ProcessId=$taskProcess.Id;ProcessStartedUtc=$taskProcess.StartTime.ToUniversalTime().ToString('o');Assemblies=@(foreach($taskAssemblyName in @('Files.dll','Files.App.Storage.dll','Files.App.CsWin32.dll')) { [pscustomobject]@{Name=$taskAssemblyName;Sha256=(Get-FileHash -LiteralPath (Join-Path $taskDelivery $taskAssemblyName) -Algorithm SHA256).Hash} })}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskProcess.MainWindowHandle)
if ($taskWindow.Current.ProcessId -ne $ProcessId) { throw 'UIA window ownership mismatch.' }
$taskWindowPattern = [Windows.Automation.WindowPattern]$taskWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { throw 'The experimental window must already be restored; this script never forces it.' }
function Get-TaskElements([string]$Id,$Root=$taskWindow) {
    $taskCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    return @($Root.FindAll([Windows.Automation.TreeScope]::Descendants,$taskCondition))
}
function Get-TaskRawMatches($Root,[string]$Id,[string[]]$Names=@()) {
    $taskCache=[Windows.Automation.CacheRequest]::new()
    $taskCache.TreeFilter=[Windows.Automation.Automation]::RawViewCondition
    $taskCache.TreeScope=[Windows.Automation.TreeScope]::Subtree
    foreach($taskProperty in @([Windows.Automation.AutomationElement]::AutomationIdProperty,[Windows.Automation.AutomationElement]::NameProperty)) {$taskCache.Add($taskProperty)}
    $taskQueue=[Collections.Generic.Queue[object]]::new()
    $taskQueue.Enqueue($Root.GetUpdatedCache($taskCache))
    $taskFound=[Collections.Generic.List[object]]::new();$taskVisits=0
    while($taskQueue.Count -and $taskVisits -lt 4096) {
        $taskItem=$taskQueue.Dequeue();$taskVisits++
        if(($Id -and $taskItem.Cached.AutomationId -eq $Id) -or ($Names.Count -and $taskItem.Cached.Name -in $Names)) {$taskFound.Add($taskItem)}
        foreach($taskChild in $taskItem.CachedChildren) {$taskQueue.Enqueue($taskChild)}
    }
    return @($taskFound)
}
function Get-TaskElement([string]$Id,$Root=$taskWindow,[switch]$Visible) {
    $taskMatches=@(Get-TaskElements $Id $Root | Where-Object { $_.Current.ProcessId -eq $ProcessId -and (!$Visible -or !$_.Current.IsOffscreen) })
    if (!$taskMatches.Count) {$taskMatches=@(Get-TaskRawMatches $Root $Id | Where-Object { $_.Current.ProcessId -eq $ProcessId -and (!$Visible -or !$_.Current.IsOffscreen) })}
    if ($taskMatches.Count -ne 1) { throw ('Pending: unique UIA control unavailable: '+$Id) }
    return $taskMatches[0]
}
function Get-TaskPattern($Element,$Pattern) {
    if ($Element.Current.ProcessId -ne $ProcessId) { throw 'UIA action owner mismatch.' }
    $taskPattern=$null
    if (!$Element.TryGetCurrentPattern($Pattern,[ref]$taskPattern)) { throw 'Pending: required UIA pattern unavailable.' }
    return $taskPattern
}
function Get-TaskCurrentPath {
    $taskGetter=Get-TaskElement 'CurrentPathGet'
    return ([Windows.Automation.ValuePattern](Get-TaskPattern $taskGetter ([Windows.Automation.ValuePattern]::Pattern))).Current.Value
}
function Assert-TaskScope([string]$Expected) {
    if ($Expected -notin $taskAllowedPaths -or !(Get-TaskCurrentPath).Equals($Expected,[StringComparison]::OrdinalIgnoreCase)) { throw 'Synthetic current-path scope is not confirmed.' }
    $taskProcess.Refresh()
    if ($taskProcess.HasExited -or $taskWindow.Current.ProcessId -ne $ProcessId) { throw 'Experimental process lifetime changed.' }
}
function Wait-TaskCondition([scriptblock]$Condition,[string]$Reason) {
    $taskWatch=[Diagnostics.Stopwatch]::StartNew()
    while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) { if (& $Condition) { return }; Start-Sleep -Milliseconds 75 }
    throw $Reason
}
function Set-TaskPath([string]$Path) {
    if ($Path -notin $taskAllowedPaths) { throw 'Navigation outside prepared fixture rejected.' }
    $taskSetter=Get-TaskElement 'CurrentPathSet'
    ([Windows.Automation.ValuePattern](Get-TaskPattern $taskSetter ([Windows.Automation.ValuePattern]::Pattern))).SetValue($Path)
    $taskActions.Add([pscustomobject]@{Action='Navigate';Control='CurrentPathSet';Pattern='ValuePattern';ExpectedPath=$Path;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o')})
    Wait-TaskCondition { (Get-TaskCurrentPath).Equals($Path,[StringComparison]::OrdinalIgnoreCase) } 'Fixture navigation did not settle.'
    Start-Sleep -Milliseconds 200
    Assert-TaskScope $Path
}
function Invoke-TaskControl([string]$Id,[string]$Path) {
    Assert-TaskScope $Path
    $taskButton=Get-TaskElement $Id -Visible
    $taskEnabledWait=[Diagnostics.Stopwatch]::StartNew()
    while (!$taskButton.Current.IsEnabled -and $taskEnabledWait.Elapsed.TotalSeconds -lt $TimeoutSeconds) {Start-Sleep -Milliseconds 75;$taskButton=Get-TaskElement $Id -Visible}
    if (!$taskButton.Current.IsEnabled) { throw ('Pending: disabled UIA action: '+$Id) }
    ([Windows.Automation.InvokePattern](Get-TaskPattern $taskButton ([Windows.Automation.InvokePattern]::Pattern))).Invoke()
    $taskActions.Add([pscustomobject]@{Action='Invoke';Control=$Id;Pattern='InvokePattern';ExpectedPath=$Path;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o')})
}
function Get-TaskSelectionRow([string]$Name,[string]$Path) {
    Assert-TaskScope $Path
    $taskList=Get-TaskElement 'FileList'
    $taskAliases=@($Name,[IO.Path]::GetFileNameWithoutExtension($Name)) | Select-Object -Unique
    $taskText=$null
    foreach ($taskAlias in $taskAliases) {
        $taskNameCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$taskAlias)
        $taskMatches=@($taskList.FindAll([Windows.Automation.TreeScope]::Descendants,$taskNameCondition) | Where-Object { $_.Current.ProcessId -eq $ProcessId })
        if ($taskMatches.Count) { $taskText=$taskMatches[0]; break }
    }
    if (!$taskText) {$taskText=@(Get-TaskRawMatches $taskList '' $taskAliases | Where-Object {$_.Current.ProcessId -eq $ProcessId -and !$_.Current.IsOffscreen}) | Select-Object -First 1}
    if (!$taskText) { throw 'Pending: synthetic file row unavailable.' }
    $taskWalker=[Windows.Automation.TreeWalker]::RawViewWalker
    $taskDepth=0
    for ($taskRow=$taskText; $taskRow -and $taskDepth -lt 8; $taskRow=$taskWalker.GetParent($taskRow)) {
        $taskDepth++
        if ($taskRow.Current.ProcessId -ne $ProcessId) { break }
        $taskCandidate=$null
        if ($taskRow.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$taskCandidate)) { return [pscustomobject]@{Element=$taskRow;Pattern=[Windows.Automation.SelectionItemPattern]$taskCandidate;Name=$Name} }
        if ([Windows.Automation.Automation]::Compare($taskRow,$taskList)) { break }
    }
    throw 'Pending: synthetic row exposes no SelectionItemPattern.'
}
function Select-TaskFile([string]$Name,[string]$Path,[switch]$Add) {
    $taskRow=Get-TaskSelectionRow $Name $Path
    if ($Add) { $taskRow.Pattern.AddToSelection() } else { $taskRow.Pattern.Select() }
    if (!$taskRow.Pattern.Current.IsSelected) { throw 'Synthetic file selection was not acknowledged.' }
    $taskActions.Add([pscustomobject]@{Action=if($Add){'AddToSelection'}else{'Select'};Control='SyntheticRow';Name=$Name;Pattern='SelectionItemPattern';ExpectedPath=$Path;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o')})
    return $taskRow
}
function Get-TaskExpectedHash([string]$Name) { return @($taskManifest.Files | Where-Object Name -eq $Name)[0].Sha256 }
function Wait-TaskFile([string]$Path,[string]$Hash,[string]$Absent='') {
    if (!$Path.StartsWith($taskFixture+'\',[StringComparison]::OrdinalIgnoreCase) -or ($Absent -and !$Absent.StartsWith($taskFixture+'\',[StringComparison]::OrdinalIgnoreCase))) { throw 'File verification escaped fixture.' }
    Wait-TaskCondition { [IO.File]::Exists($Path) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Hash -and (!$Absent -or ![IO.File]::Exists($Absent)) } 'Requested app operation did not produce the expected hash and source state.'
}
function Test-TaskScenario([string]$Name,[scriptblock]$Body) {
    $taskStarted=[DateTime]::UtcNow
    try { & $Body; $taskResults.Add([pscustomobject]@{Name=$Name;Status='Passed';StartedUtc=$taskStarted.ToString('o');CompletedUtc=[DateTime]::UtcNow.ToString('o')}) }
    catch { $taskPending=$_.Exception.Message.StartsWith('Pending:'); $taskResults.Add([pscustomobject]@{Name=$Name;Status=if($taskPending){'Pending'}else{'Failed'};Reason=if($taskPending){$_.Exception.Message}else{$_.Exception.GetType().Name};StartedUtc=$taskStarted.ToString('o');CompletedUtc=[DateTime]::UtcNow.ToString('o')}); if (!$taskPending) { throw } }
    Write-TaskReport 'Running'
}
try {
    Set-TaskPath $taskSource
    Test-TaskScenario 'single_selection' { $null=Select-TaskFile 'multi-a.txt' $taskSource }
    Test-TaskScenario 'multiselection' { $taskFirst=Select-TaskFile 'multi-a.txt' $taskSource; $taskSecond=Select-TaskFile 'multi-b.txt' $taskSource -Add; if (!$taskFirst.Pattern.Current.IsSelected -or !$taskSecond.Pattern.Current.IsSelected) { throw 'Both synthetic rows were not selected.' } }
    Test-TaskScenario 'copy_paste' {
        Set-TaskPath $taskSource; $null=Select-TaskFile 'copy-source.txt' $taskSource
        Invoke-TaskControl 'InnerNavigationToolbarCopyButton' $taskSource
        Set-TaskPath $taskCopy; Invoke-TaskControl 'InnerNavigationToolbarPasteButton' $taskCopy
        Wait-TaskFile (Join-Path $taskCopy 'copy-source.txt') (Get-TaskExpectedHash 'copy-source.txt')
        if ((Get-FileHash -LiteralPath (Join-Path $taskSource 'copy-source.txt') -Algorithm SHA256).Hash -ne (Get-TaskExpectedHash 'copy-source.txt')) { throw 'Copy altered its original fixture.' }
    }
    Test-TaskScenario 'cut_paste' {
        Set-TaskPath $taskSource; $null=Select-TaskFile 'move-source.txt' $taskSource
        Invoke-TaskControl 'InnerNavigationToolbarCutButton' $taskSource
        Set-TaskPath $taskMove; Invoke-TaskControl 'InnerNavigationToolbarPasteButton' $taskMove
        Wait-TaskFile (Join-Path $taskMove 'move-source.txt') (Get-TaskExpectedHash 'move-source.txt') (Join-Path $taskSource 'move-source.txt')
    }
    Test-TaskScenario 'rename' {
        Set-TaskPath $taskSource; $null=Select-TaskFile 'rename-source.txt' $taskSource
        Invoke-TaskControl 'InnerNavigationToolbarRenameButton' $taskSource
        $taskEditor=Get-TaskElement 'ItemNameTextBox' -Visible
        $taskConfirmFocus=Get-TaskElement 'Back' -Visible
        if ($taskConfirmFocus.Current.ControlType -ne [Windows.Automation.ControlType]::Button -or !$taskConfirmFocus.Current.IsKeyboardFocusable) { throw 'Pending: ordinary focus target for rename confirmation unavailable.' }
        Assert-TaskScope $taskSource
        ([Windows.Automation.ValuePattern](Get-TaskPattern $taskEditor ([Windows.Automation.ValuePattern]::Pattern))).SetValue('renamed-via-app.txt')
        $taskActions.Add([pscustomobject]@{Action='RenameValue';Control='ItemNameTextBox';Pattern='ValuePattern';SyntheticNewName='renamed-via-app.txt';ExpectedPath=$taskSource;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o')})
        $taskConfirmFocus.SetFocus()
        $taskActions.Add([pscustomobject]@{Action='ConfirmRenameLostFocus';Control='Back';Pattern='SetFocus';ExpectedPath=$taskSource;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o')})
        Wait-TaskFile (Join-Path $taskSource 'renamed-via-app.txt') (Get-TaskExpectedHash 'rename-source.txt') (Join-Path $taskSource 'rename-source.txt')
    }
    Test-TaskScenario 'back_forward' {
        Set-TaskPath $taskSource; Set-TaskPath $taskCopy
        Invoke-TaskControl 'Back' $taskCopy
        Wait-TaskCondition { (Get-TaskCurrentPath).Equals($taskSource,[StringComparison]::OrdinalIgnoreCase) } 'Back did not return to the source fixture.'
        Invoke-TaskControl 'Forward' $taskSource
        Wait-TaskCondition { (Get-TaskCurrentPath).Equals($taskCopy,[StringComparison]::OrdinalIgnoreCase) } 'Forward did not return to the copy fixture.'
    }
    Test-TaskScenario 'preview_toggle' {
        Set-TaskPath $taskSource; $null=Select-TaskFile 'copy-source.txt' $taskSource
        $taskPreview=Get-TaskElement 'PreviewPane' -Visible
        $taskToggle=[Windows.Automation.TogglePattern](Get-TaskPattern $taskPreview ([Windows.Automation.TogglePattern]::Pattern))
        $taskInitial=$taskToggle.Current.ToggleState
        $taskToggle.Toggle(); if ($taskToggle.Current.ToggleState -eq $taskInitial) { throw 'Preview toggle did not acknowledge its changed state.' }
        $taskToggle.Toggle(); if ($taskToggle.Current.ToggleState -ne $taskInitial) { throw 'Preview toggle did not restore its initial state.' }
        $taskActions.Add([pscustomobject]@{Action='TogglePreviewAndRestore';Control='PreviewPane';Pattern='TogglePattern';ExpectedPath=$taskSource;ProcessId=$ProcessId;Utc=[DateTime]::UtcNow.ToString('o');Limit='Toggle state verified; preview decoding/content is not verified.'})
    }
    foreach ($taskOptional in @('create_folder','new_tab','sort','layout')) { $taskResults.Add([pscustomobject]@{Name=$taskOptional;Status='Pending';Reason='No complete deterministic UIA confirmation route has been inspected at runtime; no guessed menu or keyboard action performed.'}) }
    Assert-TaskScope $taskSource
    $taskFinal=if(@($taskResults|Where-Object Status -eq 'Pending').Count){'CompletedWithPending'}else{'Completed'}
    Write-TaskReport $taskFinal
}
catch { foreach($taskRemaining in $taskScenarioNames) { if($taskRemaining -notin @($taskResults.Name)){ $taskResults.Add([pscustomobject]@{Name=$taskRemaining;Status='Pending';Reason='Run stopped before this scenario.'}) } }; Write-TaskReport 'Incomplete'; throw }
Write-Output ('Recorded app interaction results: '+$taskOutput)
