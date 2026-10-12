# Files Turbo

Files Turbo is an experimental performance fork of [Files](https://github.com/files-community/Files) 4.2.39
(upstream commit `1994d9a4269f5144c70bc67d6a48de4a20616912`). It keeps the upstream feature set and focuses on
folder loading, viewport work, thumbnails, search and idle cost. The upstream README is kept in
[UPSTREAM_README.md](UPSTREAM_README.md).

| | |
|---|---|
| Version | 4.2.39.22 (audit branch `audit/final-engineering`) |
| Previous stable | 4.2.39.21, tag `files-turbo-phase5` (`f0e4072`) |
| Runtime | .NET 10, WinUI 3, Release x64, ReadyToRun composite (upstream default) |
| Status | Experimental, side-loaded only. Not published, not signed. |

## Documentation

- [docs/turbo/ARCHITECTURE.md](docs/turbo/ARCHITECTURE.md): what Turbo changes and why.
- [docs/turbo/BENCHMARKS.md](docs/turbo/BENCHMARKS.md): FilesBench, fixtures, methodology and how to reproduce.
- [docs/turbo/CHANGELOG.md](docs/turbo/CHANGELOG.md): changes per phase.

Phase I–V reports and raw evidence from the earlier sessions are not in the tree any more; they remain in the
history of tags `files-turbo-phase3`, `files-turbo-phase4` and `files-turbo-phase5`.

## Build

Requirements: Windows 11 x64 and a .NET 10 SDK (`global.json` rolls forward; builds were verified with
10.0.401).

```powershell
$p = '-p:Configuration=Release', '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
     '-p:PublishReadyToRun=true', '-p:PublishReadyToRunComposite=true', '-p:GenerateAppxPackageOnBuild=true',
     '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never', '-p:UapAppxPackageBuildMode=SideloadOnly'
dotnet restore src/Files.App.Server/Files.App.Server.csproj @p   # not restored by the app build
dotnet build src/Files.App/Files.App.csproj @p
```

The workspace script `tooling/Build-Layout.ps1` (outside this repository) wraps the build, adds the
Files.App.Server output, expands the MSIX into a loose layout, optionally renames the package identity for
side-by-side installs and writes `provenance.json` with commit and hashes.

## Install (development registration)

Turbo is installed as a loose layout next to the regular Files and Windows Explorer; it never replaces either.

```powershell
Add-AppxPackage -Register <layout>\AppxManifest.xml
```

Leave "Set Files as the default file manager" and the dialog replacement options off. To roll back, register
the previous layout (for example the Phase V layout) again; re-registering the same version from another path
requires `Remove-AppxPackage <PackageFullName> -PreserveApplicationData` first.

## Tests

```powershell
dotnet test --project tests/Files.App.Turbo.UnitTests -c Release -p:Platform=x64
```

`Files.App.Turbo.UnitTests` compiles the production sources it covers (collection reconciliation, listing order,
viewport queue, native name search, caches, STA scheduling, navigation coordination) and runs without packaging
or UI. The upstream UI and interaction test projects are unchanged.

## Benchmarks

`benchmarks/FilesBench` measures Files, Files Turbo and Windows Explorer from the outside by watching screen
pixels and UI Automation, so the same metric applies to all three. See
[docs/turbo/BENCHMARKS.md](docs/turbo/BENCHMARKS.md).

## Licenses

Files is licensed under the MIT license ([LICENSE-MIT](LICENSE-MIT)); some components are under the
Mozilla Public License 2.0 ([LICENSE-MPL](LICENSE-MPL)). Turbo changes are under the same terms. Third-party
notices are in [.github/NOTICE.md](.github/NOTICE.md).
