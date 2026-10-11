#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Original','PhaseIII','PhaseIV')][string]$Variant,[ValidateRange(1,5)][int]$Copies = 3)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskSource = Join-Path $taskRoot 'benchmarks/media-phase4'
$taskManifest = Get-Content -Raw -LiteralPath ($taskSource + '-manifest.json') | ConvertFrom-Json
if (!$taskManifest.FixturePath.Equals($taskSource,[StringComparison]::OrdinalIgnoreCase)) { throw 'Synthetic source manifest mismatch.' }
$taskCursor = $taskSource
while ($taskCursor.Length -ge $taskRoot.Length) {
    if ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked source rejected.' }
    if ($taskCursor -eq $taskRoot) { break }
    $taskCursor = Split-Path $taskCursor -Parent
}
foreach ($taskFile in $taskManifest.Files) {
    $taskPath = [IO.Path]::GetFullPath((Join-Path $taskSource $taskFile.Name))
    if (!$taskPath.StartsWith($taskSource + '\',[StringComparison]::OrdinalIgnoreCase) -or ((Get-Item -LiteralPath $taskPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Source file path is outside its synthetic fixture or linked.' }
    if ((Get-FileHash -LiteralPath $taskPath).Hash -ne $taskFile.SHA256) { throw 'Synthetic source content changed.' }
}
$taskBatch = Join-Path $taskRoot ('benchmarks/media-phase4-firstvisit-' + $Variant + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
if (Test-Path -LiteralPath $taskBatch) { throw 'First-visit batch already exists.' }
$null = [IO.Directory]::CreateDirectory($taskBatch)
$taskCreated = [Collections.Generic.List[object]]::new()
for ($taskIndex = 0; $taskIndex -lt $Copies; $taskIndex++) {
    $taskTarget = Join-Path $taskBatch ('sample-' + $taskIndex)
    $null = [IO.Directory]::CreateDirectory($taskTarget)
    foreach ($taskFile in $taskManifest.Files) {
        $taskInput = Join-Path $taskSource $taskFile.Name
        $taskOutput = Join-Path $taskTarget $taskFile.Name
        [IO.File]::Copy($taskInput,$taskOutput,$false)
        if ((Get-FileHash -LiteralPath $taskOutput).Hash -ne $taskFile.SHA256) { throw 'Copied fixture hash mismatch; partial batch retained.' }
    }
    [ordered]@{ CreatedUtc = [DateTime]::UtcNow.ToString('o'); FixturePath = $taskTarget; VariantLabel = $Variant; Images = $taskManifest.Images; Videos = $taskManifest.Videos; Pattern = $taskManifest.Pattern; Scope = 'Fresh synthetic paths copied from hash-verified media; this script does not open them in any GUI or change caches'; Files = @($taskManifest.Files) } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath ($taskTarget + '-manifest.json') -Encoding utf8
    $taskCreated.Add([pscustomobject]@{ FixtureDirectory = [IO.Path]::GetRelativePath($taskRoot,$taskTarget); Manifest = $taskTarget + '-manifest.json'; CalibrationFixtureDirectory = 'benchmarks/media-phase4'; Repetitions = 1 })
}
[ordered]@{ CreatedUtc = [DateTime]::UtcNow.ToString('o'); Variant = $Variant; Fixtures = @($taskCreated); Method = 'Navigate once to each fresh path using -CalibrationFixtureDirectory benchmarks/media-phase4, so geometry calibration does not pre-open target'; Limitation = 'First navigation by this harness, not globally cold Shell cache. Background indexing and content caches are uncontrolled. Use a separate freshly created batch for each version.' } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskBatch 'plan.json') -Encoding utf8
Write-Output (Join-Path $taskBatch 'plan.json')
