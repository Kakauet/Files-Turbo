param([string]$OutputDirectory = 'benchmarks/results/phase2/dates-comparison')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Set-Location $workspace
. ./tooling/Enter-WorkspaceEnvironment.ps1
$dotnetPath = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
foreach ($variant in @('baseline', 'phase1', 'phase2')) {
    & (Join-Path $PSScriptRoot 'Prepare-Dates.ps1') -Variant $variant
    & $dotnetPath build benchmarks/Navigation/Dates/Dates.csproj -c Release "-p:Variant=$variant" -v:quiet -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $variant" }
}
$orders = @(@('baseline', 'phase1', 'phase2'), @('phase2', 'phase1', 'baseline'))
for ($round = 0; $round -lt $orders.Count; $round++) {
    $roundDirectory = Join-Path $OutputDirectory "round-$round"
    New-Item -ItemType Directory $roundDirectory -Force | Out-Null
    foreach ($variant in $orders[$round]) {
        & $dotnetPath (Join-Path $PSScriptRoot "bin/$variant/net10.0/Dates.dll") $variant benchmarks/synthetic $roundDirectory > (Join-Path $roundDirectory "$variant-console.log")
        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $variant" }
    }
}
