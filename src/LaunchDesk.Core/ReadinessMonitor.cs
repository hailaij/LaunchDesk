using System.Net.Sockets;

namespace LaunchDesk.Core;

internal sealed class ReadinessMonitor(ReadinessOptions options)
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly object gate = new();
    private readonly Dictionary<string, string> tails = [];
    private readonly TaskCompletionSource matched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Observe(string channel, string text)
    {
        if (options.Kind != ReadinessKind.Log || matched.Task.IsCompleted) return;
        lock (gate)
        {
            var value = tails.GetValueOrDefault(channel, "") + text;
            if (LogStore.DisplayText(value).Contains(options.Keyword, StringComparison.Ordinal)) matched.TrySetResult();
            tails[channel] = value.Length > options.Keyword.Length + 1024 ? value[^(options.Keyword.Length + 1024)..] : value;
        }
    }
    public async Task WaitAsync(Task exit, CancellationToken cancellationToken)
    {
        if (options.Kind == ReadinessKind.None) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var token = timeout.Token;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (exit.IsCompleted) throw new InvalidOperationException("进程在服务就绪前退出。");
                var probe = options.Kind == ReadinessKind.Log ? matched.Task.WaitAsync(token) : ProbeAsync(token);
                if (await Task.WhenAny(exit, probe).ConfigureAwait(false) == exit)
                {
                    timeout.Cancel();
                    try { await probe.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    throw new InvalidOperationException("进程在服务就绪前退出。");
                }
                await probe.ConfigureAwait(false);
                if (options.Kind == ReadinessKind.Log) return;
                // Network probes complete only after a successful response.
                return;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"服务就绪检测超时（{options.TimeoutSeconds} 秒）。"); }
    }
    private async Task ProbeAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                if (options.Kind == ReadinessKind.Tcp)
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(options.Host, options.Port, attempt.Token).ConfigureAwait(false);
                    return;
                }
                using var response = await Client.GetAsync(options.Url, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch (SocketException) { }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(300, token).ConfigureAwait(false);
        }
    }
}
