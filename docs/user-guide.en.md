# LaunchDesk

v0.8.0: per-item UAC elevation, separate LocalService hosting in Settings,
accessible icon names/keyboard focus, F2 inline editing and a bilingual installer.
Desktop upgrades do not automatically replace service runtimes. See
[service and accessibility details](service-accessibility-installer.md) for separate
data, permissions and deferred privileged acceptance checks.

Output encoding: new items default to `auto`. Each output pipe starts with UTF-8 and switches to the Windows system ANSI code page on invalid UTF-8 bytes. `system` always uses that ANSI code page. Select `gbk` explicitly for legacy Chinese PowerShell/BAT output or `utf-8` for npm/Node. Existing settings are preserved; restart the item after changing encoding. Historical logs are not re-decoded. Detection is heuristic: mixed encodings or non-UTF-8 bytes that happen to be valid UTF-8 require an explicit setting. Input in auto mode uses UTF-8.

[Simplified Chinese](../README.md) | [English](../README.en.md)

Windows 10/11 x64 launch manager. Run `LaunchDesk.exe`; the self-contained application includes its runtime.

v0.9.0 is an stable release, unsigned for manual installation only. SHA-256 checks integrity, not publisher identity. Exit the previous tray instance and back up data before upgrading.

New in v0.8.0: CLI and crash recovery. Open the desktop app first; use the adjacent `LaunchDesk.Cli.exe list`, `status --json`, `start "name"`, `stop "name"`, or `restart "name"`. `start --all` / `stop --all` control all items. Use an item GUID when names are duplicated. The CLI controls the existing instance, without bypassing UAC or providing remote access. Configured per-item elevation still requires user consent. Exit codes: 0 success, 1 operation failed, 2 invalid arguments, 3 connection failed, 4 timeout; query status after a timeout.

After an abnormal exit, select items to restart or skip recovery. Other auto-start items do not run on that startup. Recovery uses the current configuration, preflight, dependencies and readiness; it never adopts old PIDs. Clean exit clears the running set. The data directory holds `session.json`; corrupt journals are preserved and prevent startup. See [CLI and crash recovery](cli-and-recovery.md).

Configuration reads, saves, exports and manual backups share a 4 MiB serialized byte limit. Rejected writes preserve existing files. Failed migrations retain the source, restore the selected empty target and report the staging path; restoration permission failures are reported too. Unique temporary files and serialized commits prevent competing cleanup, but do not merge different edits.

Choose **Settings > Appearance > Language > English** or Simplified Chinese and save. The change is immediate and retained. Names, commands, paths and original log text are not translated.

First use has no sample items. Existing user configuration and logs are retained during upgrades. Closing the window hides it to the tray; **Exit** stops managed processes and drains logs. Exit before upgrading or uninstalling.

Each item has its own resource, arguments, working directory, environment variables and encoding. For npm, use the directory containing the intended `package.json` and its start script. Shell scripts require an explicit executable interpreter. Folders group items without moving disk files. Dissolving a folder retains its items and logs.

Item notes appear to the right, immediately before the run button. Long notes are truncated with a full-text tooltip and are included in item search. Double-click a note (including an empty cell) to edit inline; Enter or focus loss saves, Esc cancels. Column sorting is disabled to keep folder members below their headers; use drag-and-drop or the up/down buttons to reorder.

Reliability: corrupt configuration recovers the newest valid automatic/manual backup, archives the original and disables auto-start for that session. Invalid backups and future schemas never overwrite the original. Idle history cleanup runs at startup and hourly; locked files are deferred with bounded in-memory diagnostics. Genuine log write failures still stop managed processes. Exports use captured per-file lengths and exclude subsequent appends without holding the writer lock throughout export.

v0.6.0: Log rows are recycled and only visible rows are laid out. Visible text supports character selection; row selection and Ctrl+A/copy include off-screen rows. Retained row selections and reading positions follow the rolling history window. Pause for uninterrupted inspection, and scroll horizontally for long lines. Virtualization does not alter disk records or exports. A single extremely long line still has layout costs; this is not a real-time guarantee.

Corrupt history records are skipped on load/export. Exports report the skipped count without modifying source files, and write a temporary file before replacing the destination; failure preserves an existing export. Deleted items release runtimes and file writers without deleting history. Forced tree termination uses 0xE04C4401 and is logged separately; an already-exited root retains its actual exit code.

