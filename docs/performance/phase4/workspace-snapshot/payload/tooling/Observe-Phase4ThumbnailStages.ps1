#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$ExpectedDelivery,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$FixtureDirectory = 'benchmarks/media-phase4',
    [ValidateRange(1,2)][int]$Visits = 2
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskPackage = Get-AppxPackage -Name FilesTurbo.Experimental
$taskLayout = [IO.Path]::GetFullPath($ExpectedDelivery, $taskRoot)
if (!$taskLayout.StartsWith((Join-Path $taskRoot 'artifacts/development-layout')+'\', [StringComparison]::OrdinalIgnoreCase) -or !$taskPackage.InstallLocation.Equals($taskLayout, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected experimental layout.' }
$taskProcess = Get-Process -Id $ProcessId
if (!$taskProcess.Path.Equals((Join-Path $taskLayout 'Files.exe'), [StringComparison]::OrdinalIgnoreCase) -or $taskProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Unexpected experimental process.' }
$taskBuilds = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -ieq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:' -or $_.Name -ieq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|\")(?:build|msbuild|publish)(?:\s|\")' })
if ($taskBuilds.Count) { throw 'Finish builds before observing runtime stages.' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory, $taskRoot)
if (!$taskOutput.StartsWith($taskRoot+'\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskOutput)) { throw 'Choose a fresh workspace output.' }
for ($taskCursor=$taskOutput; $taskCursor.Length -gt $taskRoot.Length; $taskCursor=Split-Path $taskCursor -Parent) {
    if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked output rejected.' }
}
$taskLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Packages/'+$taskPackage.PackageFamilyName+'/LocalState')
$taskConfig = Get-Content -LiteralPath (Join-Path $taskLocal 'phase3-performance.json') -Raw | ConvertFrom-Json
if (!$taskConfig.Enabled -or !$taskConfig.FixtureRoot.Equals((Join-Path $taskRoot 'benchmarks'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture-scoped diagnostics must be enabled.' }
$taskTraces = @(Get-ChildItem -LiteralPath (Join-Path $taskLocal 'Phase3Performance') -Filter ('session-*-'+$ProcessId+'-*.jsonl') | Where-Object CreationTimeUtc -ge $taskProcess.StartTime.ToUniversalTime().AddSeconds(-2))
if ($taskTraces.Count -ne 1) { throw 'Exactly one current-process diagnostic trace required.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskProcess.MainWindowHandle)
$taskWindowPattern = [Windows.Automation.WindowPattern]$taskWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized -or $taskWindow.Current.ProcessId -ne $ProcessId) { throw 'A visible verified window is required.' }
function Get-TaskControl([string]$Id) {
    return $taskWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
$taskSetter = [Windows.Automation.ValuePattern](Get-TaskControl 'CurrentPathSet').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
$taskGetter = [Windows.Automation.ValuePattern](Get-TaskControl 'CurrentPathGet').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
$taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
$taskMedia = [IO.Path]::GetFullPath($FixtureDirectory, $taskRoot)
if (!$taskMedia.StartsWith((Join-Path $taskRoot 'benchmarks')+'\',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath ($taskMedia+'-manifest.json'))) { throw 'Known benchmark fixture required.' }
if (!(Test-Path -LiteralPath $taskEmpty -PathType Container) -or !(Test-Path -LiteralPath $taskMedia -PathType Container)) { throw 'Known fixtures are unavailable.' }
$taskActions = [Collections.Generic.List[object]]::new()
$taskStarted = [DateTime]::UtcNow
for ($taskVisit=0; $taskVisit -lt $Visits; $taskVisit++) {
    foreach ($taskStep in @(@{Kind='Empty';Path=$taskEmpty;Dwell=750}, @{Kind='Media';Path=$taskMedia;Dwell=4000})) {
        $taskAt = [DateTime]::UtcNow
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $taskSetter.SetValue($taskStep.Path)
        $taskCallMs = $taskWatch.Elapsed.TotalMilliseconds
        Start-Sleep -Milliseconds $taskStep.Dwell
        if (!$taskGetter.Current.Value.Equals($taskStep.Path,[StringComparison]::OrdinalIgnoreCase)) { throw 'Known navigation did not complete.' }
        $taskActions.Add([pscustomobject]@{Visit=$taskVisit;Kind=$taskStep.Kind;RequestedUtc=$taskAt.ToString('o');CallMs=$taskCallMs;DwellMs=$taskStep.Dwell;ConfirmedUtc=[DateTime]::UtcNow.ToString('o')})
    }
}
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskStream = [IO.FileStream]::new($taskTraces[0].FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
$taskReader = [IO.StreamReader]::new($taskStream)
try { $taskTraceText = $taskReader.ReadToEnd() } finally { $taskReader.Dispose(); $taskStream.Dispose() }
[IO.File]::WriteAllText((Join-Path $taskOutput 'trace.jsonl'), $taskTraceText)
$taskMeta = [ordered]@{Status='Completed';Variant='PhaseIV';Package=$taskPackage.PackageFullName;ProcessId=$ProcessId;StartTimeUtc=$taskProcess.StartTime.ToUniversalTime().ToString('o');DiagnosticState='On';StartedUtc=$taskStarted.ToString('o');CompletedUtc=[DateTime]::UtcNow.ToString('o');FixtureDirectory=$FixtureDirectory;Visits=$Visits;Actions=@($taskActions.ToArray());ConcurrentBuild=$false;Capture=$false;ReadinessPolling=$false;Scope='Known media-fixture visits with fixed dwell; cache state uncontrolled. UIA SetValue and a single post-dwell path confirmation only. Diagnostic control, not an OFF comparison, physical presentation, or decode-blocking-frame guarantee.'}
[IO.File]::WriteAllText((Join-Path $taskOutput 'session.json'),($taskMeta|ConvertTo-Json -Depth 6)+"`r`n")
Write-Output $taskOutput
