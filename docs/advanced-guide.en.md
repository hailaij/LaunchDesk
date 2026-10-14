# Advanced Usage And Development

[Back to README](../README.en.md) | [简体中文](advanced-guide.md)

This reference describes v0.9.0. Run commands from the repository root. See the [user guide](user-guide.en.md) for everyday operations.

## Run

Windows 10/11 x64. Self-contained packages include the .NET runtime. Keep the entire extracted directory and launch `LaunchDesk.exe`.

Manually download `LaunchDesk-v0.9.0-win-x64-setup.exe` or the portable `LaunchDesk-v0.9.0-win-x64.zip` from [Releases](https://github.com/hailaij/LaunchDesk/releases/tag/v0.9.0). SHA-256 sidecar files are provided. This stable release, unsigned requires manual installation and is not a trusted in-app update. Hashes do not replace publisher signatures.

First launch is empty. No local services are preconfigured or executed. Closing the window hides it to the tray. Use **Exit** in the tray menu to stop all managed processes and drain logs before upgrading. Single-instance activation may restore an already running older copy; window titles show the actual assembly version, and cross-version/path launches show a warning.

The application normally runs without administrator privileges. It does not elevate automatically or bypass PowerShell execution policy.

Theme changes take effect after saving Settings. Existing configurations remain compatible. Before upgrading a portable copy, exit it, keep a backup, and copy `portable.flag` and `data/` into the new directory.

### Item examples

| Resource | Type | Target | Arguments | Working directory |
|---|---|---|---|---|
| BAT launcher | BAT / CMD | `C:\Services\Chat\launcher.bat` | As needed | `C:\Services\Chat` |
| npm service | Command (cmd) | `npm` | `run start` | `C:\Services\Bot` |
| Executable | Executable | `C:\Tools\worker.exe` | `--port 3000` | `C:\Tools` |
| PowerShell | PowerShell script | `C:\Scripts\worker.ps1` | As needed | `C:\Scripts` |
| Git Bash | Shell script | `C:\Scripts\worker.sh` | As needed | `C:\Scripts` |

BAT defaults to its own directory when no working directory is supplied. Commands use cmd syntax. Shell requires a directly executable interpreter such as Git's `bin\bash.exe`, not the WSL launcher. Environment variables use one `NAME=VALUE` per line. New items default to automatic decoding: UTF-8 first, then system ANSI on invalid bytes. GBK is available for legacy programs. Existing settings are unchanged. Automatic mode uses UTF-8 stdin; explicit encodings apply to displayed output and stdin, not retained raw bytes.

Folders are application groups, not disk directories. Folder start also starts explicit dependencies, possibly outside the folder; folder stop affects its own members only. Deleting a folder dissolves it without deleting items, running processes, logs or scripts. Stop an item before editing its execution settings or deleting it.

### Data and logs

Configuration is limited to 4 MiB of serialized UTF-8 bytes. Oversized saves, exports and backups preserve existing files. Saves have unique temporary files; commits in the same Windows session are serialized. The last committed complete snapshot wins; different edits are not merged.

Default data directory: `%LOCALAPPDATA%\LaunchDesk`.

- `config.json`: items and preferences.
- `backups/`: up to 20 automatic configuration backups.
- `backups/manual/`: manual configuration snapshots, not subject to automatic pruning; no logs or scripts. Restore through configuration import.
- `logs/<item ID>/`: UTF-8 JSONL logs.
- `diagnostics/errors.log`: local error diagnostics, rotating at approximately 2 MiB with up to three older files; never automatically uploaded.

`portable.flag` next to the executable enables `data/` next to it. The application also allows a custom directory, recorded in `%LOCALAPPDATA%\LaunchDesk-location.json`; the portable flag takes precedence. Migration requires all items stopped, an empty writable destination, no symlinks/junctions and no ancestor/descendant relationship. Failed `.migration-<ID>` staging directories are retained for diagnosis.

Default retention is 7 days and approximately 200 MiB per item. Cleanup runs during rotation, at startup and hourly, including inactive items; a record may temporarily exceed the limit. The panel shows at most 3,000 record fragments and approximately 500,000 characters; export accesses older retained data. Pausing or clearing the panel does not stop capture or delete files. History deletion is separate.

TXT is readable; JSONL retains timestamps, channels and raw bytes. Each pipe preserves its own byte order; interleaving stdout and stderr reflects read order, not a guaranteed global source ordering. Configurations can contain private paths and environment variables; only import trusted files and review exports before sharing.

### Process lifecycle

Processes are created suspended, assigned to a Windows Job Object, then resumed. Child processes inherit the Job. Closing its final handle terminates associated processes, including on manager crashes. If a parent exits but descendants remain, the item stays active and retains the root PID.

Stop sends configured input, or tries Ctrl+C using a short-lived console helper; after the default 10-second grace period it terminates the managed tree. Bulk shutdown reverses dependency/startup order. Optional auto-restart uses 2, 4, 8 and 16-second delays and pauses after five consecutive exits; running for over a minute resets the count. Log storage failure stops the item and reports the error.

Readiness defaults to off for compatibility. Timeouts range from 1 to 300 seconds. Log matching is case-sensitive literal text, supports chunk boundaries and ignores previous runs. TCP/HTTP success does not establish process ownership or ongoing health; use dedicated endpoints. HTTP readiness does not follow redirects.

Dragging running items is disabled. Drop on a folder to join it, before an item to reorder and adopt its group, or in empty list space to ungroup. Folders can reorder but cannot nest. Complex cmd expressions remain cmd's responsibility; preflight success does not guarantee application success.

Per-item elevation requests UAC on every launch, without storing credentials. Independent LocalService hosting supports background scripts/console programs only. WSL/Docker lifecycle management and remote administration are not supported. Externally hosted services and existing GUI single-instance processes may be outside the Job. Only actual stdout/stderr writes can be captured; file-only logs, unflushed buffers, power loss and storage failure cannot be guaranteed lossless.

## Build and Verify

Requires Windows x64, **.NET SDK 10.0.401** and [Inno Setup 6](https://jrsoftware.org/isdl.php) for installers. Host uses Microsoft's locked `System.ServiceProcess.ServiceController` 10.0.0 and EventLog dependency; self-contained runtime packs restore from official NuGet.

```powershell
dotnet restore LaunchDesk.slnx --locked-mode
dotnet build LaunchDesk.slnx -c Release
dotnet run --project tests/LaunchDesk.Tests -c Release -- --report artifacts/test-results.json
./scripts/package.ps1
./scripts/build-installer.ps1 -CompilerPath 'D:\tool\Inno Setup 6\ISCC.exe'
```

Installer output: `artifacts/installers/LaunchDesk-v0.9.0-win-x64-setup.exe` plus SHA-256. Installs for the current user to `%LOCALAPPDATA%\Programs\LaunchDesk`, with optional shortcuts and no default application launch. Exit the tray instance before upgrade/uninstall. User data is retained. This version tests fresh install, upgrade preservation and uninstall using the same script and release payload with an isolated installation identity; it does not upgrade the active production installation. Physical Windows 10 and real-service acceptance remain deferred. Installers are unsigned and bilingual (Simplified Chinese/English). Desktop uninstall retains separate service registration, runtime and data; uninstall the service explicitly first.

See [Project structure](project-structure.md). Generated files belong under `artifacts/`.

Tests use isolated directories and disposable processes. They cover output flooding, UTF-8 chunking, GBK, trailing partial lines, raw bytes, BAT paths/pause, npm, stdin, environment, dependencies, lifecycle races, restart limits, log failures and process-tree cleanup. Three real services remain untested at the user's request.

The current source core inventory has 107 groups, each in a separate job-contained host with a 120-second deadline and process-tree cleanup. Without a signing fixture, three groups are explicitly skipped; strict CI rejects skips. Historical reports: [Test evidence](test-summary.md). Developers can run `./tests/Verify-Ui.ps1` and `./tests/Verify-Engineering.ps1`. Temporary self-signed trust roots are allowed only on disposable GitHub-hosted runners, never developer workstations. Bilingual XAML resource keys, 1,000-item configuration and 200 MiB log gates are described in [test isolation and scale](scale-and-test-gates.md), including bounded soak commands and limitations. The published v0.8.0 Release and [review record](release-v0.8.0.md) record its 104-group verification scope; subsequent repairs included in v0.9.0 are described in [manager, service and CLI follow-up](v0.8-review-manager-service-cli.md) and [log display boundary](log-projection-boundary.md). Packaging refuses nonempty publish directories; use a new output directory.

```powershell
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke artifacts/ui.png
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke-min artifacts/ui-min.png
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke-en artifacts/ui-en.png
tests/LaunchDesk.UiChecks/bin/Release/net10.0-windows/LaunchDesk.UiChecks.exe --ui-smoke-en-min artifacts/ui-en-min.png
```

WPF smoke tests capture actual layouts using isolated fixture data, without launching fixture items. GitHub Actions builds, tests and packages on Windows.
Production rejects all `--ui-*` test arguments. Reports include mandatory provenance
sidecars; see [test evidence and fault isolation](test-evidence-and-fault-isolation.md).
