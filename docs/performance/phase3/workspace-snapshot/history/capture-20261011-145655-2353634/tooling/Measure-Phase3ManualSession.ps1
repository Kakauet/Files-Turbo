#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Enable','Disable','Capture')][string]$Action = 'Capture',
    [ValidateRange(5,600)][int]$Seconds = 30,
    [int]$ProcessId,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskPackage = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPackage -or !$taskPackage.InstallLocation.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A workspace experimental package must already be registered.' }
$taskLocalState = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Packages/' + $taskPackage.PackageFamilyName + '/LocalState')
$taskConfig = Join-Path $taskLocalState 'phase3-performance.json'
if ($Action -eq 'Enable') {
    $null = [IO.Directory]::CreateDirectory($taskLocalState)
    [ordered]@{ Enabled = $true; FixtureRoot = Join-Path $taskRoot 'benchmarks' } | ConvertTo-Json | Set-Content -LiteralPath $taskConfig
    Write-Output 'Diagnostics enabled only for synthetic workspace fixtures. Close and reopen the experimental app before recording. Outputs contain numeric milestones, no names or paths. Disable after recording.'
    Write-Output $taskConfig
    return
}
if ($Action -eq 'Disable') {
    if ([IO.File]::Exists($taskConfig)) { Remove-Item -LiteralPath $taskConfig }
    Write-Output 'Diagnostics disabled for the next app process. Existing measurement logs are retained.'
    return
}
$taskProcesses = @(Get-Process -Name Files -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($taskPackage.InstallLocation.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (!$ProcessId -or $_.Id -eq $ProcessId)
})
if ($taskProcesses.Count -ne 1) { throw 'Specify -ProcessId for exactly one running experimental Files process.' }
$taskProcess = $taskProcesses[0]
$taskProcessStartedUtc = $taskProcess.StartTime.ToUniversalTime()
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot ('benchmarks/results/phase3/manual-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (!$taskOutput.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside the workspace.' }
if ([IO.Directory]::Exists($taskOutput)) { throw 'Use a fresh output directory.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
$taskSamples = [Collections.Generic.List[object]]::new()
$taskStartedUtc = [DateTime]::UtcNow
$taskWatch = [Diagnostics.Stopwatch]::StartNew()
Write-Output "Recording $Seconds seconds. Navigate, search, select and scroll only in workspace benchmark fixtures."
while ($taskWatch.Elapsed.TotalSeconds -lt $Seconds) {
    if ($taskProcess.HasExited) { break }
    $taskProcess.Refresh()
    $taskSamples.Add([pscustomobject]@{ ElapsedMs = $taskWatch.Elapsed.TotalMilliseconds; CPUSeconds = $taskProcess.TotalProcessorTime.TotalSeconds; WorkingSetBytes = $taskProcess.WorkingSet64; PrivateBytes = $taskProcess.PrivateMemorySize64; Handles = $taskProcess.HandleCount })
    Start-Sleep -Milliseconds 250
}
$taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'process.csv') -NoTypeInformation
$taskTraceDirectory = Join-Path $taskLocalState 'Phase3Performance'
$taskTraceFile = if ([IO.Directory]::Exists($taskTraceDirectory)) { Get-ChildItem -LiteralPath $taskTraceDirectory -Filter ('session-*-' + $taskProcess.Id + '-*.jsonl') | Sort-Object LastWriteTime -Descending | Select-Object -First 1 } else { $null }
if ($taskTraceFile) {
    if ($taskTraceFile.CreationTimeUtc -lt $taskProcessStartedUtc.AddSeconds(-2)) { throw 'Matching PID trace predates this app process; refusing reused-PID evidence.' }
    Start-Sleep -Milliseconds 300
    $taskTraceInput = [IO.FileStream]::new($taskTraceFile.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $taskTraceOutput = [IO.FileStream]::new((Join-Path $taskOutput 'trace.jsonl'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $taskTraceInput.CopyTo($taskTraceOutput) } finally { $taskTraceOutput.Dispose(); $taskTraceInput.Dispose() }
}
[ordered]@{ Package = $taskPackage.PackageFullName; ProcessId = $taskProcess.Id; StartedUtc = $taskStartedUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); Seconds = $taskWatch.Elapsed.TotalSeconds; Culture = [Globalization.CultureInfo]::CurrentCulture.Name; Samples = $taskSamples.Count; TraceFound = ($null -ne $taskTraceFile); TraceSource = if ($taskTraceFile) { $taskTraceFile.FullName } else { $null }; TraceCreatedUtc = if ($taskTraceFile) { $taskTraceFile.CreationTimeUtc.ToString('o') } else { $null }; TraceScope = 'Opt-in XAML callbacks, input delivery, dispatcher queue delay, GC counts and managed memory; names and paths omitted. Trace includes whole app process; process.csv covers only capture interval.'; PhysicalPresentationMeasured = $false; GCPausesMeasured = $false; CPUInterpretation = 'Total process CPU seconds; multicore CPU time can exceed wall time'; RetainedMemoryInterpretation = 'End private bytes/working set without forced GC; does not prove a leak'; SuggestedManualScenarios = @('Navigate between 1k/10k/50k/100k and empty fixture', 'Scroll slowly, rapidly, jump to beginning/end', 'Type exact/partial/Unicode/wildcard/no-result queries; rapidly replace query', 'Navigate away during search and enumerate', 'Repeat 20 navigation cycles; wait 10 seconds idle; sample memory', 'Select visible rows with pointer and keyboard while loading') } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'session.json')
Write-Output $taskOutput
