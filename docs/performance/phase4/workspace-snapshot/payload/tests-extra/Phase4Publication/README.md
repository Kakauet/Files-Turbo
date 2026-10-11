These contracts extract `ApplyFilesAndFoldersChangesAsync`, `TryPublishFilesAndFoldersWithoutReset` and `MatchesFilter` literally from `ShellViewModel.cs`. The project links the production bulk, concurrent and grouped collections, blocking enumerator, collection interfaces and debug view. The nested `EventArgsCache` is part of the linked bulk collection.

The dispatcher is controlled to exercise stale-filter rejection, cancellation and semaphore ordering. App services, item/group models and WinRT attributes are minimal doubles. A Reset-sensitive consumer exposes retention of existing rows and selection; these assertions concern that controlled consumer, not actual WinUI rendering. Real `CollectionChanged` events verify individual Add payloads, indices and the 128-event limit. No native subscriber or physical paint timing is measured.

Preparation records production, generated and linked-file SHA-256 hashes. The executable verifies them before and after the contracts and checks compiled source/link fingerprints against the metadata. Prepare again and rebuild whenever production changes.

Run the commands sequentially after the shared app build has finished:

```powershell
pwsh -NoProfile -File tests-extra/Phase4Publication/Prepare-PublicationContracts.ps1
dotnet build tests-extra/Phase4Publication/PublicationContracts.csproj -c Release -v:quiet -clp:ErrorsOnly
dotnet tests-extra/Phase4Publication/bin/Release/net10.0/PublicationContracts.dll reports/phase4-publication-contracts.json
```

The JSON report contains every contract result, the source snapshot and explicit scope limits. A failed contract makes the process fail after saving the report. Preparation alone does not compile or execute contracts.
