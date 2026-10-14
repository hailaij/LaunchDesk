using System.Collections.Concurrent;
using System.Text;

namespace LaunchDesk.Core;

public sealed record RuntimeSnapshot(RunStatus Status, int? Pid, int? ExitCode, DateTimeOffset? Started, string Detail, bool Active)
{
    public HealthSnapshot Health { get; init; } = new(HealthStatus.Disabled);
}
public sealed class ProcessManager : IAsyncDisposable
{
    private sealed class Runtime(LogStore logs)
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly SemaphoreSlim InputGate = new(1, 1);
        public readonly LogStore Logs = logs;
        public RunStatus Status;
        public int? Pid, ExitCode;
        public DateTimeOffset? Started;
        public string Detail = "";
        public HealthSnapshot Health = new(HealthStatus.Disabled);
        public CancellationTokenSource? Cancellation;
        public Task Completion = Task.CompletedTask;
        public Task Ready = Task.CompletedTask;
        public IManagedProcess? Current;
        public volatile bool Retired;
    }
    private readonly ConcurrentDictionary<Guid, Runtime> runtimes = new();
    private readonly object registryGate = new();
    private readonly string root;
    private readonly Func<int, Task> signal;
    private readonly Func<LaunchItem, CancellationToken, Task<IManagedProcess>> launch;
    public Configuration Config { get; }
    public SessionJournal? Session { get; set; }
    private volatile bool shuttingDown;
    private Task? disposal;
    private int bulkGeneration;
    private readonly CancellationTokenSource maintenanceCancellation = new();
    private readonly Task maintenance;
    private readonly ConcurrentQueue<string> maintenanceWarnings = new();
    public string[] MaintenanceWarnings => maintenanceWarnings.ToArray();
    public ProcessManager(Configuration config, string root, Func<int, Task>? signal = null,
        Func<LaunchItem, CancellationToken, Task<IManagedProcess>>? launch = null)
    {
        Config = config; this.root = root; this.signal = signal ?? (_ => Task.CompletedTask);
        this.launch = launch ?? ((item, _) => item.RequireElevation
            ? throw new InvalidOperationException("当前宿主不支持按项提权。") : Task.FromResult<IManagedProcess>(new NativeProcess(item)));
        maintenance = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            try { do { MaintainLogs(); } while (await timer.WaitForNextTickAsync(maintenanceCancellation.Token).ConfigureAwait(false)); }
            catch (OperationCanceledException) when (maintenanceCancellation.IsCancellationRequested) { }
        });
    }
    public void MaintainLogs()
    {
        void Warn(string message) { maintenanceWarnings.Enqueue(message); while (maintenanceWarnings.Count > 20) maintenanceWarnings.TryDequeue(out _); }
        try
        {
            var directory = Path.Combine(root, "logs");
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var path in Directory.GetDirectories(directory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(path), "N", out var id)) continue;
                if (runtimes.TryGetValue(id, out var runtime)) runtime.Logs.Maintain();
                else LogStore.CleanupDirectory(path, Config.Preferences, warn: Warn);
            }
        }
        catch (IOException error) { Warn(error.Message); }
        catch (UnauthorizedAccessException error) { Warn(error.Message); }
    }
    private Runtime Get(Guid id)
    {
        lock (registryGate)
        {
            if (runtimes.TryGetValue(id, out var runtime)) return runtime;
            if (shuttingDown) throw new ObjectDisposedException(nameof(ProcessManager));
            if (!Config.Items.Any(x => x.Id == id)) throw new KeyNotFoundException("启动项不存在。");
            runtime = new(new LogStore(root, id, Config.Preferences)); runtimes[id] = runtime; return runtime;
        }
    }
    public async Task RemoveRuntimeAsync(Guid id)
    {
        Runtime? runtime;
        lock (registryGate)
        {
            if (Config.Items.Any(x => x.Id == id)) throw new InvalidOperationException("请先删除启动项配置。");
            if (!runtimes.TryGetValue(id, out runtime)) return;
        }
        await runtime.Gate.WaitAsync().ConfigureAwait(false);
        try { runtime.Retired = true; runtime.Cancellation?.Cancel(); }
        finally { runtime.Gate.Release(); }
        await runtime.Completion.ConfigureAwait(false);
        await runtime.InputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try { runtime.Logs.Dispose(); }
            finally
            {
                await runtime.Gate.WaitAsync().ConfigureAwait(false);
                try { runtime.Cancellation?.Dispose(); runtime.Cancellation = null; }
                finally { runtime.Gate.Release(); }
                lock (registryGate) runtimes.TryRemove(new KeyValuePair<Guid, Runtime>(id, runtime));
            }
        }
        finally { runtime.InputGate.Release(); }
        // Gates have no OS wait handles; leave them valid for already queued callers until the runtime is collected.
    }
    public LogStore Logs(Guid id) => Get(id).Logs;
    public RuntimeSnapshot Snapshot(Guid id)
    {
        var r = Get(id);
        lock (r.Sync) return new(r.Status, r.Pid, r.ExitCode, r.Started, r.Detail, !r.Completion.IsCompleted) { Health = r.Health };
    }
    private static void Update(Runtime r, RunStatus status, string detail = "")
    { lock (r.Sync) { r.Status = status; r.Detail = detail; } }
    public async Task StartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (shuttingDown) throw new InvalidOperationException("程序正在退出。");
        ConfigurationStore.Validate(Config);
        var item = Config.Items.Single(x => x.Id == id);
        var r = Get(id);
        if (!Snapshot(id).Active)
        {
            try { LaunchPreflight.Check(item); }
            catch (Exception error)
            {
                Update(r, RunStatus.Failed, "启动前检查：" + error.Message);
                try { r.Logs.Write("system", "启动前检查失败：" + error.Message + "\n"); } catch { }
                throw;
            }
        }
        foreach (var dependency in item.Dependencies)
        {
            await StartAsync(dependency, cancellationToken).ConfigureAwait(false);
            if (!Snapshot(dependency).Active || Snapshot(dependency).Status != RunStatus.Running)
                throw new InvalidOperationException("依赖未保持运行：" + Config.Items.Single(x => x.Id == dependency).Name);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Task readyTask;
        await r.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shuttingDown) throw new InvalidOperationException("程序正在退出。");
            if (r.Retired) throw new ObjectDisposedException("启动项已删除。");
            if (!r.Completion.IsCompleted) readyTask = r.Ready;
            else
            {
                Session?.MarkActive(id);
                r.Cancellation?.Dispose(); r.Cancellation = new();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                readyTask = r.Ready = ready.Task;
                Update(r, RunStatus.Starting);
                // Store the lifecycle task before publishing completion state to concurrent UI operations.
                r.Completion = Task.Run(() => RunSessionAsync(ConfigurationStore.Clone(item), r, ready, r.Cancellation.Token));
            }
        }
        finally { r.Gate.Release(); }
        await readyTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private async Task RunSessionAsync(LaunchItem item, Runtime r, TaskCompletionSource ready, CancellationToken token)
    {
        var failureCount = 0;
        var healthLimiter = new HealthRestartLimiter();
        try
        {
            if (item.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(item.DelaySeconds), token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                IManagedProcess? native = null;
                Task? pumps = null;
                Task? exit = null;
                Task<bool>? healthTask = null;
                using var healthCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var healthRestart = false;
                lock (r.Sync) r.Health = new(item.Health.Enabled ? HealthStatus.Waiting : HealthStatus.Disabled);
                var start = DateTimeOffset.Now;
                var exitCode = -1;
                Exception? failure = null;
                try
                {
                    native = await launch(item, token).ConfigureAwait(false);
                    lock (r.Sync) { r.Current = native; r.Pid = native.Id; r.Started = start; r.ExitCode = null; }
                    var active = native;
                    exit = native.WaitForExitAsync();
                    var readiness = new ReadinessMonitor(item.Readiness);
                    pumps = Task.WhenAll(PumpAsync(native.Output, item.Encoding, r.Logs, "stdout", readiness.Observe),
                        PumpAsync(native.Error, item.Encoding, r.Logs, "stderr", readiness.Observe));
                    _ = pumps.ContinueWith(_ => { try { active.Kill(); } catch { } },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    r.Logs.Write("system", $"启动 PID {native.Id}\n");
                    if (item.Readiness.Kind != ReadinessKind.None)
                    {
                        Update(r, RunStatus.Starting, "等待服务就绪");
                        await readiness.WaitAsync(exit, token).ConfigureAwait(false);
                        r.Logs.Write("system", "服务就绪检测通过。\n");
                    }
                    Update(r, RunStatus.Running, item.Readiness.Kind == ReadinessKind.None ? "" : "服务已就绪"); ready.TrySetResult();
                    if (item.Health.Enabled)
                        healthTask = HealthMonitor.RunAsync(item.Health, item.Health.Endpoint(item.Readiness), healthLimiter, state =>
                        {
                            HealthSnapshot previous;
                            lock (r.Sync) { previous = r.Health; r.Health = state; }
                            if (previous.Status != state.Status || previous.RestartSuppressed != state.RestartSuppressed)
                                r.Logs.Write("system", "健康状态：" + state.Status + (state.Detail.Length == 0 ? "" : "；" + state.Detail) + "\n");
                        }, healthCancellation.Token);
                    var cancelled = Task.Delay(Timeout.Infinite, token);
                    var winner = await Task.WhenAny(exit, cancelled, (Task?)healthTask ?? Task.Delay(Timeout.Infinite, healthCancellation.Token)).ConfigureAwait(false);
                    if (healthTask is not null && winner == healthTask) healthRestart = await healthTask.ConfigureAwait(false);
                    healthCancellation.Cancel();
                    if (healthTask is not null) await healthTask.ConfigureAwait(false);
                    if (healthRestart) r.Logs.Write("system", "健康异常，准备重启当前启动项。\n");
                    if (token.IsCancellationRequested || healthRestart)
                    {
                        Update(r, RunStatus.Stopping);
                        if (item.StopInput.Length == 0 || item.SendStopSignalBeforeInput)
                        {
                            try { await native.SignalAsync(signal).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                            catch (Exception error) { r.Logs.Write("system", "退出信号发送失败：" + error.Message + "\n"); }
                        }
                        if (item.StopInput.Length > 0 && !exit.IsCompleted)
                        {
                            if (item.StopInputDelaySeconds > 0)
                            {
                                r.Logs.Write("system", $"等待 {item.StopInputDelaySeconds} 秒后发送退出输入。\n");
                                await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(item.StopInputDelaySeconds))).ConfigureAwait(false);
                            }
                            if (!exit.IsCompleted)
                            {
                                try { await SendInputAsync(item.Id, item.StopInput).ConfigureAwait(false); }
                                catch (Exception error) { r.Logs.Write("system", "退出输入发送失败：" + error.Message + "\n"); }
                            }
                        }
                        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(item.StopTimeoutSeconds))).ConfigureAwait(false) != exit)
                        { r.Logs.Write("system", "优雅停止超时，终止受管进程树。\n"); native.Kill(); }
                    }
                    await exit.ConfigureAwait(false);
                    native.CloseInput();
                    await pumps.ConfigureAwait(false);
                    exitCode = native.ExitCode;
                    if (native.ForcedTermination) r.Logs.Write("system", "管理器已强制终止进程树（0xE04C4401）。\n");
                    r.Logs.Write("system", $"退出码 {ExitCodeDisplay.Format(exitCode)}\n");
                }
                catch (Exception error)
                {
                    failure = error;
                    try { native?.Kill(); } catch { }
                    if (exit is not null) { try { await exit.ConfigureAwait(false); } catch { } }
                    if (pumps is not null) { try { await pumps.ConfigureAwait(false); } catch { } }
                    try { r.Logs.Write("system", "运行失败：" + error.Message + "\n"); } catch { }
                    ready.TrySetException(error);
                }
                finally
                {
                    healthCancellation.Cancel();
                    if (healthTask is not null) { try { await healthTask.ConfigureAwait(false); } catch (Exception error) { failure ??= error; } }
                    await r.InputGate.WaitAsync().ConfigureAwait(false);
                    try { lock (r.Sync) { r.Current = null; r.Pid = null; r.ExitCode = exitCode; } native?.Dispose(); }
                    finally { r.InputGate.Release(); }
                }
                if (token.IsCancellationRequested) { Update(r, RunStatus.Stopped, failure?.Message ?? ""); return; }
                Update(r, failure is not null || exitCode != 0 ? RunStatus.Failed : RunStatus.Exited, failure?.Message ?? "");
                if (healthRestart && failure is null)
                {
                    Update(r, RunStatus.Restarting, "健康异常重启");
                    continue;
                }
                // Never auto-restart a process whose logs could not be persisted.
                if (!item.AutoRestart || failure is IOException or UnauthorizedAccessException || !ready.Task.IsCompletedSuccessfully) return;
                if (DateTimeOffset.Now - start > TimeSpan.FromMinutes(1)) failureCount = 0;
                failureCount++;
                if (failureCount >= 5) { Update(r, RunStatus.Failed, "连续退出 5 次，已暂停自动重启。"); return; }
                Update(r, RunStatus.Restarting, $"{Math.Min(30, 1 << failureCount)} 秒后重启");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << failureCount)), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { Update(r, RunStatus.Stopped); }
        catch (Exception error) { Update(r, RunStatus.Failed, error.Message); ready.TrySetException(error); }
        finally
        {
            try { Session?.MarkInactive(item.Id); }
            catch (Exception error) { Update(r, RunStatus.Failed, "Session journal: " + error.Message); }
            lock (r.Sync) r.Health = new(item.Health.Enabled ? HealthStatus.Waiting : HealthStatus.Disabled);
            if (token.IsCancellationRequested) Update(r, RunStatus.Stopped); ready.TrySetCanceled();
        }
    }
    private static async Task PumpAsync(Stream stream, string encoding, LogStore logs, string channel, Action<string, string> observe)
    {
        var decoder = new OutputDecoder(encoding);
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            var text = decoder.Decode(buffer.AsSpan(0, count), count == 0);
            logs.Write(channel, text, count > 0 ? buffer[..count] : null);
            observe(channel, text);
            if (count == 0) return;
        }
    }
    public async Task SendInputAsync(Guid id, string text)
    {
        var r = Get(id);
        if (!await r.InputGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false)) throw new IOException("输入队列等待超时。");
        try
        {
            if (r.Retired) throw new ObjectDisposedException("启动项已删除。");
            IManagedProcess native;
            lock (r.Sync) native = r.Current ?? throw new InvalidOperationException("启动项未运行。");
            var encoding = Config.Items.Single(x => x.Id == id).Encoding;
            var bytes = OutputDecoder.Resolve(encoding).GetBytes(text + "\r\n");
            async Task WriteAndFlushAsync()
            {
                await native.Input.WriteAsync(bytes).ConfigureAwait(false);
                await native.Input.FlushAsync().ConfigureAwait(false);
            }
            var write = WriteAndFlushAsync();
            if (await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) != write)
            {
                // Anonymous pipes use synchronous OS handles: cancellation alone cannot abort a blocked write.
                r.Cancellation?.Cancel(); native.Kill();
                try { await write.ConfigureAwait(false); } catch (IOException) { }
                throw new IOException("输入发送超时，已停止受管进程树以释放阻塞管道。");
            }
            await write.ConfigureAwait(false);
            r.Logs.Write("stdin", text + "\n");
        }
        finally { r.InputGate.Release(); }
    }
    public async Task StopAsync(Guid id)
    {
        if (!runtimes.TryGetValue(id, out var r)) return;
        await StopRuntimeAsync(r).ConfigureAwait(false);
    }
    private static async Task StopRuntimeAsync(Runtime r)
    {
        Task completion;
        await r.Gate.WaitAsync().ConfigureAwait(false);
        try { r.Cancellation?.Cancel(); completion = r.Completion; }
        finally { r.Gate.Release(); }
        await completion.ConfigureAwait(false);
    }
    public async Task StartFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (!Config.Folders.Any(x => x.Id == folderId)) throw new InvalidOperationException("文件夹不存在。");
        var errors = new List<Exception>();
        foreach (var item in Config.StartupItems().Where(x => x.FolderId == folderId).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await StartAsync(item.Id, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { errors.Add(new Exception(item.Name + ": " + error.Message, error)); }
        }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    public async Task StopFolderAsync(Guid folderId)
    {
        var members = Config.StartupItems().Where(x => x.FolderId == folderId).ToDictionary(x => x.Id);
        var ordered = new List<Guid>(); var seen = new HashSet<Guid>();
        void Visit(LaunchItem item)
        {
            if (!seen.Add(item.Id)) return;
            foreach (var dependency in item.Dependencies)
                if (members.TryGetValue(dependency, out var value)) Visit(value);
            ordered.Add(item.Id);
        }
        foreach (var item in members.Values) Visit(item);
        foreach (var id in ordered.AsEnumerable().Reverse()) await StopAsync(id).ConfigureAwait(false);
    }
    public async Task StartAllAsync(bool autoOnly = false, CancellationToken cancellationToken = default)
    {
        List<Exception> errors = [];
        var generation = Volatile.Read(ref bulkGeneration);
        foreach (var item in Config.StartupItems().ToArray().Where(x => !autoOnly || x.AutoStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref bulkGeneration) || shuttingDown) break;
            try { await StartAsync(item.Id, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (generation != Volatile.Read(ref bulkGeneration) || shuttingDown) { break; }
            catch (Exception error) { errors.Add(new Exception(item.Name + ": " + error.Message, error)); }
        }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    public async Task StopAllAsync()
    {
        Interlocked.Increment(ref bulkGeneration);
        var ordered = new List<Guid>(); var seen = new HashSet<Guid>();
        void Visit(LaunchItem item)
        {
            if (!seen.Add(item.Id)) return;
            foreach (var dependency in item.Dependencies)
                if (Config.Items.FirstOrDefault(x => x.Id == dependency) is { } value) Visit(value);
            ordered.Add(item.Id);
        }
        foreach (var item in Config.StartupItems().ToArray()) Visit(item);
        foreach (var id in ordered.AsEnumerable().Reverse()) await StopAsync(id).ConfigureAwait(false);
        foreach (var runtime in runtimes.Values) await StopRuntimeAsync(runtime).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    { lock (registryGate) return new(disposal ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        shuttingDown = true;
        var errors = new List<Exception>();
        try
        {
            maintenanceCancellation.Cancel();
            await maintenance.ConfigureAwait(false);
            await StopAllAsync().ConfigureAwait(false);
        }
        finally
        {
            maintenanceCancellation.Dispose();
            foreach (var runtime in runtimes.Values)
            {
                try { await StopRuntimeAsync(runtime).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
                finally
                {
                    try { runtime.Logs.Dispose(); }
                    catch (Exception error) { errors.Add(error); }
                    await runtime.Gate.WaitAsync().ConfigureAwait(false);
                    try { runtime.Cancellation?.Dispose(); runtime.Cancellation = null; runtime.Retired = true; }
                    finally { runtime.Gate.Release(); }
                }
            }
            runtimes.Clear();
        }
        if (errors.Count > 0) throw new AggregateException(errors);
    }
}
