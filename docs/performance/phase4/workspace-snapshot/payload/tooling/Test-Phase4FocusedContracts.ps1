param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunSuffix = 'latency')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
$taskDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$taskHead = (& git -C (Join-Path $taskRoot 'source') rev-parse HEAD).Trim()
if (@(& git -C (Join-Path $taskRoot 'source') status --porcelain).Count) { throw 'Clean production source required.' }
$taskIndex = Join-Path $taskRoot "reports/phase4-focused-contracts-$RunSuffix-index.json"
if (Test-Path -LiteralPath $taskIndex) { throw 'Choose a fresh run suffix.' }
$taskCases = @(
    @{Name='viewport';Folder='Phase4Viewport';Project='ViewportContracts';Report='viewport-contracts'},
    @{Name='thumbnails';Folder='Phase4Thumbnails';Project='ThumbnailContracts';Prepare='Prepare-ThumbnailContracts.ps1';Report='thumbnail-contracts'},
    @{Name='publication';Folder='Phase4Publication';Project='PublicationContracts';Prepare='Prepare-PublicationContracts.ps1';Report='publication-contracts'},
    @{Name='icon-cache';Folder='Phase4IconCache';Project='IconCacheContracts';Prepare='Prepare-IconCacheContracts.ps1';Report='icon-cache-contracts'},
    @{Name='integration';Folder='Phase4ViewportIntegration';Project='ViewportIntegrationContracts';Prepare='Prepare-ViewportIntegrationContracts.ps1';Report='viewport-integration-contracts'},
    @{Name='dates';Folder='Phase4Dates';Project='DateContracts';Prepare='Prepare-DateContracts.ps1';Report='date-contracts-and-benchmark'},
    @{Name='search-resource';Folder='Phase4SearchResource';Project='SearchResourceContracts';Prepare='Prepare-SearchResourceContracts.ps1';Report='search-resource-contracts'}
)
$taskEvidence = [Collections.Generic.List[object]]::new()
$taskTotal = 0
foreach ($taskCase in $taskCases) {
    $taskBuilds = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -ieq 'MSBuild.exe' -and $_.CommandLine -notmatch '(?i)/nodemode:' -or $_.Name -ieq 'dotnet.exe' -and $_.CommandLine -match '(?i)(?:\s|\")(?:build|msbuild|publish)(?:\s|\")' })
    if ($taskBuilds.Count) { throw 'Another build is running; contracts must build sequentially.' }
    $taskFolder = Join-Path $taskRoot ('tests-extra/'+$taskCase.Folder)
    $taskReportRelative = "reports/phase4-$($taskCase.Report)-$RunSuffix.json"
    $taskReport = Join-Path $taskRoot $taskReportRelative
    if (Test-Path -LiteralPath $taskReport) { throw 'Existing evidence must be preserved.' }
    if ($taskCase.Prepare) { & (Join-Path $taskFolder $taskCase.Prepare) }
    & $taskDotnet build (Join-Path $taskFolder ($taskCase.Project+'.csproj')) -c Release -t:Rebuild -v:quiet -clp:ErrorsOnly *> (Join-Path $taskRoot "reports/phase4-$($taskCase.Report)-$RunSuffix-build.log")
    if ($LASTEXITCODE -ne 0) { throw ('Contract compilation failed: '+$taskCase.Name) }
    & $taskDotnet (Join-Path $taskFolder ('bin/Release/'+([xml][IO.File]::ReadAllText((Join-Path $taskFolder ($taskCase.Project+'.csproj')))).Project.PropertyGroup.TargetFramework+'/'+$taskCase.Project+'.dll')) $taskReport
    if ($LASTEXITCODE -ne 0) { throw ('Contracts failed: '+$taskCase.Name) }
    $taskResult = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
    $taskRows = if ($taskResult.Contracts) { @($taskResult.Contracts) } else { @($taskResult.Results) }
    if (!$taskRows.Count -or @($taskRows | Where-Object Passed -ne $true).Count) { throw 'Contract verdict missing or failed.' }
    $taskTotal += $taskRows.Count
    $taskEvidence.Add([pscustomobject]@{Name=$taskCase.Name;Path=$taskReportRelative;Sha256=(Get-FileHash -LiteralPath $taskReport).Hash;Passed=$taskRows.Count})
    if ((& git -C (Join-Path $taskRoot 'source') rev-parse HEAD).Trim() -ne $taskHead -or @(& git -C (Join-Path $taskRoot 'source') status --porcelain).Count) { throw 'Production source changed during tests.' }
}
$taskInterop = Join-Path $taskRoot 'source/src/Files.App.CsWin32/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Files.App.CsWin32.dll'
$taskReportIndex = [ordered]@{Status='Completed';SourceCommit=$taskHead;TotalContracts=$taskTotal;Failed=0;Evidence=@($taskEvidence.ToArray());InteropSha256=(Get-FileHash -LiteralPath $taskInterop).Hash;Scope='Fresh sequential Release rebuilds over current extracted/linked production source; controlled WinUI doubles, native STA and physical decode sizing contracts. No actual WinUI rendering claim.';CapturedUtc=[DateTime]::UtcNow.ToString('o')}
[IO.File]::WriteAllText($taskIndex,($taskReportIndex|ConvertTo-Json -Depth 6)+"`r`n")
$taskReportIndex | ConvertTo-Json -Depth 6
