#Requires -Version 7.2
[CmdletBinding()]
param([ValidateRange(5,60)][int]$TimeoutSeconds = 30)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskDelivery = Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase4-final07/layout'
$taskPackage = Get-AppxPackage -Name 'FilesTurbo.Experimental'
if (!$taskPackage -or !$taskPackage.InstallLocation.Equals($taskDelivery,[StringComparison]::OrdinalIgnoreCase)) { throw 'Register the verified Phase IV final07 layout first.' }
$taskManifest = [xml][IO.File]::ReadAllText((Join-Path $taskDelivery 'AppxManifest.xml'))
$taskProvenance = Get-Content -LiteralPath (Join-Path $taskRoot 'reports/phase4-build-provenance-firstvisit.json') -Raw | ConvertFrom-Json
if ($taskManifest.Package.Identity.Name -ne 'FilesTurbo.Experimental' -or $taskManifest.Package.Identity.Version -ne '4.2.39.9' -or $taskProvenance.Status -ne 'compiled-source-and-packaged-assembly-match') { throw 'Experimental identity or source provenance mismatch.' }
$taskAssembly = $taskProvenance.Assemblies | Where-Object Assembly -eq 'Files.dll'
if ((Get-FileHash -LiteralPath (Join-Path $taskDelivery 'Files.dll')).Hash -ne $taskAssembly.SHA256) { throw 'Registered assembly differs from verified build.' }
$taskExecutable = Join-Path $taskDelivery 'Files.exe'
function Get-TaskInstances { return @(Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase) }) }
if (@(Get-TaskInstances).Count -gt 1) { throw 'More than one experimental instance is running; no process was closed.' }
if (@(Get-TaskInstances).Count -eq 0) {
    $taskAppId = @($taskManifest.Package.Applications.Application)[0].Id
    Start-Process -FilePath explorer.exe -ArgumentList ('shell:AppsFolder\' + $taskPackage.PackageFamilyName + '!' + $taskAppId)
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$taskWatch = [Diagnostics.Stopwatch]::StartNew()
while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    $taskInstances = @(Get-TaskInstances)
    if ($taskInstances.Count -gt 1) { throw 'Launch produced companion experimental instances; no process was closed.' }
    if ($taskInstances.Count -eq 1) {
        $taskProcess = $taskInstances[0]
        $taskProcess.Refresh()
        if ($taskProcess.MainWindowHandle -ne [IntPtr]::Zero) {
            $taskWindow = [Windows.Automation.AutomationElement]::FromHandle($taskProcess.MainWindowHandle)
            $taskPathControl = $taskWindow.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'CurrentPathSet'))
            if ($taskPathControl -and $taskWindow.Current.ProcessId -eq $taskProcess.Id) {
                [pscustomobject]@{ ProcessId=$taskProcess.Id; WindowHandle=$taskProcess.MainWindowHandle; Package=$taskPackage.PackageFullName; InstallLocation=$taskPackage.InstallLocation; ReadyUtc=[DateTime]::UtcNow.ToString('o'); Scope='Window and navigation control ready; no physical presentation or startup timing claim.' }
                return
            }
        }
    }
    Start-Sleep -Milliseconds 100
}
throw 'The exact experimental window did not become ready within the timeout.'
