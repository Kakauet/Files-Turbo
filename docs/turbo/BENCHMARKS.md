# Benchmarks

## FilesBench

`benchmarks/FilesBench` is a standalone .NET 10 console program that drives Files, Files Turbo and Windows
Explorer the same way a user would and measures what appears on screen. It does not use app-internal traces,
so the numbers are comparable across the three applications.

```powershell
dotnet build benchmarks/FilesBench -c Release -o <out>
<out>\FilesBench.exe <command> <config.json> <output-directory>
```

Commands: `inspect`, `nav`, `idle`, `startup`, `thumbnails`, `search`, `operations`.

Config:

```json
{
  "Apps": [
    { "Name": "Original", "Kind": "Files", "Family": "FilesDev_ykqwq8d6ps0ag", "Layout": "<layout dir>" },
    { "Name": "Turbo", "Kind": "Files", "Family": "FilesTurbo.Audit_ykqwq8d6ps0ag", "Layout": "<layout dir>" },
    { "Name": "Explorer", "Kind": "Explorer" }
  ],
  "Empty": "<fixtures>\\empty",
  "Folders": ["<fixtures>\\natural-10000", "<fixtures>\\natural-100000"],
  "Repetitions": 12
}
```

Every Files build must be registered under its own identity (`tooling/Build-Layout.ps1 -IdentityName`), so the
builds never share settings or caches. FilesBench refuses to write settings for `FilesTurbo.Experimental`.

## Method

- **Paint detection.** The window is captured with GDI `BitBlt`. The harness first calibrates where names are
  drawn, then counts a row as painted when its text region differs from the empty-folder reference.
  - `FirstRowMs` is the first name painted.
  - `TenRowsMs` is ten rows painted.
  - `AllRowsMs` is the full viewport painted, taken as the last change before 400 ms of stability.
- **Listing completion.** `ListingCompleteMs` is when the item-count text in the status bar (Files) or the details pane
  (Explorer) shows the final count.
- **Navigation.**
  - The harness types the path into the address bar and starts the clock when it sends Enter.
  - Each sample starts from the empty folder after CPU idle (< 15 % of one core).
  - The order of the applications is rotated every repetition.
  - A sample is invalid, and recorded as such, if the user moves the mouse or presses a key, or if the window
    shows the wrong folder.
- **Idle.** CPU time of the app process over 10 s, starting 5 s after the folder was opened, with the window visible.
- **Operations.**
  - The fixtures are synthetic files under `fixtures/ops`; each run works in a fresh `run-*` directory.
  - The operations are copy 1 000 small files, copy 1 × 256 MiB, move 1 000, rename 1, and Shift+Delete 1 000.
  - Every result is verified with SHA-256, and the harness only deletes `run-*` directories it created.
- **Statistics.** Every sample, valid or not, is written as one JSON line (`navigation.jsonl`, `idle.jsonl`,
  ...). Medians and p95 are computed over valid samples only.
- **Observer overhead.** Screen capture and UI Automation polling run in the harness process. That cost is the
  same for every application. The applications themselves are not traced or instrumented.

## Fixtures

`benchmarks/New-BenchmarkFixtures.ps1 -Root <dir>` creates:

- `empty`
- `natural-10000` and `natural-100000`: `Documento N.txt` without zero padding plus 20 `Proyecto N` folders,
  so natural sort order differs from NTFS order.

The operations scenario creates `ops/` itself.

Use fixtures inside the workspace only. Never point the harness at personal folders: the operations scenario
writes and deletes files.

## Pitfalls found during the audit

- Running two Files builds under one identity shares settings, caches and session restore; results are invalid.
- `UiPerformanceTrace` costs about 35 % of a core while enabled; keep `phase3-performance.json` absent.
- Upstream Files never reaches CPU idle after the first folder load (tab ring bug), hence the 15 % idle threshold.
- Zero-padded fixture names make NTFS order equal the final order, which hides the cost of sorting.
