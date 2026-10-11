# Security Reporting

Use [GitHub private vulnerability reporting](https://github.com/hailaij/LaunchDesk/security/advisories/new)
when available. If private reporting is not enabled, open an issue requesting a private contact
without including exploit instructions, secrets, logs or personal data. Do not disclose an
unfixed vulnerability publicly merely because private reporting is unavailable.

Only the latest stable release is the current maintenance target. Unreleased local test builds
are not signed releases. A SHA-256 checksum alone is not publisher authentication.

LaunchDesk intentionally runs user-selected local commands with the user's privileges.
Command execution by an authorized configuration is not a sandbox escape. Bugs that defeat
update publisher verification, corrupt configuration, escape managed process cleanup or cause
unintended execution are in scope. Never test against other users' machines or live services.

Production signing keys and test keys must remain separate. Test roots may only be imported on
disposable CI runners; do not use a fixture certificate as a production update trust pin.
