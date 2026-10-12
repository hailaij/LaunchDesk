# Runtime Health Monitoring

Available in v0.8.0 unsigned prerelease packages; manual installation only.

## Configuration

Each launch item has a `Health` section. Monitoring and health-triggered restart
are independently off by default. Existing configurations keep their behavior.
Enable TCP or HTTP checks using a separate endpoint, or reuse TCP/HTTP readiness
configuration. Disabled/log readiness cannot be reused.

Defaults: 30-second interval, 5-second probe timeout, 3 consecutive failures to
become unhealthy, and 2 consecutive successes to become healthy/recover.
Interval is measured after a probe completes; probes never overlap. HTTP accepts
only 2xx, does not follow redirects, and keeps normal TLS verification.

Ranges: interval 1-3600 seconds, timeout 1-300 seconds, failure/recovery thresholds
1-100, restart cooldown 1-3600 seconds, restart budget 1-10 per rolling 10 minutes.

## Lifecycle

Checks start only after configured readiness succeeds. With readiness disabled,
checks start after process creation. Initial status is awaiting checks until a
threshold is reached. Individual failed probes retain the previous health state
but show their latest reason and check time. Successful recovery clears the
failure reason. Only state/suppression changes are logged, not every check.

Health status is separate from process status. The status column, hover details
and selected-item status bar show health, last check and failure reason.

Optional health-triggered restarts affect only the current item. They use the
existing graceful stop/input/timeout flow, drain both output pipes, dispose the
old process tree and then launch a new process with readiness checks. Existing
exit-triggered auto-restart remains separate. Dependencies are neither stopped
nor restarted, and health failures do not retroactively cancel dependents.

Default restart cooldown: 60 seconds; budget: 3 per rolling 10 minutes. Suppressed
restarts keep the process running/unhealthy and show the limiting reason. Once
cooldown/budget allows another restart, persistent failure can request it. The
budget counts restart requests before stopping, survives health restarts within
the managed session and resets on a new manual start. User stop, deletion and
manager disposal cancel and join monitoring, including in-flight network probes.
Log persistence failure still prevents automatic restart.

## Verification and Limits

Seven added core groups cover defaults, configuration validation/roundtrip,
deterministic cooldown and rolling budget, HTTP thresholds/recovery/log volume,
TCP/readiness ordering and reuse, restart/dependency/stop isolation, HTTP redirect
and timeout behavior, disabled monitoring and cancellation on disposal.
The v0.8.0 core inventory includes 104 groups. UI smoke includes synthetic unhealthy
row bindings and enabled-health settings screenshots in Chinese and English.
Local evidence: `artifacts/verification/health/`; no real services are launched.

Historical health-feature verification on 2026-10-12 (before CLI/hosting): Release build completed with zero warnings and
errors; the full suite passed 88 groups with zero failures and zero skips,
including isolated native signature fixtures. Chinese and compact English UI
screenshots were inspected. Both UI log performance gates and loaded/dispatcher
fatal-cleanup checks passed with no surviving child processes. Current verification:
[v0.8.0 review](release-v0.8.0.md).

Endpoint availability does not prove ownership by the current process. A probe
may hit another service already listening on the same port. This is not a
distributed monitoring system or a real-time guarantee. Monitoring endpoints
are contacted repeatedly and may leave entries in the destination's access log.
Use a lightweight, side-effect-free health endpoint; HTTP GET is not necessarily
side-effect-free for arbitrary URLs. No monitoring data is sent to LaunchDesk
maintainers. Physical Windows 10 and real-service acceptance remain deferred.
