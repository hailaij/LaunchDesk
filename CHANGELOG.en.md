# Changelog

[简体中文](CHANGELOG.md) | [English](CHANGELOG.en.md)

### Unreleased

### [0.10.0] - 2026-10-14

- Simplify the toolbar with a More actions menu; emphasize folder groups, member counts and selected/focused row actions. Fix margins clipping the last row action.
- Organize launch-item configuration into five tabs. Numeric errors select the relevant category; closing edited forms asks before discarding changes.
- Expose cleanup warnings, preserve manually resized columns within a session and update automatic widths on same-band language changes. Numeric validation names the field and range; forced exit codes use consistent hexadecimal formatting.
- Group window options, reveal the draggable splitter and keep preflight actions in a fixed footer. Cover compact Chinese and English forms.
- Color log provenance prefixes without inferring severity. Preserve string projection compatibility, virtualization, selection and links; use system colors in high contrast. See [UI acceptance notes](docs/ui-usability.md).
- Align rotation and filenames to UTC while retaining local record offsets; derive ring capacity from retention limits.
- Cover timezone and reference-identity contracts, split display boundary cases and clarify performance units. Stream the complete source-hash inventory without reducing evidence.
- Add owned fixture retention and a read-only temporary-directory inventory. See the [maintenance record](docs/log-policy-maintenance.md).

### [0.9.1] - 2026-10-14

#### Fixed And Improved
- Bound text and raw-byte cache payloads to 4 MiB per item across writes, history loading and merged snapshots; persisted and exported raw logs remain complete.
- Correlate concurrent elevated-helper signal replies by request ID, isolating late timed-out replies; remove the redundant readiness wait loop.
- After inspecting the released DLL metadata, move UI smoke implementation into the test host and add production-assembly and negative-fixture gates.
- Expand the core inventory to 110 groups with raw-cache integrity and concurrent-signal regressions.

#### Limitations
- Stable, unsigned, manual installation only. Trusted signing remains blocked without production authorization. Physical Win10, real UAC/SCM, screen readers and one-hour endurance acceptance remain deferred; the historical intermittent interpreter timeout is still unexplained.

### [0.9.0] - 2026-10-13

#### Changed
- Removed in-app installer downloading, download verification and installation actions. Startup/manual checks still prompt for newer stable releases; a user click opens the fixed GitHub release page in the browser for manual installation.
- `dev` preserves the complete history through v0.8.1; `main` continues from v0.8.1. Existing tags and Releases remain unchanged.
- Stable release, unsigned; production signing gates remain strict. The original intermittent interpreter timeout remains unexplained.


### [0.8.1] - 2026-10-12

#### Fixed

- Manager-switch rollback, isolated service diagnostics and CLI JSON contracts; production fault injection moved to the test host.
- Empty recovery selections, installer compiler resolution and long log display prefixes, truncation markers and Chinese/emoji boundaries.

#### Improved

- Batched log persistence, pooled decoding and indexed deltas while retaining stop draining and raw-log integrity.
- 107 independently job-contained tests, timeout cleanup, bilingual resource keys, 1,000-item and 200 MiB gates, source-hashed evidence.

#### Limitations

- Unsigned, manual installation only; production authorization remains required for trusted publication. One-hour endurance, physical Win10 and real-service acceptance remain deferred.

### [0.8.0] - 2026-10-12

#### Added
- Continuous TCP/HTTP health monitoring with optional, default-off rate-limited restarts.
- CLI control of the existing desktop instance and user-confirmed crash recovery without adopting old PIDs.
- Per-item UAC helper, separate LocalService hosting, accessibility labels/keyboard editing and bilingual installer.

#### Fixed and Verified
- Service executable validation uses launch working-directory/PATH resolution; local identity queries release native handles.
- Packaging refuses stale nonempty payload directories to prevent accidental user-data inclusion.
- Expanded core inventory to 104 groups; refreshed bilingual/compact screenshots, performance gates and isolated installer data preservation checks.

