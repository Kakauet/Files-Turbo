# Files Turbo

Files Turbo is an unofficial, experimental performance fork of [Files](https://github.com/files-community/Files),
the file manager built by the Files Community. It is based on Files 4.2.39 (upstream commit
`1994d9a4269f5144c70bc67d6a48de4a20616912`) with the Files 4.3 translations, keeps the upstream feature set and
storage formats, and changes how work is scheduled so that large folders, thumbnails, search and file operations
cost less.

Files Turbo is not affiliated with or endorsed by the Files Community. Please report problems with this fork
here, not in the upstream repository. The upstream README is kept in [UPSTREAM_README.md](UPSTREAM_README.md).

> [!WARNING]
> Files Turbo is experimental and its packages are not signed. It copies, moves, renames and deletes your files
> like Files does, so keep backups of anything important and try it on data you can afford to lose first.

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
- **Shortcuts and status bar**: `.lnk` files are read once on the STA pool with only the link data that the
  listing needs, and the status bar measures only itself when the item or selection count changes.
- **Settings**: settings cards prepare only the size rules that apply and skip zero-width states before the
  first layout, so the Settings dialog opens with less layout work.
- **Idle cost**: the tab loading ring no longer animates while hidden.

Behaviour visible to users follows upstream.

## Performance

Files Turbo and Files were measured on the same machine with an external harness that drives both apps like a
user (typing paths, pressing keys, using menus) and detects what is painted on screen, so no app-internal timers
are involved. Both apps were built from source with the same Release x64 ReadyToRun settings and registered
under separate package identities so they share no settings or caches.

<!-- TODO before publishing: fill this table from the final evaluation (median, Turbo vs Files 4.3). -->

| Scenario | Files 4.3 | Files Turbo | Change |
|---|---:|---:|---:|
| Open a folder with 10 000 items, full screen painted | | | |
| Open a folder with 100 000 items, listing complete | | | |
| Name search in a 100 000-file tree | | | |
| Copy 10 000 small files | | | |
| CPU while idle with a folder open | | | |

Results depend on the machine, the disk and the Windows caches; treat them as indicative, not as a guarantee.

## Versions

Files Turbo versions have the form `4.3.0.N`, where `N` counts Files Turbo releases. They are not upstream Files
releases. The package identity is separate from Files, so both can be installed at the same time.

## How this fork was made

Much of the code in this fork was written with AI coding agents (OpenAI Codex and Claude Code) under the
direction of the maintainer, which is why some commits are authored by "Codex". Changes were checked with the
unit tests below and measured with the benchmark harness before they were kept.

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
Files and Windows Explorer without replacing either. The package is not signed, so Windows only accepts it as a
development registration: turn on Developer Mode (Settings → System → For developers), expand the generated MSIX
into a folder and register it:

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

[Files Turbo CI](.github/workflows/ci.yml) runs these tests, builds the app (Release, x64) and checks the
formatting of changed XAML files on every push to `main` and on every pull request.

## Reporting problems

Open an issue in this repository with the Files Turbo version (Settings → About) and the steps to reproduce it.
If the same problem also happens in the official Files app, it is an upstream bug: please report it to
[files-community/Files](https://github.com/files-community/Files/issues) instead. Contributions are described in
[CONTRIBUTING.md](.github/CONTRIBUTING.md).

## License and attribution

Files is created by the [Files Community](https://github.com/files-community) and licensed under the MIT
license ([LICENSE-MIT](LICENSE-MIT)); some components are under the Mozilla Public License 2.0
([LICENSE-MPL](LICENSE-MPL)). Files Turbo changes are released under the same terms. Third-party notices are in
[.github/NOTICE.md](.github/NOTICE.md).
