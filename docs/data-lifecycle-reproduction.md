# Data and Manager Reproduction

Historical pre-fix observations. For the subsequent fixes and current regression
contracts, see [Data Save and Migration Reliability](data-reliability-fixes.md).
This observation harness intentionally expects the old defects; use the normal
product test suite to verify the repaired behavior.

Date: 2026-10-12. Baseline: commit 43449c5 with the existing uncommitted Windows
10 compatibility changes. This task adds reproduction tools only, not production
fixes. The original user configuration, locator, startup registry and services
were not accessed by the reproduction harness.

Run from the repository root:

```powershell
./tests/Reproduce-DataLifecycle.ps1
```

Evidence is in `artifacts/verification/data-lifecycle-repro/verified/`:
`environment.json` records commit, dirty paths, SDK, OS and source SHA-256 hashes;
`results.json` records observations and isolated paths. The sandbox is retained
for inspection. This executable is deliberately separate from the 74-group
product regression suite and is not yet a CI gate.

## Findings

| Report ID | Observation | Disposition |
| --- | --- | --- |
| D-1 | Copy failure and locator write failure each create two managers in total: the original disposed manager and one replacement. Only one maintenance task remains active; all tasks complete during final cleanup. | Claimed double replacement not reproduced. |
| D-10a | Locking a source log makes the copy fail, retaining a `.migration-*` staging directory. Original config bytes and the selected empty destination remain intact. | Residue reproduced, but source explicitly describes retention as recoverable. Lack of visible recovery/cleanup needs product-policy discussion. |
| D-10b | Holding a read directory handle without delete sharing on the staging directory lets copying finish but makes final rename fail. The selected empty destination has already been deleted; staging and source remain intact. | Finalization failure reproduced. No existing user files were lost, but the selected empty directory disappears. |
| D-11a | A controlled closed-temp/before-commit interleaving forces competing Save's open to fail. Its finally block deletes the other writer's temporary file even though it never acquired ownership. | Ownership bug reproduced with real Save code; the other writer's gap is simulated. |
| D-11b | Two genuine child processes each call Save 200 times against one isolated root. 13 saves succeed, 387 fail: 385 sharing violations and 2 missing-file exceptions. The final committed configuration remains readable. | Concurrent contention and commit failure reproduced. Counts are scheduling-dependent; this is not the normal single-instance GUI workflow. |
| D-12 | Save accepts a valid 5,244,018-byte config. Read rejects it at 4 MiB. Load restores the older backup and archives the oversized file; without a backup, Load fails. | Save/read size contract mismatch reproduced; latest changes are unavailable after recovery, although archived bytes remain recoverable. |

## D-1 Control Flow

The report's proposed sequence is not reachable in the quoted transaction:
the copy catch recreates a manager and immediately rethrows. Control transfers
to the outer rollback catch, skipping the entire locator/portable-flag block.
Conversely, locator failure can only follow a successful copy; it creates one
replacement, not a second replacement after the copy catch.

`SettingsHarness.cs` contains the extracted transaction. The script checks it
against current source before running, substituting only manager tracking,
the signal callback (no managed processes are started), and isolated filesystem
destinations. No modal settings dialog is driven, so this is control-flow and
lifecycle verification, not a full UI interaction regression.

## Limits and Safety

- Tests invoke real Core APIs; no production fault hooks or ACL changes are used.
- Directory locks, source locks and child-process handles are disposed. Worker
  processes have a 45-second deadline and are stopped if the deadline expires.
- The first exploratory rename probe used a zero-access directory handle and
  did not prevent rename; its failed expectation is retained separately. The
  second run and source-guarded final run use a read handle and both reproduce
  the failure. Exploratory failure is not counted as successful evidence.
- PASS means the scenario's observation/assertions succeeded, not that a
  reported defect is present. D-1 is explicitly a negative reproduction.
- Suggested repair order: D-12 size consistency, D-10 finalization/recovery,
  then D-11 unique temporary-file ownership and cross-process serialization.
  Manager replacement refactoring is not justified as a confirmed D-1 leak.
