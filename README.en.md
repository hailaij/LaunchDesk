# LaunchDesk

[Simplified Chinese](README.md) | [English](README.en.md)

A Windows desktop manager for scripts, commands and applications, with real-time logs. Latest published version: **v0.4.0** (early development).

![LaunchDesk](docs/images/main.png)

## Features

### New in v0.4.0

- Preflight checks for resource paths, working directories, interpreters and scripts in simple npm commands. Checks do not launch processes.
- Settings with data/backup directory access and immediate configuration snapshots.
- Drag items to reorder or change groups; drop BAT/CMD, EXE, PS1 and SH files to review each item before adding. Nothing starts automatically.
- Optional TCP, HTTP or current-run log keyword readiness detection. Dependents wait for readiness; timeouts terminate managed processes.
- Combined folder logs, sorted by timestamp and labeled by source, with filtering, clear-panel, export and confirmed history deletion.
- Double-click a name to rename inline. Enter saves, Esc cancels, focus loss saves. Renaming alone is allowed while running.
- Lower startup-order values start first; ties follow list order. Explicit dependencies take precedence. Applies to start-all, folders and login auto-start.
- HTTP/HTTPS log links are underlined, display hover hints and open with Ctrl+click. No automatic visits.
- Change the data directory by copying configuration, logs and backups to an empty destination. The original directory is retained; stop all items first.
- **Settings > Appearance > Language** switches between Simplified Chinese and English immediately after saving. Names, commands, paths and original log text are not translated.
- Background update checks at startup. Errors and up-to-date results are silent; new releases trigger a prompt, deferred until restoration when minimized to tray. Can be disabled in Settings.
- User-initiated installer downloads require the GitHub asset SHA-256, Windows Authenticode trust validation and the embedded publisher certificate pin. Failed or cancelled downloads remove temporary packages without replacing the current application or configuration. No silent installation.

**Signing limitation:** No publisher certificate is configured yet, so in-app downloads are disabled. Unsigned packages and packages from other publishers cannot pass verification. The release page remains available for manual downloads. See [Update security](docs/update-security.md). Download failure cleanup is not installation rollback.

### Core features

- BAT/CMD, EXE, npm/cmd commands, PowerShell and Shell scripts with an explicit interpreter.
- Per-item arguments, working directory, environment variables, encoding, dependencies, startup delay and stop input.
- Individual and bulk start, stop and restart; status, root PID, exit code and uptime.
- Concurrent stdout/stderr capture with live search, filtering, selection/copy, clear-panel and export.
- Original bytes retained as Base64 `RawBytes` in JSONL, even if decoding fails.
- Standard input with Enter, including BAT `pause`.
- Tray, minimize-to-tray, single-instance activation, login startup and per-item auto-start.
- Configuration import/export, atomic saves, the latest 20 automatic backups and portable mode.
- Light, dark and system themes; independent log auto-scroll control.
- Right-aligned row actions and collapsible application-only folders with group start/stop.
- About, open-source notices, issue links and manual release checks; no silent download or installation.

## Run

Windows 10/11 x64. Self-contained packages include the .NET runtime. Keep the entire extracted directory and launch `LaunchDesk.exe`.

