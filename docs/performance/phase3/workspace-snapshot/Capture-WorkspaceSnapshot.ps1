#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [string[]]$ExtraPaths = @()
)
$ErrorActionPreference = 'Stop'
$captureRoot = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\')
$captureInventoryPath = Join-Path $PSScriptRoot 'snapshot-inventory.json'
$capturePrevious = Get-Content -LiteralPath $captureInventoryPath -Raw | ConvertFrom-Json
if ((& git -C (Join-Path $captureRoot 'source') rev-parse files-turbo-phase3-start) -ne 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d') { throw 'Unexpected Phase II source reference.' }
function Resolve-CapturePath([string]$Root, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[/\\])\.\.([/\\]|$)' -or $Relative.Contains(':')) { throw "Unsafe member: $Relative" }
    $capturePath = [IO.Path]::GetFullPath($Relative, $Root)
    if (!$capturePath.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Member escaped its root.' }
    $captureCursor = $capturePath
    while ($captureCursor.Length -ge $Root.Length) {
        if ((Test-Path -LiteralPath $captureCursor) -and ((Get-Item -LiteralPath $captureCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point rejected: $captureCursor" }
        if ($captureCursor -eq $Root) { break }
        $captureCursor = Split-Path $captureCursor -Parent
    }
    return $capturePath
}
function Write-CaptureBytes([string]$Path, [byte[]]$Bytes, [switch]$CreateNew) {
    $null = [IO.Directory]::CreateDirectory((Split-Path $Path -Parent))
    $captureMode = if ($CreateNew) { [IO.FileMode]::CreateNew } else { [IO.FileMode]::Create }
    $captureStream = [IO.FileStream]::new($Path, $captureMode, [IO.FileAccess]::Write)
    try { $captureStream.Write($Bytes, 0, $Bytes.Length) } finally { $captureStream.Dispose() }
}
function Get-CaptureJsonBytes($Value) {
    return [Text.UTF8Encoding]::new($false).GetBytes((($Value | ConvertTo-Json -Depth 15) -replace '\r?\n', "`r`n"))
}
$captureCandidates = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($captureEntry in $capturePrevious.Files) { $null = $captureCandidates.Add($captureEntry.WorkspacePath) }
foreach ($captureDirectory in @('tests-extra/Phase3Navigation', 'tests-extra/Viewport', 'tests-extra/Adaptive', 'tests-extra/EntryConstruction', 'tests-extra/Phase3PathNavigation', 'tests-extra/Phase3WorkingDirectory', 'benchmarks/Phase3/Search')) {
    $captureFullDirectory = Resolve-CapturePath $captureRoot $captureDirectory
    if (Test-Path -LiteralPath $captureFullDirectory) {
        foreach ($captureFile in (Get-ChildItem -LiteralPath $captureFullDirectory -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in @('.cs', '.csproj', '.ps1', '.txt', '.json') })) {
            $null = $captureCandidates.Add([IO.Path]::GetRelativePath($captureRoot, $captureFile.FullName).Replace('\', '/'))
        }
    }
}
foreach ($captureFile in (Get-ChildItem -LiteralPath (Join-Path $captureRoot 'tooling') -Filter '*Phase3*.ps1' -File)) { $null = $captureCandidates.Add('tooling/' + $captureFile.Name) }
foreach ($captureRelative in @(
    'reports/phase3-navigation-shell-contracts-final-03.json',
    'reports/phase3-navigation-shell-contracts-final-04.json',
    'reports/phase3-path-navigation-contracts-final-02.json',
    'reports/phase3-working-directory-contracts-final-01.json',
    'reports/PHASE3_PATH_NAVIGATION.md',
    'artifacts/phase3-entry-construction-final-02/entry-contracts.json',
    'artifacts/phase3-entry-construction-build/build-final-02.log',
    'benchmarks/results/phase3/eventpipe03-20261011/windows-reproduction.json',
    'benchmarks/results/phase3/eventpipe04-20261011/windows-selection.json'
)) { if (Test-Path -LiteralPath (Resolve-CapturePath $captureRoot $captureRelative)) { $null = $captureCandidates.Add($captureRelative) } }
foreach ($captureRelative in $ExtraPaths) { $null = $captureCandidates.Add($captureRelative.Replace('\', '/')) }
$capturePlans = foreach ($captureRelative in ($captureCandidates | Sort-Object)) {
    $captureRootMorning = $captureRelative -ceq 'MORNING_REPORT_PHASE3.md'
    if ((!$captureRootMorning -and $captureRelative -notmatch '^(tooling|tests-extra|benchmarks|reports|artifacts)/') -or $captureRelative -match '(^|/)(bin|obj|fixture|fixtures|node_modules)(/|$)' -or [IO.Path]::GetExtension($captureRelative) -notin @('.cs', '.csproj', '.ps1', '.txt', '.json', '.md', '.log')) { throw "Excluded snapshot member: $captureRelative" }
    $captureSource = Resolve-CapturePath $captureRoot $captureRelative
    if (!(Test-Path -LiteralPath $captureSource -PathType Leaf)) { throw "Source member missing: $captureRelative" }
    if ((Get-Item -LiteralPath $captureSource).Length -gt 512KB) { throw "Snapshot member exceeds the small-file bound: $captureRelative" }
    $captureOriginalBytes = [IO.File]::ReadAllBytes($captureSource)
    $captureBom = $captureOriginalBytes.Length -ge 3 -and $captureOriginalBytes[0] -eq 239 -and $captureOriginalBytes[1] -eq 187 -and $captureOriginalBytes[2] -eq 191
    $captureOffset = if ($captureBom) { 3 } else { 0 }
    $captureText = [Text.UTF8Encoding]::new($false, $true).GetString($captureOriginalBytes, $captureOffset, $captureOriginalBytes.Length - $captureOffset)
    if ($captureText -match '\r(?!\n)') { throw "Unsupported lone CR: $captureRelative" }
    $captureNewlines = @([regex]::Matches($captureText, '\r\n|(?<!\r)\n'))
    $captureBareLfLines = @(for ($captureLineIndex = 0; $captureLineIndex -lt $captureNewlines.Count; $captureLineIndex++) { if ($captureNewlines[$captureLineIndex].Value -eq "`n") { $captureLineIndex } })
    $captureLineEndings = if (!$captureNewlines.Count) { 'None' } elseif (!$captureBareLfLines.Count) { 'CRLF' } elseif ($captureBareLfLines.Count -eq $captureNewlines.Count) { 'LF' } else { 'Mixed' }
    $captureEncoding = [Text.UTF8Encoding]::new($captureBom)
    $captureStoredBytes = [byte[]]($captureEncoding.GetPreamble() + $captureEncoding.GetBytes(($captureText -replace '\r?\n', "`r`n")))
    $captureMetadata = [ordered]@{
        WorkspacePath = $captureRelative
        OriginalBytes = $captureOriginalBytes.Length
        OriginalSHA256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($captureOriginalBytes))
        SnapshotSHA256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($captureStoredBytes))
        OriginalLineEndings = $captureLineEndings
        UTF8BOM = $captureBom
    }
    if ($captureLineEndings -eq 'Mixed') { $captureMetadata.OriginalBareLfLines = $captureBareLfLines }
    [pscustomobject]@{ Metadata = [pscustomobject]$captureMetadata; Bytes = $captureStoredBytes; Destination = (Resolve-CapturePath $PSScriptRoot $captureRelative) }
}
$captureHistoryName = 'capture-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$captureHistoryInventory = @()
foreach ($captureEntry in $capturePrevious.Files) {
    $captureOldRelative = if ($captureEntry.SnapshotPath) { $captureEntry.SnapshotPath } else { $captureEntry.WorkspacePath }
    $captureOldPath = Resolve-CapturePath $PSScriptRoot $captureOldRelative
    if ((Get-FileHash -LiteralPath $captureOldPath).Hash -ne $captureEntry.SnapshotSHA256) { throw "Previous snapshot member changed: $($captureEntry.WorkspacePath)" }
    $captureHistoricalRelative = "history/$captureHistoryName/$($captureEntry.WorkspacePath)"
    $captureHistoricalEntry = $captureEntry | Select-Object * -ExcludeProperty SnapshotPath
    $captureHistoricalEntry | Add-Member -NotePropertyName SnapshotPath -NotePropertyValue $captureHistoricalRelative
    $captureHistoryInventory += $captureHistoricalEntry
}
# Preserve the complete prior inventory and payload before refreshing current copies.
foreach ($captureEntry in $capturePrevious.Files) {
    $captureOldRelative = if ($captureEntry.SnapshotPath) { $captureEntry.SnapshotPath } else { $captureEntry.WorkspacePath }
    Write-CaptureBytes (Resolve-CapturePath $PSScriptRoot "history/$captureHistoryName/$($captureEntry.WorkspacePath)") ([IO.File]::ReadAllBytes((Resolve-CapturePath $PSScriptRoot $captureOldRelative))) -CreateNew
}
$capturePrevious.Files = $captureHistoryInventory
Write-CaptureBytes (Join-Path $PSScriptRoot "snapshot-inventory-$captureHistoryName.json") (Get-CaptureJsonBytes $capturePrevious) -CreateNew
foreach ($capturePlan in $capturePlans) { Write-CaptureBytes $capturePlan.Destination $capturePlan.Bytes }
$captureInventory = [ordered]@{
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
    PhaseIIIStartCommit = 'b18efc91ed3033081691f407a2bb7b5ee3d7d21d'
    OriginalBaselineCommit = '1994d9a4269f5144c70bc67d6a48de4a20616912'
    RepositoryHeadAtCapture = (& git -C (Join-Path $captureRoot 'source') rev-parse HEAD)
    Scope = 'Small source/script/project/contract/report files; production code recovered from containing Git commit. No SDK/binaries/packages/fixtures/CSV banks/nettrace. Historical copies retained with their inventories.'
    PreviousInventory = "snapshot-inventory-$captureHistoryName.json"
    FileCount = $capturePlans.Count
    OriginalTotalBytes = ($capturePlans.Metadata.OriginalBytes | Measure-Object -Sum).Sum
    Files = @($capturePlans.Metadata)
}
Write-CaptureBytes $captureInventoryPath (Get-CaptureJsonBytes $captureInventory)
Write-Output "Snapshot files: $($capturePlans.Count). Previous inventory: snapshot-inventory-$captureHistoryName.json."
