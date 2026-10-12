# Data Save and Migration Reliability

Date: 2026-10-12. Included in v0.7.1 (unsigned prerelease, manual installation).

## Contracts

- Configuration read, save, export and manual backup share a 4 MiB serialized
  UTF-8 byte limit. Exactly 4 MiB is accepted. Oversized writes fail before
  modifying the destination or making a backup, instead of succeeding and
  restoring an older configuration at the next startup.
- Every configuration save creates its own GUID temporary file with
  `CreateNew`, flushes it to disk, and cleans only that file. Commits are
  serialized by a path-derived Windows local named mutex (30-second timeout).
  Atomic replace retains the previous configuration and at most 20 automatic
  backups. Backup filenames also include a GUID. Concurrent writers use
  last-committed-snapshot semantics; this does not merge different edits.
- Locator saves also use unique temporary files and atomic overwrite, never
  deleting another writer's temporary file.
- Migration copies into an isolated sibling staging directory and keeps the
  source. A failed final move recreates the selected empty target if this
  transaction removed it, without overwriting an intervening file/directory.
  Copy/finalization errors include the retained staging path and underlying
  failure. If permissions prevent restoring the empty directory, that failure
  is reported too. The original locator is not switched on migration failure.

## Regression Evidence

`tests/LaunchDesk.Tests/DataReliabilityChecks.cs` adds five CI test groups:

1. Exact byte-boundary roundtrip, multibyte/escaped oversize rejection, existing
   and fresh configuration preservation, export and manual backup rejection.
2. Failed configuration and locator commits preserve foreign temporary files,
   clean their own files and leave committed data readable.
3. Two genuine child processes save 100 times each to an initially empty root;
   both complete, the final snapshot and 20 retained backups remain readable.
4. A locked source log fails copying; source and selected destination survive,
   and the exception names the retained staging directory.
5. A real non-delete-sharing directory handle blocks final rename; the empty
   target is restored and staging contains a readable configuration.

The normal suite inventory is now 81 groups. The original observations in
`data-lifecycle-reproduction.md` and their artifacts remain historical evidence;
the old defect-observation harness intentionally expects pre-fix behavior and
is not a current acceptance gate.

Local evidence directory: `artifacts/verification/data-reliability/`. Signature
tests use the existing publicly trusted Inno Setup fixture; no test certificate
or root is installed on this computer. Final version-specific build, cloud CI
and installation evidence are linked in the v0.7.1 Release verification section.

Final local results:

- `accepted-test-results.json`: 81 passed, 0 failed, 0 skipped; validated against
  the CI inventory checker. Earlier exploratory reports are retained separately,
  including the Windows locator-lock exception-type test correction.
- Release solution build: 0 warnings, 0 errors with `-warnaserror`.
- `ui/summary.json`: normal Chinese and compact English screenshot/pixel checks,
  timer recovery, fatal process-tree cleanup, and log performance gates passed.
  Rolling incremental p95: 78.1429 ms / 13.9303 ms; live p95: 79 ms / 56 ms.
- `engineering-contracts/results.json`: six local engineering gates passed.
  Deliberate missing/broken signature fixtures are negative test inputs, not
  silently counted as successful signature checks.

## Platform Acceptance

The development host is Windows 11 build 26300, not Windows 10. Physical Windows
10 icon, output-encoding and installer-upgrade acceptance was explicitly
deferred by the user on 2026-10-12. Local UI tests do not establish Windows 10
compatibility. No real services or user configuration were used by these tests;
no existing production application was upgraded or uninstalled in this task.

`tests/Verify-Installer.ps1` compiles the same installer script and old/new
release payloads with a random isolated AppId and installer mutex. Production
builds retain the original identity and guard. Tests disable shortcuts and
application launch, verify every installed payload file against the release
directory, load empty/preserved configurations, and compare configuration,
logs, manual backup and portable marker hashes across upgrade and uninstall.
Only the generated test registration is removed. Existing production registry
registration is checked unchanged. This tests the shared installation behavior,
not a byte-identical production installer execution or real-user configuration.
Sandbox files and installation logs are retained as evidence.
