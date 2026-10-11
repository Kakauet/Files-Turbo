#Requires -Version 7.2
[CmdletBinding()]
param([string]$OutputPath = 'reports/phase4-environment.json')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputPath))
if (!$taskOutput.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must remain inside this workspace.' }
if ([IO.File]::Exists($taskOutput)) { throw 'Use a new output filename to preserve the environment inventory.' }
$taskPackage = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if ($taskPackage -and !$taskPackage.InstallLocation.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Registered experimental package is outside the workspace.' }
$taskTools = foreach ($taskName in @('pwsh','wpr','wpa','xperf','PresentMon','dotnet','dotnet-trace','dotnet-counters','ffmpeg')) {
    $taskCommand = Get-Command $taskName -ErrorAction SilentlyContinue | Select-Object -First 1
    [pscustomobject]@{ Name = $taskName; InPath = ($null -ne $taskCommand); Path = $taskCommand.Source; FileVersion = if ($taskCommand) { (Get-Item -LiteralPath $taskCommand.Source).VersionInfo.FileVersion } else { $null } }
}
$taskTrace = Join-Path $taskRoot 'tooling/diagnostics/dotnet-trace.exe'
$taskSdk = Join-Path $taskRoot 'tooling/dotnet-10.0.401/dotnet.exe'
$taskOs = Get-CimInstance Win32_OperatingSystem
$taskSystem = Get-CimInstance Win32_ComputerSystem
$taskExecutable = if ($taskPackage) { [IO.Path]::GetFullPath((Join-Path $taskPackage.InstallLocation 'Files.exe')) } else { $null }
$taskInstances = @(Get-Process -Name Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { [pscustomobject]@{ ProcessId = $_.Id; Executable = $_.Path; StartedUtc = $_.StartTime.ToUniversalTime().ToString('o'); HasWindow = $_.MainWindowHandle -ne 0 } })
$taskLocal = if ($taskPackage) { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Packages/' + $taskPackage.PackageFamilyName + '/LocalState') } else { $null }
$taskConfig = if ($taskLocal) { Join-Path $taskLocal 'phase3-performance.json' } else { $null }
$taskUiAutomation = $false
try { Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes; $taskUiAutomation = $true } catch { }
$taskInventory = [ordered]@{
    RecordedUtc = [DateTime]::UtcNow.ToString('o')
    Workspace = $taskRoot
    Scope = 'Read-only inventory of project tooling, experimental package and hardware required for performance interpretation. No tracing, application activation, package update or UI automation actions.'
    OperatingSystem = [ordered]@{ Caption = $taskOs.Caption; Version = $taskOs.Version; BuildNumber = $taskOs.BuildNumber; Architecture = $taskOs.OSArchitecture }
    Cpu = @(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed)
    PhysicalMemoryBytes = $taskSystem.TotalPhysicalMemory
    VideoControllers = @(Get-CimInstance Win32_VideoController | Select-Object Name,CurrentRefreshRate,CurrentHorizontalResolution,CurrentVerticalResolution)
    RefreshRateInterpretation = 'CurrentRefreshRate is adapter-reported mode; it is not physical presentation timing and is not an exact window-monitor mapping.'
    Elevated = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Tools = @($taskTools)
    WorkspaceDotnetTrace = [ordered]@{ Available = [IO.File]::Exists($taskTrace); Path = $taskTrace; Version = if ([IO.File]::Exists($taskTrace)) { (& $taskTrace --version 2>&1 | Select-Object -First 1).ToString() } else { $null } }
    WorkspaceSdk = [ordered]@{ Available = [IO.File]::Exists($taskSdk); Path = $taskSdk; Sdks = if ([IO.File]::Exists($taskSdk)) { @(& $taskSdk --list-sdks 2>&1 | Select-Object -First 8 | ForEach-Object ToString) } else { @() } }
    WprStatus = if (Get-Command wpr -ErrorAction SilentlyContinue) { @(& wpr -status 2>&1 | Select-Object -First 16 | ForEach-Object ToString) } else { @() }
    WprProfiles = if (Get-Command wpr -ErrorAction SilentlyContinue) { @(& wpr -profiles 2>&1 | Select-Object -First 50 | ForEach-Object ToString) } else { @() }
    WprCaptureVerified = $false
    WprLimitation = 'Tools/profile availability alone does not prove unprivileged capture. No system-wide profile was started; no elevation will be requested.'
    UiAutomationAssembliesAvailable = $taskUiAutomation
    Package = if ($taskPackage) { $taskPackage | Select-Object Name,PackageFullName,PackageFamilyName,InstallLocation } else { $null }
    ExperimentalInstances = @($taskInstances)
    DiagnosticConfigPresent = if ($taskConfig) { [IO.File]::Exists($taskConfig) } else { $null }
    PhaseIIIRecoveryManifestPresent = [IO.File]::Exists((Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase3-stable06/layout/AppxManifest.xml'))
    PhysicalPresentationMeasured = $false
    VisualMeasurementPlan = 'Capture only verified fixture FileList bounds while experimental HWND is foreground; detect synthetic four-color thumbnail quadrants. Record observer overhead and sparse-sampling bounds separately from UIA milestones.'
}
$taskInventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskOutput -Encoding utf8
Write-Output $taskOutput
