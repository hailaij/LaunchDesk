# Test Evidence and Fault Isolation / 测试证据与故障隔离

## Production Boundary / 生产边界

The production entry point rejects all `--ui-*` arguments with exit code 2 before
opening user configuration or creating a manager. Fatal fault injection is owned
only by `tests/LaunchDesk.UiChecks/FaultProbe.cs`; there is no `FaultProbe` member
or fatal-injection branch in the application assembly. Screenshot commands also
run through the separate UI host. Existing rendering probes inside MainWindow
remain available to that host, but are not exposed by the production CLI.
The release packager rejects fault-probe metadata and test-host binaries in its
actual staging payload; engineering regression rejects the real test-host DLL
as a negative fixture for this production-assembly check.

`Verify-UiFailures.ps1` checks rejection of six legacy production test arguments,
then starts independent loaded/dispatcher fault fixtures. The test host raises
the fault; the actual application error handler, diagnostics and manager cleanup
handle it. An observer records root/child PIDs before injection and verifies
exit code 1, diagnostic output and no survivors afterward. No real service runs.

## Report Provenance / 报告来源

The core runner keeps the existing test-result array and adds a mandatory
`<report>.metadata.json` sidecar. It contains schema version, start/end UTC times,
Git HEAD, working-tree changes, test filter, SDK/runtime/OS/architecture, runner
image and workflow SHA, report SHA-256 and working-source file SHA-256 values.
Only allowlisted runner information is collected; no environment/secret dump.

`Assert-TestReport.ps1` requires the sidecar, validates provenance fields and
timestamps, binds its report hash and checks workflow/source commit consistency.
Engineering negative contracts prove missing metadata and tampered reports fail.
The metadata is an audit record, not a cryptographic publisher attestation.

The old 73-case `docs/test-results.json` had no provenance and has been removed.
Historical counts remain clearly labeled in `test-summary.md`; fresh reports are
generated under ignored `artifacts/verification/`, not overwritten into historical
release records. Remote CI uploads reports and metadata together.

## Commands / 命令

```powershell
dotnet restore LaunchDesk.slnx --locked-mode
dotnet build LaunchDesk.slnx -c Release --no-restore -warnaserror
./tests/Verify-Ui.ps1
./tests/Verify-Engineering.ps1
# Screenshot test host, not the distributed application:
./tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke artifacts/verification/screenshot.png
```

Formal SignPath credentials and production pins are unchanged. Test signatures
never authorize production updates. This work does not perform UAC/SCM/Win10
acceptance or publish a new application version.
