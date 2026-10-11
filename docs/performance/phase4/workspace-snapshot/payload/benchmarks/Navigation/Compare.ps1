param([string]$OutputDirectory = 'benchmarks/results/phase2/navigation-final-verified', [switch]$UseMeasuredSnapshot)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $workspace
. ./tooling/Enter-WorkspaceEnvironment.ps1
$dotnetPath = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
foreach ($variant in @('baseline', 'phase1', 'phase2')) {
    $extraArguments = if ($UseMeasuredSnapshot -and $variant -eq 'phase2') { @('-p:EnumeratorFile=phase2-measured/UniversalStorageEnumerator.cs') } else { @() }
    & $dotnetPath build benchmarks/Navigation/Navigation.csproj -c Release "-p:Variant=$variant" -v:quiet -clp:ErrorsOnly @extraArguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $variant" }
}
$orders = @(@('baseline', 'phase1', 'phase2'), @('phase2', 'phase1', 'baseline'))
for ($round = 0; $round -lt $orders.Count; $round++) {
    $roundDirectory = Join-Path $OutputDirectory "round-$round"
    New-Item -ItemType Directory $roundDirectory -Force | Out-Null
    foreach ($variant in $orders[$round]) {
        $assemblyPath = Join-Path $PSScriptRoot "bin/$variant/net10.0/Navigation.dll"
        & $dotnetPath $assemblyPath $variant benchmarks/synthetic $roundDirectory > (Join-Path $roundDirectory "$variant-console.log")
        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $variant" }
    }
}
