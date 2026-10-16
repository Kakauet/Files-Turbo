# Files Turbo

A faster build of [Files](https://github.com/files-community/Files), the file manager for Windows made by the
Files Community.

Files Turbo doesn't remove anything from Files. Every feature and every setting is still there, and so are the
animations, transparency and the rest of the visual effects. Nothing was traded away for speed. What changed is
the work the app does behind the scenes: it does less of it, does it in a better order, and stops as soon as the
result is no longer needed. You get the same app, just much better optimized, especially in large folders, search,
thumbnails and file operations.

This is an unofficial fork and is not affiliated with or endorsed by the Files Community. It is based on Files
4.2.39 (commit `1994d9a4269f5144c70bc67d6a48de4a20616912`) plus the Files 4.3 translations, and it reads and
writes the same settings and data as Files. Please report problems with Files Turbo here, not upstream. The
original README is in [UPSTREAM_README.md](UPSTREAM_README.md).

> [!WARNING]
> Files Turbo is experimental and its packages are not signed. It copies, moves, renames and deletes files just
> like Files does, so keep backups of anything important and try it first on data you can afford to lose.

## Fixed in Files Turbo

- **No more CPU use in the background.** In Files, the tab loading indicator keeps animating after it's hidden,
  so a window that's just sitting there keeps using about 6 % of a CPU core. Files Turbo stops it, and an idle
  window drops to practically zero (under 1 % in every measurement).
- **Smooth sidebar resizing.** Dragging the edge of the left sidebar used to stutter badly, because Files wrote
  the new width to its settings file on every mouse movement. Now the sidebar follows the pointer smoothly and
  the width is saved once, when you let go.
- **Instant search.** As you type in the address bar, matches appear right away by filtering the ones already
  found, before the full search even starts. The full search is also faster in most cases: up to 2.6 times
  faster for exact names.

## What's faster

- **Opening folders.** The files that fit on screen show up first and the rest are added without rebuilding the
  list over and over. If you leave a folder before it finishes loading, that work is cancelled.
- **Scrolling.** Icons and thumbnails for the rows you're looking at load first. Rows you've scrolled past are
  skipped.
- **Thumbnails and icons.** They're kept at the size they're shown, in caches that don't grow forever. Cloud files
  that are only placeholders aren't downloaded just to draw a thumbnail.
- **Search.** Name search runs in parallel, results are added in groups instead of one at a time, and typing a
  new letter cancels the previous search instead of waiting for it.
- **File operations.** Copy, move, delete and rename have less overhead per file. Bulk rename is a single
  operation, tags are only updated for files that have them, and folder change notifications are handled in
  batches.
- **Desktop and folders full of shortcuts.** Shortcuts are read once, and only for the details the list needs.
- **Settings.** The Settings page opens with much less layout work.

### For developers

The main changes are Win32 folder enumeration with an ordered first screen, in-place reconciliation of the item
collection instead of resets, a prioritized STA thread pool, a viewport work queue that drops work for rows that
are no longer visible, bounded thumbnail and icon caches, and batched publication of search results and file
system watcher events. The [unit tests](tests/Files.App.Turbo.UnitTests) cover most of this logic.

## Performance

Both apps were measured on the same machine with an external tool that uses them like a person would (typing
paths, pressing keys, using menus) and watches what appears on screen, so neither app's own timers are involved.
Files and Files Turbo were built from source with the same Release x64 ReadyToRun settings and installed side by
side, so they never shared settings or caches.

| Scenario | Files 4.3 | Files Turbo | Change |
|---|---:|---:|---:|
| Open a folder with 10 000 items, until the screen is filled | 544 ms | 105 ms | 5.2× faster |
| Open a folder with 100 000 items, until the screen is filled | 4 254 ms | 215 ms | 19.8× faster |
| Open a folder with 100 000 items, until every item is listed | 4 097 ms | 1 337 ms | 3.1× faster |
| Thumbnails in a folder you open for the first time | 766 ms | 96 ms | 8.0× faster |
| Start the app until the first file is shown | 2 920 ms | 2 074 ms | 1.4× faster |
| Exact name search in a 100 000-file tree | 244 ms | 93 ms | 2.6× faster |
| Bulk rename 1 000 files | 87.3 s | 3.0 s | 29× faster |
| CPU while idle with a folder open | 6.4 % | 0.0 % | idle |
| Private memory with a 10 000-item folder open | 325 MiB | 289 MiB | 11 % less |

Medians of 10 runs per app (5 for bulk rename in Files) on a laptop with an Intel Core i7-13620H, 32 GB of RAM and
an NVMe SSD, running Windows 11 25H2. Every row is a clear difference: its 95 % confidence interval doesn't
include "no change". Some things showed no measurable difference, such as how fast the window itself appears at
startup or how long it takes to list every item in a 10 000-item folder.

About memory: Files empties its working set while the window is idle, so Task Manager shows it using very little.
That memory is still reserved and has to be paged back in, with a small hitch, the next time you use the window.
Files Turbo only does this after its window is closed to the background, so Task Manager shows a higher number,
even though the memory it actually reserves (private memory, above) is lower.

Your numbers will depend on your machine, your disk and what Windows already has cached, so take these as a guide
rather than a promise.

## Install

Files Turbo installs next to Files and Windows Explorer and doesn't replace either of them (its package name is
`FilesTurbo.Experimental`). Because the package isn't signed, Windows only accepts it as a developer install:

