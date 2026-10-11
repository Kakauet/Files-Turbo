#Requires -Version 7.2
[CmdletBinding()]
param([string]$OutputReport = 'reports/phase4-final-verification.json')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskOutput = [IO.Path]::GetFullPath($OutputReport,$taskRoot)
if (!$taskOutput.StartsWith($taskRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskOutput)) { throw 'Choose a fresh workspace report.' }
& (Join-Path $PSScriptRoot 'Register-Phase4Experimental.ps1') -ValidateOnly
$taskProof = Get-Content -LiteralPath (Join-Path $taskRoot 'reports/phase4-build-provenance-firstvisit.json') -Raw | ConvertFrom-Json
if ($taskProof.Status -ne 'compiled-source-and-packaged-assembly-match' -or $taskProof.SourceWorkingTreeDirty) { throw 'Clean compiled source provenance is required.' }
foreach ($taskInput in @($taskProof.Documents)+@($taskProof.SourceInputs)) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot ('source/'+$taskInput.Path))).Hash -ne $taskInput.SHA256) { throw ('Compiled source changed: '+$taskInput.Path) }
}
$taskChanges = @(& git -C (Join-Path $taskRoot 'source') diff $taskProof.SourceCommit --name-only -- src)
if ($LASTEXITCODE -ne 0 -or $taskChanges.Count) { throw 'Production differs from the verified build commit.' }
$taskTag = & git -C (Join-Path $taskRoot 'source') rev-parse 'files-turbo-phase4^{commit}'
if ($LASTEXITCODE -ne 0) { throw 'The local Phase IV recovery tag is missing.' }
& git -C (Join-Path $taskRoot 'source') merge-base --is-ancestor $taskProof.SourceCommit $taskTag
if ($LASTEXITCODE -ne 0) { throw 'The Phase IV tag does not include the verified production commit.' }
$taskTaggedChanges = @(& git -C (Join-Path $taskRoot 'source') diff $taskProof.SourceCommit $taskTag --name-only -- src)
if ($LASTEXITCODE -ne 0 -or $taskTaggedChanges.Count) { throw 'Tagged production differs from the verified build.' }
& git -C (Join-Path $taskRoot 'source') cat-file -e ($taskTag + ':docs/performance/phase4/workspace-snapshot/snapshot-inventory.json')
if ($LASTEXITCODE -ne 0) { throw 'The recovery tag does not include the Phase IV workspace snapshot.' }
$taskPackage = Get-AppxPackage -Name FilesTurbo.Experimental
$taskLayout = Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase4-final07/layout'
if (!$taskPackage -or $taskPackage.Version.ToString() -ne '4.2.39.9' -or !$taskPackage.InstallLocation.Equals($taskLayout,[StringComparison]::OrdinalIgnoreCase)) { throw 'The verified final layout is not registered.' }
foreach ($taskAssembly in $taskProof.Assemblies) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskLayout $taskAssembly.LayoutPath)).Hash -ne $taskAssembly.SHA256) { throw 'Registered binary differs from provenance.' }
}
$taskRegression = Get-Content -LiteralPath (Join-Path $taskRoot 'reports/phase4-regression-contracts-firstvisit-ready.json') -Raw | ConvertFrom-Json
$taskFocused = Get-Content -LiteralPath (Join-Path $taskRoot 'reports/phase4-focused-contracts-firstvisit-ready-index.json') -Raw | ConvertFrom-Json
if ($taskRegression.Status -ne 'Completed' -or $taskRegression.TotalContracts -ne 159 -or $taskRegression.Failed -ne 0 -or $taskFocused.Status -ne 'Completed' -or $taskFocused.TotalContracts -ne 168 -or $taskFocused.Failed -ne 0) { throw 'Expected final contract results are incomplete.' }
if ($taskRegression.SourceCommit -ne $taskProof.SourceCommit -or $taskFocused.SourceCommit -ne $taskProof.SourceCommit) { throw 'Contracts must cover the verified production commit.' }
foreach ($taskEvidence in $taskRegression.Cases) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskEvidence.Evidence)).Hash -ne $taskEvidence.EvidenceSha256) { throw 'Regression evidence changed.' }
}
foreach ($taskEvidence in $taskFocused.Evidence) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskEvidence.Path)).Hash -ne $taskEvidence.Sha256) { throw 'Focused evidence changed.' }
}
$taskUi = foreach ($taskSession in @(
    @{Name='Images';Directory='thumbnails-IV-off-final07-primary52';Count=10;Pictures=52},
    @{Name='Videos';Directory='thumbnails-IV-off-final07-primary-videos';Count=10;Pictures=3},
    @{Name='Navigation';Directory='navigation-IV-off-final07-raw';Count=40}
)) {
    $taskDirectory = Join-Path $taskRoot ('benchmarks/results/phase4/'+$taskSession.Directory)
    $taskMeta = Get-Content -LiteralPath (Join-Path $taskDirectory 'session.json') -Raw | ConvertFrom-Json
    $taskSamples = @(Import-Csv -LiteralPath (Join-Path $taskDirectory 'samples.csv'))
    if ($taskMeta.Status -ne 'Completed' -or $taskMeta.DiagnosticState -ne 'Off' -or $taskMeta.ConcurrentBuild -or $taskMeta.ConcurrentBuildAtStart -or $taskMeta.Package -ne $taskPackage.PackageFullName -or $taskSamples.Count -ne $taskSession.Count -or @($taskSamples | Where-Object TimedOut -eq 'True').Count) { throw ('Final UI session is incomplete: '+$taskSession.Name) }
    if ($taskSession.Pictures -and @($taskSamples | Where-Object { [int]$_.VisiblePictures -ne $taskSession.Pictures -or [int]$_.DetectedRealThumbnails -ne $taskSession.Pictures }).Count) { throw 'The complete calibrated thumbnail viewport was not detected.' }
    [pscustomobject]@{Name=$taskSession.Name;Session=$taskSession.Directory;Samples=$taskSamples.Count;MetadataSha256=(Get-FileHash -LiteralPath (Join-Path $taskDirectory 'session.json')).Hash;SamplesSha256=(Get-FileHash -LiteralPath (Join-Path $taskDirectory 'samples.csv')).Hash;PhysicalPresentationMeasured=$false}
}
$taskExecutable = Join-Path $taskLayout 'Files.exe'
$taskInstances = @(Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase) })
if ($taskInstances.Count -ne 1 -or $taskInstances[0].MainWindowHandle -eq [IntPtr]::Zero) { throw 'Exactly one final experimental window must be running.' }
$taskLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Packages/'+$taskPackage.PackageFamilyName+'/LocalState')
if (Test-Path -LiteralPath (Join-Path $taskLocal 'phase3-performance.json')) { throw 'Diagnostics must be disabled in the delivered app.' }
$taskCurrentTrace = @(Get-ChildItem -LiteralPath (Join-Path $taskLocal 'Phase3Performance') -Filter ('session-*-'+$taskInstances[0].Id+'-*.jsonl') | Where-Object CreationTimeUtc -ge $taskInstances[0].StartTime.ToUniversalTime().AddSeconds(-2))
if ($taskCurrentTrace.Count) { throw 'The delivered process started with diagnostics enabled.' }
$taskResult = [ordered]@{Status='verified-experimental-delivery-with-documented-measurement-limitations';VerifiedUtc=[DateTime]::UtcNow.ToString('o');ProductionCommit=$taskProof.SourceCommit;LocalRecoveryTagCommit=$taskTag;Package=$taskPackage.PackageFullName;InstallLocation=$taskLayout;ProcessId=$taskInstances[0].Id;Diagnostics='Off';ContractsPassed=327;ContractsFailed=0;PrimaryUi=$taskUi;Recovery='Phase III full inventory revalidated; actual restoration recorded earlier';Limitations='UIA accessible readiness and PrintWindow application surface are observed. Physical FPS, monitor presentation and broad daily-use stability are not proven; see final reports for pending scenarios.'}
[IO.File]::WriteAllText($taskOutput,(($taskResult | ConvertTo-Json -Depth 7) -replace '\r?\n',"`r`n")+"`r`n")
$taskResult | ConvertTo-Json -Depth 7
