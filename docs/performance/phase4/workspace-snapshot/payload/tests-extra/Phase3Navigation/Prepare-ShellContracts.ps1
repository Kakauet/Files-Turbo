$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourcePath = Join-Path $workspace 'source/src/Files.App/ViewModels/ShellViewModel.cs'
$sourceText = [IO.File]::ReadAllText($sourcePath)
$signatures = @(
    'public void CancelLoadAndClearFiles()',
    'private CancellationToken GetPropertyLoadToken(',
    'private bool CompleteItemPropertyLoad(',
    'public void CancelExtendedPropertiesLoadingForItem(',
    'public void CancelExtendedPropertiesLoading()',
    'public async Task ApplyFilesAndFoldersChangesAsync(',
    'private bool TryPublishFilesAndFoldersWithoutReset(',
    'private async Task OrderFilesAndFoldersAsync(',
    'private async Task RapidAddItemsToCollectionAsync(string? path, string? previousDir',
    'public void RefreshItems(',
    'public async Task SearchAsync(FolderSearch search)',
    'private async Task AppendSearchResultsAsync(',
    'private async Task PreloadIconsAsync(',
    'public void CancelSearch()',
    'private async Task RetryThumbnailAfterDelayAsync('
)
$methods = foreach ($signature in $signatures) {
    $start = $sourceText.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Source method missing: $signature" }
    $open = $sourceText.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($end -lt $sourceText.Length -and $depth -gt 0) {
        if ($sourceText[$end] -eq '{') { $depth++ }
        elseif ($sourceText[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced source method: $signature" }
    $sourceText.Substring($start, $end - $start)
}
$generated = '// Generated from the actual ShellViewModel methods. Do not edit.' + "`r`n" +
    'namespace Files.App.ViewModels { public partial class ShellViewModel {' + "`r`n" +
    ($methods -join "`r`n`r`n") + "`r`n}}`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ShellCore.generated.cs'), $generated)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-hash.txt'), (Get-FileHash $sourcePath -Algorithm SHA256).Hash + "`r`n")
