$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskTargets = @(
    @{ Relative = 'source/src/Files.App/ViewModels/ShellViewModel.cs'; Signature = 'public async Task SetWorkingDirectoryAsync(string? value)'; Namespace = 'Files.App.ViewModels'; Class = 'ShellViewModel'; Output = 'WorkingDirectoryCore.generated.cs' },
    @{ Relative = 'source/src/Files.App/ViewModels/ShellViewModel.cs'; Signature = 'public async Task UpdateFolderThumbnailImageSource()'; Namespace = 'Files.App.ViewModels'; Class = 'ShellViewModel'; Output = 'FolderThumbnailCore.generated.cs' },
    @{ Relative = 'source/src/Files.App/ViewModels/ShellViewModel.cs'; Signature = 'public bool IsWorkingDirectoryUpdatePending =>'; Expression = $true; Namespace = 'Files.App.ViewModels'; Class = 'ShellViewModel'; Output = 'WorkingDirectoryPending.generated.cs' },
    @{ Relative = 'source/src/Files.App/Views/Shells/BaseShellPage.cs'; Signature = 'public async Task UpdatePathUIToWorkingDirectoryAsync(string? newWorkingDir, string? singleItemOverride = null)'; Namespace = 'Files.App.Views.Shells'; Class = 'BaseShellPage'; Output = 'BreadcrumbCore.generated.cs' },
    @{ Relative = 'source/src/Files.App/Views/Shells/ModernShellPage.xaml.cs'; Signature = 'public override void NavigateToPath(string? navigationPath, Type? sourcePageType, NavigationArguments? navArgs = null)'; Namespace = 'Files.App.Views.Shells'; Class = 'ModernShellPage'; Output = 'ModernRouteCore.generated.cs' }
)
$taskSources = foreach ($taskTarget in $taskTargets) {
    $taskPath = Join-Path $taskRoot $taskTarget.Relative
    $taskText = [IO.File]::ReadAllText($taskPath)
    $taskStart = $taskText.IndexOf($taskTarget.Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw ('Source method missing: ' + $taskTarget.Signature) }
    if ($taskTarget.Expression) {
        $taskEnd = $taskText.IndexOf(';', $taskStart) + 1
        if ($taskEnd -le $taskStart) { throw 'Expression member has no terminator.' }
    }
    else {
        $taskOpen = $taskText.IndexOf('{', $taskStart)
        $taskDepth = 1
        $taskEnd = $taskOpen + 1
        while ($taskEnd -lt $taskText.Length -and $taskDepth -gt 0) {
            if ($taskText[$taskEnd] -eq '{') { $taskDepth++ }
            elseif ($taskText[$taskEnd] -eq '}') { $taskDepth-- }
            $taskEnd++
        }
        if ($taskDepth -ne 0) { throw ('Unbalanced source method: ' + $taskTarget.Signature) }
    }
    $taskMethod = $taskText.Substring($taskStart, $taskEnd - $taskStart)
    $taskGenerated = '// Generated verbatim from the actual app method. Do not edit.' + "`r`n" +
        ('namespace ' + $taskTarget.Namespace + ' { public partial class ' + $taskTarget.Class + ' {') + "`r`n" +
        $taskMethod + "`r`n}}`r`n"
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot $taskTarget.Output), $taskGenerated)
    $taskMethodHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskMethod)))
    [ordered]@{ Source = $taskTarget.Relative; Signature = $taskTarget.Signature; SourceSha256 = (Get-FileHash $taskPath -Algorithm SHA256).Hash; MethodUtf8Sha256 = $taskMethodHash; Generated = $taskTarget.Output; GeneratedSha256 = (Get-FileHash (Join-Path $PSScriptRoot $taskTarget.Output) -Algorithm SHA256).Hash; Verbatim = $true }
}
$taskMetadata = [ordered]@{ PreparedUtc = [DateTime]::UtcNow.ToString('o'); Sources = @($taskSources); Scope = 'Actual method bodies compiled verbatim against deterministic substitutes; no WinUI runtime, filesystem, registry or package operations' }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-hashes.json'), (($taskMetadata | ConvertTo-Json -Depth 6) -replace "`r?`n", "`r`n") + "`r`n")
