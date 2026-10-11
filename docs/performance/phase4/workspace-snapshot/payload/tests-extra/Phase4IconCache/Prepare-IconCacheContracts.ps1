$ErrorActionPreference = 'Stop'
$taskCrLf = [string][char]13 + [char]10
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskIconPath = Join-Path $taskWorkspace 'source/src/Files.App/Services/Storage/IconCacheService.cs'
$taskBitmapPath = Join-Path $taskWorkspace 'source/src/Files.App/Helpers/BitmapHelper.cs'
$taskIconHash = (Get-FileHash -LiteralPath $taskIconPath -Algorithm SHA256).Hash
$taskBitmapHash = (Get-FileHash -LiteralPath $taskBitmapPath -Algorithm SHA256).Hash
$taskBitmapSource = [IO.File]::ReadAllText($taskBitmapPath)
$taskRevision = (& git -C (Join-Path $taskWorkspace 'source') rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Source revision could not be resolved.' }

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
    (Get-TaskMethod $taskBitmapSource 'public static async Task<BitmapImage?> ToBitmapAsync('),
    (Get-TaskMethod $taskBitmapSource 'private static bool ShouldUseSynchronousSmallThumbnail('),
    (Get-TaskMethod $taskBitmapSource 'private static void SetDecodeSize('),
    (Get-TaskMethod $taskBitmapSource 'private static bool TryGetPngDimensions(')
)
$taskGenerated = @"
// Bitmap methods extracted literally; IconCacheService is linked directly from production.
#nullable enable
using System.Buffers.Binary;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
namespace Files.App.Helpers { internal static class BitmapHelper {
$($taskMethods -join [Environment]::NewLine)
} }
internal static class IconCacheBuildStamp {
    public const string IconSourceSha256 = "$taskIconHash";
    public const string BitmapSourceSha256 = "$taskBitmapHash";
    public const string SourceRevision = "$taskRevision";
}
"@
$taskGeneratedPath = Join-Path $PSScriptRoot 'BitmapCore.generated.cs'
[IO.File]::WriteAllText($taskGeneratedPath, ($taskGenerated -replace '\r?\n', $taskCrLf) + $taskCrLf, [Text.UTF8Encoding]::new($false))
$taskMetadata = [ordered]@{
    SourceRevision = $taskRevision
    IconSourcePath = $taskIconPath
    IconSourceSha256 = $taskIconHash
    BitmapSourcePath = $taskBitmapPath
    BitmapSourceSha256 = $taskBitmapHash
    GeneratedPath = $taskGeneratedPath
    GeneratedSha256 = (Get-FileHash -LiteralPath $taskGeneratedPath -Algorithm SHA256).Hash
    Scope = 'Full production IconCacheService linked directly; four literal BitmapHelper decode/guard methods. Synthetic Shell bytes and tiny codec/dispatcher doubles. No WinUI rendering, native provider, timing or physical presentation claim.'
}
$taskJson = ($taskMetadata | ConvertTo-Json -Depth 4) -replace '\r?\n', $taskCrLf
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-metadata.json'), $taskJson + $taskCrLf, [Text.UTF8Encoding]::new($false))
if ((Get-FileHash -LiteralPath $taskIconPath -Algorithm SHA256).Hash -ne $taskIconHash -or
    (Get-FileHash -LiteralPath $taskBitmapPath -Algorithm SHA256).Hash -ne $taskBitmapHash) {
    throw 'Production sources changed during preparation; prepare again.'
}
Write-Output "Prepared icon cache contracts from $taskRevision. No build or tests executed."
