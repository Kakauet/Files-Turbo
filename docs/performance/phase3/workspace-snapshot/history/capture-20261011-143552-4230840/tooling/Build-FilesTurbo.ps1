param([switch]$Baseline, [switch]$Package, [switch]$Managed, [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug', [string]$OutputName)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$taskSource = if ($Baseline) { 'source-baseline' } else { 'source' }
$taskName = if ($Baseline) { 'baseline' } else { 'turbo' }
if ($Managed) { $taskName += '-managed-' + $Configuration.ToLowerInvariant() }
if ($OutputName) {
    if ($OutputName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid output name.' }
    $taskName = $OutputName
    if (Test-Path -LiteralPath "reports/$taskName-final-build.log") { throw 'Output already exists; use a fresh name to preserve evidence.' }
}
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$taskProperties = @("-p:Configuration=$Configuration", '-p:Platform=x64', '-p:RuntimeIdentifiers=win-x64', '-p:RuntimeIdentifier=win-x64', '-p:SentryUploadSymbols=false')
if ($Managed) { $taskProperties += @('-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false', '-p:PublishReadyToRunComposite=false', '-p:SelfContained=false') }
& $taskDotnet restore "$taskSource/src/Files.App.Server/Files.App.Server.csproj" @taskProperties -v:quiet *> "reports/$taskName-server-restore-final.log"
if ($LASTEXITCODE -ne 0) { throw 'App.Server restore failed; see reports.' }
$taskPackageProperties = @()
if ($Package) {
    $taskPackageDir = Join-Path $taskRoot "artifacts\$taskName-package\"
    New-Item -ItemType Directory -Path $taskPackageDir -Force | Out-Null
    $taskPackageProperties = @('-p:GenerateAppxPackageOnBuild=true', '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never', '-p:UapAppxPackageBuildMode=SideloadOnly', "-p:AppxPackageDir=$taskPackageDir")
}
& $taskDotnet build "$taskSource/src/Files.App/Files.App.csproj" @taskProperties @taskPackageProperties -v:quiet -clp:ErrorsOnly "-flp:logfile=reports\$taskName-final-build.log;verbosity=normal" *> "reports/$taskName-final-console.log"
$taskExit = $LASTEXITCODE
Get-Content -LiteralPath "reports/$taskName-final-console.log" -Tail 24
if ($taskExit -ne 0) { throw "$taskName app build failed; see reports/$taskName-final-build.log" }
Write-Output "$taskName app project compiled; native C++ solution helpers and runtime behavior need separate verification. No registration or execution performed."
