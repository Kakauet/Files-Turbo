#Requires -Version 7.2
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$repo = Join-Path $workspace 'source'
$reports = Join-Path $workspace 'reports'
$nuget = Join-Path $PSScriptRoot 'nuget.exe'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'

function Assert-WorkspacePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.Equals($workspace, [StringComparison]::OrdinalIgnoreCase) -and !$full.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes workspace: $Path" }
    $cursor = $full
    while ($cursor -and $cursor.Length -ge $workspace.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point rejected: $cursor" }
        }
        if ($cursor.Equals($workspace, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = Split-Path -Parent $cursor
    }
    return $full
}

foreach ($relative in @('source', 'source\packages', 'source\artifacts', 'source\src\Files.App\Assets\FilesOpenDialog', 'reports', 'tooling\dotnet-10.0.401', 'tooling\dotnet-home', 'tooling\nuget-packages', 'tooling\nuget-http-cache', 'tooling\nuget-plugins-cache', 'tooling\nuget-scratch', 'tooling\temp')) {
    $null = Assert-WorkspacePath (Join-Path $workspace $relative)
}
foreach ($name in @('Files.App.Launcher.exe', 'Files.App.OpenDialog64.dll', 'Files.App.SaveDialog64.dll', 'Files.App.OpenDialog32.dll', 'Files.App.SaveDialog32.dll')) {
    $null = Assert-WorkspacePath (Join-Path $repo ('src\Files.App\Assets\FilesOpenDialog\' + $name))
}
$environmentScript = Assert-WorkspacePath (Join-Path $PSScriptRoot 'Enter-WorkspaceEnvironment.ps1')
. $environmentScript
$env:NUGET_SCRATCH = Assert-WorkspacePath (Join-Path $PSScriptRoot 'nuget-scratch')
$null = [IO.Directory]::CreateDirectory($env:NUGET_SCRATCH)
$null = [IO.Directory]::CreateDirectory((Assert-WorkspacePath $reports))
$null = Assert-WorkspacePath $nuget
if (!(Test-Path -LiteralPath $nuget -PathType Leaf)) { throw 'Missing portable tooling/nuget.exe. Restore the official verified executable before building.' }
$signature = Get-AuthenticodeSignature -LiteralPath $nuget
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation(,|$)') { throw 'NuGet requires a valid Microsoft Authenticode signature.' }
if (!(Test-Path -LiteralPath $msbuild -PathType Leaf)) { throw 'The installed VS2022 MSBuild path is unavailable. No installer is run by this script.' }
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$restoreLog = Assert-WorkspacePath (Join-Path $reports "native-rebuild-$runId-restore.log")
$packagesConfig = Assert-WorkspacePath (Join-Path $repo 'src\Files.App.Launcher\packages.config')
$config = Assert-WorkspacePath (Join-Path $repo 'nuget.config')
$packages = Assert-WorkspacePath (Join-Path $repo 'packages')
& $nuget restore $packagesConfig -PackagesDirectory $packages -ConfigFile $config -Source https://api.nuget.org/v3/index.json -DirectDownload -NoHttpCache -NonInteractive -Verbosity quiet *> $restoreLog
if ($LASTEXITCODE -ne 0) { throw "Native package restore failed ($LASTEXITCODE). See $restoreLog" }
foreach ($relative in @('Microsoft.Windows.CppWinRT.3.0.260818.1\build\native\Microsoft.Windows.CppWinRT.props', 'Microsoft.Windows.CppWinRT.3.0.260818.1\build\native\Microsoft.Windows.CppWinRT.targets', 'Microsoft.Windows.ImplementationLibrary.1.0.260126.7\build\native\Microsoft.Windows.ImplementationLibrary.targets')) {
    $path = Assert-WorkspacePath (Join-Path $packages $relative)
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Native package import is missing: $relative" }
}

$projects = @(
    @{ Relative = 'src\Files.App.Launcher\Files.App.Launcher.vcxproj'; Platform = 'x64' },
    @{ Relative = 'src\Files.App.OpenDialog\Files.App.OpenDialog.vcxproj'; Platform = 'x64' },
    @{ Relative = 'src\Files.App.SaveDialog\Files.App.SaveDialog.vcxproj'; Platform = 'x64' },
    @{ Relative = 'src\Files.App.OpenDialog\Files.App.OpenDialog.Win32.vcxproj'; Platform = 'Win32' },
    @{ Relative = 'src\Files.App.SaveDialog\Files.App.SaveDialog.Win32.vcxproj'; Platform = 'Win32' }
)
foreach ($project in $projects) {
    $projectPath = Assert-WorkspacePath (Join-Path $repo $project.Relative)
    $null = Assert-WorkspacePath (Join-Path $repo ('artifacts\Release\' + $project.Platform + '\' + [IO.Path]::GetFileNameWithoutExtension($projectPath)))
    $null = Assert-WorkspacePath (Join-Path $repo ('artifacts\intermediates\' + $project.Platform + '\' + [IO.Path]::GetFileNameWithoutExtension($projectPath)))
    $projectXml = [xml][IO.File]::ReadAllText($projectPath)
    $registration = @($projectXml.SelectNodes('//*[local-name()="RegisterOutput"]'))
    if ($project.Relative -notmatch 'Launcher' -and (!$registration.Count -or @($registration | Where-Object { $_.InnerText -ne 'false' }).Count)) { throw "COM output registration must be disabled: $projectPath" }
    $name = [IO.Path]::GetFileName($projectPath)
    $log = Assert-WorkspacePath (Join-Path $reports "native-rebuild-$runId-$name.log")
    $consoleLog = Assert-WorkspacePath ($log + '.console')
    Write-Output "Building $name Release/$($project.Platform) with v143; log $log"
    & $msbuild $projectPath -t:Build -p:Configuration=Release "-p:Platform=$($project.Platform)" -p:PlatformToolset=v143 -p:WindowsTargetPlatformVersion=10.0.26100.0 -m:1 -v:quiet -clp:ErrorsOnly -fl "-flp:LogFile=$log;Verbosity=normal;Encoding=UTF-8" *> $consoleLog
    if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE): $name. See $log and $consoleLog" }
}
$payload = Assert-WorkspacePath (Join-Path $repo 'src\Files.App\Assets\FilesOpenDialog')
foreach ($name in @('Files.App.Launcher.exe', 'Files.App.OpenDialog64.dll', 'Files.App.SaveDialog64.dll', 'Files.App.OpenDialog32.dll', 'Files.App.SaveDialog32.dll')) {
    if (!(Test-Path -LiteralPath (Assert-WorkspacePath (Join-Path $payload $name)) -PathType Leaf)) { throw "Built native payload missing: $name" }
}
Write-Output 'Five native helpers built. This v143 fallback does not establish upstream v145 equivalence or runtime/Shell validation. No COM registration, package installation or UI launch was performed.'
