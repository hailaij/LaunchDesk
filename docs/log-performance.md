# Incremental Log Refresh And Performance Gates

Implemented and locally verified on 2026-10-12, on the uncommitted v0.6.0 working tree.
This initial snapshot is not version-bumped release acceptance. The implementation
is included in v0.7.0; see its release notes for the new commit and CI verification.

## Runtime Changes

- `LogStore.ReadSince` returns an atomic cursor, retained-buffer boundary, reset flag,
  and only new records. Clear, history loading, disposal, and missed retention invalidate
  old cursors. All cursor/memory changes share the existing memory lock, not the disk lock.
- `LogProjection` follows each selected source's cursor and caches ANSI stripping and
  formatted lines by record identity. Folder names, membership, channel/search filters,
  and clearing remain supported. Cache entries outside the merged snapshot are retired.
- The merged ordering and display bounds still scan bounded record metadata. This is
  not an incremental priority queue or a claim of constant-time folder merging.
- `LogView.SetLines` avoids joining/splitting the entire display on normal refresh.
  `Text` is materialized lazily for copying/inspection. Retained rows keep their identity;
  WPF receives individual remove/add notifications instead of a Reset on append/rolling
  retention. Unrelated replacements can still Reset, such as selection/filter changes.
- Prefix links identify retained suffixes in linear time, including repeated/blank lines.
  A head offset avoids shifting the backing list on every removal. Cached ASCII/wide
  character counts avoid recounting every retained character for horizontal extents.
- The existing 500 ms refresh interval, 3,000-record/500,000-character memory bounds,
  disk capture, raw bytes, persistence, export, and production signature rules are unchanged.
  Viewer line endings are normalized; raw exports remain unchanged.

## Mandatory Gates

`Verify-Ui.ps1` runs these checks for normal Chinese and compact English windows.
It is already called by `Invoke-CiVerification.ps1` in the Windows workflow, so a gate
failure fails verification before packaging. Missing/malformed metrics also fail.

| Workload | Gate |
| --- | --- |
| 40 rolling batches, 20 rows each, 3,000 retained rows | P95 <= 250 ms; maximum <= 1,500 ms |
| Same rolling workload | 0 collection Resets; selection and reading anchor preserved |
| Synchronous rolling refresh allocation | <= 1 MiB per batch |
| 10 live batches, 1,000 lines each | P95 <= 750 ms; maximum <= 1,500 ms |
| Initial 30,000-line virtualization and replacement | maximum <= 1,500 ms |
| Live/rolling views | 1-100 realized rows; display remains bounded |

Timing includes refresh through dispatcher ApplicationIdle/layout, not disk-writing time.
Allocation measures the synchronous SetLines call plus its input-array creation on the
UI thread, not subsequent layout, background work, or total process working set. P95 uses
nearest rank. Thresholds allow CI scheduling variation; structural checks are exact.
They are regression budgets, not a promise of frame-rate performance on every machine.

`Verify-LogPerformanceContracts.ps1` creates explicitly synthetic negative reports for
slow refresh, excessive allocation, Reset, missing samples, and nonnumeric samples.
Each must be rejected. These are expected-negative contracts, not skipped/passing UI runs.

## Local Evidence

Evidence root: `artifacts/verification/log-incremental/`.

- `test-results.json`: 76 passed, 0 failed, 0 skipped. The existing public Authenticode
  fixture was used for signing tests; no certificate/root was generated or imported.
- `accepted-ui/`: normal/compact screenshots, raw timing/allocation metrics, gate reports,
  selection, links, copy-all, repeated-line retention, timer recovery, and fatal cleanup.
- `accepted-ui/performance-contracts/results.json`: all five expected rejections passed.
- Chinese normal window: rolling P95 72.56 ms; 1,000-line batch P95 79 ms.
- English compact window: rolling P95 11.81 ms; 1,000-line batch P95 61 ms.
- Both: 0 rolling Resets; synchronous allocations at most 102,680 bytes per batch.

Earlier exploratory runs are retained separately, including a failing probe that allowed
an earlier auto-scroll callback to interfere with its reading-anchor setup. The accepted
probe disables auto-scroll and drains the dispatcher before measuring manual reading.
No user services were started or stopped. Actual Windows 10 hardware and latest cloud CI
have not been run for these changes. Prior data-lifecycle reproduction findings are not
fixed by this log-only change.

## Run Locally

```powershell
dotnet build LaunchDesk.slnx -c Release --no-restore -warnaserror
dotnet run --project tests/LaunchDesk.Tests -c Release --no-build -- --filter "Incremental log" --report artifacts/verification/log-incremental/focused.json
./tests/Verify-Ui.ps1 -OutputDirectory artifacts/verification/log-incremental/ui
```

The complete strict CI suite still requires its isolated signing fixture. Do not import
CI trust roots onto a developer workstation to make signing tests pass.
