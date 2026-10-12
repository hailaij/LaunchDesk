# Services, Elevation, Accessibility and Installer Languages

Available in v0.8.0. Tests and desktop installer do not
register production services. Unsigned builds require manual installation.

Local results and deferred acceptance: [evidence](service-accessibility-evidence.md).

## Per-item UAC

Enable "Request administrator consent on every launch" in the item editor.
`LaunchDesk.Host.exe` uses Windows `runas` for every launch, including opted-in
restarts. No passwords are stored. Cancelled UAC never falls back to unelevated
execution; the desktop manager itself stays unelevated.

The helper owns the process-tree job and forwards raw stdout/stderr/stdin. Stop
signals run in the helper's context. Pipes restrict access to the owner's SID,
verify both endpoint PIDs, use a random nonce, and validate owner start time.
Disconnect or owner/helper termination kills the job. Different-account UAC
credentials are intentionally unsupported; use same-account administrator consent.

## Windows Service

Settings -> Background hosting -> Windows service management. Export a desktop
configuration, then select that JSON in Install service. This is a copy, not a
link: desktop editing/import/migration cannot change service configuration.
Install, replace configuration, start, stop and uninstall require user confirmation
and administrator authorization. Service status can be queried without elevation.

- Name: `LaunchDesk.Managed`; manual/demand start; `NT AUTHORITY\LocalService`.
- Runtime: `%ProgramFiles%\LaunchDesk\Service`, administrator-protected.
- Data/logs/backups/diagnostics: `%ProgramData%\LaunchDesk\Service`.
- Administrators/SYSTEM: full access; LocalService: runtime read/execute and data
  modify access; ordinary users: no service data access. Links are refused.
- Starting the service starts all independent service items with dependencies,
  readiness, health monitoring and process-tree ownership. Stop before replacing
  configuration. Elevated or visible-window items and GUI-subsystem executables
  are rejected. Scripts indirectly launching GUI apps are also unsupported;
  indirect GUI launches cannot reliably be detected before execution.
- LocalService needs resource/directory permissions. User-profile resources,
  mapped drives and interactive apps are unsuitable. Grant only required access;
  do not promote the account to administrator to bypass permission errors.
- Uninstall removes registration but retains data/runtime. Reinstall requires
  administrator review and clearing the retained runtime directory. Desktop
  upgrades do not silently upgrade service runtimes; stop/uninstall first.
- The GUI panel shows SCM status, not an additional live log viewer. Logs remain
  in the separate protected service data directory.

Administrator CLI equivalents (no UAC bypass):

```powershell
LaunchDesk.Host.exe service install C:\service-config\config.json
LaunchDesk.Host.exe service status
LaunchDesk.Host.exe service start
LaunchDesk.Host.exe service stop
LaunchDesk.Host.exe service configure C:\service-config\config.json
LaunchDesk.Host.exe service uninstall
```

## Accessibility

Translated automation names for icon buttons, row context, labelled form inputs,
actual log text and stable keyboard focus outlines. Tab/Shift+Tab navigates;
Enter/Space activates standard controls; F2 edits selected name or note cell;
Left/Right collapses/expands folders. System high contrast overrides the regular
palette when themes are applied. Chinese/English UI Automation regression checks
do not replace manual Narrator/NVDA or Win10 acceptance.

## Installer Languages

English and simplified Chinese are selectable, including shortcut tasks, launch
choice and uninstall warning. Existing AppId is unchanged; previous language is
retained by Inno Setup. Translation provenance/hash: `installer/Languages/README.md`.

## Evidence Boundaries

Broker tests run a non-elevated helper in the *test executable* for isolated
transport/log/input/crash checks. Production Host always requires an administrator
token and has no test bypass argument. Actual UAC consent, SCM registration,
LocalService resource permissions, Session 0 stopping and physical Win10 acceptance
remain manual checks. Tests must not report these unexecuted checks as passed.
