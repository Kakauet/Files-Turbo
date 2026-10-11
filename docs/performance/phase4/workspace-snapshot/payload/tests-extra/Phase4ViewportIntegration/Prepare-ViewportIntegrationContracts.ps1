$ErrorActionPreference='Stop'
$taskWorkspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$taskSourcePath=Join-Path $taskWorkspace 'source/src/Files.App/Views/Layouts/BaseLayoutPage.cs'
$taskSource=[IO.File]::ReadAllText($taskSourcePath)
$taskMethods=foreach ($taskSignature in @('private bool NeedsDeferredProperties(', 'private void QueueItemProperties(', 'private void QueueItemMetadata(', 'private void RefillVisibleItemWork(', 'protected void LoadVisibleItemsAfterScrollSettled(', 'private void ReprioritizeViewportWork(', 'protected Task ReloadRealizedItemPropertiesAsync(', 'private void HookScrollDeferTracking(', 'private void UnhookScrollDeferTracking(', 'private void DeferScrollViewer_ViewChanged(', 'private void RefreshItem(')) {
    $taskStart=$taskSource.IndexOf($taskSignature,[StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Method missing: $taskSignature" }
    $taskOpen=$taskSource.IndexOf('{',$taskStart); $taskEnd=$taskOpen+1; $taskDepth=1
    while ($taskEnd -lt $taskSource.Length -and $taskDepth -gt 0) { if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }; if ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }; $taskEnd++ }
    $taskSource.Substring($taskStart,$taskEnd-$taskStart)
}
$taskGuardStart=$taskSource.IndexOf('private bool IsCurrentContainer(',[StringComparison]::Ordinal)
$taskGuardEnd=$taskSource.IndexOf(';',$taskGuardStart)+1
$taskMethods+= $taskSource.Substring($taskGuardStart,$taskGuardEnd-$taskGuardStart)
$taskGenerated="// Extracted literally from production scheduler handoff methods.`r`n#nullable enable`r`nnamespace Files.App.Views.Layouts { public partial class BaseLayoutPage {`r`n"+($taskMethods -join "`r`n`r`n")+"`r`n} }`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ViewportCore.generated.cs'),($taskGenerated -replace '\r?\n',"`r`n"))
$taskMetadata=[ordered]@{SourcePath=$taskSourcePath;SourceSha256=(Get-FileHash $taskSourcePath -Algorithm SHA256).Hash;QueuePath=(Join-Path $taskWorkspace 'source/src/Files.App/Helpers/ViewportWorkQueue.cs');QueueSha256=(Get-FileHash (Join-Path $taskWorkspace 'source/src/Files.App/Helpers/ViewportWorkQueue.cs') -Algorithm SHA256).Hash;GeneratedSha256=(Get-FileHash (Join-Path $PSScriptRoot 'ViewportCore.generated.cs') -Algorithm SHA256).Hash;Scope='Literal production phase0 realization, handoff, priority dispatch, bounded retries, reload, visible enumeration, scroll hook/delta/debounce and reprioritization; real queue. Shell/WinUI/viewport surfaces and timers are controlled doubles.'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'source-metadata.json'),($taskMetadata|ConvertTo-Json)+"`r`n")