#### Limitations
- Unsigned prerelease for manual installation only; production signing and trusted updates remain blocked.
- Real UAC/SCM, LocalService permissions, screen-reader, physical Win10 and real-service acceptance remain pending.

### [0.7.1] - 2026-10-12

#### Fixed
- Shared 4 MiB read/save/export/manual-backup limit prevents successful saves from reverting to older backups on restart.
- Failed final migration moves restore the selected empty directory and report retained staging data.
- Unique configuration/locator temporary files, serialized configuration commits and unique backup names prevent competing cleanup.

#### Verification
- Five new data reliability groups expand the core inventory to 81.
- Isolated fresh install, upgrade and data preservation with the same installer script and release payload, without interrupting active instances.

#### Limitations
- Unsigned prerelease for manual installation only; trusted update checks remain strict.
- Physical Windows 10 icon, encoding and installer-upgrade acceptance remains deferred; isolated identity tests do not upgrade an existing production installation.

### [0.7.0] - 2026-10-12

#### Added
- Automatic/system log encodings, incremental cursors and formatting cache; existing explicit encodings remain unchanged.
- Performance budgets and five negative gate tests; 76 main test groups.
- Independent data/lifecycle reproducers, verification index and isolated signing drill documentation.

#### Fixed
- Windows 10 missing icon glyphs, legacy Chinese output and split UTF-8 decoding.
- Retained duplicate-row identity, selection and reading position without routine full resets.

#### Optimized
- New-record processing, retained rows, cached width counts and lazy full-text materialization.

#### Limitations
- Unsigned prerelease, manual installation only; production signing/trusted updates remain blocked.
- Physical Windows 10, real services and full installation/upgrade acceptance not completed.
- Reproduced saving/migration edge cases remain unfixed; no independent post-cleanup certificate-store snapshot.

### [0.6.0] - 2026-10-12

#### Added

- Local rotating error diagnostics, runtime retirement and atomic log exports, preserving existing APIs and configurations.
- Explicit pass/fail/skip reports, isolated signing fixtures, bilingual UI and fatal process-cleanup CI.
- SHA-pinned Actions, dependency locks, unified SDK/version sources and a strict signed-release pipeline.

#### Fixed

- Concurrent log locking, corrupt configuration recovery, retention maintenance and blocked standard input.
- Windows command quoting, relative resource paths, npm preflight, stop/delete resource cleanup and exception recovery.
- Distinct forced-termination exit codes and detailed console errors; corrupt log records are skipped and failed exports preserve the destination.

#### Improved

- Visible-row log virtualization retains row/character selection, copying, links and reading positions.
- 73 test groups; CI rejects skips and uploads reports, screenshots and process-cleanup evidence.

#### Limitations

- Unsigned prerelease for manual installation only; in-app downloads remain disabled. Missing signing configuration blocks trusted publication.
- Real-service acceptance is deferred. Extremely long visible lines still have layout costs; no installation rollback.

### [0.5.0] - 2026-10-11

#### Added

- Item notes before the right-side run button, with search, full-text tooltips and double-click inline editing. Enter or focus loss saves; Esc cancels.
- Stop input delay of 0-300 seconds and optional Ctrl+C before input. Early exit skips pending input; stop timeout starts after input. Defaults preserve legacy behavior.

#### Fixed

- Disable flat column sorting and clear stale sort state so folder members remain below their headers. Drag and up/down reordering are retained.
- Increase spacing between Auto and Note headers and adjust compact Chinese/English column widths to retain complete action buttons.

#### Improved

- 42 isolated tests cover note/delay compatibility, stop signal/input timing and early exit. UI smoke tests cover note save/cancel, folder dragging and sorting recovery.

#### Limitations

- Stop input uses a fixed delay, not automatic Y/N prompt detection. Tune the delay using actual logs.
- Installers remain unsigned and in-app downloads disabled. Real-service acceptance is deferred; no automatic installation.

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
