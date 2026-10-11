# P1 Correctness And Log Virtualization

2026-10-11. Unreleased working-tree changes, following `reliability-first-batch.md`.
The public v0.5.0 release has not been replaced. No real user services were started or stopped.

## Reproduction Before Repair

Baseline: the working tree with the first reliability batch, before this batch's repairs.
An isolated copy of the baseline Core/App binaries was preserved under
`artifacts/verification/correctness/baseline-run` and `ui-baseline`.

`before-corrected.json` contains 60 groups: 53 passed, 7 newly added groups failed:

| Reproduction | Original result | Repair |
| --- | --- | --- |
| Native argv containing `C:\`, spaces, empty strings, embedded quotes and slash/quote combinations | Values changed after launch | CRT-compatible escaping; cmd resource quoting remains separate |
| `my worker.cmd` relative to a configured working directory | Preflight looked for `my`; command could not run | Resolve literal resources against that directory before quoting |
| Malformed npm package | Raw JsonException without file context | InvalidDataException retains JsonException and exact package path; non-object roots also rejected |
| Delete an item after execution | No runtime retirement path | Retire, cancel/drain, close writer, remove registry entry; prevent resurrection; history retained |
| Force stop a sleeping worker | Exit code 1, indistinguishable from application failure | Manager termination code 0xE04C4401 and separate system record |
| Valid log records surrounding a broken JSON line | Entire export aborted | Skip/count corrupt records for TXT, JSONL and merged exports; preserve original files |
| Export with a locked source and an existing destination | Existing export truncated before input was opened | Write a sibling temporary file and commit only after all reads/writes succeed |

The first attempted locked-source test held an active writer's file incorrectly; it was corrected
to lock a historical fixture. Use `before-corrected.json`, not the earlier draft report.

Separate UI hosts reproduce two more failures against the baseline assemblies:

- `timer-before.json`: an actual dispatcher timer tick sees duplicate fixture IDs;
  the application stops its timer and shows a modal error. Timer never resumes; no diagnostics.
- `loaded-before.json`: an async loaded-handler failure escapes the application's handler;
  the test host catches it. No application diagnostics.

After repair, `timer-final.json` verifies subsequent real ticks update a sentinel summary;
`loaded-final.json` verifies containment and diagnostic creation.

## Reliability Changes

- Check SetConsoleCtrlHandler and preserve the failing native operation/Win32 code in helper stderr.
  The application drains stderr, awaits helper exit, and reports the detail in process logs.
  AttachConsole failure is tested; rare SetConsoleCtrlHandler/GenerateConsoleCtrlEvent failures
  have explicit branches but were not artificially injected into kernel32.
- Retired runtimes cannot restart or reopen a disposed LogStore. Native pipes/Job/Process are drained
  and disposed by their lifecycle. Runtime semaphore gates are managed-only and have no allocated
  OS wait handles; they remain valid for already queued callers until collection.
- Shutdown also stops runtime entries no longer present in configuration. Repeated DisposeAsync
  calls await the same cleanup task. Shutdown preserves the graceful stdin path.
- Failure-path process-exit tasks are awaited after termination rather than abandoned.
- Refresh errors are diagnosed, rate-limited and retried on the next tick without repeated dialogs.
  OnLoaded is contained; dispatcher-fatal failures diagnose and stop managed processes before exit.
  Unobserved tasks and terminating AppDomain errors are recorded when possible.
- Diagnostics follow data-directory changes. `diagnostics/errors.log` rotates near 2 MiB with
  three old files. An unwritable disk cannot guarantee a diagnostic record.
- Corrupt recent-history records, including null/missing text, no longer crash history loading.
  Failed history reads remain retryable on a later selection.
- Existing void export APIs remain intact; overloads expose the corrupt-record count.
- Column headers use one language-notified binding source instead of imperative values in Populate.
  A direct DynamicResource experiment did not update detached column headers and was rejected.

## Virtualized Logs

The full-history FlowDocument has been replaced with a recycling VirtualizingStackPanel.
Data remains bounded by the existing record/character limits. Only visible rows create rich-text
documents, link inlines and measured text layouts. A character-width estimate stabilizes horizontal
extent without shaping off-screen text. Exact shaping occurs only for visible rows.

Visible rows preserve character selection, link underlines/tooltips and Ctrl+click. Ctrl/Shift row
selection and Ctrl+A/copy include off-screen rows; context-menu focus retains character copy.
Stable line identities retain selected rows and reading
anchors during append/rolling retention. Horizontal scrolling, pause, filters and merged sources
are exercised. Long-row geometry assertions ensure a fixed-height row does not wrap and hide text.

This is UI virtualization, not disk/data virtualization. Copy-all, filtering and text snapshots still
process bounded data; one extremely long visible line still has shaping costs. The UI is not a
real-time renderer, and the estimate may leave some extra horizontal scrolling space.

### Measurements

Same-machine English 940 x 640 smoke, ten batches of 1,000 lines, approximately 347,828 retained
display characters. Measurements include Refresh, visible layout and deferred auto-scroll:

- Baseline `ui-before-live-log-performance.json`: 200-362 ms per batch.
- Selectable virtual rows `ui-visual-min-live-log-performance.json`: 50-133 ms per batch.
- Approximately 2,001 data rows but only 3 realized row controls at this small viewport.

An earlier TextBlock-only prototype measured 11-68 ms but removed character selection;
it was not the delivered implementation. Detached construction timings are not frame latency.
Packaging validation emits its own measurements and is a separate run, not a new baseline.
The final packaged English minimum smoke (`packaged-final-en-min-live-log-performance.json`)
measured 32-60 ms with the same ten batches, character count and three realized rows.

## Report Dispositions

| Original P1 | Disposition |
| --- | --- |
| 1, 7, 10, 19 | Fixed and tested in the first reliability batch |
| 2-6, 8-9, 17-18 | Addressed in this batch as described above |
| 11 | Binding hardening plus live bilingual UI regression; original language-switch failure was not reproduced |
| 12 | Current/legacy schema 1 is supported; future schema is preserved/refused, not guessed or silently downgraded. Migration requires a real future schema contract |
| 13 | Separate mutex security review needed. A same-user local DoS is not prevented simply by a SID ACL; installer/application coordination must remain consistent |
| 14 | Intentional descendant tracking, not a demonstrated correctness defect; existing root-exit/child-liveness/stop tests pass |
| 15 | Surrogate-split keyword reproduction passes against original and fixed Core; no speculative readiness change applied |
| 16 | HTTP 2xx-only/no-follow is intentional; a new 302 timeout/cleanup test passes. Guides explicitly state the policy |
| 20 | Update-cache reparse race remains a separate security-hardening task; this batch does not claim it solved |

The original native creation failure path also passes a warmed-up repeated handle-count probe;
no Job/pipe handle leak was observed there. Windows returned error 216 for the invalid PE fixture
on this host; both 193 and 216 are valid fixture rejection outcomes. Cold runtime initialization
was excluded before measuring handles.

## Verification

- `final-all.json`: 71 isolated Core groups, zero failures, including the available native signed fixture.
- Build: zero warnings/errors. No new third-party package dependencies.
- UI hosts: timer recovery and loaded-error containment, before/after evidence.
- Chinese normal / English minimum smoke: empty first use, folders, inline edits, filters, pause,
  links, theme/language switch, 10 x 1,000 live lines, auto-scroll and 30,000 short-row virtualization.
- Virtualization assertions cover off-screen copy, retained row selection, reading anchor,
  character selection and recycled link removal.
- `tests/Verify-UiFailures.ps1`: loaded/dispatcher fatal fixtures start only a cmd/ping test tree;
  root and observed children all exit, code 1 and diagnostics are verified.
- Final packaged binaries passed Chinese normal and English minimum smoke with exit code 0.
  `packaged-final-fatal/results.json` verifies both fatal modes: two observed children each,
  no survivors, diagnostics present and exit code 1. The package contains no PDBs, test screenshots,
  configuration, JSONL histories, logs or backups.
- Real NapCat/SnowLuma/koishi services remain outside acceptance at the user's request.

Focused Core rerun:

```powershell
dotnet run --project tests/LaunchDesk.Tests -c Release -- --report artifacts/verification/correctness/focused.json --filter Correctness
dotnet run --project tests/LaunchDesk.UiChecks -c Release -- --timer artifacts/verification/correctness/timer.json
dotnet run --project tests/LaunchDesk.UiChecks -c Release -- --loaded artifacts/verification/correctness/loaded.json
./tests/Verify-UiFailures.ps1
```

## Local Installer

`artifacts/previews/p1-correctness/installer/LaunchDesk-v0.5.0-win-x64-setup.exe`

Verified local size: 53,534,194 bytes. SHA-256 (matches the adjacent checksum file):
`4DFDDBA4B48BAA30E0A2BBABF0EFE3C8981F7F3260D2D6A3E28E28DAF8EC1E81`.

This is an unsigned, unreleased test build; its version has intentionally not been bumped and it
is not the GitHub v0.5.0 download. The adjacent SHA-256 file describes this local binary, not an
Authenticode identity. No signing workflow, certificate approval, silent update or installation
rollback is implied. Exit the existing tray instance before installation; that stops its services.
