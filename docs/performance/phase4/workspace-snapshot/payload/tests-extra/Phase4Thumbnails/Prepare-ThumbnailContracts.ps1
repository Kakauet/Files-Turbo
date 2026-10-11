$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSourcePath = Join-Path $taskWorkspace 'source/src/Files.App/ViewModels/ShellViewModel.cs'
$taskSource = [IO.File]::ReadAllText($taskSourcePath)
$taskMethods = foreach ($taskSignature in @(
    'public async Task<InitialThumbnailLoadResult> ApplyCachedThumbnailOrPreloadedIconAsync(',
    'public async Task<InitialThumbnailLoadResult> LoadViewportThumbnailAsync(',
    'private async Task<bool> ApplyThumbnailImageAsync(',
    'private static async Task<BitmapImage?> DecodeThumbnailAsync(',
    'private async Task LoadThumbnailAsync(',
    'private async Task LoadThumbnailOverlayAsync(',
    'private async Task RetryThumbnailAfterDelayAsync(',
    'private CancellationToken GetPropertyLoadToken('
)) {
    $taskStart = $taskSource.IndexOf($taskSignature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Source method missing: $taskSignature" }
    $taskOpen = $taskSource.IndexOf('{', $taskStart)
    $taskDepth = 1
    $taskEnd = $taskOpen + 1
    while ($taskEnd -lt $taskSource.Length -and $taskDepth -gt 0) {
        if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw "Unbalanced source: $taskSignature" }
    $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskExpressions = foreach ($taskSignature in @(
    'public readonly record struct InitialThumbnailLoadResult(',
    'private bool CanReuseInitialThumbnail(',
    'public bool IsViewportThumbnailCurrent(',
    'private uint GetPreloadIconSize()',
    'private static bool HasPerFileIcon(',
    'private static readonly string[] _perFileIconExtensions'
)) {
    $taskStart = $taskSource.IndexOf($taskSignature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Source expression missing: $taskSignature" }
    $taskEnd = $taskSource.IndexOf(';', $taskStart) + 1
    $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskGenerated = '// Source methods extracted verbatim; run Prepare-ThumbnailContracts.ps1 after source changes.' + "`r`n" +
    '#nullable enable' + "`r`n" +
    'namespace Files.App.ViewModels { public partial class ShellViewModel {' + "`r`n" +
    (($taskMethods + $taskExpressions) -join "`r`n`r`n") + "`r`n}}`r`n"
$taskBitmapSourcePath = Join-Path $taskWorkspace 'source/src/Files.App/Helpers/BitmapHelper.cs'
$taskBitmapSource = [IO.File]::ReadAllText($taskBitmapSourcePath)
$taskBitmapMethods = foreach ($taskBitmapSignature in @(
    'private static void SetDecodeSize(',
    'private static bool ShouldUseSynchronousSmallThumbnail(',
    'private static bool TryGetPngDimensions('
)) {
    $taskBitmapStart = $taskBitmapSource.IndexOf($taskBitmapSignature, [StringComparison]::Ordinal)
    if ($taskBitmapStart -lt 0) { throw "Source method missing: $taskBitmapSignature" }
    $taskBitmapOpen = $taskBitmapSource.IndexOf('{', $taskBitmapStart)
    $taskBitmapDepth = 1
    $taskBitmapEnd = $taskBitmapOpen + 1
    while ($taskBitmapEnd -lt $taskBitmapSource.Length -and $taskBitmapDepth -gt 0) {
        if ($taskBitmapSource[$taskBitmapEnd] -eq '{') { $taskBitmapDepth++ }
        elseif ($taskBitmapSource[$taskBitmapEnd] -eq '}') { $taskBitmapDepth-- }
        $taskBitmapEnd++
    }
    if ($taskBitmapDepth -ne 0) { throw "Unbalanced source: $taskBitmapSignature" }
    $taskBitmapSource.Substring($taskBitmapStart, $taskBitmapEnd - $taskBitmapStart)
}
$taskGenerated += 'namespace Files.App.Helpers { public static partial class BitmapHelper {' + "`r`n" +
    ($taskBitmapMethods -join "`r`n`r`n") + "`r`n}}`r`n"
$taskGenerated = 'using System.Buffers.Binary;' + "`r`n" + $taskGenerated
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ThumbnailCore.generated.cs'), $taskGenerated.Replace("`r`n", "`n").Replace("`n", "`r`n"))
$taskPaths = @(
    'source/src/Files.App/ViewModels/ShellViewModel.cs',
    'source/src/Files.App/Helpers/BitmapHelper.cs',
    'source/src/Files.App/Utils/Storage/Helpers/FileThumbnailHelper.cs',
    'source/src/Files.App.Storage/Windows/Managers/STATask.cs'
)
$taskHashes = foreach ($taskPath in $taskPaths) {
    @{ Path = $taskPath; SHA256 = (Get-FileHash (Join-Path $taskWorkspace $taskPath) -Algorithm SHA256).Hash }
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-hashes.json'), ($taskHashes | ConvertTo-Json -Depth 3) + "`r`n")
