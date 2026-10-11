using System.Collections.Concurrent;
using System.Text;

namespace LaunchDesk.Core;

public sealed record RuntimeSnapshot(RunStatus Status, int? Pid, int? ExitCode, DateTimeOffset? Started, string Detail, bool Active);
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
        public CancellationTokenSource? Cancellation;
        public Task Completion = Task.CompletedTask;
        public Task Ready = Task.CompletedTask;
        public NativeProcess? Current;
    }
    private readonly ConcurrentDictionary<Guid, Runtime> runtimes = new();
    private readonly string root;
    private readonly Func<int, Task> signal;
    public Configuration Config { get; }
    private volatile bool shuttingDown;
    private int bulkGeneration;
    public ProcessManager(Configuration config, string root, Func<int, Task>? signal = null)
    { Config = config; this.root = root; this.signal = signal ?? (_ => Task.CompletedTask); }
    private Runtime Get(Guid id) => runtimes.GetOrAdd(id, key => new(new LogStore(root, key, Config.Preferences)));
    public LogStore Logs(Guid id) => Get(id).Logs;
    public RuntimeSnapshot Snapshot(Guid id)
    {
        var r = Get(id);
        lock (r.Sync) return new(r.Status, r.Pid, r.ExitCode, r.Started, r.Detail, !r.Completion.IsCompleted);
    }
    private static void Update(Runtime r, RunStatus status, string detail = "")
    { lock (r.Sync) { r.Status = status; r.Detail = detail; } }
    public async Task StartAsync(Guid id)
    {
        if (shuttingDown) throw new InvalidOperationException("程序正在退出。");
        ConfigurationStore.Validate(Config);
        var item = Config.Items.Single(x => x.Id == id);
        foreach (var dependency in item.Dependencies)
        {
            await StartAsync(dependency).ConfigureAwait(false);
            if (!Snapshot(dependency).Active || Snapshot(dependency).Status != RunStatus.Running)
                throw new InvalidOperationException("依赖未保持运行：" + Config.Items.Single(x => x.Id == dependency).Name);
        }
        var r = Get(id);
        Task readyTask;
        await r.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (shuttingDown) throw new InvalidOperationException("程序正在退出。");
            if (!r.Completion.IsCompleted) readyTask = r.Ready;
            else
            {
                r.Cancellation?.Dispose(); r.Cancellation = new();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                readyTask = r.Ready = ready.Task;
                Update(r, RunStatus.Starting);
                // Store the lifecycle task before publishing completion state to concurrent UI operations.
                r.Completion = Task.Run(() => RunSessionAsync(ConfigurationStore.Clone(item), r, ready, r.Cancellation.Token));
            }
        }
        finally { r.Gate.Release(); }
        await readyTask.ConfigureAwait(false);
    }
    private async Task RunSessionAsync(LaunchItem item, Runtime r, TaskCompletionSource ready, CancellationToken token)
    {
        var failureCount = 0;
        try
        {
            if (item.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(item.DelaySeconds), token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                NativeProcess? native = null;
                Task? pumps = null;
                var start = DateTimeOffset.Now;
                var exitCode = -1;
                Exception? failure = null;
                try
                {
                    native = new NativeProcess(item);
                    lock (r.Sync) { r.Current = native; r.Pid = native.Id; r.Started = start; r.ExitCode = null; }
                    var active = native;
                    var exit = native.WaitForExitAsync();
                    pumps = Task.WhenAll(PumpAsync(native.Output, item.Encoding, r.Logs, "stdout"),
                        PumpAsync(native.Error, item.Encoding, r.Logs, "stderr"));
                    _ = pumps.ContinueWith(_ => { try { active.Kill(); } catch { } },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    r.Logs.Write("system", $"启动 PID {native.Id}\n");
                    Update(r, RunStatus.Running); ready.TrySetResult();
                    var cancelled = Task.Delay(Timeout.Infinite, token);
                    if (await Task.WhenAny(exit, cancelled).ConfigureAwait(false) == cancelled)
                    {
                        Update(r, RunStatus.Stopping);
                        if (item.StopInput.Length > 0)
                        {
                            try { await SendInputAsync(item.Id, item.StopInput).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                            catch (Exception error) { r.Logs.Write("system", "退出输入发送失败：" + error.Message + "\n"); }
                        }
                        else
                        {
                            try { await signal(native.Id).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                            catch (Exception error) { r.Logs.Write("system", "退出信号发送失败：" + error.Message + "\n"); }
                        }
                        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(item.StopTimeoutSeconds))).ConfigureAwait(false) != exit)
                        { r.Logs.Write("system", "优雅停止超时，终止受管进程树。\n"); native.Kill(); }
                    }
                    await exit.ConfigureAwait(false);
                    native.CloseInput();
                    await pumps.ConfigureAwait(false);
                    exitCode = native.ExitCode;
                    r.Logs.Write("system", $"退出码 {exitCode}\n");
                }
                catch (Exception error)
                {
                    failure = error;
                    try { native?.Kill(); } catch { }
                    if (pumps is not null) { try { await pumps.ConfigureAwait(false); } catch { } }
                    try { r.Logs.Write("system", "运行失败：" + error.Message + "\n"); } catch { }
                    ready.TrySetException(error);
                }
                finally
                {
                    await r.InputGate.WaitAsync().ConfigureAwait(false);
                    try { lock (r.Sync) { r.Current = null; r.Pid = null; r.ExitCode = exitCode; } native?.Dispose(); }
                    finally { r.InputGate.Release(); }
                }
                if (token.IsCancellationRequested) { Update(r, RunStatus.Stopped, failure?.Message ?? ""); return; }
                Update(r, failure is not null || exitCode != 0 ? RunStatus.Failed : RunStatus.Exited, failure?.Message ?? "");
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
        finally { if (token.IsCancellationRequested) Update(r, RunStatus.Stopped); ready.TrySetCanceled(); }
    }
    private static async Task PumpAsync(Stream stream, string encoding, LogStore logs, string channel)
    {
        var decoder = Encoding.GetEncoding(encoding).GetDecoder();
        var buffer = new byte[8192];
        var characters = new char[Encoding.GetEncoding(encoding).GetMaxCharCount(buffer.Length)];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            var decoded = decoder.GetChars(buffer, 0, count, characters, 0, count == 0);
            logs.Write(channel, new string(characters, 0, decoded), count > 0 ? buffer[..count] : null);
            if (count == 0) return;
        }
    }
    public async Task SendInputAsync(Guid id, string text)
    {
        var r = Get(id); await r.InputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            NativeProcess native;
            lock (r.Sync) native = r.Current ?? throw new InvalidOperationException("启动项未运行。");
            var encoding = Config.Items.Single(x => x.Id == id).Encoding;
            var bytes = Encoding.GetEncoding(encoding).GetBytes(text + "\r\n");
            await native.Input.WriteAsync(bytes).ConfigureAwait(false); await native.Input.FlushAsync().ConfigureAwait(false);
            r.Logs.Write("stdin", text + "\n");
        }
        finally { r.InputGate.Release(); }
    }
    public async Task StopAsync(Guid id)
    {
        var r = Get(id); Task completion;
        await r.Gate.WaitAsync().ConfigureAwait(false);
        try { r.Cancellation?.Cancel(); completion = r.Completion; }
        finally { r.Gate.Release(); }
        await completion.ConfigureAwait(false);
    }
    public async Task StartAllAsync(bool autoOnly = false)
    {
        List<Exception> errors = [];
        var generation = Volatile.Read(ref bulkGeneration);
        foreach (var item in Config.Items.ToArray().Where(x => !autoOnly || x.AutoStart))
        {
            if (generation != Volatile.Read(ref bulkGeneration) || shuttingDown) break;
            try { await StartAsync(item.Id).ConfigureAwait(false); }
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
        foreach (var item in Config.Items.ToArray()) Visit(item);
        foreach (var id in ordered.AsEnumerable().Reverse()) await StopAsync(id).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        shuttingDown = true; await StopAllAsync().ConfigureAwait(false);
        foreach (var runtime in runtimes.Values) { runtime.Logs.Dispose(); runtime.Cancellation?.Dispose(); }
    }
}
