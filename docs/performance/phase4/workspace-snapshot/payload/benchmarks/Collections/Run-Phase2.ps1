param([string]$RunName = 'verified')
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid run name.' }
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $taskRoot 'tooling/Enter-WorkspaceEnvironment.ps1')
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$taskOutput = Join-Path $taskRoot "artifacts/phase2-collections/$RunName"
if (Test-Path -LiteralPath $taskOutput) { throw 'Results already exist; choose a new RunName.' }
New-Item -ItemType Directory -Path $taskOutput | Out-Null
Set-Location -LiteralPath $taskRoot
$taskSources = @()
foreach ($taskVariant in @('baseline', 'phase1', 'turbo')) {
    & $taskDotnet build benchmarks/Collections/Collections.csproj -c Release "-p:Variant=$taskVariant" -v:quiet -clp:ErrorsOnly *> (Join-Path $taskOutput "$taskVariant-build.log")
    if ($LASTEXITCODE -ne 0) { throw "$taskVariant build failed." }
    $taskDll = "benchmarks/Collections/bin/$taskVariant/net10.0/Collections.dll"
    & $taskDotnet $taskDll --tests (Join-Path $taskOutput "$taskVariant-tests.json") *> (Join-Path $taskOutput "$taskVariant-tests.log")
    if ($LASTEXITCODE -ne 0) { throw "$taskVariant contracts failed." }
    if ($taskVariant -ne 'turbo') {
        & $taskDotnet $taskDll --contracts (Join-Path $taskOutput "$taskVariant-defect-probes.json") *> (Join-Path $taskOutput "$taskVariant-defect-probes.log")
        # Old-source failures are recorded defects, not validation of the new implementation.
        if ($LASTEXITCODE -notin @(0, 1)) { throw "$taskVariant defect probes crashed." }
    }
    $taskSource = switch ($taskVariant) {
        baseline { 'benchmarks/baseline/Collections/src/Files.App/Utils/Storage/Collection/BulkConcurrentObservableCollection.cs' }
        phase1 { 'artifacts/phase2-collections/phase1-source/src/Files.App/Utils/Storage/Collection/BulkConcurrentObservableCollection.cs' }
        turbo { 'source/src/Files.App/Utils/Storage/Collection/BulkConcurrentObservableCollection.cs' }
    }
    $taskSources += [ordered]@{ variant = $taskVariant; source = $taskSource; sha256 = (Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash }
}
$taskScenarios = @('normal_add_list', 'normal_add_iterator', 'bulk_replace_list', 'bulk_insert_list', 'bulk_add_iterator', 'normal_insert_list', 'normal_replace_iterator')
foreach ($taskReference in @('baseline', 'phase1')) {
    $taskPairOutput = Join-Path $taskOutput "$taskReference-vs-phase2"
    New-Item -ItemType Directory -Path $taskPairOutput | Out-Null
    $taskReferenceRows = @()
    $taskPhase2Rows = @()
    $taskPairScenarios = if ($taskReference -eq 'baseline') { @('normal_add_list', 'bulk_replace_list', 'normal_add_iterator') } else { $taskScenarios }
    foreach ($taskScenario in $taskPairScenarios) {
        $taskPass = 0
        foreach ($taskVariant in @($taskReference, 'turbo', 'turbo', $taskReference)) {
            $taskPass++
            $taskCsv = Join-Path $taskPairOutput "$taskScenario-$taskPass-$taskVariant.csv"
            & $taskDotnet "benchmarks/Collections/bin/$taskVariant/net10.0/Collections.dll" --benchmark $taskCsv --scenario $taskScenario *> (Join-Path $taskPairOutput "$taskScenario-$taskPass-$taskVariant.log")
            if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $taskReference $taskScenario $taskPass." }
            $taskRows = @(Import-Csv -LiteralPath $taskCsv)
            if ($taskVariant -eq 'turbo') { $taskPhase2Rows += $taskRows }
            else { $taskReferenceRows += $taskRows }
        }
        Write-Output "$taskReference vs phase2: $taskScenario complete."
    }
    $taskReferenceCsv = Join-Path $taskPairOutput "$taskReference-aggregate.csv"
    $taskPhase2Csv = Join-Path $taskPairOutput 'phase2-aggregate.csv'
    $taskReferenceRows | Export-Csv -LiteralPath $taskReferenceCsv -NoTypeInformation
    $taskPhase2Rows | Export-Csv -LiteralPath $taskPhase2Csv -NoTypeInformation
    & (Join-Path $PSScriptRoot 'Compare.ps1') -BaselineCsv $taskReferenceCsv -TurboCsv $taskPhase2Csv -OutputJson (Join-Path $taskPairOutput 'comparison.json') *> (Join-Path $taskPairOutput 'comparison.log')
}
[ordered]@{
    scope = 'Actual collection component; no Files UI, disk, metadata, thumbnail or application navigation measured'
    sdk = (& $taskDotnet --version)
    configuration = 'Release x64 host JIT; DOTNET_TieredCompilation=0'
    each_pair_order = 'reference, phase2, phase2, reference (ABBA), fresh process per scenario/pass'
    repetitions_per_process_and_count = 21
    aggregated_samples_per_variant_scenario_count = 42
    iterations_per_sample = 'max(32, 4000000 / count)'
    warmup_operations_per_scenario_count = 8
    counts = @(1000, 10000, 50000, 100000)
    percentiles = 'Batch-average operation times; p95 nearest-rank, not UI or individual-operation latency'
    cache = 'Warm JIT and list capacity; forced GC outside timed section; filesystem cache not measured'
    source_hashes = $taskSources
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'run-metadata.json')
