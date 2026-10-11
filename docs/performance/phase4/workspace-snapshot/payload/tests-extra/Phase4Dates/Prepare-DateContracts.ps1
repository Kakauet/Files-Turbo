$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskShellPath = Join-Path $taskWorkspace 'source/src/Files.App/ViewModels/ShellViewModel.cs'
$taskItemPath = Join-Path $taskWorkspace 'source/src/Files.App/Data/Items/ListedItem.cs'
$taskLayoutPath = Join-Path $taskWorkspace 'source/src/Files.App/Views/Layouts/BaseLayoutPage.cs'
$taskShell = [IO.File]::ReadAllText($taskShellPath)
$taskItem = [IO.File]::ReadAllText($taskItemPath)
$taskLayout = [IO.File]::ReadAllText($taskLayoutPath)
$taskBaseline = (& git -C (Join-Path $taskWorkspace 'source') show 'files-turbo-phase3:src/Files.App/ViewModels/ShellViewModel.cs') -join "`r`n"
function Get-TaskMethod([string]$content, [string]$signature) {
    $taskStart = $content.IndexOf($signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Method missing: $signature" }
    $taskOpen = $content.IndexOf('{', $taskStart)
    $taskDepth = 1; $taskEnd = $taskOpen + 1
    while ($taskEnd -lt $content.Length -and $taskDepth -gt 0) { if ($content[$taskEnd] -eq '{') { $taskDepth++ }; if ($content[$taskEnd] -eq '}') { $taskDepth-- }; $taskEnd++ }
    $content.Substring($taskStart, $taskEnd - $taskStart)
}
$taskCurrentMethod = Get-TaskMethod $taskShell 'public void UpdateDateDisplay('
$taskBaselineMethod = (Get-TaskMethod $taskBaseline 'public void UpdateDateDisplay()').Replace('public void UpdateDateDisplay()', 'public void BaselineUpdateDateDisplay()')
$taskDatesStart = $taskItem.IndexOf('private string? itemDateModified;', [StringComparison]::Ordinal)
$taskDatesEnd = $taskItem.IndexOf('private ObservableCollection<FileProperty>? itemProperties;', $taskDatesStart, [StringComparison]::Ordinal)
$taskDateMembers = $taskItem.Substring($taskDatesStart, $taskDatesEnd - $taskDatesStart)
$taskIsRealStart = $taskItem.IndexOf('public virtual bool IsRealChanges =>', [StringComparison]::Ordinal)
$taskIsRealEnd = $taskItem.IndexOf(';', $taskIsRealStart) + 1
$taskIsReal = $taskItem.Substring($taskIsRealStart, $taskIsRealEnd - $taskIsRealStart)
$taskUpdateReal = Get-TaskMethod $taskItem 'public virtual void UpdateReal()'
$taskLayoutMethod = Get-TaskMethod $taskLayout 'public void UpdateDateDisplay()'
$taskGuardStart = $taskLayout.IndexOf('private bool IsCurrentContainer(', [StringComparison]::Ordinal)
$taskGuardEnd = $taskLayout.IndexOf(';', $taskGuardStart) + 1
$taskLayoutGuard = $taskLayout.Substring($taskGuardStart, $taskGuardEnd - $taskGuardStart)
$taskRealizationMatch = [regex]::Match($taskLayout, 'if \(args\.Phase == 0 && listedItem\.IsRealChanges\)\s+listedItem\.UpdateReal\(\);')
if (!$taskRealizationMatch.Success) { throw 'Realization date refresh block missing.' }
$taskRealization = $taskRealizationMatch.Value
$taskGenerated = @"
// Generated from production date members/methods. The baseline method name alone is changed.
#nullable enable
namespace Files.App.Utils { public partial class ListedItem {
$taskDateMembers
$taskIsReal
$taskUpdateReal
} }
namespace Files.App.ViewModels { public partial class ShellViewModel {
$taskCurrentMethod
$taskBaselineMethod
} }
namespace Files.App.Views.Layouts { public partial class BaseLayoutPage {
$taskLayoutMethod
$taskLayoutGuard
public void RefreshRealizationDate(ListedItem listedItem, StubArgs args) {
$taskRealization
}
} }
"@
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'DateCore.generated.cs'), ($taskGenerated -replace '\r?\n', "`r`n"))
$taskMetadata = [ordered]@{ ShellPath=$taskShellPath; ShellSha256=(Get-FileHash $taskShellPath -Algorithm SHA256).Hash; ListedItemPath=$taskItemPath; ListedItemSha256=(Get-FileHash $taskItemPath -Algorithm SHA256).Hash; LayoutPath=$taskLayoutPath; LayoutSha256=(Get-FileHash $taskLayoutPath -Algorithm SHA256).Hash; GeneratedSha256=(Get-FileHash (Join-Path $PSScriptRoot 'DateCore.generated.cs') -Algorithm SHA256).Hash; Baseline='files-turbo-phase3'; Scope='Exact production date methods, container guard, realization refresh block and collection; resource strings and app services substituted.' }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-metadata.json'), ($taskMetadata | ConvertTo-Json) + "`r`n")
