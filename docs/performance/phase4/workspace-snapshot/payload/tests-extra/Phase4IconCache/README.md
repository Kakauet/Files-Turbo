Phase IV icon cache contracts

IconCacheContracts.csproj links the full production IconCacheService directly. Prepare-IconCacheContracts.ps1 extracts the four BitmapHelper decode/guard methods literally, with source hashes compiled into the harness. Prepare again after the coordinator commits .9, then build and execute sequentially under the coordinator's build lock.

The runner accepts a JSON report path as its first argument. The default is reports/phase4-icon-cache-contracts.json under the workspace. It records the source revision, hashes and every assertion result, and refuses stale source/preparation/compiled metadata before running.

The tiny codec records requested decode properties and whether the actual BitmapHelper guard selected SetSource or SetSourceAsync. Synthetic PNG headers only exercise parsing and control flow. The dispatcher double tests the API entry gate; it is not a real WinUI dispatcher. No native Shell generation, pixels, transparency, thread marshaling, rendering speed, FPS or physical presentation is measured.

Existing tests-extra/IconCache and benchmarks/IconCache remain unchanged. This harness includes cache coalescing and generation/failure contracts as well as physical DPI rounding, aspect ratio and the existing 256px/256KiB synchronous limits.
