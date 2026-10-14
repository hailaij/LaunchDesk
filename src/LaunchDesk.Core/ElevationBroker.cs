using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Collections.Concurrent;

namespace LaunchDesk.Core;

public sealed record BrokerStart(string Nonce, LaunchItem Item);
public sealed record BrokerMessage(string Kind, int Pid = 0, int ExitCode = 0, bool Forced = false, string Error = "", string RequestId = "");

public sealed class ElevationBroker : IManagedProcess
{
    private const int FrameLimit = 4 * 1024 * 1024;
    private readonly NamedPipeServerStream control;
    private readonly NamedPipeServerStream output;
    private readonly NamedPipeServerStream error;
    private readonly NamedPipeServerStream input;
    private readonly Process helper;
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly Task completion;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> signalReplies = new();
    public Stream Output => output;
    public Stream Error => error;
    public Stream Input => input;
    public int Id { get; }
    public int ExitCode { get; private set; }
    public bool ForcedTermination { get; private set; }
    private bool disposed;
    private ElevationBroker(NamedPipeServerStream[] pipes, Process helper, int pid)
    { control = pipes[0]; output = pipes[1]; error = pipes[2]; input = pipes[3]; this.helper = helper; Id = pid; completion = ReadRepliesAsync(); }
    public static ProcessStartInfo StartInfo(string helperPath, string pipe, string nonce, int owner, long started)
    {
        var info = new ProcessStartInfo(helperPath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "--elevated", pipe, nonce, owner.ToString(), started.ToString() }) info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task<IManagedProcess> StartAsync(LaunchItem item, string helperPath, CancellationToken token,
        Func<ProcessStartInfo, Process?>? start = null)
    {
        if (!File.Exists(helperPath)) throw new FileNotFoundException("管理员助手未安装。", helperPath);
        var name = "LaunchDesk-Elevate-" + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        // CurrentUserOnly also checks elevation level; this explicit SID ACL permits only this user's elevated token.
        var pipes = Enumerable.Range(0, 4).Select(i => NamedPipeServerStreamAcl.Create(name + "-" + i, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security)).ToArray();
        Process? helper = null;
        var transferred = false;
        try
        {
            using var owner = Process.GetCurrentProcess();
            token.ThrowIfCancellationRequested();
            helper = (start ?? Process.Start)(StartInfo(helperPath, name, nonce, owner.Id, owner.StartTime.ToUniversalTime().Ticks))
                ?? throw new IOException("管理员助手未启动。");
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(TimeSpan.FromSeconds(30));
            foreach (var pipe in pipes)
            {
                await pipe.WaitForConnectionAsync(handshake.Token);
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var id) || id != helper.Id)
                    throw new UnauthorizedAccessException("管理员助手连接身份不匹配。");
            }
            await LocalControl.WriteAsync(pipes[0], new BrokerStart(nonce, ConfigurationStore.Clone(item)), FrameLimit, handshake.Token);
            var reply = await LocalControl.ReadAsync<BrokerMessage>(pipes[0], FrameLimit, handshake.Token);
            if (reply.Kind != "started" || reply.Pid <= 0) throw new IOException("管理员助手启动失败：" + reply.Error);
            var result = new ElevationBroker(pipes, helper, reply.Pid);
            transferred = true;
            return result;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("用户取消了管理员授权。", ex, token); }
        catch { throw; }
        finally
        {
            // Ownership transfers only after a valid started reply; closing pipes tells the helper to kill its job.
            if (!transferred) { foreach (var pipe in pipes) pipe.Dispose(); helper?.Dispose(); }
        }
    }
    private async Task ReadRepliesAsync()
    {
        try
        {
            while (true)
            {
                var reply = await LocalControl.ReadAsync<BrokerMessage>(control, FrameLimit, CancellationToken.None);
                if (reply.Kind == "signal")
                {
                    if (signalReplies.TryGetValue(reply.RequestId, out var pending))
                    { if (reply.Error.Length == 0) pending.TrySetResult(); else pending.TrySetException(new IOException(reply.Error)); }
                }
                else if (reply.Kind == "exited")
                { ExitCode = reply.ExitCode; ForcedTermination = reply.Forced; await helper.WaitForExitAsync(); FailSignals(new IOException("管理员助手进程已退出。")); return; }
                else throw new IOException("管理员助手协议无效。");
            }
        }
        catch (Exception ex) { FailSignals(ex); output.Dispose(); error.Dispose(); input.Dispose(); control.Dispose(); throw; }
    }
    private void FailSignals(Exception error) { foreach (var pending in signalReplies.Values) pending.TrySetException(error); }
    private async Task CommandAsync(string kind, string requestId = "")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await writes.WaitAsync(timeout.Token);
        try { await LocalControl.WriteAsync(control, new BrokerMessage(kind, RequestId: requestId), FrameLimit, timeout.Token); }
        finally { writes.Release(); }
    }
    public Task WaitForExitAsync() => completion;
    public async Task SignalAsync(Func<int, Task> fallback)
    {
        var id = Guid.NewGuid().ToString("N");
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signalReplies[id] = reply;
        try { await CommandAsync("signal", id); await reply.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { signalReplies.TryRemove(id, out _); }
    }
    public void Kill() { try { CommandAsync("kill").GetAwaiter().GetResult(); } catch { control.Dispose(); } }
    public void CloseInput() { try { input.Dispose(); } catch (IOException) { } }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        FailSignals(new ObjectDisposedException(nameof(ElevationBroker)));
        control.Dispose(); input.Dispose(); output.Dispose(); error.Dispose(); helper.Dispose();
    }
    public static async Task RunHelperAsync(string name, string nonce, int ownerId, long ownerStarted, Func<int, Task> signal, bool requireAdministrator = true)
    {
        if (requireAdministrator && !IsAdministrator()) throw new UnauthorizedAccessException("管理员助手未获得管理员权限。");
        using var owner = Process.GetProcessById(ownerId);
        if (owner.StartTime.ToUniversalTime().Ticks != ownerStarted) throw new UnauthorizedAccessException("管理器身份已失效。");
        using var lifetime = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            try { while (!owner.HasExited) await Task.Delay(100, lifetime.Token); }
            catch (OperationCanceledException) { }
            finally { lifetime.Cancel(); }
        });
        var pipes = Enumerable.Range(0, 4).Select(i => new NamedPipeClientStream(".", name + "-" + i, PipeDirection.InOut,
            PipeOptions.Asynchronous)).ToArray();
        NativeProcess? native = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); handshake.CancelAfter(TimeSpan.FromSeconds(30));
            foreach (var pipe in pipes)
            {
                await pipe.ConnectAsync(handshake.Token);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var id) || id != ownerId)
                    throw new UnauthorizedAccessException("管理器管道身份不匹配。");
            }
            var request = await LocalControl.ReadAsync<BrokerStart>(pipes[0], FrameLimit, handshake.Token);
            if (request.Nonce != nonce) throw new UnauthorizedAccessException("管理员助手授权凭据不匹配。");
            var validation = ConfigurationStore.Clone(request.Item);
            validation.Dependencies.Clear(); validation.FolderId = null;
            ConfigurationStore.Validate(new() { Items = [validation] });
            request.Item.RequireElevation = false;
            native = new NativeProcess(request.Item);
            await LocalControl.WriteAsync(pipes[0], new BrokerMessage("started", native.Id), FrameLimit, handshake.Token);
            var outPump = native.Output.CopyToAsync(pipes[1], lifetime.Token);
            var errPump = native.Error.CopyToAsync(pipes[2], lifetime.Token);
            async Task CopyInputAsync()
            { try { await pipes[3].CopyToAsync(native.Input, lifetime.Token); } finally { native.CloseInput(); } }
            var inputPump = CopyInputAsync();
            var exit = native.WaitForExitAsync();
            var ownerExit = Task.Delay(Timeout.Infinite, lifetime.Token);
            var pumpFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var pump in new[] { outPump, errPump })
                _ = pump.ContinueWith(t => { _ = t.Exception; pumpFailure.TrySetResult(); }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Task<BrokerMessage>? pending = null;
            while (!exit.IsCompleted)
            {
                var command = pending = LocalControl.ReadAsync<BrokerMessage>(pipes[0], FrameLimit, lifetime.Token);
                var winner = await Task.WhenAny(exit, command, ownerExit, pumpFailure.Task);
                if (winner == exit) break;
                if (winner != command) { native.Kill(); break; }
                var message = await command;
                pending = null;
                if (message.Kind == "kill") native.Kill();
                else if (message.Kind == "signal")
                {
                    string detail = ""; try { await signal(native.Id); } catch (Exception ex) { detail = ex.Message; }
                    await LocalControl.WriteAsync(pipes[0], new BrokerMessage("signal", Error: detail, RequestId: message.RequestId), FrameLimit, lifetime.Token);
                }
                else throw new InvalidDataException("管理员助手命令无效。");
            }
            await exit; native.CloseInput();
            await Task.WhenAll(outPump, errPump);
            using var final = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await LocalControl.WriteAsync(pipes[0], new BrokerMessage("exited", ExitCode: native.ExitCode, Forced: native.ForcedTermination), FrameLimit, final.Token);
            lifetime.Cancel();
            if (pending is not null) try { await pending; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            try { await inputPump; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        finally
        {
            native?.Dispose(); lifetime.Cancel(); foreach (var pipe in pipes) pipe.Dispose(); await watcher;
        }
    }
    public static bool IsAdministrator()
    { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint id);
}