1. Turn on Developer Mode in Settings → System → For developers.
2. Download `Files-Turbo-<version>-x64.zip` and `Microsoft.WindowsAppRuntime.2-x64.msix` from the
   [latest release](https://github.com/Kakauet/Files-Turbo/releases/latest), and extract the zip into a folder
   you'll keep (Windows runs the app from there).
3. Install the Windows App SDK runtime (signed by Microsoft; skip this if you already have it) and register Files
   Turbo from PowerShell:

```powershell
Add-AppxPackage .\Microsoft.WindowsAppRuntime.2-x64.msix
Add-AppxPackage -Register <folder>\AppxManifest.xml
```

To go back to an earlier build, register its folder the same way. If both builds have the same version number,
run `Remove-AppxPackage <PackageFullName> -PreserveApplicationData` first.

## Build from source

You need Windows 11 x64 and the .NET 10 SDK.

```powershell
$p = '-p:Configuration=Release', '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true',
     '-p:PublishReadyToRun=true', '-p:PublishReadyToRunComposite=true', '-p:GenerateAppxPackageOnBuild=true',
     '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never', '-p:UapAppxPackageBuildMode=SideloadOnly'
dotnet restore src/Files.App.Server/Files.App.Server.csproj @p   # the app build doesn't restore this one
dotnet build src/Files.App/Files.App.csproj @p
```

## Tests

```powershell
dotnet test --project tests/Files.App.Turbo.UnitTests -c Release -p:Platform=x64
```

These tests compile the Files Turbo code they cover and run without packaging or UI. The upstream test projects
are unchanged. [CI](.github/workflows/ci.yml) runs them, builds the app and checks the formatting of changed XAML
files on every push to `main` and every pull request.

## Versions

Versions look like `4.3.0.N`, where `N` counts Files Turbo releases. They don't match upstream Files releases.

## How it was made

I built Files Turbo with the help of AI coding agents (OpenAI Codex and Claude Code), which is why some commits
are authored by "Codex". I chose what to change, and every change was tested and measured before it stayed in.

## Known issues

- After Files Turbo has been open for many hours, moving, deleting or recycling files can hang, also in Windows
  Explorer and other apps. Closing and reopening Files Turbo fixes it. Version 4.3.0.10 includes a likely fix that
  hasn't been confirmed yet, so please open an issue if it still happens to you.

## Reporting problems

Open an issue here with your Files Turbo version (Settings → About) and the steps to reproduce the problem. If it
also happens in the official Files app, it's an upstream bug, so please report it to
[files-community/Files](https://github.com/files-community/Files/issues) instead. See
[CONTRIBUTING.md](.github/CONTRIBUTING.md) if you'd like to help.

## License

Files is made by the [Files Community](https://github.com/files-community) and released under the MIT license
([LICENSE-MIT](LICENSE-MIT)), with some components under the Mozilla Public License 2.0
([LICENSE-MPL](LICENSE-MPL)). Files Turbo changes are released under the same terms. Third-party notices are in
[.github/NOTICE.md](.github/NOTICE.md).
