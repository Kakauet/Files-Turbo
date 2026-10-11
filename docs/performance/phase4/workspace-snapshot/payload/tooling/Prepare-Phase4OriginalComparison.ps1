#Requires -Version 7.2
[CmdletBinding()]
param([string]$Destination = 'artifacts/development-layout/managed-release-phase4-original')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$taskOriginal = Join-Path $taskRoot 'artifacts/phase4-original-managed-release-01-package'
$taskInput = @(Get-ChildItem -LiteralPath $taskOriginal -Recurse -File -Filter '*.msix' | Where-Object { $_.Name -notmatch 'Microsoft\.' })
if ($taskInput.Count -ne 1) { throw 'Exactly one newly built original application package is required.' }
if ((git -C (Join-Path $taskRoot 'source-baseline') rev-parse HEAD) -ne '1994d9a4269f5144c70bc67d6a48de4a20616912' -or @(git -C (Join-Path $taskRoot 'source-baseline') status --porcelain).Count) { throw 'Original source must remain at the clean baseline.' }
$taskOutput = Join-Path $taskOriginal 'original-experimental-adapter.msix'
if (Test-Path -LiteralPath $taskOutput) { throw 'Adapter already exists; preserve previous evidence.' }
$taskNative = @('Files.App.Launcher.exe','Files.App.OpenDialog64.dll','Files.App.SaveDialog64.dll','Files.App.OpenDialog32.dll','Files.App.SaveDialog32.dll')
foreach ($relative in @(git -C (Join-Path $taskRoot 'source') diff files-turbo-baseline --name-only -- src/Files.App.Launcher src/Files.App.OpenDialog src/Files.App.SaveDialog)) {
    $originalText = (git -C (Join-Path $taskRoot 'source') show ("files-turbo-baseline:$relative")) -join "`n"
    $currentText = [IO.File]::ReadAllText((Join-Path $taskRoot ('source/' + $relative))) -replace '\r\n',"`n"
    if ($originalText.Replace('files-dev','files-turbo').TrimEnd() -ne $currentText.TrimEnd()) { throw 'Native helpers differ beyond the authorized experimental protocol adapter.' }
}
$taskSource = [IO.Compression.ZipFile]::OpenRead($taskInput[0].FullName)
$taskAdapter = [IO.Compression.ZipFile]::Open($taskOutput, [IO.Compression.ZipArchiveMode]::Create)
$taskHashes = [Collections.Generic.List[object]]::new()
try {
    foreach ($entry in $taskSource.Entries) {
        if ($entry.FullName.EndsWith('/') -or $entry.FullName -in @('AppxSignature.p7x')) { continue }
        $copy = $taskAdapter.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Fastest)
        $inputStream = $entry.Open(); $outputStream = $copy.Open()
        try {
            if ($entry.FullName -eq 'AppxManifest.xml') {
                $reader = [IO.StreamReader]::new($inputStream)
                $text = $reader.ReadToEnd().Replace('Name="FilesDev"','Name="FilesTurbo.Experimental"').Replace('Name="files-dev"','Name="files-turbo"').Replace('Alias="files-dev.exe"','Alias="files-turbo.exe"').Replace('Files - Dev','Files Original Comparison')
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
                $outputStream.Write($bytes)
            } else {
                $inputStream.CopyTo($outputStream)
            }
        } finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
    foreach ($name in $taskNative) {
        $relative = 'Assets/FilesOpenDialog/' + $name
        if ($taskSource.GetEntry($relative)) { continue }
        $native = Join-Path $taskRoot ('artifacts/development-layout/managed-release-phase3-stable06/layout/' + $relative)
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskAdapter, $native, $relative, [IO.Compression.CompressionLevel]::Fastest)
        $taskHashes.Add([pscustomobject]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $native).Hash;Kind='native-helper-with-protocol-only-adaptation'})
    }
} finally { $taskAdapter.Dispose(); $taskSource.Dispose() }
& (Join-Path $PSScriptRoot 'Complete-DevelopmentPackage.ps1') -InputPackage $taskOutput -ServerOutput (Join-Path $taskRoot 'source-baseline/src/Files.App.Server/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64') -Destination (Join-Path $taskRoot $Destination) -DependencyDirs @((Join-Path $taskRoot 'artifacts/development-layout/managed-release-phase3-stable06/Dependencies'))
$taskLayout = Join-Path $taskRoot ($Destination + '/layout')
foreach ($name in @('Files','Files.App.Storage','Files.App.CsWin32','Files.Shared')) {
    $project = if ($name -eq 'Files') { 'Files.App' } else { $name }
    $framework = if ($name -eq 'Files.Shared') { 'net10.0' } else { 'net10.0-windows10.0.26100.0' }
    $binary = Join-Path $taskRoot ("source-baseline/src/$project/bin/x64/Release/$framework/win-x64/$name.dll")
    $hash = (Get-FileHash -LiteralPath $binary).Hash
    if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $taskLayout ($name + '.dll'))).Hash) { throw 'Original packaged assembly differs from original build.' }
    $taskHashes.Add([pscustomobject]@{Path=($name + '.dll');SHA256=$hash;Kind='original-assembly-unchanged'})
}
[ordered]@{Status='original-source-and-assemblies-preserved';SourceCommit='1994d9a4269f5144c70bc67d6a48de4a20616912';SourceClean=$true;PackagingOnlyChanges='Experimental identity/protocol/alias/display name for temporary reuse of existing authorized registration; native helpers have only the files-dev to files-turbo protocol adaptation, verified against original sources. Missing Server payload completed. No original installation is updated. New-window helpers in unchanged managed original code still reference files-dev; excluded from comparison.';Delivery=$Destination;Files=@($taskHashes)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'reports/phase4-original-package-provenance.json')
