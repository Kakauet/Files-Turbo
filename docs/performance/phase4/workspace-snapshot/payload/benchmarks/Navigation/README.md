# Source-backed navigation component benchmarks

Run from the Files Turbo workspace root in the workspace environment. `Compare.ps1` builds baseline, frozen Phase I and current Phase II sequentially, then launches separate forward/reverse processes on the existing 1k/10k/50k/100k datasets. Each process warms the 1k pipeline and measures five samples per folder; all runs check final count, uniqueness and order, then exercise cancellation. Phase II also repeats 20 component navigations and measures managed retention after collection.

The complete source UniversalStorageEnumerator, SortingHelper and publication collections are compiled into the harness. Storage is a sequential DirectoryInfo adapter reading real metadata; shell/WinRT wrappers, icons, ListedItem and WinUI are stubs. Publication mirrors the source snapshot/reset/sort pattern. The natural comparer adapter is ordinal-ignore-case and the padded numeric synthetic names preserve ordering. This is a filesystem-backed component pipeline benchmark, not an application navigation, native Win32 enumeration, UI scroll or thumbnail benchmark. Peak working set is process lifetime maximum. Filesystem caches remain warm/uncontrolled; there is no cold-cache claim.

`phase1/` contains the enumerator and collection from tag `files-turbo-phase1` (`34acafc`). The collection snapshot was corrected after final audit found it had accidentally been copied from Phase II. New `navigation-final-verified` results supersede the initial `navigation-comparison` results, which cannot establish an exact Phase I comparison. Raw initial samples, prior summaries/manifest and binaries remain preserved in `reports/phase2-navigation-prior-comparison`. Other linked source files were unchanged between baseline/Phase I in the relevant code. `Dates/` independently compiles verbatim date properties and actual formatters; it measures deferred label materialization with 100 visible rows and the all-labels control. Read `reports/PHASE2_NAVIGATION.md` for results, tests and limitations.

The first all-labels 50k date series showed transient 2x slowdown. `Dates/Control.ps1` reproduces the investigation with eight warmups, GC/CPU counters, independent forward/reverse processes and process-only tiering/ReadyToRun controls. Raw negative results remain preserved; warmed controls did not reproduce a sustained regression. This does not prove the first-realization UI cost is absent.

The final verified series uses current Phase II source, including both final cancellation checks at EOF/return, and the corrected Phase I snapshot. `reports/phase2-navigation-final-provenance.json` records binary hashes and portable PDB document checksums; every compiled source matches. `phase2-measured/` preserves the previous enumerator and mislabelled collection for auditing. The optional `-UseMeasuredSnapshot` switch substitutes only the old enumerator and does not reproduce the superseded mislabelled series. Final cancellation contracts use current source.

```powershell
./benchmarks/Navigation/Compare.ps1
./benchmarks/Navigation/Dates/Compare.ps1
./benchmarks/Navigation/Summarize-Results.ps1
./benchmarks/Navigation/Dates/Control.ps1
./benchmarks/Navigation/Dates/Summarize-Control.ps1
```

Each comparison stores CSV and JSON in `benchmarks/results/phase2/`. Tests under `tests-extra/Navigation` extract actual ShellViewModel methods before compilation and execute them with controlled asynchronous storage/dispatcher dependencies. They do not load WinUI or register a package.
