# LaunchDesk

[Simplified Chinese](../README.md) | [English](../README.en.md)

Windows 10/11 x64 launch manager. Run `LaunchDesk.exe`; the self-contained application includes its runtime.

Choose **Settings > Appearance > Language > English** or Simplified Chinese and save. The change is immediate and retained. Names, commands, paths and original log text are not translated.

First use has no sample items. Existing user configuration and logs are retained during upgrades. Closing the window hides it to the tray; **Exit** stops managed processes and drains logs. Exit before upgrading or uninstalling.

Each item has its own resource, arguments, working directory, environment variables and encoding. For npm, use the directory containing the intended `package.json` and its start script. Shell scripts require an explicit executable interpreter. Folders group items without moving disk files. Dissolving a folder retains its items and logs.

Preflight checks validate paths, interpreters and simple npm scripts without executing commands. They also run before actual startup. Complex cmd expressions only receive basic checks. Passing preflight cannot guarantee the underlying service will succeed.

Drag stopped items to reorder or join folders. Drop in empty list space to ungroup. Folders reorder but cannot nest. Dropped BAT/CMD, EXE, PS1 and SH files open an editor for confirmation; they do not move or auto-start.

Double-click names to rename inline: Enter saves, Esc cancels, focus loss saves. Lower startup-order numbers start first; ties follow list order, while explicit dependencies take precedence. Folder logs combine members in timestamp order with source labels. Underlined HTTP/HTTPS links show hover hints; Ctrl+click opens the default browser, without automatic visits.

Readiness defaults to off. Optional TCP, HTTP and current-run log keyword checks wait before starting dependents, with 1-300-second timeouts. Keywords are case-sensitive and ignore historical output. HTTP requires 2xx and does not follow redirects. Endpoint availability does not prove process ownership or ongoing health.

Default data: `%LOCALAPPDATA%\LaunchDesk`. Configuration: `config.json`; automatic backups: `backups/` (20 retained); manual snapshots: `backups/manual/` (not automatically pruned); logs: `logs/<item ID>/`. Manual snapshots contain configuration, not scripts or logs.

Portable mode uses `portable.flag` next to the EXE and its `data/` directory. Settings can copy data to an empty custom directory after stopping all items; the original directory remains. Symlinks/junctions and ancestor/descendant destinations are rejected. Failed `.migration-<ID>` directories remain for diagnosis. Custom user-mode paths are recorded in `%LOCALAPPDATA%\LaunchDesk-location.json`; portable mode takes precedence.

Default log retention: 7 days, approximately 200 MiB per item. Panel limits: 3,000 record fragments and approximately 500,000 characters. Pause/clear affects display only. Export retained logs as TXT or JSONL; JSONL retains original bytes. History deletion is separate and requires confirmation. Each stdout/stderr pipe preserves its own order; interleaving is read order, not a guaranteed global sequence.

Background startup update checks are on by default and configurable. Failures and current versions do not interrupt. New releases prompt when the window is visible. Download requires a user click and SHA-256, Windows Authenticode and pinned publisher certificate verification. Failure or cancellation removes temporary packages without replacing the current program/configuration. No silent installation or installation rollback. **No publisher certificate is configured yet; in-app downloads are disabled and unsigned installers are rejected.** Manual release-page access remains available.

The application normally runs without administrator privileges. It does not bypass PowerShell policy. Job Objects manage associated process trees; externally hosted services, existing GUI instances and WSL/Docker internals are not guaranteed to belong to them. File-only/unflushed output, power loss and disk failures cannot be guaranteed lossless.

Source and releases: [GitHub](https://github.com/hailaij/LaunchDesk). License: [MIT](../LICENSE). Runtime notices: [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). Local data and network handling: [Privacy Policy](../PRIVACY.md).
