#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Delivery = 'artifacts/development-layout/managed-release-phase3-stable06',
    [string]$OutputReport = 'reports/phase3-final-verification.json',
    [string]$BuildProvenance = 'reports/phase3-build-provenance.json',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$BuildName = 'phase3-managed-release-06'
)
$ErrorActionPreference = 'Stop'
$phase3Root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
function Resolve-Phase3Path([string]$Path) {
    $phase3Path = [IO.Path]::GetFullPath($Path, $phase3Root)
    if (!$phase3Path.StartsWith($phase3Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside workspace: $Path" }
    $phase3Cursor = $phase3Path
    while ($phase3Cursor.Length -ge $phase3Root.Length) {
        if ((Test-Path -LiteralPath $phase3Cursor) -and ((Get-Item -LiteralPath $phase3Cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked evidence rejected: $Path" }
        if ($phase3Cursor -eq $phase3Root) { break }
        $phase3Cursor = Split-Path $phase3Cursor -Parent
    }
    return $phase3Path
}
function Read-Phase3Json([string]$Path) { Get-Content -LiteralPath (Resolve-Phase3Path $Path) -Raw | ConvertFrom-Json }
function Assert-Phase3Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath (Resolve-Phase3Path $Path)).Hash -ne $Expected) { throw "Evidence changed: $Path" }
}
function Test-Phase3Layout([string]$Relative) {
    $phase3PackageRoot = Resolve-Phase3Path $Relative
    $phase3Validation = Read-Phase3Json ($Relative + '/package-validation.json')
    if ($phase3Validation.Status -ne 'content-validated-not-installed' -or !$phase3Validation.ManifestUnchanged -or $phase3Validation.Signed) { throw "Unexpected package validation state: $Relative" }
    if ((Resolve-Phase3Path $phase3Validation.Layout) -ne (Resolve-Phase3Path ($Relative + '/layout'))) { throw 'Package validation belongs to a different layout.' }
    $phase3Manifest = [xml][IO.File]::ReadAllText((Resolve-Phase3Path ($Relative + '/layout/AppxManifest.xml')))
    if ($phase3Manifest.Package.Identity.Name -ne 'FilesTurbo.Experimental' -or $phase3Manifest.Package.Identity.Publisher -ne 'CN=Files') { throw 'Package identity is not the isolated experimental identity.' }
    Assert-Phase3Hash $phase3Validation.OutputPackage $phase3Validation.OutputSHA256
    foreach ($phase3Entry in $phase3Validation.Inventory) {
        $phase3File = Resolve-Phase3Path ($Relative + '/layout/' + $phase3Entry.Path)
        if (!$phase3File.StartsWith($phase3PackageRoot + '\layout\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package inventory path escaped layout.' }
        if ((Get-Item -LiteralPath $phase3File).Length -ne $phase3Entry.Length) { throw "Layout length differs: $($phase3Entry.Path)" }
        Assert-Phase3Hash $phase3File $phase3Entry.SHA256
    }
    $phase3LayoutFiles = @(Get-ChildItem -LiteralPath (Resolve-Phase3Path ($Relative + '/layout')) -File -Recurse -Force)
    if ($phase3LayoutFiles.Count -ne $phase3Validation.Inventory.Count -or @($phase3Validation.Inventory | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3Validation.Inventory.Count) { throw "Unrecorded layout files or incomplete inventory: $Relative" }
    foreach ($phase3Entry in $phase3Validation.DependencyInventory) {
        $phase3File = Resolve-Phase3Path ($Relative + '/Dependencies/' + $phase3Entry.Path)
        if (!$phase3File.StartsWith($phase3PackageRoot + '\Dependencies\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency inventory path escaped dependency directory.' }
        if ((Get-Item -LiteralPath $phase3File).Length -ne $phase3Entry.Length) { throw "Dependency length differs: $($phase3Entry.Path)" }
        Assert-Phase3Hash $phase3File $phase3Entry.SHA256
    }
    $phase3DependencyFiles = @(Get-ChildItem -LiteralPath (Resolve-Phase3Path ($Relative + '/Dependencies')) -File -Recurse -Force)
    if ($phase3DependencyFiles.Count -ne $phase3Validation.DependencyInventory.Count -or @($phase3Validation.DependencyInventory | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3Validation.DependencyInventory.Count) { throw "Unrecorded dependency files or incomplete inventory: $Relative" }
    return $phase3Validation
}
$phase3ReportPath = Resolve-Phase3Path $OutputReport
if (Test-Path -LiteralPath $phase3ReportPath) { throw 'Report already exists; use a fresh output path to preserve evidence.' }
$phase3Repository = Resolve-Phase3Path 'source'
$phase3Original = '1994d9a4269f5144c70bc67d6a48de4a20616912'
$phase3PhaseII = 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d'
if ((& git -C (Resolve-Phase3Path 'source-baseline') rev-parse HEAD) -ne $phase3Original) { throw 'Original baseline reference changed.' }
if (@(& git -C (Resolve-Phase3Path 'source-baseline') status --porcelain).Count) { throw 'Original baseline working tree changed.' }
foreach ($phase3Reference in @('files-turbo-phase2', 'files-turbo-phase3-start')) {
    if ((& git -C $phase3Repository rev-parse $phase3Reference) -ne $phase3PhaseII) { throw "Exact Phase II reference changed: $phase3Reference" }
}
& git -C $phase3Repository diff --check files-turbo-phase3-start
if ($LASTEXITCODE -ne 0) { throw 'Phase III source diff check failed.' }
$phase3Tracked = @(& git -C $phase3Repository diff files-turbo-phase3-start --name-only -- src)
$phase3Untracked = @(& git -C $phase3Repository ls-files --others --exclude-standard -- src)
$phase3Changes = @($phase3Tracked + $phase3Untracked | Sort-Object -Unique)
$phase3Sources = foreach ($phase3Relative in $phase3Changes) {
    $phase3File = Resolve-Phase3Path ('source/' + $phase3Relative)
    if ($phase3Relative -match '\.(cs|xaml|appxmanifest|txt|xml|json|csproj|props|targets|md)$' -and [IO.File]::ReadAllText($phase3File) -match '(?<!\r)\n') { throw "Changed source requires CRLF: $phase3Relative" }
    [pscustomobject]@{ Path = $phase3Relative; SHA256 = (Get-FileHash -LiteralPath $phase3File).Hash }
}
$phase3ScriptPaths = @((Get-ChildItem -LiteralPath (Resolve-Phase3Path 'tooling') -Filter '*.ps1' -File).FullName)
foreach ($phase3Directory in @('tests-extra/Phase3Navigation', 'tests-extra/Viewport', 'tests-extra/EntryConstruction', 'tests-extra/Adaptive', 'tests-extra/Phase3PathNavigation', 'tests-extra/Phase3WorkingDirectory', 'benchmarks/Phase3')) {
    if (Test-Path -LiteralPath (Resolve-Phase3Path $phase3Directory)) {
        $phase3ScriptPaths += @((Get-ChildItem -LiteralPath (Resolve-Phase3Path $phase3Directory) -Filter '*.ps1' -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).FullName)
    }
}
$phase3Parses = foreach ($phase3Script in @($phase3ScriptPaths | Sort-Object -Unique)) {
    $phase3SafeScript = Resolve-Phase3Path $phase3Script
    $phase3Tokens = $null; $phase3Errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($phase3SafeScript, [ref]$phase3Tokens, [ref]$phase3Errors)
    if ($phase3Errors.Count) { throw "PowerShell parse failed: $phase3SafeScript" }
    [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($phase3Root, $phase3SafeScript); SHA256 = (Get-FileHash -LiteralPath $phase3SafeScript).Hash }
}
$phase3Package = Test-Phase3Layout $Delivery
$phase3RecoveryPackage = Test-Phase3Layout 'artifacts/development-layout/managed-release-phase2'
$phase3HistoricalCopies = @(
    @('MORNING_REPORT.md', 'artifacts/phase2-recovery/MORNING_REPORT.md'),
    @('reports/final-verification.json', 'artifacts/phase2-recovery/final-verification.json')
)
foreach ($phase3Pair in $phase3HistoricalCopies) { Assert-Phase3Hash $phase3Pair[0] (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3Pair[1])).Hash }
$phase3HistoricalReports = @('MORNING_REPORT.md', 'MORNING_REPORT_PHASE2.md', 'PERFORMANCE_REPORT.md', 'README_FILES_TURBO.md') + @(
    Get-ChildItem -LiteralPath (Resolve-Phase3Path 'reports') -File | Where-Object { $_.Name -match '^(PHASE2_|phase2-|DELIVERY\.md$|final-verification\.json$)' } | ForEach-Object { [IO.Path]::GetRelativePath($phase3Root, $_.FullName) }
)
$phase3HistoricalInventory = foreach ($phase3HistoricalPath in @($phase3HistoricalReports | Sort-Object -Unique)) {
    [pscustomobject]@{ Path = $phase3HistoricalPath; SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3HistoricalPath)).Hash }
}
$phase3Provenance = Read-Phase3Json $BuildProvenance
$phase3Head = (& git -C $phase3Repository rev-parse HEAD)
if ($phase3Provenance.Status -ne 'compiled-source-and-packaged-assembly-match' -or $phase3Provenance.SourceCommit -ne $phase3Head -or $phase3Provenance.PhaseIIIStartCommit -ne $phase3PhaseII) { throw 'Build provenance does not identify the current source commit and exact Phase II start.' }
if ($phase3Provenance.SourceWorkingTreeDirty -or @(& git -C $phase3Repository status --porcelain).Count) { throw 'Final provenance requires a clean committed Phase III source tree.' }
$phase3ExpectedCs = @($phase3Changes | Where-Object { $_.EndsWith('.cs') })
if (@($phase3Provenance.Documents | Select-Object -ExpandProperty Path -Unique).Count -ne $phase3ExpectedCs.Count -or $phase3Provenance.ChangedCSharpCount -ne $phase3ExpectedCs.Count) { throw 'Final compiled source document coverage is incomplete.' }
$phase3ActualCs = @($phase3Provenance.Documents | Select-Object -ExpandProperty Path -Unique | Sort-Object)
if (@(Compare-Object @($phase3ExpectedCs | Sort-Object) $phase3ActualCs).Count) { throw 'Compiled document paths do not match changed production C# paths.' }
$phase3ExpectedInputs = @($phase3Changes | Where-Object { $_.EndsWith('.xaml') -or $_.EndsWith('.appxmanifest') -or ($_ -like 'src/Files.App.CsWin32/*' -and $_.EndsWith('.txt')) } | Sort-Object)
$phase3ActualInputs = @($phase3Provenance.SourceInputs | Select-Object -ExpandProperty Path -Unique | Sort-Object)
if (@(Compare-Object $phase3ExpectedInputs $phase3ActualInputs).Count) { throw 'XAML, manifest or CsWin32 source-input hash coverage is incomplete.' }
foreach ($phase3Entry in @($phase3Provenance.Documents) + @($phase3Provenance.SourceInputs)) { Assert-Phase3Hash ('source/' + $phase3Entry.Path) $phase3Entry.SHA256 }
foreach ($phase3Entry in $phase3Provenance.Assemblies) {
    if (!$phase3Entry.PdbIdentityMatchesAssembly) { throw 'Assembly/PDB identity not verified.' }
    Assert-Phase3Hash ($Delivery + '/layout/' + $phase3Entry.LayoutPath) $phase3Entry.SHA256
    Assert-Phase3Hash $phase3Entry.PdbPath $phase3Entry.PdbSHA256
}
Assert-Phase3Hash ('source/' + $phase3Provenance.Manifest.Path) $phase3Provenance.Manifest.SourceSHA256
Assert-Phase3Hash ($Delivery + '/layout/AppxManifest.xml') $phase3Provenance.Manifest.LayoutSHA256
$phase3Navigation = Read-Phase3Json 'reports/phase3-navigation-shell-contracts-final-04.json'
$phase3Viewport = Read-Phase3Json 'reports/phase3-viewport-contracts-final.json'
$phase3SearchPipeline = @(Read-Phase3Json 'artifacts/phase3-search-verified/contracts-additional/search-pipeline-contracts.json')
$phase3SearchNative = @(Read-Phase3Json 'artifacts/phase3-search-verified/native-contracts/search-contracts.json')
$phase3SearchTraversal = @(Read-Phase3Json 'artifacts/phase3-search-verified/search-traversal-contracts.json')
if ($phase3Navigation.results.Count -ne 22 -or $phase3Viewport.contracts.Count -ne 17 -or $phase3SearchPipeline.Count -ne 11 -or $phase3SearchNative.Count -ne 28 -or $phase3SearchTraversal.Count -ne 3) { throw 'Expected Phase III contract evidence is incomplete.' }
if (@($phase3Navigation.results | Where-Object { !$_.passed }).Count -or @($phase3Viewport.contracts | Where-Object { !$_.Passed }).Count -or @($phase3SearchPipeline | Where-Object { !$_.Passed }).Count -or @($phase3SearchNative | Where-Object Status -ne passed).Count -or @($phase3SearchTraversal | Where-Object { !$_.passed }).Count) { throw 'A required Phase III contract failed.' }
$phase3NavigationHash = [IO.File]::ReadAllText((Resolve-Phase3Path 'tests-extra/Phase3Navigation/source-hash.txt')).Trim()
Assert-Phase3Hash 'source/src/Files.App/ViewModels/ShellViewModel.cs' $phase3NavigationHash
Assert-Phase3Hash $phase3Viewport.productionSource $phase3Viewport.productionSourceSha256
$phase3SearchMetadata = Read-Phase3Json 'artifacts/phase3-search-verified/run-metadata.json'
Assert-Phase3Hash 'source/src/Files.App/Utils/Storage/Search/NativeNameSearch.cs' $phase3SearchMetadata.sourceNativeProviderSHA256
Assert-Phase3Hash 'source/src/Files.App/Utils/Storage/Search/SearchSuggestionPipeline.cs' $phase3SearchMetadata.sourceSuggestionPipelineSHA256
$phase3EntryContracts = Read-Phase3Json 'artifacts/phase3-entry-construction-final-02/entry-contracts.json'
if ($phase3EntryContracts.results.Count -ne 16 -or @($phase3EntryContracts.results | Where-Object { !$_.Passed }).Count) { throw 'Entry construction and solution detection contracts are incomplete or failed.' }
if ($phase3EntryContracts.source.OriginalCommit -ne $phase3PhaseII) { throw 'Entry construction contracts do not compare the exact Phase II source.' }
$phase3ExpectedEntrySources = @('source/src/Files.App/Data/Items/ListedItem.cs', 'source/src/Files.App/Utils/Storage/Enumerators/Win32StorageEnumerator.cs', 'source/src/Files.Shared/Helpers/FileExtensionHelpers.cs', 'source/src/Files.App/Utils/Storage/StorageItems/ZipStorageFolder.cs', 'source/src/Files.App/ViewModels/ShellViewModel.cs')
if (@(Compare-Object @($phase3ExpectedEntrySources | Sort-Object) @($phase3EntryContracts.source.CurrentSource | Select-Object -ExpandProperty Path | Sort-Object)).Count) { throw 'Entry construction source coverage is incomplete.' }
foreach ($phase3Entry in $phase3EntryContracts.source.CurrentSource) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.SHA256 }
if ($phase3EntryContracts.source.SnapshotName -ne 'verified-final04' -or $phase3EntryContracts.source.ExtractedSource.Count -ne 10 -or $phase3EntryContracts.source.HarnessSource.Count -ne 3) { throw 'Entry construction extraction or harness metadata is incomplete.' }
foreach ($phase3Entry in $phase3EntryContracts.source.ExtractedSource) { Assert-Phase3Hash ('tests-extra/EntryConstruction/snapshots/verified-final04/' + $phase3Entry.Path) $phase3Entry.SHA256 }
foreach ($phase3Entry in $phase3EntryContracts.source.HarnessSource) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.SHA256 }
$phase3Adaptive = Read-Phase3Json 'reports/phase3-adaptive-contracts-final.json'
if ($phase3Adaptive.Passed -ne 17 -or $phase3Adaptive.Failed -ne 0 -or $phase3Adaptive.Results.Count -ne 17 -or @($phase3Adaptive.Results | Where-Object { !$_.Passed }).Count) { throw 'Adaptive layout contracts are incomplete or failed.' }
if ((Resolve-Phase3Path $phase3Adaptive.ProductionSource) -ne (Resolve-Phase3Path 'source/src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs')) { throw 'Adaptive contracts identify an unexpected production source.' }
Assert-Phase3Hash $phase3Adaptive.ProductionSource $phase3Adaptive.SourceSha256
$phase3PathNavigation = Read-Phase3Json 'reports/phase3-path-navigation-contracts-final-02.json'
if ($phase3PathNavigation.Passed -ne 37 -or $phase3PathNavigation.Failed -ne 0 -or $phase3PathNavigation.Results.Count -ne 37 -or @($phase3PathNavigation.Results | Where-Object { !$_.Passed }).Count) { throw 'Toolbar path navigation contracts are incomplete or failed.' }
$phase3ExpectedPathSources = @('source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs', 'source/src/Files.App/Helpers/Navigation/LatestPathNavigationCoordinator.cs')
if (@(Compare-Object @($phase3ExpectedPathSources | Sort-Object) @($phase3PathNavigation.ProductionSources | Select-Object -ExpandProperty Path | Sort-Object)).Count) { throw 'Toolbar navigation source coverage is incomplete.' }
foreach ($phase3Entry in $phase3PathNavigation.ProductionSources) { Assert-Phase3Hash $phase3Entry.Path $phase3Entry.Sha256 }
Assert-Phase3Hash 'source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs' $phase3PathNavigation.ExtractedToolbarSha256
Assert-Phase3Hash 'tests-extra/Phase3PathNavigation/PathCore.generated.cs' $phase3PathNavigation.GeneratedMethodsSha256
$phase3WorkingDirectory = Read-Phase3Json 'reports/phase3-working-directory-contracts-final-01.json'
if (!$phase3WorkingDirectory.passed -or $phase3WorkingDirectory.total -ne 41 -or $phase3WorkingDirectory.failures -ne 0 -or $phase3WorkingDirectory.results.Count -ne 41 -or @($phase3WorkingDirectory.results | Where-Object { !$_.passed }).Count) { throw 'Working directory and breadcrumb contracts are incomplete or failed.' }
$phase3ExpectedWorkingMembers = @(
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public async Task SetWorkingDirectoryAsync(string? value)', 'WorkingDirectoryCore.generated.cs'),
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public async Task UpdateFolderThumbnailImageSource()', 'FolderThumbnailCore.generated.cs'),
    @('source/src/Files.App/ViewModels/ShellViewModel.cs', 'public bool IsWorkingDirectoryUpdatePending =>', 'WorkingDirectoryPending.generated.cs'),
    @('source/src/Files.App/Views/Shells/BaseShellPage.cs', 'public async Task UpdatePathUIToWorkingDirectoryAsync(string? newWorkingDir, string? singleItemOverride = null)', 'BreadcrumbCore.generated.cs'),
    @('source/src/Files.App/Views/Shells/ModernShellPage.xaml.cs', 'public override void NavigateToPath(string? navigationPath, Type? sourcePageType, NavigationArguments? navArgs = null)', 'ModernRouteCore.generated.cs')
)
if ($phase3WorkingDirectory.sources.Sources.Count -ne $phase3ExpectedWorkingMembers.Count) { throw 'Working directory extraction coverage is incomplete.' }
foreach ($phase3WorkingMember in $phase3ExpectedWorkingMembers) {
    $phase3Matches = @($phase3WorkingDirectory.sources.Sources | Where-Object { $_.Source -eq $phase3WorkingMember[0] -and $_.Signature -eq $phase3WorkingMember[1] -and $_.Generated -eq $phase3WorkingMember[2] -and $_.Verbatim })
    if ($phase3Matches.Count -ne 1) { throw 'Required working directory member extraction is missing or duplicated.' }
    $phase3Member = $phase3Matches[0]
    Assert-Phase3Hash $phase3Member.Source $phase3Member.SourceSha256
    Assert-Phase3Hash ('tests-extra/Phase3WorkingDirectory/' + $phase3Member.Generated) $phase3Member.GeneratedSha256
    $phase3SourceText = [IO.File]::ReadAllText((Resolve-Phase3Path $phase3Member.Source))
    $phase3MemberStart = $phase3SourceText.IndexOf($phase3Member.Signature, [StringComparison]::Ordinal)
    if ($phase3MemberStart -lt 0) { throw 'Working directory production member is missing.' }
    if ($phase3Member.Signature.EndsWith('=>')) { $phase3MemberEnd = $phase3SourceText.IndexOf(';', $phase3MemberStart) + 1 }
    else {
        $phase3MemberBrace = $phase3SourceText.IndexOf('{', $phase3MemberStart)
        $phase3MemberDepth = 1
        $phase3MemberEnd = $phase3MemberBrace + 1
        while ($phase3MemberEnd -lt $phase3SourceText.Length -and $phase3MemberDepth -gt 0) {
            if ($phase3SourceText[$phase3MemberEnd] -eq '{') { $phase3MemberDepth++ }
            elseif ($phase3SourceText[$phase3MemberEnd] -eq '}') { $phase3MemberDepth-- }
            $phase3MemberEnd++
        }
        if ($phase3MemberDepth) { throw 'Working directory production member has unbalanced braces.' }
    }
    if ($phase3MemberEnd -le $phase3MemberStart) { throw 'Working directory production member could not be extracted.' }
    $phase3MemberBytes = [Text.Encoding]::UTF8.GetBytes($phase3SourceText.Substring($phase3MemberStart, $phase3MemberEnd - $phase3MemberStart))
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($phase3MemberBytes)) -ne $phase3Member.MethodUtf8Sha256) { throw 'Working directory literal production method differs from the compiled extraction.' }
}
$phase3SearchRows = @(Import-Csv -LiteralPath (Resolve-Phase3Path 'artifacts/phase3-search-verified/pipeline/pipeline-publication.csv'))
$phase3ExpectedSamples = 6 * 4 * 6 * [int]$phase3SearchMetadata.measuredRepetitionsPerPass
if ($phase3SearchRows.Count -ne $phase3ExpectedSamples) { throw 'Search pipeline sample count is incomplete.' }
foreach ($phase3Group in ($phase3SearchRows | Group-Object count,scenario)) {
    if (@($phase3Group.Group | Select-Object -ExpandProperty variant -Unique).Count -ne 3 -or @($phase3Group.Group | Select-Object -ExpandProperty result_hash -Unique).Count -ne 1) { throw "Search suggestion result sets differ: $($phase3Group.Name)" }
}
if (!$BuildName) {
    $phase3LatestBuild = Get-ChildItem -LiteralPath (Resolve-Phase3Path 'reports') -Filter 'phase3-*-final-build.log' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (!$phase3LatestBuild) { throw 'Final Phase III build log missing.' }
    $BuildName = $phase3LatestBuild.Name -replace '-final-build\.log$', ''
}
$phase3BuildLog = "reports/$BuildName-final-build.log"
$phase3BuildText = [IO.File]::ReadAllText((Resolve-Phase3Path $phase3BuildLog))
if ($phase3BuildText -notmatch '(?m)^\s*0 (?:Error\(s\)|Errores)\s*$') { throw 'Final integrated build did not report zero errors.' }
$phase3Warnings = [regex]::Match($phase3BuildText, '(?m)^\s*(\d+) (?:Warning\(s\)|Advertencia\(s\))\s*$').Groups[1].Value
if (!$phase3Warnings) { throw 'Final integrated warning count missing.' }
$phase3EvidencePaths = @($BuildProvenance, $phase3BuildLog, 'reports/phase3-navigation-shell-contracts-final-04.json', 'reports/phase3-viewport-contracts-final.json', 'artifacts/phase3-search-verified/contracts-additional/search-pipeline-contracts.json', 'artifacts/phase3-search-verified/native-contracts/search-contracts.json', 'artifacts/phase3-search-verified/search-traversal-contracts.json', 'artifacts/phase3-search-verified/run-metadata.json', 'artifacts/phase3-search-verified/pipeline/pipeline-publication.csv', 'artifacts/phase3-search-verified/pipeline/summary-corrected.csv', 'artifacts/phase3-entry-construction-final-02/entry-contracts.json', 'artifacts/phase3-entry-construction-build/build-final-02.log', 'reports/phase3-adaptive-contracts-final.json', 'reports/phase3-path-navigation-contracts-final-02.json', 'reports/phase3-working-directory-contracts-final-01.json', 'tests-extra/Phase3WorkingDirectory/source-hashes.json')
$phase3Evidence = foreach ($phase3EvidencePath in $phase3EvidencePaths) { [pscustomobject]@{ Path = $phase3EvidencePath; SHA256 = (Get-FileHash -LiteralPath (Resolve-Phase3Path $phase3EvidencePath)).Hash } }
$phase3Result = [ordered]@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    Status = 'content-and-components-validated-runtime-reported-separately'
    Scope = 'Exact source references, clean committed source, CRLF, script syntax, Release/PDB/source provenance, complete recorded package/dependency hashes and directed component contracts. GUI/COM/scroll observations are reported separately.'
    BaselineCommit = $phase3Original
    PhaseIICommit = $phase3PhaseII
    PhaseIIICommit = $phase3Head
    BaselineClean = $true
    HistoricalSnapshotCopiesVerified = @($phase3HistoricalCopies)
    HistoricalReportInventory = @($phase3HistoricalInventory)
    PhaseIIRecoveryPackageSHA256 = $phase3RecoveryPackage.OutputSHA256
    Source = @($phase3Sources)
    ScriptParses = @($phase3Parses)
    Contracts = @{ Navigation = $phase3Navigation.results.Count; Viewport = $phase3Viewport.contracts.Count; SearchPipeline = $phase3SearchPipeline.Count; SearchProvider = $phase3SearchNative.Count; RecursiveSearch = $phase3SearchTraversal.Count; EntryConstruction = $phase3EntryContracts.results.Count; AdaptiveLayout = $phase3Adaptive.Results.Count; ToolbarPathNavigation = $phase3PathNavigation.Results.Count; WorkingDirectory = $phase3WorkingDirectory.results.Count; Total = $phase3Navigation.results.Count + $phase3Viewport.contracts.Count + $phase3SearchPipeline.Count + $phase3SearchNative.Count + $phase3SearchTraversal.Count + $phase3EntryContracts.results.Count + $phase3Adaptive.Results.Count + $phase3PathNavigation.Results.Count + $phase3WorkingDirectory.results.Count }
    SearchBenchmark = @{ Samples = $phase3SearchRows.Count; ResultSetsEquivalent = $true; Metric = 'NTFS names and debounce-to-harness-callback; not visible UI latency' }
    Evidence = @($phase3Evidence)
    IntegratedBuild = @{ Configuration = 'Release managed x64'; Errors = 0; Warnings = [int]$phase3Warnings; CompiledChangedCSharpDocuments = $phase3ExpectedCs.Count }
    Package = @{ Delivery = $Delivery; Path = [IO.Path]::GetRelativePath($phase3Root, $phase3Package.OutputPackage); SHA256 = $phase3Package.OutputSHA256; ContentFiles = $phase3Package.Inventory.Count; Dependencies = $phase3Package.DependencyInventory.Count; Signed = $false; Identity = 'FilesTurbo.Experimental'; Version = $phase3Provenance.Manifest.Version; SourceManifestSHA256 = $phase3Provenance.Manifest.SourceSHA256 }
}
$phase3Json = $phase3Result | ConvertTo-Json -Depth 10
$phase3Output = [IO.FileStream]::new($phase3ReportPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $phase3Bytes = [Text.UTF8Encoding]::new($false).GetBytes($phase3Json)
    $phase3Output.Write($phase3Bytes, 0, $phase3Bytes.Length)
} finally { $phase3Output.Dispose() }
Write-Output $phase3Result.Status
