# Files Turbo changelog

Versions are package versions. Upstream base: Files 4.2.39.0, commit `1994d9a4`.

## 4.2.39.22 — final audit (branch `audit/final-engineering`)

Fixes
- Tab loading ring no longer animates while collapsed; idle CPU after the first folder load drops from about 9 %
  of one core to under 0.5 % (the bug is upstream too).
- Large-folder loading is linear: the tail of a large listing was published with a quadratic diff, and the first
  64 rows are now selected with a bounded heap instead of a full sort.
- The first batch is flushed after 64 items or 25 ms; a fallback published a single item.
- In-place reconciliation is limited to small order-preserving changes; larger or grouped changes use one Reset,
  as upstream does.
- Renaming no longer upper-cases the thumbnail key, so the row keeps its image.
- Image preview no longer decodes at 1 px or waits forever for layout when the preview pane is hidden.
- The performance trace accepts any `FilesTurbo.*` identity.

Added
- `tests/Files.App.Turbo.UnitTests` (52 tests on production sources); replaces the out-of-tree `tests-extra` projects.
- `benchmarks/FilesBench`, an external paint-based benchmark for Files, Files Turbo and Explorer, and
  `benchmarks/New-BenchmarkFixtures.ps1`.

Removed
- `docs/performance`, `docs/phase3`, `docs/phase4` (phase evidence; still in the history of tags `files-turbo-phase3..5`).

## 4.2.39.21 — Phase V (`files-turbo-phase5`, `f0e4072`)

- The first large-folder viewport is ordered and painted before the rest is sorted.
- Faster day grouping; the day-label cache was removed after measurement.
- ReadyToRun composite chosen over Native AOT as the delivered runtime.
- Faster local navigation through Win32 path validation off the UI thread.
- Omnibar suggestions narrow previous matches immediately.

## 4.2.39.13 – 4.2.39.20 — Phases III and IV

- STA pool with priority lanes and reserved capacity; viewport work served top to bottom.
- Rows painted in the same frame from memory or the generic icon; shell images kept as scaled pixels.
- Bounded, generation-aware icon and thumbnail caches; cloud placeholders cache-only.
- Row metadata and tooltips off the UI thread; status bar layout deferred.
- Grouped rows reconciled without resetting containers; Git status no longer blocks navigation.
- Indexed and parallel directory search streamed together.

## Phases I and II

- Native Win32 name search and listing order; obsolete work cancelled on navigation.
- Progressive folder publication, deferred date formatting, viewport-first thumbnails.
