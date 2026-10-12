using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

internal static class HealthChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test, Func<string, LaunchItem> worker)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!condition()) await Task.Delay(25, timeout.Token);
        }
        HealthOptions Options(ProbeServer server) => new() { Enabled = true, ReuseReadiness = false, Kind = ReadinessKind.Http,
            Url = server.Url, IntervalSeconds = 1, TimeoutSeconds = 1, FailureThreshold = 2, RecoveryThreshold = 2 };
        await test("Health: legacy defaults validation clone and roundtrip", () =>
        {
            var legacy = JsonSerializer.Deserialize<LaunchItem>("{}", ConfigurationStore.JsonOptions)!;
            Check(!legacy.Health.Enabled && !legacy.Health.AutoRestart && legacy.Health.IntervalSeconds == 30
                && legacy.Health.TimeoutSeconds == 5 && legacy.Health.FailureThreshold == 3 && legacy.Health.RecoveryThreshold == 2, "legacy changed");
            var item = worker("--worker sleep"); item.Health = new() { Enabled = true, ReuseReadiness = false, Kind = ReadinessKind.Http, Url = "http://127.0.0.1:12345/", AutoRestart = true };
            var store = new ConfigurationStore(Path.Combine(root, "health-config")); store.Save(new() { Items = [item] });
            Check(store.Load().Items[0].Health.AutoRestart && ConfigurationStore.Clone(item).Health.Url == item.Health.Url, "health lost on save/clone");
            foreach (var bad in new HealthOptions[] { new() { IntervalSeconds = 0 }, new() { TimeoutSeconds = 0 }, new() { FailureThreshold = 0 },
                new() { RecoveryThreshold = 0 }, new() { MaxRestartsPerTenMinutes = 0 }, new() { RestartCooldownSeconds = 0 },
                new() { Enabled = true }, new() { Enabled = true, ReuseReadiness = false, Kind = ReadinessKind.Http, Url = "https://user:pass@localhost/" },
                new() { Enabled = true, ReuseReadiness = false, Port = 0 }, new() { AutoRestart = true } })
            {
                item.Health = bad; var rejected = false;
                try { ConfigurationStore.Validate(new() { Items = [item] }); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid health accepted");
            }
            return Task.CompletedTask;
        });
        await test("Health: restart cooldown and rolling rate limit", () =>
        {
            var limiter = new HealthRestartLimiter(); var options = new HealthOptions(); var now = DateTimeOffset.UtcNow;
            Check(limiter.Reserve(options, now, out _), "initial restart");
            Check(!limiter.Reserve(options, now.AddSeconds(59), out var cooldown) && cooldown.Contains("冷却"), "cooldown missing");
            Check(limiter.Reserve(options, now.AddSeconds(60), out _) && limiter.Reserve(options, now.AddSeconds(120), out _), "allowed restarts denied");
            Check(!limiter.Reserve(options, now.AddSeconds(180), out var limit) && limit.Contains("上限"), "budget missing");
            Check(limiter.Reserve(options, now.AddMinutes(10), out _), "expired budget not recovered"); return Task.CompletedTask;
        });
        await test("Health: HTTP failure recovery thresholds and transition-only logs", async () =>
        {
            await using var server = new ProbeServer(); var item = worker("--worker sleep"); item.Health = Options(server);
            await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "health-http"));
            await manager.StartAsync(item.Id);
            await Until(() => manager.Snapshot(item.Id).Health.CheckedAt is not null);
            Check(manager.Snapshot(item.Id).Health.Status == HealthStatus.Waiting, "first failure crossed threshold");
            await Until(() => manager.Snapshot(item.Id).Health.Status == HealthStatus.Unhealthy);
            var pid = manager.Snapshot(item.Id).Pid; server.Status = 200;
            await Until(() => manager.Snapshot(item.Id).Health.Successes == 1);
            Check(manager.Snapshot(item.Id).Health.Status == HealthStatus.Unhealthy, "first recovery changed state");
            await Until(() => manager.Snapshot(item.Id).Health.Status == HealthStatus.Healthy);
            await Task.Delay(1200);
            Check(manager.Snapshot(item.Id).Pid == pid && manager.Snapshot(item.Id).Status == RunStatus.Running, "monitor restarted without permission");
            Check(manager.Logs(item.Id).Snapshot().Count(x => x.Text.StartsWith("健康状态：")) == 2, "per-check log spam");
            await manager.StopAsync(item.Id);
        });
        await test("Health: checks wait for readiness and TCP endpoint reuse", async () =>
        {
            await using var server = new ProbeServer(); var marker = Path.Combine(root, "health-ready.marker");
            var item = worker("--worker health-ready " + LaunchCommand.Quote(marker));
            item.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "READY" };
            item.Health = new() { Enabled = true, ReuseReadiness = false, Kind = ReadinessKind.Tcp, Host = "127.0.0.1", Port = server.Port, IntervalSeconds = 1 };
            await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "health-ready"));
            var start = manager.StartAsync(item.Id);
            await Until(() => manager.Snapshot(item.Id).Pid is not null);
            await Task.Delay(200);
            Check(manager.Snapshot(item.Id).Health.CheckedAt is null && server.Accepts == 0, "probe before readiness");
            File.WriteAllText(marker, "ready"); await start;
            await Until(() => manager.Snapshot(item.Id).Health.Status == HealthStatus.Healthy); await manager.StopAsync(item.Id);
            item.Readiness = new() { Kind = ReadinessKind.Tcp, Host = "127.0.0.1", Port = server.Port };
            item.Health.ReuseReadiness = true; item.Health.Port = 0;
            await manager.StartAsync(item.Id);
            await Until(() => manager.Snapshot(item.Id).Health.Status == HealthStatus.Healthy); await manager.StopAsync(item.Id);
        });
        await test("Health: unhealthy restart budget does not affect dependencies or undo stop", async () =>
        {
            await using var server = new ProbeServer(); var dependency = worker("--worker sleep"); var item = worker("--worker sleep");
            item.Dependencies = [dependency.Id]; item.Health = Options(server); item.Health.AutoRestart = true;
            item.Health.FailureThreshold = 1; item.Health.RestartCooldownSeconds = 1; item.Health.MaxRestartsPerTenMinutes = 1;
            await using var manager = new ProcessManager(new() { Items = [dependency, item] }, Path.Combine(root, "health-restart"));
            await manager.StartAsync(item.Id); var original = manager.Snapshot(item.Id).Started; var dependencyPid = manager.Snapshot(dependency.Id).Pid;
            await Until(() => manager.Snapshot(item.Id).Started != original && manager.Snapshot(item.Id).Pid is not null);
            await Until(() => manager.Snapshot(item.Id).Health.RestartSuppressed);
            Check(manager.Snapshot(item.Id).Health.Detail.Contains("上限"), "budget suppression missing");
            Check(manager.Snapshot(dependency.Id).Pid == dependencyPid, "dependency restarted");
            await manager.StopAsync(item.Id); var starts = manager.Logs(item.Id).Snapshot().Count(x => x.Text.StartsWith("启动 PID"));
            await Task.Delay(1200);
            Check(!manager.Snapshot(item.Id).Active && starts == 2 && manager.Logs(item.Id).Snapshot().Count(x => x.Text.StartsWith("启动 PID")) == starts, "stop was undone");
            Check(manager.Snapshot(dependency.Id).Active, "dependency stopped");
        });
        await test("Health: HTTP timeout stop cancellation and redirect rejection", async () =>
        {
            await using var server = new ProbeServer { Status = 302 }; var item = worker("--worker sleep");
            item.Health = Options(server); item.Health.FailureThreshold = 1;
            await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "health-timeout"));
            await manager.StartAsync(item.Id); await Until(() => manager.Snapshot(item.Id).Health.Status == HealthStatus.Unhealthy);
            Check(manager.Snapshot(item.Id).Health.Detail == "HTTP 302", "redirect accepted"); await manager.StopAsync(item.Id);
            server.Status = 0; await manager.StartAsync(item.Id);
            await Until(() => manager.Snapshot(item.Id).Health.Detail.Contains("超时"));
            await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(5));
            Check(!manager.Snapshot(item.Id).Active, "probe prevented stop");
        });
        await test("Health: disabled monitoring makes no requests and disposal cancels active checks", async () =>
        {
            await using var server = new ProbeServer(); var item = worker("--worker sleep"); item.Health.Url = server.Url;
            var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "health-dispose"));
            try
            {
                await manager.StartAsync(item.Id); await Task.Delay(200);
                Check(manager.Snapshot(item.Id).Health.Status == HealthStatus.Disabled && server.Accepts == 0, "disabled monitor probed");
                await manager.StopAsync(item.Id); item.Health = Options(server); item.Health.TimeoutSeconds = 30; server.Status = 0;
                await manager.StartAsync(item.Id); await Until(() => server.Accepts > 0);
                await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                var count = server.Accepts; await Task.Delay(1100); Check(server.Accepts == count, "monitor survived disposal");
            }
            finally { await manager.DisposeAsync(); }
        });
    }

    private sealed class ProbeServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task run;
        public volatile int Status = 503;
        private int accepts;
        public int Accepts => Volatile.Read(ref accepts);
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public string Url => $"http://127.0.0.1:{Port}/";
        public ProbeServer()
        {
            listener.Start();
            run = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                        Interlocked.Increment(ref accepts);
                        var stream = client.GetStream(); var buffer = new byte[4096];
                        if (await stream.ReadAsync(buffer, cancellation.Token) == 0) continue;
                        if (Status == 0) { await Task.Delay(Timeout.Infinite, cancellation.Token); continue; }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {Status} Test\r\nContent-Length: 0\r\nConnection: close\r\nLocation: http://127.0.0.1:1/\r\n\r\n"), cancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (IOException) when (cancellation.IsCancellationRequested) { }
                catch (SocketException) when (cancellation.IsCancellationRequested) { }
            });
        }
        public async ValueTask DisposeAsync() { cancellation.Cancel(); listener.Stop(); await run; cancellation.Dispose(); }
    }
}
