param([string]$RunName = 'phase3-search-controlled', [ValidateRange(1, 10)][int]$Repetitions = 3)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid run name.' }
. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$phase3Root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $phase3Root
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$phase3Dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$phase3Results = Join-Path $phase3Root "artifacts\$RunName"
if (Test-Path -LiteralPath $phase3Results) { throw 'Results already exist; choose a fresh run name.' }
New-Item -ItemType Directory -Path $phase3Results | Out-Null
& $phase3Dotnet build benchmarks/Phase3/Search/SearchPipeline.csproj -c Release -v:quiet -clp:ErrorsOnly *> (Join-Path $phase3Results 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $phase3Results 'build.log') -Tail 20; throw 'Search pipeline build failed.' }
& $phase3Dotnet benchmarks/Phase3/Search/bin/Release/net10.0-windows10.0.26100.0/SearchPipeline.dll --contracts (Join-Path $phase3Results 'contracts')
if ($LASTEXITCODE -ne 0) { throw 'Search pipeline contracts failed.' }
$phase3NativeContracts = 'tests-extra/Search/bin/Release/net10.0-windows10.0.26100.0/Search.Tests.dll'
if (Test-Path -LiteralPath $phase3NativeContracts) {
    & $phase3Dotnet $phase3NativeContracts (Join-Path $phase3Results 'fixture') (Join-Path $phase3Results 'native-contracts')
    if ($LASTEXITCODE -ne 0) { throw 'Existing native provider contracts failed.' }
}
$phase3Traversal = 'benchmarks/Search/bin/Release/net10.0-windows10.0.26100.0/FilesTurbo.SearchBench.dll'
if ((Test-Path -LiteralPath $phase3Traversal) -and (Test-Path -LiteralPath (Join-Path $phase3Results 'fixture'))) {
    $phase3Fixture = (Resolve-Path -LiteralPath (Join-Path $phase3Results 'fixture')).Path
    if (-not $phase3Fixture.StartsWith($phase3Results + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped result directory.' }
    New-Item -ItemType Junction -Path (Join-Path $phase3Fixture 'junction') -Value $phase3Fixture | Out-Null
    & $phase3Dotnet $phase3Traversal --traversal-tests $phase3Fixture (Join-Path $phase3Results 'search-traversal-contracts.json')
    if ($LASTEXITCODE -ne 0) { throw 'Recursive search contracts failed.' }
}
& $phase3Dotnet benchmarks/Phase3/Search/bin/Release/net10.0-windows10.0.26100.0/SearchPipeline.dll --bench (Join-Path $phase3Root 'benchmarks/synthetic') (Join-Path $phase3Results 'pipeline') $Repetitions
if ($LASTEXITCODE -ne 0) { throw 'Search pipeline benchmark failed.' }
[ordered]@{
    scope = 'Real NTFS suggestion names and debounce-to-callback publication; excludes WinRT index, ListedItem, shell icons, dispatcher, WinUI layout/rendering and interactivity.'
    variants = @('Original', 'PhaseII', 'PhaseIII', 'PhaseIII', 'PhaseII', 'Original')
    warmupsPerScenario = 1
    measuredRepetitionsPerPass = $Repetitions
    sourceNativeProviderSHA256 = (Get-FileHash 'source/src/Files.App/Utils/Storage/Search/NativeNameSearch.cs').Hash
    sourceSuggestionPipelineSHA256 = (Get-FileHash 'source/src/Files.App/Utils/Storage/Search/SearchSuggestionPipeline.cs').Hash
    sourceSuggestionViewModelSHA256 = (Get-FileHash 'source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs').Hash
    sourceOriginalFolderSearchSHA256 = (Get-FileHash 'source-baseline/src/Files.App/Utils/Storage/Search/FolderSearch.cs').Hash
    phaseII = 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d'
    sdk = (& $phase3Dotnet --version)
    os = [Environment]::OSVersion.VersionString
    cache = 'Warm filesystem metadata after scenario warmup; no global cache eviction.'
    timestampUtc = [DateTimeOffset]::UtcNow
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $phase3Results 'run-metadata.json')
