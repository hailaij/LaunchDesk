# Historical Test Evidence (v0.6.0/v0.7.0)

Pre-release evidence recorded 2026-10-12. Do not mix the original local v0.6.0
snapshot, baseline remote CI, and the then-uncommitted working tree. Its changes
are included in v0.7.0; that release's notes record version-bumped acceptance.
None of these historical reports validates older v0.5.0 binaries.

- Original local v0.6.0 evidence: 73 passed, 0 failed, 0 skipped.
  The unprovenanced `docs/test-results.json` snapshot has been removed; it remains
  accessible in Git history and must not validate current source or binaries.
- Historical v0.7.0 working tree: 76 passed, 0 failed, 0 skipped, recorded in
  `artifacts/verification/log-incremental/test-results.json`. The assertion inventory
  was 76 at that time; these changes were subsequently published in v0.7.0.
- Remote commit `43449c5a876096dd0af75fcce9fa482e78d342f9`: 73 passed, 0 failed,
  0 skipped in [run 38215650657](https://github.com/hailaij/LaunchDesk/actions/runs/38215650657).
  All four isolated signature cases actually executed. This is not cloud acceptance
  of the local 76-group working tree.
- Native signature tests used the existing Windows-trusted Inno Setup 6.7.3 compiler and its
  public DER certificate. No private key or trusted-root installation was required locally.
- Temporary CI self-signing fixtures run only on disposable GitHub-hosted runners.
  Details and cleanup-evidence limits: [Signing drill](signing-drill.md).
- UI and engineering-contract evidence/limitations: [trust-and-engineering.md](trust-and-engineering.md).
- CI generates and uploads fresh reports; this local snapshot is not substituted for a CI result.

See [Verification status](verification-status.md) for report locations, known gaps,
performance budgets, and the local evidence hash index. Expected-negative contract
reports intentionally contain failures/skips and must not be counted as main-suite results.
