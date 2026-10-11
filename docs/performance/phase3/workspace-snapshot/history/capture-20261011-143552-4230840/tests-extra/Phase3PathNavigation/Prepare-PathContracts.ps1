$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$sourcePath = Join-Path $taskRoot 'source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs'
$sourceText = [IO.File]::ReadAllText($sourcePath)
$signatures = @(
    'public async Task<bool> HandleItemNavigationAsync(string path)',
    'public async Task CheckPathInputAsync(string currentInput, string? currentSelectedPath, IShellPage shellPage)',
    'private static string NormalizePathInput(string currentInput, bool isFtp)'
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
$generated = '// Generated verbatim from NavigationToolbarViewModel methods. Do not edit.' + "`r`n" +
    '#nullable enable' + "`r`n" +
    'namespace Files.App.ViewModels.UserControls { public partial class NavigationToolbarViewModel {' + "`r`n" +
    ($methods -join "`r`n`r`n") + "`r`n}}`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'PathCore.generated.cs'), $generated)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-hash.txt'), (Get-FileHash $sourcePath -Algorithm SHA256).Hash + "`r`n")
