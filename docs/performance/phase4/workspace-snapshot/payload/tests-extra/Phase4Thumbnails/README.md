# Phase IV thumbnail contracts

The project links the complete production `FileThumbnailHelper`, `STATask` and `ViewportWorkQueue` files. `Prepare-ThumbnailContracts.ps1` extracts the named thumbnail methods and reuse predicate verbatim from `ShellViewModel`, with source hashes. It also extracts `BitmapHelper` PNG dimension, decode-size and bounded synchronous-selection functions verbatim. It does not replace extraction, cancellation or scheduling logic.

The pool starts real Windows STA apartments and calls the existing CsWin32 OleInitialize wrapper. Shell/GDI retrieval, WinUI dispatcher and bitmap decoding are doubles. The tests verify native-call occupancy, stale-result guards, cache-hit and cache-miss call counts, image-before-overlay ordering, retries and diagnostics gating. They also verify the real helper requests 512 pixels for 256 DIP at 2x DPI and rounds physical targets above 256 upwards, while preserving smaller buckets and sidebar rounding. Synthetic PNG headers verify aspect selection and that synchronous decoding requires explicit thumbnail opt-in, Physical/aspect mode, a target and both source dimensions within 1..256, and at most 256 KiB encoded data. The double records requested and selected modes; it does not execute WinUI `SetSource` or verify complete PNG payload integrity. These tests do not measure thumbnails from actual providers, physical frames, UI latency or application memory.

The shared-icon handoff contracts record image-cache calls and dispatcher priorities. A valid initial result retains the exact assigned bitmap reference and starts full native generation without a second generic-icon Low application. Direct/default loads and failed initial shared-icon loads still apply or retry the generic image. Changes to item, path, modification time, size, DPI, thumbnail preference, row token, navigation generation or current image force conservative reapplication. These assertions prove call ordering with controlled delegates, not the absence or duration of actual WinUI scheduling waits.

Run sequentially from the workspace root:

```powershell
. ./tooling/Enter-WorkspaceEnvironment.ps1
./tests-extra/Phase4Thumbnails/Prepare-ThumbnailContracts.ps1
& "$env:DOTNET_ROOT/dotnet.exe" build tests-extra/Phase4Thumbnails/ThumbnailContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
& "$env:DOTNET_ROOT/dotnet.exe" tests-extra/Phase4Thumbnails/bin/Release/net10.0-windows10.0.26100.0/ThumbnailContracts.dll reports/phase4-thumbnail-contracts.json
```

Application stage markers use the existing opt-in fixture configuration and operation ID: `ThumbnailCacheQueueWait/Started/Completed`, `ThumbnailNativeQueueWait/Started/Completed`, `ItemIconQueueWait/Started/Completed`, `OverlayQueueWait/Completed`, `ThumbnailDispatcherWait`, `ThumbnailDecodeCompleted` and `ThumbnailApplied`. Native stages include Shell retrieval and GDI conversion; the decode stage covers `SetSourceAsync`, and `ThumbnailApplied` is a property assignment, not proof of physical presentation.
