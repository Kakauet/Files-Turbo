#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [string]$DestinationRelative = 'source/docs/performance/phase4/workspace-snapshot',
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SnapshotSourceCommit = 'bf4390ee0a2eed6cece0e99d1a021ce7fd255574',
    [ValidateRange(1, 64)][int]$MaximumFileMiB = 64,
    [ValidateRange(1, 128)][int]$MaximumSnapshotMiB = 128
)
$ErrorActionPreference = 'Stop'
$captureRoot = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$captureFileLimit = [long]$MaximumFileMiB * 1MB
$captureTotalLimit = [long]$MaximumSnapshotMiB * 1MB
$captureUtf8 = [Text.UTF8Encoding]::new($false, $true)

function Resolve-CaptureMember([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or $Relative -match '(^|[/\\])\.\.?([/\\]|$)') { throw "Unsafe member: $Relative" }
    $member = [IO.Path]::GetFullPath($Relative, $Root)
    if (!$member.StartsWith($Root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Member escaped its root: $Relative" }
    $cursor = $member
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point rejected: $Relative" }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (!$parent -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $member
}

function Add-CaptureFile([string]$Relative, [string]$Kind = 'Evidence', [switch]$Required) {
    $relativeMember = $Relative.Replace('\', '/')
    $full = Resolve-CaptureMember $captureRoot $relativeMember
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) {
        if ($Required) { throw "Required snapshot input missing: $relativeMember" }
        return
    }
    if (!$captureCandidates.ContainsKey($relativeMember)) { $captureCandidates.Add($relativeMember, $Kind) }
}

function Add-CaptureTree([string]$Relative, [string[]]$Extensions, [string]$Kind = 'Evidence') {
    $root = Resolve-CaptureMember $captureRoot $Relative
    if (!(Test-Path -LiteralPath $root -PathType Container)) { return }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count) {
        foreach ($entry in (Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
            if ($entry.PSIsContainer -and $entry.Name -in @('bin', 'obj', '.git', '.vs', 'node_modules', 'fixture', 'fixtures')) { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point in selected input tree: $Relative" }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            elseif ($entry.Extension.ToLowerInvariant() -in $Extensions) { Add-CaptureFile ([IO.Path]::GetRelativePath($captureRoot, $entry.FullName)) $Kind }
        }
    }
}

function Get-CaptureHash([byte[]]$Bytes) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }
function Get-CaptureJsonBytes($Value) { return $captureUtf8.GetBytes((($Value | ConvertTo-Json -Depth 20) -replace '\r?\n', "`r`n") + "`r`n") }
function Write-CaptureNew([string]$Relative, [byte[]]$Bytes) {
    $path = Resolve-CaptureMember $captureDestination $Relative
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $path = Resolve-CaptureMember $captureDestination $Relative
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}

if (!(Test-Path -LiteralPath $captureRoot -PathType Container)) { throw 'Workspace root must already exist.' }
$captureDestination = Resolve-CaptureMember $captureRoot $DestinationRelative
$captureAllowedParent = Resolve-CaptureMember $captureRoot 'source/docs/performance/phase4'
if (!$captureDestination.StartsWith($captureAllowedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Snapshot destination must be a new directory beneath source/docs/performance/phase4.' }
if (Test-Path -LiteralPath $captureDestination) { throw 'Snapshot destination already exists. No refresh, overwrite or deletion is permitted.' }
$captureSource = Resolve-CaptureMember $captureRoot 'source'
$captureHead = (& git -C $captureSource rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'The workspace source must be a Git checkout.' }
& git -C $captureSource cat-file -e ($SnapshotSourceCommit + '^{commit}')
if ($LASTEXITCODE -ne 0) { throw 'Snapshot source commit is not available in the checkout.' }
& git -C $captureSource diff --quiet $SnapshotSourceCommit -- . ':(exclude)docs'
if ($LASTEXITCODE -ne 0) { throw 'Non-documentation source differs from the frozen production commit.' }
$captureUntracked = @(& git -C $captureSource ls-files --others --exclude-standard -- . ':(exclude)docs')
if ($LASTEXITCODE -ne 0 -or $captureUntracked.Count) { throw 'Non-documentation source contains untracked files or could not be inspected.' }

$captureCandidates = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('PHASE4_PERFORMANCE.md', 'PHASE4_THUMBNAILS.md', 'PHASE4_SCROLL.md', 'PHASE4_NAVIGATION_SEARCH_GLOBAL_AUDIT.md', 'FINAL_DELIVERY.md', 'FINAL_EXPLORER_COMPARISON.md', 'FINAL_STABILITY.md')) { Add-CaptureFile ('reports/' + $name) 'FinalReport' -Required }
$captureMorning = @('MORNING_REPORT_PHASE4_FINAL.md', 'reports/MORNING_REPORT_PHASE4_FINAL.md') | Where-Object { Test-Path -LiteralPath (Resolve-CaptureMember $captureRoot $_) -PathType Leaf }
if (!$captureMorning.Count) { throw 'The final Phase IV morning report must exist before capture.' }
foreach ($relative in $captureMorning) { Add-CaptureFile $relative 'FinalReport' -Required }
foreach ($entry in (Get-ChildItem -LiteralPath (Resolve-CaptureMember $captureRoot 'reports') -File -Filter 'phase4*.json')) { Add-CaptureFile ('reports/' + $entry.Name) 'ContractOrProvenance' }
foreach ($entry in (Get-ChildItem -LiteralPath (Resolve-CaptureMember $captureRoot 'tooling') -File -Filter '*Phase4*.ps1')) { Add-CaptureFile ('tooling/' + $entry.Name) 'ToolingSource' }
foreach ($relative in @('tooling/Build-FilesTurbo.ps1', 'tooling/Build-NativeHelpers.ps1', 'tooling/Complete-DevelopmentPackage.ps1', 'tooling/Enter-WorkspaceEnvironment.ps1', 'tooling/Install-LocalSdk.ps1', 'tooling/New-SyntheticDataset.ps1', 'tooling/New-ThumbnailFixtures.ps1', 'tooling/Run-SearchBenchmarks.ps1', 'tooling/Run-Phase3SearchBenchmarks.ps1', 'tooling/Run-ComponentBenchmarks.ps1', 'NuGet.Config', 'nuget.config', 'benchmarks/Analyze-EventPipe.ps1')) { Add-CaptureFile $relative 'ToolingSource' }

$captureSourceExtensions = @('.cs', '.csproj', '.ps1', '.txt', '.md', '.json', '.config', '.props', '.targets')
foreach ($entry in (Get-ChildItem -LiteralPath (Resolve-CaptureMember $captureRoot 'tests-extra') -Directory -Filter 'Phase4*')) { Add-CaptureTree ('tests-extra/' + $entry.Name) $captureSourceExtensions 'HarnessSource' }
foreach ($relative in @('tests-extra/Phase3Navigation', 'tests-extra/Phase3PathNavigation', 'tests-extra/Phase3WorkingDirectory', 'tests-extra/Adaptive', 'tests-extra/Search', 'tests-extra/EntryConstruction', 'tests-extra/Viewport', 'tests-extra/Navigation', 'tests-extra/IconCache', 'tests-extra/Collections')) { Add-CaptureTree $relative $captureSourceExtensions 'HarnessSource' }
foreach ($relative in @('benchmarks/Phase4CaptureInterop', 'benchmarks/Phase3/Search', 'benchmarks/Search', 'benchmarks/Navigation', 'benchmarks/Collections', 'benchmarks/IconCache', 'benchmarks/IconImages', 'benchmarks/STAQueue', 'benchmarks/AnalyzeEventPipe', 'tooling/AnalyzeEventPipe', 'tooling/TraceTools')) { Add-CaptureTree $relative $captureSourceExtensions 'BenchmarkSource' }
Add-CaptureTree 'benchmarks/results/phase4' @('.json', '.csv', '.jsonl') 'RawMeasurement'
foreach ($entry in (Get-ChildItem -LiteralPath (Resolve-CaptureMember $captureRoot 'benchmarks') -File -Filter '*phase4*manifest.json')) { Add-CaptureFile ('benchmarks/' + $entry.Name) 'FixtureManifest' }
Add-CaptureTree 'artifacts/phase4-recovery' @('.json') 'RecoveryMetadata'

$captureArtifactReferences = [Collections.Generic.List[object]]::new()
function Add-CaptureArtifact([string]$Relative, [string]$Kind) {
    $member = $Relative.Replace('\', '/')
    $full = Resolve-CaptureMember $captureRoot $member
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { return }
    $entry = Get-Item -LiteralPath $full
    $captureArtifactReferences.Add([pscustomobject]@{ WorkspacePath = $member; Kind = $Kind; Bytes = $entry.Length; SHA256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash; Copied = $false })
}
Add-CaptureFile 'artifacts/phase4-recovery/phase3-inventory.json' 'RecoveryMetadata' -Required
Add-CaptureArtifact 'source/docs/performance/phase3/workspace-snapshot/snapshot-inventory.json' 'PhaseIIIWorkspaceSnapshotInventory'
$captureLayoutRoot = Resolve-CaptureMember $captureRoot 'artifacts/development-layout'
foreach ($directory in (Get-ChildItem -LiteralPath $captureLayoutRoot -Directory | Where-Object { $_.Name -match '^managed-release-phase4-(final\d+|original)$' -or $_.Name -eq 'managed-release-phase3-stable06' })) {
    $relative = 'artifacts/development-layout/' + $directory.Name
    $null = Resolve-CaptureMember $captureRoot $relative
    Add-CaptureFile ($relative + '/package-validation.json') 'ArtifactInventory'
    Add-CaptureArtifact ($relative + '/package-validation.json') 'PackageInventory'
    foreach ($package in (Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.msix')) { Add-CaptureArtifact ($relative + '/' + $package.Name) 'DevelopmentMSIX' }
    foreach ($assembly in @('Files.dll', 'Files.App.Storage.dll', 'Files.App.CsWin32.dll', 'Files.App.Server/Files.App.Server.dll')) { Add-CaptureArtifact ($relative + '/layout/' + $assembly) 'PackagedAssembly' }
}
foreach ($entry in (Get-ChildItem -LiteralPath (Resolve-CaptureMember $captureRoot 'artifacts/phase4-recovery') -File -Filter '*.json')) { Add-CaptureArtifact ('artifacts/phase4-recovery/' + $entry.Name) 'RecoveryOrRegistrationMetadata' }
foreach ($relative in @('benchmarks/results/phase4/baseline-III-profile/scroll.nettrace', 'benchmarks/results/phase4/baseline-III-profile/scroll.nettrace.etlx', 'benchmarks/results/phase4/profile-IV-final03-eventpipe/session.nettrace', 'benchmarks/results/phase4/profile-IV-final03-eventpipe/session.nettrace.etlx')) { Add-CaptureArtifact $relative 'ExternalEventPipeTrace' }

$capturePlans = [Collections.Generic.List[object]]::new()
$captureOriginalTotal = [long]0
$captureStoredTotal = [long]0
foreach ($relative in ($captureCandidates.Keys | Sort-Object)) {
    $source = Resolve-CaptureMember $captureRoot $relative
    if ((Get-Item -LiteralPath $source).Length -gt $captureFileLimit) { throw "Snapshot input exceeds file limit: $relative" }
    $original = [IO.File]::ReadAllBytes($source)
    $bom = $original.Length -ge 3 -and $original[0] -eq 239 -and $original[1] -eq 187 -and $original[2] -eq 191
    $offset = if ($bom) { 3 } else { 0 }
    $text = $captureUtf8.GetString($original, $offset, $original.Length - $offset)
    if ($text.Contains([char]0)) { throw "Input is not supported UTF-8 text: $relative" }
    $newlines = @([regex]::Matches($text, '\r+\n|\r+|\n'))
    $bareLf = @(for ($i = 0; $i -lt $newlines.Count; $i++) { if ($newlines[$i].Value -eq "`n") { $i } })
    $newlineTokens = @($newlines | ForEach-Object Value)
    $lineEndings = if (!$newlines.Count) { 'None' }
        elseif (@($newlineTokens | Where-Object { $_ -ne "`r`n" }).Count -eq 0) { 'CRLF' }
        elseif ($bareLf.Count -eq $newlines.Count) { 'LF' }
        elseif (@($newlineTokens | Where-Object { $_ -ne "`r`r`n" }).Count -eq 0) { 'CRCRLF' }
        elseif (@($newlineTokens | Where-Object { $_ -ne "`r`n" -and $_ -ne "`n" }).Count -eq 0) { 'Mixed' }
        else { 'MixedTokens' }
    $encoding = [Text.UTF8Encoding]::new($bom, $true)
    $stored = [byte[]]($encoding.GetPreamble() + $encoding.GetBytes(($text -replace '\r+\n|\r+|\n', "`r`n")))
    $captureOriginalTotal += $original.Length
    $captureStoredTotal += $stored.Length
    if ($stored.Length -gt $captureFileLimit -or $captureOriginalTotal -gt $captureTotalLimit -or $captureStoredTotal -gt $captureTotalLimit) { throw 'Snapshot file/aggregate byte limit exceeded.' }
    $metadata = [ordered]@{ WorkspacePath = $relative; SnapshotPath = 'payload/' + $relative; Kind = $captureCandidates[$relative]; OriginalBytes = $original.Length; SnapshotBytes = $stored.Length; OriginalSHA256 = Get-CaptureHash $original; SnapshotSHA256 = Get-CaptureHash $stored; UTF8BOM = $bom; OriginalLineEndings = $lineEndings; OriginalNewlineCount = $newlines.Count; SnapshotLineEndings = 'CRLF'; LineEndingsNormalized = $lineEndings -notin @('None', 'CRLF') }
    if ($lineEndings -eq 'Mixed') { $metadata.OriginalBareLfLines = $bareLf }
    if ($lineEndings -eq 'MixedTokens') { $metadata.OriginalNewlineTokens = $newlineTokens }
    $capturePlans.Add([pscustomobject]@{ Metadata = [pscustomobject]$metadata; Bytes = $stored })
}

$captureInventory = [ordered]@{
    SchemaVersion = 1
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
    SnapshotSourceCommit = $SnapshotSourceCommit.ToLowerInvariant()
    RepositoryHeadAtCapture = $captureHead
    Scope = 'Delimited UTF-8 reports, tooling, harness/benchmark source and raw JSON/CSV/JSONL. Production recovered from Git commit. Artifact binaries, SDKs, bin/obj, fixtures, PNG and nettrace are references only or excluded. No overwrite or deletion.'
    MaximumFileBytes = $captureFileLimit
    MaximumSnapshotBytes = $captureTotalLimit
    FileCount = $capturePlans.Count
    OriginalTotalBytes = $captureOriginalTotal
    SnapshotTotalBytes = $captureStoredTotal
    Files = @($capturePlans | ForEach-Object Metadata)
    ArtifactReferences = $captureArtifactReferences.ToArray()
}
$captureInventoryBytes = Get-CaptureJsonBytes $captureInventory
if ($captureInventoryBytes.Length -gt $captureFileLimit -or $captureStoredTotal + $captureInventoryBytes.Length + 92 -gt $captureTotalLimit) { throw 'Inventory would exceed the snapshot file/aggregate byte limit.' }
if (Test-Path -LiteralPath $captureDestination) { throw 'Snapshot destination appeared during planning; no overwrite is permitted.' }
foreach ($plan in $capturePlans) {
    $source = Resolve-CaptureMember $captureRoot $plan.Metadata.WorkspacePath
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $plan.Metadata.OriginalSHA256) { throw "Input changed during capture: $($plan.Metadata.WorkspacePath)" }
}
$null = [IO.Directory]::CreateDirectory($captureDestination)
$null = Resolve-CaptureMember $captureRoot $DestinationRelative
foreach ($plan in $capturePlans) {
    $source = Resolve-CaptureMember $captureRoot $plan.Metadata.WorkspacePath
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $plan.Metadata.OriginalSHA256) { throw "Input changed during capture: $($plan.Metadata.WorkspacePath)" }
    Write-CaptureNew $plan.Metadata.SnapshotPath $plan.Bytes
}
Write-CaptureNew 'snapshot-inventory.json' $captureInventoryBytes
$captureInventoryHash = Get-CaptureHash $captureInventoryBytes
Write-CaptureNew 'snapshot-inventory.sha256' ($captureUtf8.GetBytes($captureInventoryHash + "  snapshot-inventory.json`r`n"))
Write-Output ([pscustomobject]@{ Status = 'Captured'; Destination = $DestinationRelative.Replace('\', '/'); SourceCommit = $SnapshotSourceCommit; Files = $capturePlans.Count; SnapshotBytes = $captureStoredTotal; InventorySHA256 = $captureInventoryHash; ArtifactReferences = $captureArtifactReferences.Count })