Refresh failures are diagnosed and retried on the next tick. Fatal loading/dispatcher errors stop managed processes before exiting; unobserved task errors are recorded. Diagnostics live in the current data directory at `diagnostics/errors.log`, rotate at approximately 2 MiB and retain three older files. Diagnostic writes cannot be guaranteed if the disk is unavailable.

Input writes have an approximately two-second timeout. If a target does not read stdin, the managed tree is stopped to release the underlying blocked pipe before reporting failure. Input queue waits also time out after two seconds; a queue timeout alone does not stop the process.

Stop input delay accepts 0-300 seconds (default 0). For npm/BAT termination confirmation, enable **Send Ctrl+C before stop input**, enter Y, and set a delay such as 2 seconds. Stop timeout starts after sending input; early process exit skips any remaining delay and input. This is a fixed delay, not prompt detection. Without the checkbox, stop input is sent without a signal, preserving quit/exit commands. With no stop input, Ctrl+C is sent as before.

Preflight checks validate paths, interpreters and simple npm scripts without executing commands. They also run before actual startup. Complex cmd expressions only receive basic checks. Passing preflight cannot guarantee the underlying service will succeed.

Drag stopped items to reorder or join folders. Drop in empty list space to ungroup. Folders reorder but cannot nest. Dropped BAT/CMD, EXE, PS1 and SH files open an editor for confirmation; they do not move or auto-start.

Double-click names to rename inline: Enter saves, Esc cancels, focus loss saves. Lower startup-order numbers start first; ties follow list order, while explicit dependencies take precedence. Folder logs combine members in timestamp order with source labels. Underlined HTTP/HTTPS links show hover hints; Ctrl+click opens the default browser, without automatic visits.

Readiness defaults to off. Optional TCP, HTTP and current-run log keyword checks wait before starting dependents, with 1-300-second timeouts. Keywords are case-sensitive and ignore historical output. HTTP requires 2xx and does not follow redirects. Endpoint availability does not prove process ownership or ongoing health.

Optional TCP/HTTP health monitoring starts after readiness and can reuse a network readiness endpoint or use a separate address. Defaults: 30 seconds after each probe, 5-second timeout, 3 consecutive failures and 2 consecutive successes to recover. Health, check time and failure details appear in the status column, tooltip and status bar. Stop cancels monitoring.

Health-triggered restart is independently off by default. It affects only the current item, respects the existing stop-input/timeout flow, and does not stop/restart dependents. Defaults: 60-second cooldown and at most 3 restarts per rolling 10 minutes; configurable. Rate-limited failures retain the running process and show the reason. Persistent failures can restart after limits expire. User stop is never undone. Use lightweight, side-effect-free endpoints; monitoring repeatedly contacts the configured address.

Default data: `%LOCALAPPDATA%\LaunchDesk`. Configuration: `config.json`; automatic backups: `backups/` (20 retained); manual snapshots: `backups/manual/` (not automatically pruned); logs: `logs/<item ID>/`. Manual snapshots contain configuration, not scripts or logs.

Portable mode uses `portable.flag` next to the EXE and its `data/` directory. Settings can copy data to an empty custom directory after stopping all items; the original directory remains. Symlinks/junctions and ancestor/descendant destinations are rejected. Failed `.migration-<ID>` directories remain for diagnosis. Custom user-mode paths are recorded in `%LOCALAPPDATA%\LaunchDesk-location.json`; portable mode takes precedence.

Default log retention: 7 days, approximately 200 MiB per item. Panel limits: 3,000 record fragments and approximately 500,000 characters. Pause/clear affects display only. Export retained logs as TXT or JSONL; JSONL retains original bytes. History deletion is separate and requires confirmation. Each stdout/stderr pipe preserves its own order; interleaving is read order, not a guaranteed global sequence.

Startup checks are configurable and only prompt for newer stable releases. Click Open download page to launch the fixed GitHub Release URL in your default browser. No browser opens automatically. Download and installation are manual; in-app download, signature validation and installation actions were removed in v0.9.0.

The application normally runs without administrator privileges. It does not bypass PowerShell policy. Job Objects manage associated process trees; externally hosted services, existing GUI instances and WSL/Docker internals are not guaranteed to belong to them. File-only/unflushed output, power loss and disk failures cannot be guaranteed lossless.

Source and releases: [GitHub](https://github.com/hailaij/LaunchDesk). License: [MIT](../LICENSE). Runtime notices: [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). Local data and network handling: [Privacy Policy](../PRIVACY.md).
