$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSourcePath = Join-Path $taskWorkspace 'source/src/Files.App/ViewModels/ShellViewModel.cs'
$taskSource = [IO.File]::ReadAllText($taskSourcePath)
$taskSourceHash = (Get-FileHash $taskSourcePath -Algorithm SHA256).Hash

function Get-TaskMethod([string]$content, [string]$signature) {
    $taskStart = $content.IndexOf($signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Method missing: $signature" }
    $taskOpen = $content.IndexOf('{', $taskStart)
    $taskEnd = $taskOpen + 1
    $taskDepth = 1
    while ($taskEnd -lt $content.Length -and $taskDepth -gt 0) {
        if ($content[$taskEnd] -eq '{') { $taskDepth++ }
        if ($content[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw "Unterminated method: $signature" }
    $content.Substring($taskStart, $taskEnd - $taskStart)
}

$taskMethods = @(
    (Get-TaskMethod $taskSource 'public async Task ApplyFilesAndFoldersChangesAsync('),
    (Get-TaskMethod $taskSource 'private bool TryPublishFilesAndFoldersWithoutReset(')
)
$taskFilterStart = $taskSource.IndexOf('private static bool MatchesFilter(', [StringComparison]::Ordinal)
if ($taskFilterStart -lt 0) { throw 'MatchesFilter missing.' }
$taskFilterEnd = $taskSource.IndexOf(';', $taskFilterStart) + 1
if ($taskFilterEnd -le $taskFilterStart) { throw 'MatchesFilter expression terminator missing.' }
$taskMethods += $taskSource.Substring($taskFilterStart, $taskFilterEnd - $taskFilterStart)

$taskLinkedFiles = foreach ($taskRelativePath in @(
    'source/src/Files.App/Utils/Storage/Collection/BulkConcurrentObservableCollection.cs',
    'source/src/Files.App/Utils/Storage/Collection/ConcurrentCollection.cs',
    'source/src/Files.App/Utils/Storage/Collection/BlockingListEnumerator.cs',
    'source/src/Files.App/Utils/Storage/Collection/GroupedCollection.cs',
    'source/src/Files.App/Utils/Storage/Collection/IGroupableItem.cs',
    'source/src/Files.App/Utils/Storage/Collection/IGroupedCollectionHeader.cs',
    'source/src/Files.App/Helpers/CollectionDebugView.cs'
)) {
    $taskPath = Join-Path $taskWorkspace $taskRelativePath
    [ordered]@{ Path = $taskPath; Sha256 = (Get-FileHash $taskPath -Algorithm SHA256).Hash }
}
$taskFingerprintBytes = [Text.Encoding]::UTF8.GetBytes(($taskLinkedFiles.Sha256 -join '|'))
$taskLinkedFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskFingerprintBytes))
$taskGenerated = @"
// Production methods extracted literally; collection implementations are linked without edits.
#nullable enable
namespace Files.App.ViewModels { public partial class ShellViewModel {
$($taskMethods -join "`r`n`r`n")
} }
internal static class PublicationBuildStamp {
    public const string SourceSha256 = "$taskSourceHash";
    public const string LinkedFingerprint = "$taskLinkedFingerprint";
}
"@
$taskGeneratedPath = Join-Path $PSScriptRoot 'PublicationCore.generated.cs'
[IO.File]::WriteAllText($taskGeneratedPath, ($taskGenerated -replace '\r?\n', "`r`n") + "`r`n")
$taskMetadata = [ordered]@{
    SourcePath = $taskSourcePath
    SourceSha256 = $taskSourceHash
    GeneratedPath = $taskGeneratedPath
    GeneratedSha256 = (Get-FileHash $taskGeneratedPath -Algorithm SHA256).Hash
    LinkedFiles = @($taskLinkedFiles)
    LinkedFingerprint = $taskLinkedFingerprint
    SourceRevision = (& git -C (Join-Path $taskWorkspace 'source') rev-parse HEAD)
    Scope = 'Literal ApplyFilesAndFoldersChangesAsync, TryPublishFilesAndFoldersWithoutReset and MatchesFilter; real bulk/concurrent/grouped collections, enumerator, nested EventArgsCache and debug view. Controlled dispatcher and app/model doubles. No WinUI rendering.'
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-metadata.json'), ($taskMetadata | ConvertTo-Json -Depth 6) + "`r`n")
if ((Get-FileHash $taskSourcePath -Algorithm SHA256).Hash -ne $taskSourceHash) { throw 'Production source changed during extraction; prepare again.' }
Write-Output "Prepared publication contracts from $($taskMetadata.SourceRevision). No build or tests executed."
