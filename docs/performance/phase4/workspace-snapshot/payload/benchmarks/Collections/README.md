# Collection component benchmark

The harness compiles the actual `BulkConcurrentObservableCollection<T>`, grouped collection, enumerator, debugger helper and LINQ extensions. The original `baseline` sources are the preserved `1994d9a` snapshots in `../baseline/Collections`, with their original manifest. `phase1` uses snapshots of the `files-turbo-phase1` recovery tag under `artifacts/phase2-collections/phase1-source`. `turbo` links the current Phase II source and emits CSV variant `phase2`.

Only WinRT marker attributes, localization and the visual group header model are replaced. This is a Release JIT component test, with no WinUI application, disk enumeration, ListedItem metadata, thumbnails or UI rendering. It cannot establish application navigation or UI latency. Inputs are reused synthetic objects in memory. JIT and list capacity are warmed equally. Forced GC is outside the timed section; GC during the operations is included and recorded.

The Phase II harness covers seven scenarios at 1,000 / 10,000 / 50,000 / 100,000 items: bulk replacement and insertion from lists, normal list addition and insertion, deferred addition in normal and bulk modes, and deferred replacement. Bulk list replacement matches `BeginBulkOperation -> Clear -> AddRange -> EndBulkOperation` in the actual navigation publisher. Deferred replacement operates on an already populated collection. Each count has eight warmup operations and 21 samples, with `max(32, 4000000 / count)` operations per sample. Contents and notification counts are checked after each sample. CSV retains batch time, allocations, GC counts and event counts. Median and nearest-rank p95 describe average operation times within batches, not individual-operation or UI latency.

The previous session used 15 samples and `max(8, 500000 / count)` operations. Its data and reports remain unchanged; do not pool its results with the revised protocol.

Run from the workspace with no concurrent build or benchmark:

```powershell
./benchmarks/Collections/Run-Phase2.ps1 -RunName a-new-name
```

This builds sequentially with the existing workspace SDK and caches. It runs the original eleven contracts on all three variants, ten new defect probes on the two old sources, and all 21 contracts on Phase II. Old probe failures remain in explicitly named JSON files. Fresh processes per scenario/pass use ABBA order for three representative original-vs-Phase-II paths and all seven Phase-I-vs-Phase-II paths. Each aggregate has 42 samples per scenario/count. The runner refuses to overwrite existing results.

To isolate a single current scenario:

```powershell
. ./tooling/Enter-WorkspaceEnvironment.ps1
$collectionsDotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$env:DOTNET_TieredCompilation = '0'
& $collectionsDotnet build benchmarks/Collections/Collections.csproj -c Release -p:Variant=turbo -v:quiet -clp:ErrorsOnly
& $collectionsDotnet benchmarks/Collections/bin/turbo/net10.0/Collections.dll --tests artifacts/phase2-collections/new-contracts.json
& $collectionsDotnet benchmarks/Collections/bin/turbo/net10.0/Collections.dll --benchmark artifacts/phase2-collections/new-normal.csv --scenario normal_add_list
```

`--contracts` isolates the ten new defect probes and also works on old sources. Exit code 1 means a contract found a defect; inspect the JSON instead of counting known old defects as Phase II failures. The original eleven tests still require the old enumeration counts when compiled against old sources. Phase II requires one enumeration of deferred sources. New contracts cover single-use ranges, grouped membership, bulk resets, throwing-source atomicity, self-source event payloads and bounded deferred self references.

The tests preserve empty-range/invalid-index behavior, stable event snapshots, properties, ordering, sequential grouping and concurrent ungrouped bulk additions. Concurrent grouped mutations, WinUI binding and real UI behavior are outside this component verification. Deferred bulk input now allocates a snapshot before mutation to guarantee single-use behavior and avoid partially changed collections on an enumeration exception; list-based navigation keeps the allocation-free bulk path.
