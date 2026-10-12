using System.Net.Sockets;

namespace LaunchDesk.Core;

public sealed record HealthSnapshot(HealthStatus Status, DateTimeOffset? CheckedAt = null, string Detail = "",
    int Failures = 0, int Successes = 0, bool RestartSuppressed = false);

public sealed class HealthRestartLimiter
{
    private readonly Queue<DateTimeOffset> attempts = new();
    private DateTimeOffset? last;
    public bool Reserve(HealthOptions options, DateTimeOffset now, out string reason)
    {
        while (attempts.TryPeek(out var time) && now - time >= TimeSpan.FromMinutes(10)) attempts.Dequeue();
        reason = "";
        if (last is { } previous && now - previous < TimeSpan.FromSeconds(options.RestartCooldownSeconds))
            reason = "健康重启冷却中";
        else if (attempts.Count >= options.MaxRestartsPerTenMinutes) reason = "健康重启达到 10 分钟频率上限";
        if (reason.Length != 0) return false;
        attempts.Enqueue(now); last = now; return true;
    }
}

internal static class HealthMonitor
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<bool> RunAsync(HealthOptions options, ReadinessOptions endpoint,
        HealthRestartLimiter limiter, Action<HealthSnapshot> publish, CancellationToken token)
    {
        var status = HealthStatus.Waiting;
        var failures = 0; var successes = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string detail = "";
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    attempt.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                    try
                    {
                        if (endpoint.Kind == ReadinessKind.Tcp)
                        {
                            using var tcp = new TcpClient();
                            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, attempt.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            using var response = await Client.GetAsync(endpoint.Url, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode) detail = "HTTP " + (int)response.StatusCode;
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { detail = "健康检查超时"; }
                    catch (SocketException error) { detail = error.Message; }
                    catch (HttpRequestException error) { detail = error.Message; }
                }
                token.ThrowIfCancellationRequested();
                if (detail.Length == 0)
                {
                    failures = 0; successes = Math.Min(options.RecoveryThreshold, successes + 1);
                    if (successes >= options.RecoveryThreshold) status = HealthStatus.Healthy;
                }
                else
                {
                    successes = 0; failures = Math.Min(options.FailureThreshold, failures + 1);
                    if (failures >= options.FailureThreshold) status = HealthStatus.Unhealthy;
                }
                var restart = false; var suppressed = false;
                if (status == HealthStatus.Unhealthy && failures >= options.FailureThreshold && options.AutoRestart)
                {
                    restart = limiter.Reserve(options, DateTimeOffset.UtcNow, out var reason);
                    suppressed = !restart;
                    if (suppressed) detail += "; " + reason;
                }
                publish(new(status, DateTimeOffset.Now, detail, failures, successes, suppressed));
                if (restart) return true;
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
    }
}
