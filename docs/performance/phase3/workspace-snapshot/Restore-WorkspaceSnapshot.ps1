#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$WorkspaceRoot,
    [ValidatePattern('^[a-zA-Z0-9_-]+\.json$')][string]$InventoryName = 'snapshot-inventory.json'
)
$ErrorActionPreference = 'Stop'
$restoreRoot = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $restoreRoot 'source/.git'))) { throw 'Workspace must contain the final source Git checkout.' }
$restoreInventory = Get-Content -LiteralPath (Join-Path $PSScriptRoot $InventoryName) -Raw | ConvertFrom-Json
function Resolve-RestorePath([string]$Root, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[/\\])\.\.([/\\]|$)' -or $Relative.Contains(':')) { throw "Unsafe snapshot member: $Relative" }
    $restorePath = [IO.Path]::GetFullPath($Relative, $Root)
    if (!$restorePath.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Snapshot member escaped its root.' }
    $restoreCursor = $restorePath
    while ($restoreCursor.Length -ge $Root.Length) {
        if ((Test-Path -LiteralPath $restoreCursor) -and ((Get-Item -LiteralPath $restoreCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point rejected: $restoreCursor" }
        if ($restoreCursor -eq $Root) { break }
        $restoreCursor = Split-Path $restoreCursor -Parent
    }
    return $restorePath
}
$restorePlan = foreach ($restoreEntry in $restoreInventory.Files) {
    $restoreMember = if ($restoreEntry.SnapshotPath) { $restoreEntry.SnapshotPath } else { $restoreEntry.WorkspacePath }
    $restoreSource = Resolve-RestorePath $PSScriptRoot $restoreMember
    if ((Get-FileHash -LiteralPath $restoreSource).Hash -ne $restoreEntry.SnapshotSHA256) { throw "Snapshot member changed: $($restoreEntry.WorkspacePath)" }
    $restoreText = [IO.File]::ReadAllText($restoreSource)
    if ($restoreEntry.OriginalLineEndings -eq 'LF') { $restoreText = $restoreText.Replace("`r`n", "`n") }
    elseif ($restoreEntry.OriginalLineEndings -eq 'Mixed') {
        $restoreLines = $restoreText.Split([string[]]@("`r`n"), [StringSplitOptions]::None)
        $restoreBuilder = [Text.StringBuilder]::new()
        for ($restoreLineIndex = 0; $restoreLineIndex -lt $restoreLines.Length - 1; $restoreLineIndex++) {
            $null = $restoreBuilder.Append($restoreLines[$restoreLineIndex])
            $null = $restoreBuilder.Append($(if ($restoreLineIndex -in $restoreEntry.OriginalBareLfLines) { "`n" } else { "`r`n" }))
        }
        $null = $restoreBuilder.Append($restoreLines[-1])
        $restoreText = $restoreBuilder.ToString()
    }
    $restoreEncoding = [Text.UTF8Encoding]::new([bool]$restoreEntry.UTF8BOM)
    $restoreBytes = [byte[]]($restoreEncoding.GetPreamble() + $restoreEncoding.GetBytes($restoreText))
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($restoreBytes)) -ne $restoreEntry.OriginalSHA256) { throw "Original bytes could not be reconstructed: $($restoreEntry.WorkspacePath)" }
    $restoreTarget = Resolve-RestorePath $restoreRoot $restoreEntry.WorkspacePath
    if (Test-Path -LiteralPath $restoreTarget) {
        if (!(Test-Path -LiteralPath $restoreTarget -PathType Leaf) -or (Get-FileHash -LiteralPath $restoreTarget).Hash -ne $restoreEntry.OriginalSHA256) { throw "Existing workspace member differs; preserve it before restoring: $($restoreEntry.WorkspacePath)" }
    }
    [pscustomobject]@{ Path = $restoreTarget; Bytes = $restoreBytes; OriginalSHA256 = $restoreEntry.OriginalSHA256 }
}
$restoreCreated = 0
foreach ($restoreItem in $restorePlan) {
    if (Test-Path -LiteralPath $restoreItem.Path) {
        if ((Get-FileHash -LiteralPath $restoreItem.Path).Hash -ne $restoreItem.OriginalSHA256) { throw 'Workspace member changed during restore.' }
        continue
    }
    if ($PSCmdlet.ShouldProcess($restoreItem.Path, 'Create recovered workspace file')) {
        $null = [IO.Directory]::CreateDirectory((Split-Path $restoreItem.Path -Parent))
        $restoreStream = [IO.FileStream]::new($restoreItem.Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $restoreStream.Write($restoreItem.Bytes, 0, $restoreItem.Bytes.Length) } finally { $restoreStream.Dispose() }
        $restoreCreated++
    }
}
Write-Output "Snapshot members verified: $($restorePlan.Count). New files created: $restoreCreated."
