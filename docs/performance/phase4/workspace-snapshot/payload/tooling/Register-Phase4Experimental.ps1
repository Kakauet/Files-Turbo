#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase4-final07',
    [switch]$RestorePhase3,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskAllowed = Join-Path $taskRoot 'artifacts/development-layout'
$taskRecovery = Join-Path $taskAllowed 'managed-release-phase3-stable06'
if ($RestorePhase3) { $Delivery = 'artifacts/development-layout/managed-release-phase3-stable06' }

function Resolve-TaskDelivery([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path, $taskRoot)
    if (!$full.StartsWith($taskAllowed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Delivery must be in this workspace development-layout.' }
    $cursor = $full
    while ($cursor.Length -ge $taskRoot.Length) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked delivery rejected: $cursor" }
        if ($cursor -eq $taskRoot) { break }
        $cursor = Split-Path $cursor -Parent
    }
    return $full
}

function Test-TaskContent([string]$Directory) {
    $Directory = Resolve-TaskDelivery $Directory
    $validation = Get-Content -LiteralPath (Join-Path $Directory 'package-validation.json') -Raw | ConvertFrom-Json
    if ($validation.Status -ne 'content-validated-not-installed') { throw 'Complete package content validation is required.' }
    $layout = Join-Path $Directory 'layout'
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    $manifest = [xml][IO.File]::ReadAllText($manifestPath)
    if ($manifest.Package.Identity.Name -ne 'FilesTurbo.Experimental' -or $manifest.Package.Identity.Publisher -ne 'CN=Files' -or $manifest.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'Only the existing experimental x64 identity is authorized.' }
    foreach ($entry in @($validation.Inventory)) {
        $file = [IO.Path]::GetFullPath($entry.Path, $layout)
        if (!$file.StartsWith($layout + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid inventory path.' }
        $null = Resolve-TaskDelivery $file
        if ((Get-Item -LiteralPath $file).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw "Delivery hash differs: $($entry.Path)" }
    }
    foreach ($entry in @($validation.DependencyInventory)) {
        $dependencies = Join-Path $Directory 'Dependencies'
        $file = [IO.Path]::GetFullPath($entry.Path, $dependencies)
        if (!$file.StartsWith($dependencies + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid dependency path.' }
        $null = Resolve-TaskDelivery $file
        if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw 'Dependency hash differs.' }
    }
    return $manifestPath
}

$taskRecoveryManifest = Test-TaskContent $taskRecovery
$taskSnapshot = Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts/phase4-recovery/phase3-inventory.json') -Raw | ConvertFrom-Json
foreach ($entry in $taskSnapshot.Files) {
    $file = [IO.Path]::GetFullPath($entry.Path, $taskRecovery)
    if (!$file.StartsWith($taskRecovery + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid recovery path.' }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw 'Preserved Phase III recovery differs from the start snapshot.' }
}
$taskManifestPath = Test-TaskContent $Delivery
$taskPrevious = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPrevious -or $taskPrevious.Publisher -ne 'CN=Files') { throw 'An existing authorized experimental registration is required.' }
$null = Resolve-TaskDelivery $taskPrevious.InstallLocation
if ($ValidateOnly) { Write-Output 'Target and Phase III recovery content verified; registration unchanged.'; return }

$taskRecord = Join-Path $taskRoot ('artifacts/phase4-recovery/registration-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.json')
$taskResult = [ordered]@{ PreviousPackage=$taskPrevious.PackageFullName; PreviousLocation=$taskPrevious.InstallLocation; RequestedLocation=(Split-Path $taskManifestPath -Parent); RecoveryManifest=$taskRecoveryManifest; RestorePhase3=[bool]$RestorePhase3; Status='pending'; Error=$null }
$taskResult | ConvertTo-Json | Set-Content -LiteralPath $taskRecord
try {
    $taskExecutable = Join-Path $taskPrevious.InstallLocation 'Files.exe'
    foreach ($process in @(Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable, [StringComparison]::OrdinalIgnoreCase) })) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id -ErrorAction Stop }
    }
    Add-AppxPackage -Register $taskManifestPath -ForceUpdateFromAnyVersion -ErrorAction Stop
    $registered = Get-AppxPackage -Name 'FilesTurbo.Experimental'
    if ($registered.InstallLocation.TrimEnd('\') -ne (Split-Path $taskManifestPath -Parent).TrimEnd('\')) { throw 'Registration did not select the requested layout.' }
    $taskResult.Status = 'registered'
    Write-Output ('Experimental layout registered: ' + $registered.InstallLocation)
} catch {
    $taskResult.Status = 'failed'
    $taskResult.Error = $_.Exception.Message
    throw
} finally {
    $taskResult | ConvertTo-Json | Set-Content -LiteralPath $taskRecord
}
