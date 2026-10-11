$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSourcePath = Join-Path $taskWorkspace 'source/src/Files.App/Utils/Storage/Search/FolderSearch.cs'
$taskHelperPath = Join-Path $taskWorkspace 'source/src/Files.App/Utils/Storage/Enumerators/StorageEnumerationTask.cs'
$taskSourceText = [IO.File]::ReadAllText($taskSourcePath)
$taskMethodStart = $taskSourceText.IndexOf('private async Task SearchTagsAsync(', [StringComparison]::Ordinal)
$taskBlockStart = $taskSourceText.IndexOf('var findTask = Task.Run(', $taskMethodStart, [StringComparison]::Ordinal)
$taskBlockEnd = $taskSourceText.IndexOf('if (token.IsCancellationRequested)', $taskBlockStart, [StringComparison]::Ordinal)
if ($taskMethodStart -lt 0 -or $taskBlockStart -lt 0 -or $taskBlockEnd -lt 0) { throw 'Tag lookup source block missing.' }
$taskBlock = $taskSourceText.Substring($taskBlockStart, $taskBlockEnd - $taskBlockStart)
if (!$taskBlock.Contains('StorageEnumerationTask.AwaitAsync(') -or !$taskBlock.Contains('findTask, TimeSpan.FromSeconds(5), token, abandoned => abandoned.Item1.Dispose()')) { throw 'Native cleanup wiring changed; review extraction.' }
$taskGenerated = @"
// Generated literally from the production tag-search lookup. Do not edit.
#nullable enable
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
namespace Files.App.Utils.Storage;
internal static class TagLookup
{
    public static async Task<(FindCloseSafeHandle? Handle, WIN32_FIND_DATAW Data)> OpenAsync(string filePath, CancellationToken token)
    {
        var match = new Match(filePath);
        $taskBlock
        return (hFile, findData);
    }
    private sealed record Match(string FilePath);
}
"@
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'TagLookup.generated.cs'), ($taskGenerated -replace '\r?\n', "`r`n"))
$taskMetadata = [ordered]@{ SourcePath = $taskSourcePath; SourceSha256 = (Get-FileHash $taskSourcePath -Algorithm SHA256).Hash; HelperPath = $taskHelperPath; HelperSha256 = (Get-FileHash $taskHelperPath -Algorithm SHA256).Hash; GeneratedSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'TagLookup.generated.cs') -Algorithm SHA256).Hash; Extraction = 'Literal native lookup block; providers replaced, five-second timeout unchanged.' }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-metadata.json'), ($taskMetadata | ConvertTo-Json) + "`r`n")
