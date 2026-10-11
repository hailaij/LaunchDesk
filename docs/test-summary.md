# Local Test Evidence (v0.6.0)

This replaces the outdated 19-group snapshot. `test-results.json` is local v0.6.0 evidence,
not evidence for older v0.5.0 binaries and not a substitute for GitHub-hosted CI results.

- 73 groups: 73 passed, 0 failed, 0 skipped. Status/null-passed semantics are explicit.
- Native signature tests used the existing Windows-trusted Inno Setup 6.7.3 compiler and its
  public DER certificate. No private key or trusted-root installation was required locally.
- Temporary CI self-signing fixtures run only on disposable GitHub-hosted runners.
  Consult the release notes and Actions reports for cloud acceptance, not this local snapshot.
- UI and engineering-contract evidence/limitations: [trust-and-engineering.md](trust-and-engineering.md).
- CI generates and uploads fresh reports; this local snapshot is not substituted for a CI result.
