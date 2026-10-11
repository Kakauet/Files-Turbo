#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase3-verified',
    [switch]$RestorePhase2
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
if ($RestorePhase2) { $Delivery = 'artifacts/development-layout/managed-release-phase2' }
$taskDelivery = [IO.Path]::GetFullPath((Join-Path $taskRoot $Delivery))
$taskAllowed = Join-Path $taskRoot 'artifacts/development-layout'
if (!$taskDelivery.StartsWith($taskAllowed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Delivery must be inside the development-layout workspace directory.' }
$taskManifestPath = Join-Path $taskDelivery 'layout/AppxManifest.xml'
if (!(Test-Path -LiteralPath $taskManifestPath)) { throw 'The requested development layout does not exist.' }
$taskManifest = [xml][IO.File]::ReadAllText($taskManifestPath)
if ($taskManifest.Package.Identity.Name -ne 'FilesTurbo.Experimental' -or $taskManifest.Package.Identity.Publisher -ne 'CN=Files') { throw 'Only the existing experimental identity can be updated.' }
$taskValidation = Get-Content -LiteralPath (Join-Path $taskDelivery 'package-validation.json') -Raw | ConvertFrom-Json
if ($taskValidation.Status -ne 'content-validated-not-installed') { throw 'Package content validation is required before registration.' }
$taskPrevious = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPrevious) { throw 'This script updates the already authorized experimental installation; it requires an existing registration.' }
if (!$taskPrevious.InstallLocation.StartsWith($taskAllowed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The registered experimental package is outside this workspace.' }
$taskRecovery = Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase2/layout/AppxManifest.xml'
$taskReference = Get-Content -LiteralPath (Join-Path $taskRoot 'artifacts/phase3-recovery/registration-before.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $taskRecovery).Hash -ne $taskReference.Phase2ManifestSHA256) { throw 'The preserved Phase II manifest differs from the recovery reference.' }
$taskRecord = Join-Path $taskRoot ('artifacts/phase3-recovery/registration-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.json')
$taskResult = [ordered]@{ PreviousLocation=$taskPrevious.InstallLocation; RequestedLocation=(Split-Path $taskManifestPath -Parent); RestorePhase2=[bool]$RestorePhase2; Status='pending'; Error=$null }
try {
    Add-AppxPackage -Register $taskManifestPath -ForceUpdateFromAnyVersion -ForceTargetApplicationShutdown -ErrorAction Stop
    $taskRegistered = Get-AppxPackage -Name 'FilesTurbo.Experimental'
    if ($taskRegistered.InstallLocation.TrimEnd('\') -ne (Split-Path $taskManifestPath -Parent).TrimEnd('\')) { throw 'Registration did not select the requested layout.' }
    $taskResult.Status = 'registered'
    Write-Output ('Experimental layout registered: ' + $taskRegistered.InstallLocation)
} catch {
    $taskResult.Status = 'failed'
    $taskResult.Error = $_.Exception.Message
    throw
} finally {
    $taskResult | ConvertTo-Json | Set-Content -LiteralPath $taskRecord
}
