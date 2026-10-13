# Files Turbo

Files Turbo is an unofficial, experimental performance fork of [Files](https://github.com/files-community/Files),
the file manager built by the Files Community. It is based on Files 4.2.39 (upstream commit
`1994d9a4269f5144c70bc67d6a48de4a20616912`), keeps the upstream feature set and storage formats, and changes how
work is scheduled so that large folders, thumbnails, search and file operations cost less.

Files Turbo is not affiliated with or endorsed by the Files Community. Please report problems with this fork
here, not in the upstream repository. The upstream README is kept in [UPSTREAM_README.md](UPSTREAM_README.md).

## What is different

- **Folder loading**: Win32 enumeration with an ordered first screen, linear publication and in-place
  reconciliation instead of repeated resets; obsolete navigations are cancelled.
- **Background work**: a prioritized STA pool and a viewport queue, so rows on screen are served first and
  work for rows that scrolled away is dropped.
- **Images**: shell images kept as scaled pixel buffers, bounded thumbnail and icon caches, no downloads of
  cloud placeholders, association checks that also read the registry class keys.
- **Search**: native parallel name search with batched result publication.
- **File operations**: one parse per folder when queuing shell operations, tag database updates only for
  tagged items, watcher events applied in batches, bulk rename in one shell operation, coalesced recycle bin
  size queries.
- **Idle cost**: the tab loading ring no longer animates while hidden.

Behaviour visible to users follows upstream. Measurements and the reasons for each change are documented
outside this repository by the fork's maintainer.

## Build

Requirements: Windows 11 x64 and a .NET 10 SDK (`global.json` rolls forward).

```powershell
$p = '-p:Configuration=Release', '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
     '-p:PublishReadyToRun=true', '-p:PublishReadyToRunComposite=true', '-p:GenerateAppxPackageOnBuild=true',
     '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never', '-p:UapAppxPackageBuildMode=SideloadOnly'
dotnet restore src/Files.App.Server/Files.App.Server.csproj @p   # not restored by the app build
dotnet build src/Files.App/Files.App.csproj @p
```

## Install

The package identity is `FilesTurbo.Experimental` with the display name "Files Turbo", so it installs next to
Files and Windows Explorer without replacing either. Expand the generated MSIX into a folder and register it in
development mode:

```powershell
Add-AppxPackage -Register <layout>\AppxManifest.xml
```

To go back, register the previous layout again (`Remove-AppxPackage <PackageFullName> -PreserveApplicationData`
first when the version number is the same).

## Tests

```powershell
dotnet test --project tests/Files.App.Turbo.UnitTests -c Release -p:Platform=x64
```

`Files.App.Turbo.UnitTests` compiles the production sources it covers (collection reconciliation, listing
order, viewport queue, search, caches, STA scheduling, navigation, tag synchronization, operation results,
association lookup) and runs without packaging or UI. The upstream test projects are unchanged.

## License and attribution

Files is created by the [Files Community](https://github.com/files-community) and licensed under the MIT
license ([LICENSE-MIT](LICENSE-MIT)); some components are under the Mozilla Public License 2.0
([LICENSE-MPL](LICENSE-MPL)). Files Turbo changes are released under the same terms. Third-party notices are in
[.github/NOTICE.md](.github/NOTICE.md).
