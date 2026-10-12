# CLI and Crash Recovery

Available in v0.8.0 unsigned prerelease packages; manual installation only.

## Command-Line Control

Open the desktop application first, then run `LaunchDesk.Cli.exe` from the same
installation. The CLI never creates another process manager or starts the GUI
implicitly. It controls the existing instance for the current Windows user and
login session, including when the window is in the tray.

```powershell
.\LaunchDesk.Cli.exe list
.\LaunchDesk.Cli.exe status --json
.\LaunchDesk.Cli.exe start "koishi"
.\LaunchDesk.Cli.exe stop "koishi"
.\LaunchDesk.Cli.exe restart "koishi" --timeout 180
.\LaunchDesk.Cli.exe start --all
.\LaunchDesk.Cli.exe stop --all
```

Names are case-insensitive exact matches. Use a GUID from `list` if names are
duplicated or the name starts with `--`. Targets are individual items, not folder
names. `list` and unqualified `status` return all items, including health status.
`--json` returns the versioned response structure with `Success`, `Message` and
`Items`. Arguments and command contents are not exposed in these responses.
The unpublished post-v0.8.0 repair also returns this JSON schema on stdout for
invalid arguments, transport failures and timeouts when `--json` is present.
These failures retain their existing exit codes; non-JSON errors use stderr.
Exact help requests remain plain text. See [reproduction evidence](https://github.com/hailaij/LaunchDesk/blob/main/docs/v0.8-review-manager-service-cli.md).
`--all` follows configured priorities and dependencies; stop uses the existing
graceful stop/input/timeout logic. It is not limited to auto-start items.

Exit codes: 0 success/help; 1 operation rejected/failed; 2 invalid command;
3 manager unavailable or transport error; 4 response timeout. Default response
timeout is 120 seconds; `--timeout` accepts 1-3600 seconds. Connecting to an absent
manager fails within two seconds. A response timeout/disconnect does not undo a
command already accepted; query `status` before retrying. There is no remote
control, elevation, configuration editing, shell execution, or folder command.

IPC uses a current-user-only named pipe scoped by SID and login session. Frames
are length bounded (16 KiB requests, 4 MiB responses), connections are bounded,
and partial requests/write stalls time out. The endpoint is not a security boundary
against another program already running as the same Windows user. Configuration
replacement/migration refuses concurrent external operations; commands arriving
during migration or exit are rejected. Protocol version mismatches are rejected.

## Crash Recovery

The selected data directory contains `session.json`, separate from `config.json`.
It records a version, dirty-exit flag and active item GUIDs, not process IDs or
credentials. Each launch intent is written and flushed atomically before creating
the managed process, including dependencies. A journal write failure blocks that
launch. Completed/stopped items are removed. Clean application exit clears the
running set and marks the session clean; fatal cleanup preserves the pre-cleanup
set. Forced termination leaves the dirty journal for the next startup.

After a dirty exit, a dialog lets you select items to restart or skip recovery.
Only still-existing GUIDs are offered, in startup priority order. Recovery uses
the current configuration and normal preflight/dependency/readiness flow; it
does not adopt old PIDs. Configuration changes since the crash can therefore
change what is launched. Review the selection before confirming. Declining or
closing the dialog starts nothing. Other auto-start items are suppressed for this
startup, even when recovery is declined. When configuration was recovered from a
backup, no session recovery or auto-start is attempted.

A malformed/future/oversized journal is preserved, services are not started, and
the application reports its path. Back up and inspect that file before removing
it to reset recovery state. Session data is local and not uploaded. A stale dirty
journal in a copied/old data directory can cause a recovery prompt if you later
return to it. This is restart assistance, not process memory or transaction
recovery; shutdown/power-loss durability ultimately depends on the filesystem.

## Verification

Nine added core test groups cover CLI parsing/output/exit codes, duplicate names
and IDs, IPC lifecycle/errors, concurrent stop, bounded frames/disconnects,
journal lifecycle/dependencies, failed writes/corrupt input, fatal preservation
and forced owner termination. UI tests exercise IPC through the real WPF
dispatcher, recovery accept/skip, suppressed auto-start and clean-exit journals.
All use isolated data roots and test pipe names, not user services or data.

Historical pre-release verification on 2026-10-12: the Release build passed without warnings/errors, locked restore
passed, and all 97 core groups passed with zero failures/skips (including isolated
signature fixtures). UI integration, log performance gates and fatal child-process
cleanup passed. Evidence: `artifacts/verification/product/`. Physical Windows 10
and real-service acceptance remain deferred. No installer publication is part of
that development task. Current verification: [v0.8.0 review](release-v0.8.0.md).