Download `LaunchDesk-v0.4.0-win-x64-setup.exe` or the portable `LaunchDesk-v0.4.0-win-x64.zip` from [Releases](https://github.com/hailaij/LaunchDesk/releases/tag/v0.4.0). SHA-256 sidecar files are provided. Current installers are unsigned.

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

BAT defaults to its own directory when no working directory is supplied. Commands use cmd syntax. Shell requires a directly executable interpreter such as Git's `bin\bash.exe`, not the WSL launcher. Environment variables use one `NAME=VALUE` per line. UTF-8 is the default; GBK is available for legacy programs. Encoding applies to displayed output and stdin, not retained raw bytes.

Folders are application groups, not disk directories. Folder start also starts explicit dependencies, possibly outside the folder; folder stop affects its own members only. Deleting a folder dissolves it without deleting items, running processes, logs or scripts. Stop an item before editing its execution settings or deleting it.

### Data and logs

Default data directory: `%LOCALAPPDATA%\LaunchDesk`.

- `config.json`: items and preferences.
- `backups/`: up to 20 automatic configuration backups.
- `backups/manual/`: manual configuration snapshots, not subject to automatic pruning; no logs or scripts. Restore through configuration import.
- `logs/<item ID>/`: UTF-8 JSONL logs.

`portable.flag` next to the executable enables `data/` next to it. The application also allows a custom directory, recorded in `%LOCALAPPDATA%\LaunchDesk-location.json`; the portable flag takes precedence. Migration requires all items stopped, an empty writable destination, no symlinks/junctions and no ancestor/descendant relationship. Failed `.migration-<ID>` staging directories are retained for diagnosis.

Default retention is 7 days and approximately 200 MiB per item. Cleanup runs during rotation; a record may temporarily exceed the limit. Inactive items are not cleaned in the background. The panel shows at most 3,000 record fragments and approximately 500,000 characters; export accesses older retained data. Pausing or clearing the panel does not stop capture or delete files. History deletion is separate.

TXT is readable; JSONL retains timestamps, channels and raw bytes. Each pipe preserves its own byte order; interleaving stdout and stderr reflects read order, not a guaranteed global source ordering. Configurations can contain private paths and environment variables; only import trusted files and review exports before sharing.

### Process lifecycle

Processes are created suspended, assigned to a Windows Job Object, then resumed. Child processes inherit the Job. Closing its final handle terminates associated processes, including on manager crashes. If a parent exits but descendants remain, the item stays active and retains the root PID.

Stop sends configured input, or tries Ctrl+C using a short-lived console helper; after the default 10-second grace period it terminates the managed tree. Bulk shutdown reverses dependency/startup order. Optional auto-restart uses 2, 4, 8 and 16-second delays and pauses after five consecutive exits; running for over a minute resets the count. Log storage failure stops the item and reports the error.

Readiness defaults to off for compatibility. Timeouts range from 1 to 300 seconds. Log matching is case-sensitive literal text, supports chunk boundaries and ignores previous runs. TCP/HTTP success does not establish process ownership or ongoing health; use dedicated endpoints. HTTP readiness does not follow redirects.

Dragging running items is disabled. Drop on a folder to join it, before an item to reorder and adopt its group, or in empty list space to ungroup. Folders can reorder but cannot nest. Complex cmd expressions remain cmd's responsibility; preflight success does not guarantee application success.

No per-item elevation, WSL/Docker lifecycle management, Windows service hosting or remote administration. Externally hosted services and existing GUI single-instance processes may be outside the Job. Only actual stdout/stderr writes can be captured; file-only logs, unflushed buffers, power loss and storage failure cannot be guaranteed lossless.

## Build and Verify

Requires Windows x64, **.NET SDK 10.0.401** and [Inno Setup 6](https://jrsoftware.org/isdl.php) for installers. No third-party NuGet PackageReference dependencies; self-contained runtime packs restore from official NuGet.

```powershell
dotnet build LaunchDesk.slnx -c Release
dotnet run --project tests/LaunchDesk.Tests -c Release -- --report artifacts/test-results.json
./scripts/package.ps1
./scripts/build-installer.ps1 -CompilerPath 'D:\tool\Inno Setup 6\ISCC.exe'
```

Installer output: `artifacts/installers/LaunchDesk-v0.4.0-win-x64-setup.exe` plus SHA-256. Installs for the current user to `%LOCALAPPDATA%\Programs\LaunchDesk`, creates Start menu and optional desktop shortcuts, and does not start the app by default. Exit the tray instance before update/uninstall. Uninstall retains user data and removes only login startup entries belonging to its install path. The installer is English; the application supports Chinese/English. Certificates are not configured, so current installers remain unsigned.

See [Project structure](docs/project-structure.md). Generated files belong under `artifacts/`.

Tests use isolated directories and disposable processes. They cover output flooding, UTF-8 chunking, GBK, trailing partial lines, raw bytes, BAT paths/pause, npm, stdin, environment, dependencies, lifecycle races, restart limits, log failures and process-tree cleanup. Three real services remain untested at the user's request.

```powershell
artifacts/LaunchDesk-win-x64/LaunchDesk.exe --ui-smoke artifacts/ui.png
artifacts/LaunchDesk-win-x64/LaunchDesk.exe --ui-smoke-min artifacts/ui-min.png
artifacts/LaunchDesk-win-x64/LaunchDesk.exe --ui-smoke-en artifacts/ui-en.png
artifacts/LaunchDesk-win-x64/LaunchDesk.exe --ui-smoke-en-min artifacts/ui-en-min.png
```

WPF smoke tests capture actual layouts using isolated fixture data, without launching fixture items. GitHub Actions builds, tests and packages on Windows.

## Contributing

Use [Issues](https://github.com/hailaij/LaunchDesk/issues) or pull requests. Run isolated tests and do not commit private configuration, paths, environment variables or logs.

## License and Privacy

[MIT](LICENSE). Runtime terms: [Third-party notices](THIRD-PARTY-NOTICES.md). Network and local data handling: [Privacy Policy](PRIVACY.md).

## Changelog

### [0.4.0] - 2026-10-11

#### Added

- Immediate, persisted Chinese/English application switching and bilingual README navigation; user names, paths, commands and original logs are unchanged.
- Non-executing preflight checks for paths, interpreters, working directories and simple npm scripts.
- Manual configuration backups, data-directory access and safe migration to an empty destination while retaining the original data.
- Drag ordering/grouping and reviewed external script imports, without automatic launch.
- TCP, HTTP and current-run log readiness checks, dependency waiting and managed-process cleanup on timeout.
- Startup priority, inline name editing and source-labeled combined folder logs.
- Underlined HTTP/HTTPS log links with hover hints and Ctrl+click navigation.
- Background startup release checks and new-version prompts. User-initiated downloads require SHA-256, Windows trust and a pinned publisher certificate; failed/cancelled downloads clean temporary files without replacing the application/configuration. No silent installation.
- Bilingual privacy policy, user guides and signing integration documentation.

#### Fixed

- Folder logs now include all member output.
- Live language switching refreshes column headers; minimum-window English headings no longer clip.

#### Improved

- 39 isolated tests covering migration, readiness, merged logs, language compatibility and failed-download cleanup.
- Native trust checks accept valid signatures and reject mismatched publishers, tampered files and unsigned installers.
- Packages include bilingual guides and privacy policy, without local configuration, logs, screenshots or debug symbols.

#### Limitations

- No publisher certificate is configured yet. In-app downloads are disabled; manual release-page downloads remain available. Current installers are unsigned.
- No installation rollback. Real-service acceptance remains deferred at the user's request.

### 0.3.0 - 2026-10-11

Added row/group actions, folder grouping, About/open-source information, manual update checks, per-user installer and uninstall. Fixed routed-event startup failures, auto-start header clipping and row alignment. Excluded test data/screenshots/debug symbols from packages. Improved minimum-window layout, tray themes and cancellable folder startup; expanded isolated tests.

### 0.2.1 - 2026-10-11

Added multi-size icons throughout the executable/windows/tray, cross-version/path instance warnings and actual assembly version display. Fixed export assertions for chunk boundaries; preserved full output/raw-byte verification.

### 0.2.0 - 2026-10-11

Added persisted light/dark/system themes, log auto-scroll controls, pause and empty states. Fixed minimum-window input clipping and dialog/button layout. Expanded theme/filter/size smoke coverage.

### 0.1.0 - 2026-10-11

Initial release: item management, live/persistent logs, Job Object cleanup, tray, single instance, login startup, backups, portable mode, isolated tests and self-contained Windows packages.
