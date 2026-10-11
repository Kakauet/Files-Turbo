param([string]$RunName = 'controlled')
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid run name.' }
. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$taskResults = Join-Path $taskRoot "artifacts\$RunName"
if (Test-Path -LiteralPath $taskResults) { throw 'Result directory already exists; use a new RunName to preserve measurements.' }
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
foreach ($taskVariant in @('baseline', 'turbo')) {
    foreach ($taskProject in @('Collections', 'IconCache')) {
        $taskLog = Join-Path $taskResults "$taskProject-$taskVariant-build.log"
        & $taskDotnet build "benchmarks/$taskProject/$taskProject.csproj" -c Release "-p:Variant=$taskVariant" -v:quiet -clp:ErrorsOnly *> $taskLog
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $taskLog -Tail 30; throw "$taskProject $taskVariant build failed." }
    }
}
# Repeat in reverse order to expose changes in the machine's state.
for ($taskPass = 1; $taskPass -le 2; $taskPass++) {
    $taskVariants = if ($taskPass -eq 1) { @('baseline', 'turbo') } else { @('turbo', 'baseline') }
    $taskPassRoot = Join-Path $taskResults "pass-$taskPass"
    New-Item -ItemType Directory -Path $taskPassRoot -Force | Out-Null
    foreach ($taskVariant in $taskVariants) {
        $taskCollectionsDll = "benchmarks/Collections/bin/$taskVariant/net10.0/Collections.dll"
        & $taskDotnet $taskCollectionsDll --tests (Join-Path $taskPassRoot "collections-$taskVariant-tests.json")
        if ($LASTEXITCODE -ne 0) { throw "Collection tests failed: $taskVariant" }
        & $taskDotnet $taskCollectionsDll --benchmark (Join-Path $taskPassRoot "collections-$taskVariant.csv")
        if ($LASTEXITCODE -ne 0) { throw "Collection benchmark failed: $taskVariant" }
        $taskIconsDll = "benchmarks/IconCache/bin/$taskVariant/Release/net10.0/FilesTurbo.IconCache.$taskVariant.dll"
        & $taskDotnet $taskIconsDll benchmark $taskVariant $taskPassRoot 31
        if ($LASTEXITCODE -ne 0) { throw "Icon cache benchmark/tests failed: $taskVariant" }
    }
}
[ordered]@{
    scope = 'Internal real source; UI and shell dependencies replaced by documented test doubles'
    sdk = (& $taskDotnet --version)
    tieredCompilation = $env:DOTNET_TieredCompilation
    order = @('baseline', 'turbo', 'turbo', 'baseline')
    collectionsRepetitionsPerPass = 15
    iconsRepetitionsPerPass = 31
    filesystemCache = 'Not measured; in-memory component benchmarks'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'run-metadata.json')
