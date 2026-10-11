#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [string]$SnapshotRoot,
    [switch]$ValidateOnly,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
if ($Apply -and $ValidateOnly) { throw 'Choose Apply or ValidateOnly; default behavior is ValidateOnly.' }
$restoreRoot = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (!$SnapshotRoot) {
    $embeddedRoot = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
    $SnapshotRoot = if (Test-Path -LiteralPath (Join-Path $embeddedRoot 'snapshot-inventory.json') -PathType Leaf) { $embeddedRoot } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'source/docs/performance/phase4/workspace-snapshot' }
}
$restoreSnapshot = [IO.Path]::GetFullPath($SnapshotRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$restoreUtf8 = [Text.UTF8Encoding]::new($false, $true)

function Resolve-RestoreMember([string]$Root, [string]$Relative) {
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
function Get-RestoreHash([byte[]]$Bytes) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)) }

if (!(Test-Path -LiteralPath $restoreRoot -PathType Container)) { throw 'Target workspace must already exist.' }
if (!(Test-Path -LiteralPath $restoreSnapshot -PathType Container)) { throw 'Snapshot root must already exist.' }
$inventoryPath = Resolve-RestoreMember $restoreSnapshot 'snapshot-inventory.json'
$hashPath = Resolve-RestoreMember $restoreSnapshot 'snapshot-inventory.sha256'
if ((Get-Item -LiteralPath $inventoryPath).Length -gt 64MB) { throw 'Snapshot inventory exceeds the hard file bound.' }
if ((Get-Item -LiteralPath $hashPath).Length -gt 256) { throw 'Snapshot hash sidecar exceeds its byte bound.' }
$hashSidecarLength = (Get-Item -LiteralPath $hashPath).Length
$inventoryBytes = [IO.File]::ReadAllBytes($inventoryPath)
if ($inventoryBytes.Length -gt 64MB) { throw 'Snapshot inventory exceeds the hard file bound.' }
$expectedInventoryHash = ([IO.File]::ReadAllText($hashPath)).Trim()
if ($expectedInventoryHash -notmatch '^([0-9A-Fa-f]{64})\s+snapshot-inventory\.json$') { throw 'Invalid inventory hash sidecar.' }
if ((Get-RestoreHash $inventoryBytes) -ne $Matches[1].ToUpperInvariant()) { throw 'Snapshot inventory hash mismatch.' }
$restoreInventory = $restoreUtf8.GetString($inventoryBytes) | ConvertFrom-Json
if ($restoreInventory.SchemaVersion -ne 1 -or $restoreInventory.SnapshotSourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Unsupported snapshot schema or production commit.' }
$fileLimit = [long]$restoreInventory.MaximumFileBytes
$totalLimit = [long]$restoreInventory.MaximumSnapshotBytes
if ($fileLimit -lt 1 -or $fileLimit -gt 64MB -or $totalLimit -lt 1 -or $totalLimit -gt 128MB) { throw 'Snapshot byte limits exceed the accepted hard bounds.' }
$seenTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$restorePlans = [Collections.Generic.List[object]]::new()
$originalTotal = [long]0
$storedTotal = [long]0
$existingMatching = 0
foreach ($entry in $restoreInventory.Files) {
    if ($entry.WorkspacePath -notmatch '^(reports|tooling|tests-extra|benchmarks|artifacts)/' -and $entry.WorkspacePath -cne 'MORNING_REPORT_PHASE4_FINAL.md' -and $entry.WorkspacePath -notin @('NuGet.Config', 'nuget.config')) { throw "Unsupported workspace member: $($entry.WorkspacePath)" }
    if ($entry.WorkspacePath -match '(^|/)(bin|obj|\.git|\.vs|node_modules|fixture|fixtures)(/|$)' -or [IO.Path]::GetExtension($entry.WorkspacePath).ToLowerInvariant() -notin @('.cs', '.csproj', '.ps1', '.txt', '.md', '.json', '.csv', '.jsonl', '.config', '.props', '.targets')) { throw "Excluded member: $($entry.WorkspacePath)" }
    if ($entry.WorkspacePath.Contains('\') -or $entry.SnapshotPath -cne ('payload/' + $entry.WorkspacePath)) { throw 'Snapshot path must map to its portable declared workspace member.' }
    if ($entry.OriginalSHA256 -notmatch '^[0-9A-Fa-f]{64}$' -or $entry.SnapshotSHA256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid member hash.' }
    $source = Resolve-RestoreMember $restoreSnapshot $entry.SnapshotPath
    $target = Resolve-RestoreMember $restoreRoot $entry.WorkspacePath
    if (!$seenTargets.Add($target) -or !$seenSources.Add($source)) { throw 'Duplicate snapshot member.' }
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing snapshot member: $($entry.WorkspacePath)" }
    $sourceLength = (Get-Item -LiteralPath $source).Length
    if ($sourceLength -gt $fileLimit -or $sourceLength -ne [long]$entry.SnapshotBytes -or [long]$entry.OriginalBytes -lt 0 -or [long]$entry.OriginalBytes -gt $fileLimit) { throw 'Snapshot member byte bound/count mismatch.' }
    $stored = [IO.File]::ReadAllBytes($source)
    if ((Get-RestoreHash $stored) -ne $entry.SnapshotSHA256) { throw "Snapshot hash mismatch: $($entry.WorkspacePath)" }
    $bom = $stored.Length -ge 3 -and $stored[0] -eq 239 -and $stored[1] -eq 187 -and $stored[2] -eq 191
    if ($bom -ne [bool]$entry.UTF8BOM) { throw 'Snapshot UTF-8 BOM metadata mismatch.' }
    $offset = if ($bom) { 3 } else { 0 }
    $text = $restoreUtf8.GetString($stored, $offset, $stored.Length - $offset)
    if ($text.Contains([char]0) -or $text -match '(?<!\r)\n|\r(?!\n)') { throw 'Snapshot text is not normalized CRLF UTF-8.' }
    $newlineCount = [regex]::Matches($text, '\r\n').Count
    if ($newlineCount -ne [long]$entry.OriginalNewlineCount) { throw 'Newline metadata mismatch.' }
    switch ($entry.OriginalLineEndings) {
        'None' { if ($newlineCount) { throw 'Unexpected newline in a newline-free input.' } }
        'CRLF' { }
        'LF' { $text = $text.Replace("`r`n", "`n") }
        'CRCRLF' { $text = $text.Replace("`r`n", "`r`r`n") }
        'Mixed' {
            $lfIndices = [Collections.Generic.HashSet[int]]::new()
            foreach ($index in $entry.OriginalBareLfLines) {
                if ([long]$index -lt 0 -or [long]$index -ge $newlineCount -or !$lfIndices.Add([int]$index)) { throw 'Invalid mixed-newline index.' }
            }
            if (!$lfIndices.Count -or $lfIndices.Count -eq $newlineCount) { throw 'Mixed-newline metadata is not mixed.' }
            $lines = $text.Split([string[]]@("`r`n"), [StringSplitOptions]::None)
            $builder = [Text.StringBuilder]::new()
            for ($i = 0; $i -lt $lines.Length - 1; $i++) {
                $null = $builder.Append($lines[$i])
                $null = $builder.Append($(if ($lfIndices.Contains($i)) { "`n" } else { "`r`n" }))
            }
            $null = $builder.Append($lines[-1])
            $text = $builder.ToString()
        }
        'MixedTokens' {
            $tokens = @($entry.OriginalNewlineTokens)
            if ($tokens.Count -ne $newlineCount) { throw 'Newline token count mismatch.' }
            foreach ($token in $tokens) {
                if ($token -isnot [string] -or $token -notmatch '^(?:\r+\n?|\n)$') { throw 'Invalid original newline token.' }
            }
            $lines = $text.Split([string[]]@("`r`n"), [StringSplitOptions]::None)
            $builder = [Text.StringBuilder]::new()
            for ($i = 0; $i -lt $tokens.Count; $i++) {
                $null = $builder.Append($lines[$i])
                $null = $builder.Append($tokens[$i])
            }
            $null = $builder.Append($lines[-1])
            $text = $builder.ToString()
        }
        default { throw 'Unsupported original newline metadata.' }
    }
    $encoding = [Text.UTF8Encoding]::new($bom, $true)
    $original = [byte[]]($encoding.GetPreamble() + $encoding.GetBytes($text))
    if ($original.Length -ne [long]$entry.OriginalBytes -or (Get-RestoreHash $original) -ne $entry.OriginalSHA256) { throw "Original bytes cannot be reconstructed: $($entry.WorkspacePath)" }
    $originalTotal += $original.Length
    $storedTotal += $stored.Length
    if ($originalTotal -gt $totalLimit -or $storedTotal + $inventoryBytes.Length + $hashSidecarLength -gt $totalLimit) { throw 'Snapshot aggregate byte bound exceeded.' }
    if (Test-Path -LiteralPath $target) {
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.OriginalSHA256) { throw "Existing workspace member conflicts; no replacement is supported: $($entry.WorkspacePath)" }
        $existingMatching++
    }
    $restorePlans.Add([pscustomobject]@{ WorkspacePath = $entry.WorkspacePath; Bytes = $original; SHA256 = $entry.OriginalSHA256 })
}
if ($restorePlans.Count -ne [long]$restoreInventory.FileCount -or $originalTotal -ne [long]$restoreInventory.OriginalTotalBytes -or $storedTotal -ne [long]$restoreInventory.SnapshotTotalBytes) { throw 'Snapshot aggregate count/byte metadata mismatch.' }
foreach ($reference in $restoreInventory.ArtifactReferences) {
    $null = Resolve-RestoreMember $restoreRoot $reference.WorkspacePath
    if ($reference.Copied -ne $false -or [long]$reference.Bytes -lt 0 -or $reference.SHA256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid external artifact reference metadata.' }
}

$created = 0
if ($Apply) {
    foreach ($plan in $restorePlans) {
        $target = Resolve-RestoreMember $restoreRoot $plan.WorkspacePath
        if (Test-Path -LiteralPath $target) {
            if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $plan.SHA256) { throw "Workspace member changed after validation: $($plan.WorkspacePath)" }
            continue
        }
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        $target = Resolve-RestoreMember $restoreRoot $plan.WorkspacePath
        $stream = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($plan.Bytes, 0, $plan.Bytes.Length) } finally { $stream.Dispose() }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $plan.SHA256) { throw 'Restored file hash mismatch.' }
        $created++
    }
}
Write-Output ([pscustomobject]@{ Status = if ($Apply) { 'Applied' } else { 'ValidatedOnly' }; SnapshotSourceCommit = $restoreInventory.SnapshotSourceCommit; VerifiedFiles = $restorePlans.Count; ExistingMatching = $existingMatching; MissingFiles = $restorePlans.Count - $existingMatching; CreatedFiles = $created; OriginalBytes = $originalTotal; SnapshotBytes = $storedTotal; ArtifactReferences = @($restoreInventory.ArtifactReferences).Count; Scope = 'Snapshot payload hashes and exact reconstructed original bytes verified. Artifact binaries are external references, not restored or asserted present.' })
