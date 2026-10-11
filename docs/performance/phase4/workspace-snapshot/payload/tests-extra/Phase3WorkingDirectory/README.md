# Working-directory and breadcrumb race contracts

The preparation script copies five actual production members verbatim into partial classes: `SetWorkingDirectoryAsync`, `IsWorkingDirectoryUpdatePending`, `UpdateFolderThumbnailImageSource`, `UpdatePathUIToWorkingDirectoryAsync` and `ModernShellPage.NavigateToPath`. `source-hashes.json` records the full source SHA256, exact UTF-8 member SHA256 and generated-file SHA256 for each member.

The harness uses controlled root, Git, head, icon, breadcrumb and frame providers. It performs no actual filesystem, WinUI, registry or package operations. It validates stale continuation suppression, same-path ABA, disposal, reentrant events and normal behavior. It does not replace runtime GUI verification.

Run from the workspace root, sequentially with other builds:

```powershell
./tests-extra/Phase3WorkingDirectory/Prepare-WorkingDirectoryContracts.ps1
$env:DOTNET_CLI_HOME = Join-Path $PWD 'tooling/dotnet-home'
$env:DOTNET_ROOT = Join-Path $PWD 'tooling/dotnet'
$env:NUGET_PACKAGES = Join-Path $PWD 'tooling/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
./tooling/dotnet/dotnet.exe build ./tests-extra/Phase3WorkingDirectory/WorkingDirectoryContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
./tooling/dotnet/dotnet.exe ./tests-extra/Phase3WorkingDirectory/bin/Release/net10.0/WorkingDirectoryContracts.dll ./reports/phase3-working-directory-contracts-final-01.json
```

Preserve existing evidence and supply a fresh report filename for a new run. The report embeds the preparation metadata and records every case, including failures. The authoritative run on 2026-10-11 passed 41/41 cases; the build had 0 errors and 8 warnings in the isolated harness. Source hashes matched the current app sources after the run.
