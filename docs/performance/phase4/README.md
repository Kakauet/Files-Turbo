# Files Turbo — Phase IV evidence

Production is frozen at bf4390ee0a2eed6cece0e99d1a021ce7fd255574 (4.2.39.9, final07, build08 Release x64). The immutable workspace snapshot records the reports, harnesses, tooling and raw JSON/CSV/JSONL before the local recovery tag and final delivery verification. Reports inside that snapshot accurately retain the pre-verification state; completed reports and final verification are archived separately in this directory after closure.

The local annotated tag files-turbo-phase4 identifies the commit containing production and this immutable snapshot. A following documentation-only commit records the verification and completed reports without changing tagged production. No push was performed.

The snapshot inventory and its SHA-256 sidecar describe each original and stored file. Stored UTF-8 text uses CRLF; the original BOM and newline metadata allow exact byte reconstruction. Package binaries, dependency runtimes, bin/obj, generated fixtures, PNG captures and EventPipe binaries remain in the workspace; selected large artifacts have hashes in ArtifactReferences. This snapshot is an evidence archive, not a complete offline SDK or installer bundle.

Validate the archive against an existing workspace using PowerShell 7.2 or later:

```powershell
$taskWorkspace = 'C:\Users\guill\Documents\ChatGPT\Files Fast'
& "$taskWorkspace\tooling\Restore-Phase4WorkspaceSnapshot.ps1" -WorkspaceRoot $taskWorkspace -ValidateOnly
```

Validation also checks existing targets against captured original bytes. Completed reports or work-state records may differ after closure; validate against a fresh evidence workspace when checking the frozen snapshot later. Explicit -Apply creates only missing files and rejects conflicts; it never replaces existing files. Production is recovered through Git. The final delivery report describes the independently preserved Phase III package and its validated registration/restoration route.

The final verification checks package inventory and provenance, current production, the local tag and snapshot membership, 327 passing contracts, complete primary OFF sessions, and exactly one final OFF application window. Accessible readiness and captured application buffers do not establish physical FPS, first-visit performance under globally cold caches, or all-provider/day-to-day stability. Seven real fixture operations passed; four UI scenarios remain without confirmed coverage.

Final verification passed on 2026-10-11 at 18:49:51 UTC, final07 4.2.39.9, PID 38424, diagnostics Off, 327 passed contracts. The local recovery tag points to 374b7f07ef9cededfee1267aa98b21b23f2ff10b. Snapshot payload and inventory are explicitly preserved as exact Git blob bytes, including members ignored by ordinary source rules.

Completed reports are in [final-reports](final-reports/MORNING_REPORT_PHASE4_FINAL.md), with working links to archived raw evidence or external workspace artifacts. [Final verification](phase4-final-verification.json) and [snapshot validation](phase4-snapshot-validation.json) record actual completion. The supplemental verifier in final-tooling preserves the post-freeze field correction (Regression.SourceHead); restore it to workspace tooling before rerunning final verification on a reconstructed workspace. Initial failed archive and verification attempts are preserved separately. The following documentation-only commit leaves production and the recovery tag unchanged.
