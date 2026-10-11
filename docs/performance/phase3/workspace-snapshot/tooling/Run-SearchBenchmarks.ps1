param([string]$RunName = 'phase2-search-controlled', [ValidateRange(3,15)][int]$Repetitions = 5)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid run name.' }
. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$taskResults = Join-Path $taskRoot "artifacts\$RunName"
if (Test-Path -LiteralPath $taskResults) { throw 'Results already exist; choose a fresh run name.' }
New-Item -ItemType Directory -Path $taskResults | Out-Null
& $taskDotnet build benchmarks/Search/Search.csproj -c Release -v:quiet -clp:ErrorsOnly *> (Join-Path $taskResults 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $taskResults 'build.log') -Tail 20; throw 'Search benchmark build failed.' }
& $taskDotnet build tests-extra/Search/Search.Tests.csproj -c Release -v:quiet -clp:ErrorsOnly *> (Join-Path $taskResults 'tests-build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $taskResults 'tests-build.log') -Tail 20; throw 'Search contracts build failed.' }
& $taskDotnet tests-extra/Search/bin/Release/net10.0-windows10.0.26100.0/Search.Tests.dll (Join-Path $taskResults 'fixture') $taskResults
if ($LASTEXITCODE -ne 0) { throw 'Search contracts failed; see search-contracts.json.' }
$taskFixture = Join-Path $taskResults 'fixture'
New-Item -ItemType Junction -Path (Join-Path $taskFixture 'junction') -Value $taskFixture | Out-Null
& $taskDotnet benchmarks/Search/bin/Release/net10.0-windows10.0.26100.0/FilesTurbo.SearchBench.dll --traversal-tests $taskFixture (Join-Path $taskResults 'search-traversal-contracts.json')
if ($LASTEXITCODE -ne 0) { throw 'Recursive search contracts failed.' }
foreach ($taskOrder in @(@('baseline','phase1','phase2'), @('phase2','phase1','baseline'))) {
    foreach ($taskVariant in $taskOrder) {
        & $taskDotnet benchmarks/Search/bin/Release/net10.0-windows10.0.26100.0/FilesTurbo.SearchBench.dll $taskVariant (Join-Path $taskRoot 'benchmarks/synthetic') $taskResults $Repetitions
        if ($LASTEXITCODE -ne 0) { throw "Search benchmark failed: $taskVariant" }
    }
}
$taskRows = @(Get-ChildItem $taskResults -Filter '*.csv' | ForEach-Object { Import-Csv $_.FullName })
$taskMismatch = @($taskRows | Group-Object count,scenario | Where-Object { @($_.Group.result_hash | Select-Object -Unique).Count -ne 1 })
if ($taskMismatch.Count -ne 0) { throw "Search completeness mismatch: $($taskMismatch.Name -join '; ')" }
[ordered]@{
    source = 'Legacy traversal extracted from unchanged original/Phase I FolderSearch; original shared shortcut helper source-linked. Phase II NativeNameSearch source-linked from the app.'
    scope = 'Real NTFS directory enumeration, filters and enumeration metadata; excludes ListedItem creation, WinUI publication, icon loading, Windows Search and Everything.'
    sdk = (& $taskDotnet --version)
    os = [Environment]::OSVersion.VersionString
    filesystemCache = 'Warm OS metadata cache after each scenario warmup. No cache eviction, local application index or dictionary benchmark.'
    executionOrder = @('baseline','phase1','phase2','phase2','phase1','baseline')
    independentProcesses = 6
    warmupsPerScenario = 1
    measuredRepetitionsPerProcess = $Repetitions
    dataset = 'Existing benchmarks/synthetic: 1000,10000,50000,100000; not regenerated.'
    sourceFolderSearchBaselineSHA256 = (Get-FileHash 'source-baseline/src/Files.App/Utils/Storage/Search/FolderSearch.cs').Hash
    sourceFolderSearchPhase2SHA256 = (Get-FileHash 'source/src/Files.App/Utils/Storage/Search/FolderSearch.cs').Hash
    sourceNativeProviderSHA256 = (Get-FileHash 'source/src/Files.App/Utils/Storage/Search/NativeNameSearch.cs').Hash
    timestampUtc = [DateTimeOffset]::UtcNow
    resultHashesAllEquivalent = $true
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $taskResults 'run-metadata.json')
