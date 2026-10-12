# Isolated Signing Drill

Date: 2026-10-12. [GitHub Actions run 38215650657](https://github.com/hailaij/LaunchDesk/actions/runs/38215650657).
Remote main commit: `43449c5a876096dd0af75fcce9fa482e78d342f9`.
The verification job completed successfully in 3 minutes 29 seconds.

## Observed Results

| Native Authenticode case | Result |
| --- | --- |
| Valid isolated signature | Passed, 46 ms |
| Wrong publisher after native trust | Rejected as expected, 2 ms |
| Modified signed PE | Rejected as expected, 2 ms |
| Unsigned PE | Rejected as expected, 1 ms |

The main suite has 73 passed, 0 failed, 0 skipped. Five engineering contracts also
passed, covering missing production signing configuration, honest skip/failure reporting,
embedded pin mismatch, and unsigned release rejection. The bilingual UI/pixel,
virtualization, timer recovery and fatal process cleanup checks passed.

Runner: `github-hosted`, image `win25-vs2026`, image version `20260925.250.1`,
.NET SDK `10.0.401`, PowerShell `7.6.6`, as recorded by the uploaded environment report.

## Isolation And Cleanup

The runner generated a two-day self-signed test key/certificate in CurrentUser/My,
exported only its public certificate, and temporarily trusted it in LocalMachine/Root.
The fixture pin was supplied only to the test verifier, never to production signing
configuration or the application publisher pin. No private-key export is present in
the fixture script; downloaded evidence contains no PFX/P12/PVK/KEY/PEM files.

The log records creation, trust import, signing, native verification, and finally cleanup.
The strict certificate/key removal commands completed without failing the job. However,
there is no independent post-cleanup certificate-store inventory in this run's evidence.
Do not describe this as an independently observed empty certificate store. The disposable
runner lifecycle is a further isolation boundary; no root was imported on this workstation.

## Evidence Locations

Local root: `artifacts/verification/signing-drill/38215650657/`.

- `run.json` and `run.log`: run identity, source commit, all steps and complete logs.
- `summary.json`: locally checked counts and scope.
- `evidence/environment.json` and `evidence/test-results.json`: uploaded runner evidence.
- `evidence/engineering-contracts/results.json`: five expected-negative contracts.
- `evidence/ui/summary.json`, screenshots and fatal-cleanup results.
- `evidence/signing-fixture/fixture.json`: public fixture identity metadata.

The Actions artifact is named `LaunchDesk-verification-evidence`. Local artifacts are
git-ignored and are not embedded in release packages or included by pushing these docs.
Keep the local copy: Actions logs/artifacts can expire under repository retention settings.
The consolidated local SHA-256 inventory is described in [Verification status](verification-status.md).

## What This Does Not Prove

- No publicly trusted production certificate was issued or approved.
- The full SignPath application/uninstaller/installer signing and trusted release path
  did not run. The workflow created an unsigned preview artifact, not a public Release.
- Uncommitted Windows 10 fixes and incremental-log performance gates were not included.
- Test self-signing is not permission to accept self-signed production updates.

To repeat the remote baseline drill, dispatch `windows.yml` on an explicitly reviewed
remote ref. To validate local changes, first obtain authorization to commit/push them,
then run CI against that new commit. Never fake runner environment variables locally.
