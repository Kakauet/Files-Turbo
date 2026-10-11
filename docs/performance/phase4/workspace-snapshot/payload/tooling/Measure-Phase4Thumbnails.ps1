#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Original','PhaseIII','PhaseIV')][string]$Variant = 'PhaseIII',
    [ValidateRange(1,40)][int]$Repetitions = 10,
    [ValidateRange(2,60)][int]$TimeoutSeconds = 15,
    [ValidateRange(10,500)][int]$PollIntervalMs = 25,
    [ValidateRange(250,3000)][int]$EmptyDwellMs = 750,
    [int]$ProcessId,
    [switch]$ActivateWindow,
    [ValidateSet('Screen','Window')][string]$CaptureMode = 'Screen',
    [ValidateSet('Comparison','ObserverValidation')][string]$SessionPurpose = 'Comparison',
    [ValidateSet('Off','On')][string]$DiagnosticState = 'Off',
    [string]$FixtureDirectory = 'benchmarks/media-phase4',
    [string]$CalibrationFixtureDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-TaskPath([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $Path))
    if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'All fixtures and outputs must remain inside the workspace.' }
    $taskCursor = $taskFull
    while ($taskCursor.Length -ge $taskRoot.Length) {
        if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked measurement path rejected.' }
        if ($taskCursor -eq $taskRoot) { break }
        $taskCursor = Split-Path $taskCursor -Parent
    }
    return $taskFull
}
$taskFixture = Resolve-TaskPath $FixtureDirectory
if (!$taskFixture.StartsWith((Join-Path $taskRoot 'benchmarks') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Only benchmark fixtures may be opened.' }
$taskManifest = Get-Content -Raw -LiteralPath ($taskFixture + '-manifest.json') | ConvertFrom-Json
if (!$taskManifest.FixturePath.Equals($taskFixture,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture manifest path mismatch.' }
$taskExpectedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($taskFile in $taskManifest.Files) { $null = $taskExpectedNames.Add($taskFile.Name) }
$taskCalibrationFixture = if ($CalibrationFixtureDirectory) { Resolve-TaskPath $CalibrationFixtureDirectory } else { $taskFixture }
if (!$taskCalibrationFixture.StartsWith((Join-Path $taskRoot 'benchmarks') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Calibration must use a workspace benchmark fixture.' }
$taskCalibrationManifest = Get-Content -Raw -LiteralPath ($taskCalibrationFixture + '-manifest.json') | ConvertFrom-Json
if ($taskCalibrationManifest.Files.Count -ne $taskManifest.Files.Count -or @($taskCalibrationManifest.Files | Where-Object { !$taskExpectedNames.Contains($_.Name) }).Count) { throw 'Calibration and target fixtures must have identical manifest names/counts.' }
$taskPackage = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPackage -or !$taskPackage.InstallLocation.StartsWith($taskRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'A verified workspace experimental package is required.' }
if ($Variant -eq 'PhaseIII' -and !$taskPackage.InstallLocation.Equals((Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase3-stable06/layout'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Phase III baseline must use the preserved stable06 layout.' }
$taskExecutable = [IO.Path]::GetFullPath((Join-Path $taskPackage.InstallLocation 'Files.exe'))
$taskInstances = @(Get-Process -Name Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase) })
if ($taskInstances.Count -ne 1 -or ($ProcessId -and $taskInstances[0].Id -ne $ProcessId)) { throw 'Exactly one verified experimental process is required; no process was closed.' }
$taskProcess = $taskInstances[0]
$taskStarted = $taskProcess.StartTime.ToUniversalTime()
$taskLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Packages/' + $taskPackage.PackageFamilyName + '/LocalState')
$taskConfigPresent = [IO.File]::Exists((Join-Path $taskLocal 'phase3-performance.json'))
$taskTraceDirectory = Join-Path $taskLocal 'Phase3Performance'
$taskCurrentTraces = if ([IO.Directory]::Exists($taskTraceDirectory)) { @(Get-ChildItem -LiteralPath $taskTraceDirectory -Filter ('session-*-' + $taskProcess.Id + '-*.jsonl') | Where-Object CreationTimeUtc -ge $taskStarted.AddSeconds(-2)) } else { @() }
if ($DiagnosticState -eq 'Off' -and ($taskConfigPresent -or $taskCurrentTraces.Count)) { throw 'OFF comparisons require no config/current-process trace; restart after disabling diagnostics.' }
if ($DiagnosticState -eq 'On') {
    if (!$taskConfigPresent -or $taskCurrentTraces.Count -ne 1) { throw 'ON observation requires config and exactly one current-process trace.' }
    $taskAnchor = Get-Content -LiteralPath $taskCurrentTraces[0].FullName -TotalCount 1 | ConvertFrom-Json
    if ($taskAnchor.Milestone -ne 'SessionStarted' -or $taskAnchor.UtcTicks -le 0) { throw 'The current-process diagnostic UTC anchor is unavailable.' }
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing.Common
if ($CaptureMode -eq 'Window') {
    $taskCaptureLibrary = Join-Path $taskRoot 'benchmarks/Phase4CaptureInterop/bin/Release/net10.0-windows10.0.26100.0/Phase4CaptureInterop.dll'
    if (![IO.File]::Exists($taskCaptureLibrary)) { throw 'Build the test-only Phase4CaptureInterop project before window-surface capture.' }
    $null = [Reflection.Assembly]::LoadFrom($taskCaptureLibrary)
}
$taskNativeAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $taskPackage.InstallLocation 'Files.App.CsWin32.dll'))
if (!('Phase4ThumbnailPixels' -as [type])) {
$taskReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) + @([Drawing.Bitmap].Assembly.Location)
Add-Type -ReferencedAssemblies $taskReferences -CompilerOptions '/unsafe' -TypeDefinition @'
using System;
using System.Drawing;
using System.Reflection;
public static class Phase4ThumbnailPixels
{
    public static unsafe IntPtr Foreground(Assembly assembly)
    {
        var api = assembly.GetType("Windows.Win32.PInvoke", true);
        var call = api.GetMethod("GetForegroundWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var handle = call.Invoke(null, null);
        var field = handle.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return new IntPtr(Pointer.Unbox(field.GetValue(handle)));
    }
    public static void Activate(Assembly assembly, IntPtr window)
    {
        var handleType = assembly.GetType("Windows.Win32.Foundation.HWND", true);
        var conversion = handleType.GetMethod("op_Explicit", BindingFlags.Static | BindingFlags.Public, null, new[]{typeof(IntPtr)}, null);
        var handle = conversion.Invoke(null, new object[]{window});
        var api = assembly.GetType("Windows.Win32.PInvoke", true);
        var call = api.GetMethod("SetForegroundWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[]{handleType}, null);
        call.Invoke(null, new[]{handle});
    }
    public static double Match(Bitmap image, Rectangle rectangle)
    {
        if (rectangle.Width < 12 || rectangle.Height < 12 || !new Rectangle(0,0,image.Width,image.Height).Contains(rectangle)) return 0;
        int[,] colors = {{220,30,60},{25,195,70},{40,80,220},{225,205,25}};
        int correct = 0, total = 0;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            for (int yy = 2; yy <= 7; yy++) for (int xx = 2; xx <= 7; xx++)
            {
                double x = (quadrant % 2) * 0.5 + xx / 20.0;
                double y = (quadrant / 2) * 0.5 + yy / 20.0;
                var color = image.GetPixel(rectangle.X + Math.Min(rectangle.Width-1,(int)(rectangle.Width*x)),rectangle.Y + Math.Min(rectangle.Height-1,(int)(rectangle.Height*y)));
                if (Math.Abs(color.R-colors[quadrant,0]) <= 55 && Math.Abs(color.G-colors[quadrant,1]) <= 55 && Math.Abs(color.B-colors[quadrant,2]) <= 55) correct++;
                total++;
            }
        }
        return correct/(double)total;
    }
}
'@
}
$taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskProcess.MainWindowHandle)
if ($taskWindow.Current.ProcessId -ne $taskProcess.Id) { throw 'Window process ownership mismatch.' }
$taskWindowPattern = [Windows.Automation.WindowPattern]$taskWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { $taskWindowPattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal); Start-Sleep -Milliseconds 300 }
function Get-TaskElement([string]$Id) {
    return $taskWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
$taskGetter = [Windows.Automation.ValuePattern](Get-TaskElement 'CurrentPathGet').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
$taskSetter = [Windows.Automation.ValuePattern](Get-TaskElement 'CurrentPathSet').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
$taskEmpty = Join-Path $taskRoot 'benchmarks/ui-empty-phase3'
if (![IO.Directory]::Exists($taskEmpty) -or @([IO.Directory]::EnumerateFileSystemEntries($taskEmpty)).Count) { throw 'The preserved empty transition fixture must exist and be empty.' }
$taskOutput = if ($OutputDirectory) { Resolve-TaskPath $OutputDirectory } else { Join-Path $taskRoot ('benchmarks/results/phase4/thumbnails-' + $Variant + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)) }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a fresh output directory.' }
$null = [IO.Directory]::CreateDirectory($taskOutput)
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $taskOutput 'observer-script.ps1')
$taskSamples = [Collections.Generic.List[object]]::new()
$taskPolling = [Collections.Generic.List[object]]::new()
$taskRectangles = [Collections.Generic.List[object]]::new()
$taskSessionStart = [DateTime]::UtcNow
$taskStatus = 'Incomplete'
$taskBuildChecks = [Collections.Generic.List[object]]::new()
$script:taskConcurrentBuild = $false
function Assert-TaskNoConcurrentBuild {
    $taskBuilds = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='MSBuild.exe'" | Where-Object {
        ($_.Name -eq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:') -or
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|\")(?:build|msbuild|publish)(?:\s|\")')
    })
    $taskBuildChecks.Add([pscustomobject]@{ CheckedUtc = [DateTime]::UtcNow.ToString('o'); ActiveBuildCount = $taskBuilds.Count; ProcessIds = @($taskBuilds | ForEach-Object ProcessId) })
    if ($taskBuilds.Count) {
        $script:taskConcurrentBuild = $true
        if ($SessionPurpose -eq 'Comparison') { throw 'An active build was detected; comparative timing is invalid and this session is retained incomplete.' }
    }
}
function Assert-TaskForeground {
    $taskProcess.Refresh()
    if ($taskProcess.HasExited -or !$taskProcess.StartTime.ToUniversalTime().Equals($taskStarted) -or !$taskProcess.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase)) { throw 'Experimental process identity changed.' }
    if ($CaptureMode -eq 'Screen' -and [Phase4ThumbnailPixels]::Foreground($taskNativeAssembly) -ne $taskProcess.MainWindowHandle) { throw 'Experimental HWND is not foreground; capture stopped before reading screen pixels.' }
    if ($taskWindowPattern.Current.WindowVisualState -eq [Windows.Automation.WindowVisualState]::Minimized) { throw 'Window minimized during visual observation.' }
}
function Assert-TaskNoDialog {
    $taskPrimary = Get-TaskElement 'PrimaryButton'
    if ($taskPrimary -and !$taskPrimary.Current.IsOffscreen -and !$taskPrimary.Current.BoundingRectangle.IsEmpty) { throw 'A ContentDialog is visible; real thumbnails may be dimmed and this comparison is invalid.' }
}
function Get-TaskVisiblePictures([Windows.Automation.AutomationElement]$List, $Bounds) {
    $taskRows = [Collections.Generic.List[object]]::new()
    $taskPictures = $List.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'Picture'))
    foreach ($taskPicture in $taskPictures) {
        $taskCurrent = $taskPicture.Current
        $taskRect = $taskCurrent.BoundingRectangle
        if ($taskCurrent.ProcessId -ne $taskProcess.Id -or $taskCurrent.IsOffscreen -or $taskRect.Width -lt 12 -or $taskRect.Height -lt 12 -or !$Bounds.Contains($taskRect)) { continue }
        $taskAncestor = $taskPicture
        $taskName = $null
        for ($taskDepth = 0; $taskDepth -lt 8 -and $taskAncestor -and $taskAncestor -ne $List; $taskDepth++) {
            $taskNameElement = $taskAncestor.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ItemName'))
            if ($taskNameElement -and $taskExpectedNames.Contains($taskNameElement.Current.Name)) { $taskName = $taskNameElement.Current.Name; break }
            $taskAncestor = [Windows.Automation.TreeWalker]::RawViewWalker.GetParent($taskAncestor)
            if ($taskAncestor -and $taskAncestor.Current.ProcessId -ne $taskProcess.Id) { throw 'Picture ancestry crossed process boundary.' }
        }
        if ($taskName) { $taskRows.Add([pscustomobject]@{ Name = $taskName; Bounds = $taskRect }) }
        if ($taskRows.Count -ge 128) { break }
    }
    return ,$taskRows
}
try {
    Assert-TaskNoConcurrentBuild
    if ($ActivateWindow) { [Phase4ThumbnailPixels]::Activate($taskNativeAssembly,$taskProcess.MainWindowHandle); Start-Sleep -Milliseconds 300 }
    Assert-TaskForeground
    Assert-TaskNoDialog
    $taskSetter.SetValue($taskCalibrationFixture)
    $taskCalibrationWatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $taskCalibrationCount = Get-TaskElement 'DirectoryItemCount'
        if ($taskGetter.Current.Value.Equals($taskCalibrationFixture,[StringComparison]::OrdinalIgnoreCase) -and $taskCalibrationCount -and [regex]::Replace($taskCalibrationCount.Current.Name,'\D','') -eq [string]$taskCalibrationManifest.Files.Count) { break }
        Start-Sleep -Milliseconds 50
    } while ($taskCalibrationWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    if ($taskCalibrationWatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Fixture calibration did not reach its manifest count.' }
    Start-Sleep -Milliseconds 5000
    $taskCalibrationList = Get-TaskElement 'FileList'
    if (!$taskCalibrationList) { throw 'FileList is unavailable for viewport calibration.' }
    $taskCalibrationBounds = $taskCalibrationList.Current.BoundingRectangle
    $taskViewportPictures = Get-TaskVisiblePictures $taskCalibrationList $taskCalibrationBounds
    if (!$taskViewportPictures.Count) { throw 'No manifest-associated Picture rectangles are fully inside the calibration viewport.' }
    $taskViewportPictures | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Bounds = $_.Bounds.ToString() } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOutput 'calibration-viewport.json')
    for ($taskRepeat = 0; $taskRepeat -lt $Repetitions; $taskRepeat++) {
        Assert-TaskNoConcurrentBuild
        $taskSetter.SetValue($taskEmpty)
        $taskAway = [Diagnostics.Stopwatch]::StartNew()
        do {
            Assert-TaskForeground
            $taskCount = Get-TaskElement 'DirectoryItemCount'
            if ($taskGetter.Current.Value.Equals($taskEmpty,[StringComparison]::OrdinalIgnoreCase) -and $taskCount -and [regex]::Replace($taskCount.Current.Name,'\D','') -eq '0') { break }
            Start-Sleep -Milliseconds 25
        } while ($taskAway.Elapsed.TotalSeconds -lt $TimeoutSeconds)
        if ($taskAway.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Empty transition did not stabilize.' }
        Start-Sleep -Milliseconds $EmptyDwellMs
        Assert-TaskForeground
        Assert-TaskNoDialog
        $taskRequestUtc = [DateTime]::UtcNow
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $taskSetter.SetValue($taskFixture)
        $taskSetMs = $taskWatch.Elapsed.TotalMilliseconds
        $taskList = $null; $taskFirst = $null; $taskFull = $null; $taskPreviousPollMs = 0.0; $taskFirstLower = $null; $taskFullLower = $null
        $taskExpected = $taskViewportPictures.Count; $taskObserved = 0; $taskPoll = 0
        while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            Assert-TaskForeground
            $taskPoll++
            $taskPollStart = $taskWatch.Elapsed.TotalMilliseconds
            $taskQueryWatch = [Diagnostics.Stopwatch]::StartNew()
            if (!$taskGetter.Current.Value.Equals($taskFixture,[StringComparison]::OrdinalIgnoreCase)) { Start-Sleep -Milliseconds $PollIntervalMs; continue }
            if (!$taskList) { $taskList = Get-TaskElement 'FileList'; if (!$taskList) { Start-Sleep -Milliseconds $PollIntervalMs; continue } }
            $taskBounds = $taskList.Current.BoundingRectangle
            if ($taskBounds.IsEmpty -or !$taskWindow.Current.BoundingRectangle.Contains($taskBounds)) { throw 'FileList crop is outside its verified experimental window.' }
            if (!$taskBounds.Equals($taskCalibrationBounds)) { Start-Sleep -Milliseconds $PollIntervalMs; $taskList = $null; continue }
            $taskPictures = $taskViewportPictures
            $taskQueryMs = $taskQueryWatch.Elapsed.TotalMilliseconds
            if (!$taskPictures.Count) { $taskPreviousPollMs = $taskWatch.Elapsed.TotalMilliseconds; Start-Sleep -Milliseconds $PollIntervalMs; continue }
            $taskLeft = [int][Math]::Ceiling($taskBounds.Left); $taskTop = [int][Math]::Ceiling($taskBounds.Top)
            $taskWidth = [int][Math]::Floor($taskBounds.Right) - $taskLeft; $taskHeight = [int][Math]::Floor($taskBounds.Bottom) - $taskTop
            $taskCaptureWatch = [Diagnostics.Stopwatch]::StartNew()
            $taskCaptureStartedMs = $taskWatch.Elapsed.TotalMilliseconds
            Assert-TaskForeground
            if ($CaptureMode -eq 'Screen') {
                $taskBitmap = [Drawing.Bitmap]::new($taskWidth,$taskHeight)
                $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
                try { $taskGraphics.CopyFromScreen($taskLeft,$taskTop,0,0,[Drawing.Size]::new($taskWidth,$taskHeight),[Drawing.CopyPixelOperation]::SourceCopy) } finally { $taskGraphics.Dispose() }
            } else {
                $taskWindowBounds = $taskWindow.Current.BoundingRectangle
                $taskBitmap = [Phase4WindowCapture]::Capture($taskProcess.MainWindowHandle,[Drawing.Size]::new([int]$taskWindowBounds.Width,[int]$taskWindowBounds.Height),[Drawing.Rectangle]::new([int]($taskLeft-$taskWindowBounds.Left),[int]($taskTop-$taskWindowBounds.Top),$taskWidth,$taskHeight))
            }
            $taskCaptureMs = $taskCaptureWatch.Elapsed.TotalMilliseconds
            $taskCapturedMs = $taskWatch.Elapsed.TotalMilliseconds
            try {
                Assert-TaskForeground
                $taskDetectWatch = [Diagnostics.Stopwatch]::StartNew()
                $taskObserved = 0
                foreach ($taskPicture in $taskPictures) {
                    $taskRect = $taskPicture.Bounds
                    $taskCrop = [Drawing.Rectangle]::new([int][Math]::Round($taskRect.Left-$taskLeft),[int][Math]::Round($taskRect.Top-$taskTop),[int][Math]::Round($taskRect.Width),[int][Math]::Round($taskRect.Height))
                    $taskScore = [Phase4ThumbnailPixels]::Match($taskBitmap,$taskCrop)
                    if ($taskScore -ge 0.80) { $taskObserved++ }
                    $taskRectangles.Add([pscustomobject]@{ Repetition = $taskRepeat; Poll = $taskPoll; Fixture = $taskPicture.Name; X = $taskCrop.X; Y = $taskCrop.Y; Width = $taskCrop.Width; Height = $taskCrop.Height; MatchFraction = $taskScore; Detected = ($taskScore -ge 0.80) })
                }
                $taskDetectMs = $taskDetectWatch.Elapsed.TotalMilliseconds
                $taskObservedMs = $taskWatch.Elapsed.TotalMilliseconds
                if ($taskObserved -gt 0 -and $null -eq $taskFirst) { $taskFirst = $taskCapturedMs; $taskFirstLower = $taskPreviousPollMs; $taskBitmap.Save((Join-Path $taskOutput ('sample-{0:D2}-first.png' -f $taskRepeat)),[Drawing.Imaging.ImageFormat]::Png) }
                $taskPolling.Add([pscustomobject]@{ Repetition = $taskRepeat; Poll = $taskPoll; PollStartedMs = $taskPollStart; CaptureStartedMs = $taskCaptureStartedMs; CaptureCompletedMs = $taskCapturedMs; ClassificationCompletedMs = $taskObservedMs; QueryMs = $taskQueryMs; CaptureMs = $taskCaptureMs; DetectorMs = $taskDetectMs; VisiblePictures = $taskExpected; DetectedRealThumbnails = $taskObserved; SnapshotTakenUtc = [DateTime]::UtcNow.ToString('o') })
                if ($taskObserved -eq $taskExpected) { $taskFull = $taskCapturedMs; $taskFullLower = $taskPreviousPollMs; $taskBitmap.Save((Join-Path $taskOutput ('sample-{0:D2}-full.png' -f $taskRepeat)),[Drawing.Imaging.ImageFormat]::Png); break }
                $taskPreviousPollMs = $taskCaptureStartedMs
                if ($taskWatch.Elapsed.TotalSeconds + $PollIntervalMs/1000.0 -ge $TimeoutSeconds) { $taskBitmap.Save((Join-Path $taskOutput ('sample-{0:D2}-timeout.png' -f $taskRepeat)),[Drawing.Imaging.ImageFormat]::Png) }
            } finally { $taskBitmap.Dispose() }
            Start-Sleep -Milliseconds $PollIntervalMs
        }
        $taskProcess.Refresh()
        Assert-TaskNoDialog
        Assert-TaskNoConcurrentBuild
        $taskSamples.Add([pscustomobject]@{ Variant = $Variant; Repetition = $taskRepeat; StartedUtc = $taskRequestUtc.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); Metric = $CaptureMode + 'Sample_RealPatternThumbnail'; FirstDetectedMs = $taskFirst; FirstDetectionLowerBoundMs = $taskFirstLower; ViewportAllDetectedMs = $taskFull; ViewportDetectionLowerBoundMs = $taskFullLower; SetValueCallMs = $taskSetMs; VisiblePictures = $taskExpected; DetectedRealThumbnails = $taskObserved; Polls = $taskPoll; TimedOut = ($null -eq $taskFull); PrivateBytes = $taskProcess.PrivateMemorySize64; WorkingSetBytes = $taskProcess.WorkingSet64; WindowBounds = $taskWindow.Current.BoundingRectangle.ToString() })
        $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
        Start-Sleep -Milliseconds 500
    }
    $taskStatus = if (@($taskSamples | Where-Object TimedOut).Count) { 'CompletedWithTimeouts' } else { 'Completed' }
} catch {
    if ($CaptureMode -eq 'Window' -and $taskCalibrationBounds -and !$taskWindow.Current.BoundingRectangle.IsEmpty -and $taskWindow.Current.BoundingRectangle.Contains($taskCalibrationBounds) -and $taskGetter.Current.Value.StartsWith((Join-Path $taskRoot 'benchmarks') + '\',[StringComparison]::OrdinalIgnoreCase)) {
        try {
            $taskFaultWindow = $taskWindow.Current.BoundingRectangle
            $taskFaultBitmap = [Phase4WindowCapture]::Capture($taskProcess.MainWindowHandle,[Drawing.Size]::new([int]$taskFaultWindow.Width,[int]$taskFaultWindow.Height),[Drawing.Rectangle]::new([int]($taskCalibrationBounds.Left-$taskFaultWindow.Left),[int]($taskCalibrationBounds.Top-$taskFaultWindow.Top),[int]$taskCalibrationBounds.Width,[int]$taskCalibrationBounds.Height))
            try { $taskFaultBitmap.Save((Join-Path $taskOutput 'failure-filelist.png'),[Drawing.Imaging.ImageFormat]::Png) } finally { $taskFaultBitmap.Dispose() }
        } catch { }
    }
    [ordered]@{ FailedUtc = [DateTime]::UtcNow.ToString('o'); Message = $_.Exception.Message; ScriptLine = $_.InvocationInfo.ScriptLineNumber } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'failure.json')
    throw
} finally {
    $taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput 'samples.csv') -NoTypeInformation
    $taskPolling | Export-Csv -LiteralPath (Join-Path $taskOutput 'polls.csv') -NoTypeInformation
    $taskRectangles | Export-Csv -LiteralPath (Join-Path $taskOutput 'picture-detection.csv') -NoTypeInformation
    [ordered]@{ ConcurrentBuildObserved = $script:taskConcurrentBuild; Checks = @($taskBuildChecks); Scope = 'Active MSBuild excluding node servers, and dotnet build/msbuild/publish; snapshots before/after repetitions, not a complete CPU-isolation proof' } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'concurrent-build-checks.json')
    [ordered]@{ Status = $taskStatus; Variant = $Variant; CaptureMode = $CaptureMode; SessionPurpose = $SessionPurpose; DiagnosticState = $DiagnosticState; ConcurrentBuild = $script:taskConcurrentBuild; Package = $taskPackage.PackageFullName; InstallLocation = $taskPackage.InstallLocation; ProcessId = $taskProcess.Id; ProcessStartedUtc = $taskStarted.ToString('o'); StartedUtc = $taskSessionStart.ToString('o'); CompletedUtc = [DateTime]::UtcNow.ToString('o'); Culture = [Globalization.CultureInfo]::CurrentCulture.Name; FixtureManifest = $taskFixture + '-manifest.json'; CalibrationFixture = $taskCalibrationFixture; TargetOpenedDuringCalibration = $taskCalibrationFixture.Equals($taskFixture,[StringComparison]::OrdinalIgnoreCase); Repetitions = $Repetitions; RequestedPollIntervalMs = $PollIntervalMs; DiagnosticsStateEvidence = if ($DiagnosticState -eq 'On') { 'Configuration present and exactly one current-process trace with SessionStarted UTC anchor' } else { 'Configuration absent and no current-process Phase3 trace; app was restarted after disabling' }; CacheState = 'Warm/uncontrolled; no global or app cache flush'; Crop = 'FileList bounds contained in verified experimental UIA HWND; exact PID/path/starttime checked. Screen mode requires foreground; Window uses PrintWindow and exposes only FileList crop after disposing the internal window surface.'; Detector = 'At least 80% of 144 interior points match expected RGB quadrant colors within tolerance 55; generic icon source changes are not treated as thumbnails'; Timing = 'Stopwatch from before ValuePattern.SetValue to completed capture whose buffer matches; lower bound is previous capture start or zero. These observer intervals are not physical presentation bounds.'; Observer = 'polls.csv retains UIA query, capture and detector durations; sampling and image-save overhead are part of observation; no overhead subtraction'; PhysicalPresentationMeasured = $false; Evidence = 'Pattern pixels confirm real synthetic thumbnail content in captured buffer. Window mode requests PrintWindow rendering and can perturb UI work; it does not demonstrate unoccluded desktop pixels or physical frames.'; ViewportDefinition = 'Manifest-associated fully contained Picture rectangles calibrated outside timing after full count+5s; timed samples reuse their geometry and require identical FileList bounds'; VisibilityLimitation = 'Screen mode refuses foreign foreground HWND; Window capture excludes unrelated window content but is an observation of the app surface, not current monitor presentation'; NativeInterop = 'Existing CsWin32 foreground APIs via offline reflection; test-only Phase4CaptureInterop generates PrintWindow, no application-native declarations changed' } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'session.json') -Encoding utf8
}
Write-Output $taskOutput
