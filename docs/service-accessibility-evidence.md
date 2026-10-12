# Local Evidence: Services, Elevation, Accessibility, Installer

Historical development snapshot: 2026-10-12, before v0.8.0 release review.
The counts and hashes below refer to that snapshot, not the current package.
Current review and verification: [v0.8.0](release-v0.8.0.md).

## Automated Checks

- Release build: zero warnings/errors; locked dependency restore succeeds.
- Core suite: 102 passed, zero failed/skipped, including the isolated native
  signature fixture. Report: `artifacts/verification/hosting/test-results.json`.
- Five hosting groups re-run after helper changes:
  `artifacts/verification/hosting/hosting-final.json`. Tests verify UAC launch
  intent/cancellation (not actual consent), service restrictions including GUI PE
  rejection, raw stdout/stderr tails, stdin, forced job stop, helper crash, and
  manager crash cleanup. Test helper is never a production privilege bypass.
- Chinese/English UI Automation checks include action names, form labels,
  log text, focus visuals, F2 editing and folder arrow keys:
  `artifacts/verification/hosting/accessibility-rows.json` and
  `artifacts/verification/hosting/accessibility-en-final.json`.
- Full UI/performance/fatal cleanup run:
  `artifacts/verification/hosting/ui/summary.json`. Measured normal-window
  incremental P95 93.07 ms/live P95 153 ms; English compact-window incremental
  P95 27.51 ms/live P95 169 ms. Both fatal cleanup cases left zero child survivors.
- Engineering contracts (including reviewed translation hash and complete Host
  signing inventory): `artifacts/verification/hosting/engineering-contracts/results.json`.
- Chinese installer isolation:
  `artifacts/verification/hosting/installer-zh/results.json`. Language registration,
  fresh empty data, upgrade/uninstall sentinel preservation and unchanged production
  registration passed. The prior-version test uses the same payload with synthetic
  old version metadata; it is not acceptance of a historical release binary.
- English installer isolation also passed all the same preservation/registration
  checks: `artifacts/verification/hosting/installer-en/results.json`.
- Final UI re-run: `artifacts/verification/hosting/ui-final/summary.json`, all
  checks passed. Incremental/live P95: 90.01/155 ms (normal) and 34.39/105 ms
  (English compact). Both fatal cleanup cases again left zero child survivors.
- Final unsigned development installer:
  `artifacts/verification/hosting/acceptance-installers/LaunchDesk-v0.7.1-win-x64-setup.exe`;
  SHA-256 `0512cac7d007c7fffb1d229ed69bb2c25198a88cafad98ceacbb73fd9642025c`.
  Its service-status query returned `NotInstalled`; no test helper processes
  remained. Privileged service deployment is still pending, not passed.

## Not Executed

Actual UAC approval/denial, alternate-account credentials, SCM registration/start/
stop under LocalService, Session 0 graceful stop, resource ACL denial, Narrator/
NVDA user acceptance and physical Win10 installation remain pending. No existing
service was interrupted and no system service was registered by these tests.

Local installers are unsigned development acceptance artifacts and require manual
installation. They have not replaced GitHub release v0.7.1. A desktop installer
does not register or upgrade the independent service runtime automatically.
