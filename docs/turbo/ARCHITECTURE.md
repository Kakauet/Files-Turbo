# Files Turbo architecture

Turbo changes how Files loads folders, schedules background work, paints thumbnails and searches. It does not
add features or change storage formats. This document lists the mechanisms, where they live and the limits
found in the final audit.

## Folder loading

`ShellViewModel` → `Win32StorageEnumerator` → `Win32ListingOrder` → `BulkConcurrentObservableCollection`.

- **Pre-scan and ordered first screen.** Local folders are enumerated with `FindFirstFileExW`. For up to
  150 ms the enumerator buffers raw records instead of building `ListedItem`s. If the scan finishes within that
  budget, the records are ordered with the user's sort settings before anything is published, so the first
  screen is final and does not reshuffle.
- **Ordered prefix.** Above 2 000 entries `Win32ListingOrder` selects the first 64 entries with a bounded heap
  (`OrderedPrefix`) and publishes them before sorting the rest. `CreateComparison` replicates
  `SortingHelper` (natural names, folders first, grouping) with an index tie-break so the order is stable and
  identical to the upstream LINQ chain.
- **Publication cadence.** The first batch is flushed at 64 items or after 25 ms; later batches follow the
  upstream sampler. `TryPublishFilesAndFoldersWithoutReset` reconciles small, order-preserving changes
  (`CanReconcile`, ≤ 128 changes, ≤ 10 000 items) in place and otherwise falls back to one `Reset`, as upstream
  does.
- **Off-UI work.** Path validation, row metadata, tooltips and Git status run off the UI thread; tooltips are
  created on pointer entry.

Trade-off: in a folder with 100 000 entries Turbo paints the first rows about 100 ms later than upstream,
because it reads the whole folder before showing an ordered first screen. Upstream paints NTFS order first and
re-sorts afterwards. Turbo completes the listing 2.5–3× sooner.

## Background work

- **STA pool with priority lanes** (`STATask`): visible-row work, user-initiated work and background work use
  separate lanes, and slow background work cannot take all workers.
- **`ViewportWorkQueue`**: realized rows are served top to bottom; work for rows that scrolled away is cancelled.
- **`LatestPathNavigationCoordinator`**: only the latest navigation request is completed; obsolete listings are
  cancelled.

## Images and thumbnails

- Shell images are kept as scaled pixel buffers instead of PNG round trips.
- `ThumbnailImageCache` and the generic icon caches are bounded by decoded memory, keyed case-sensitively and
  invalidated by file change notifications and cache generations.
- Cloud placeholders and per-file cloud icons are cache-only, so Turbo never triggers a download.
- Cached thumbnails are painted in the same frame as the row; uncached ones start during initial realization.
- `ImagePreviewViewModel` shows a cached preview first and decodes for the pane size. The audit added a
  bounded layout wait and a 1024 px default, so a hidden preview pane can no longer decode at 1 px or block
  the details.

## Search

`NativeNameSearch` enumerates with Win32, runs subfolders in parallel through `ParallelDirectoryWork` and
streams indexed and non-indexed results together. Results are appended in batches of at most 64 per UI tick.
Omnibar suggestions narrow previous matches immediately before the new query runs.

## Idle cost

The tab loading `ProgressRing` (`Styles/TabBarStyles.xaml`) is active only in the `Loading` visual state.
Upstream left it active while collapsed, which kept the composition thread busy, at about 9 % of one core,
after the first folder load.

## Diagnostics

`Helpers/Performance/UiPerformanceTrace` is off by default. It is enabled for packages whose identity starts with
`FilesTurbo.` by creating `phase3-performance.json` in the package LocalState. While enabled it costs CPU and
must stay off during measurements.

## Known limits (final audit)

- Copying or moving many small files through Files (upstream or Turbo) is several times slower than Explorer.
  This comes from the shared operation code and Turbo does not change it.
- Native AOT builds are possible but not the delivery: ReadyToRun composite is the measured runtime.
- The `UserChoice` ProgID branch of the shell association cache was reviewed but not exercised on a machine
  with per-user overrides.
