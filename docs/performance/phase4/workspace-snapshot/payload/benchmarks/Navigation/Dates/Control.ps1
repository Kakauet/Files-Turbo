param([string]$OutputRoot = 'benchmarks/results/phase2')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Set-Location $workspace
. ./tooling/Enter-WorkspaceEnvironment.ps1
$dotnetPath = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
foreach ($variant in 'phase1', 'phase2') {
    & (Join-Path $PSScriptRoot 'Prepare-Dates.ps1') -Variant $variant
    & $dotnetPath build benchmarks/Navigation/Dates/Dates.csproj -c Release "-p:Variant=$variant" -v:quiet -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $variant" }
}
$previousTiering = $env:DOTNET_TieredCompilation
$previousReadyToRun = $env:DOTNET_ReadyToRun
try {
    foreach ($mode in @('tiering-off', 'warm-default')) {
        $env:DOTNET_TieredCompilation = if ($mode -eq 'tiering-off') { '0' } else { '1' }
        $env:DOTNET_ReadyToRun = if ($mode -eq 'tiering-off') { '0' } else { '1' }
        $orders = @(@('phase1', 'phase2'), @('phase2', 'phase1'))
        for ($round = 0; $round -lt $orders.Count; $round++) {
            $directory = Join-Path $OutputRoot "dates-control-$mode/round-$round"
            New-Item -ItemType Directory $directory -Force | Out-Null
            foreach ($variant in $orders[$round]) {
                & $dotnetPath (Join-Path $PSScriptRoot "bin/$variant/net10.0/Dates.dll") $variant benchmarks/synthetic $directory --warmups=8 --count=50000 > (Join-Path $directory "$variant-console.log")
                if ($LASTEXITCODE -ne 0) { throw "Control failed: $mode/$variant" }
            }
        }
    }
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    $env:DOTNET_ReadyToRun = $previousReadyToRun
}
