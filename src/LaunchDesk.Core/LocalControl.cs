using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace LaunchDesk.Core;

public sealed record ControlRequest(string Command, string? Target = null, bool All = false, int Version = 1);
public sealed record ControlItem(Guid Id, string Name, RunStatus Status, int? Pid, bool Active, string Detail, HealthSnapshot Health);
public sealed record ControlResponse(bool Success, string Message, ControlItem[] Items, int Version = 1);

public static class ControlCommands
{
    public static void Validate(ControlRequest request)
    {
        if (request.Version != 1 || request.Command is not ("list" or "status" or "start" or "stop" or "restart")
            || request.Target?.Length > 100 || request.Target?.Contains('\0') == true
            || (request.All && request.Target is not null) || (request.Command == "list" && (request.Target is not null || request.All))
            || (request.Command is "start" or "stop" or "restart" && !request.All && string.IsNullOrWhiteSpace(request.Target)))
            throw new InvalidDataException("Invalid command. Use list, status, start|stop|restart <name-or-id>, or start|stop|restart --all.");
    }
    public static LaunchItem Resolve(Configuration config, string target)
    {
        if (Guid.TryParse(target, out var id) && config.Items.FirstOrDefault(x => x.Id == id) is { } byId) return byId;
        var matches = config.Items.Where(x => x.Name.Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "Launch item not found." : "Ambiguous item name; use its ID from list.");
        return matches[0];
    }
    public static async Task<ControlResponse> ExecuteAsync(ControlRequest request, ProcessManager manager, CancellationToken token = default)
    {
        Validate(request);
        var item = request.Target is null ? null : Resolve(manager.Config, request.Target);
        if (request.Command is "stop" or "restart")
        { if (request.All) await manager.StopAllAsync(); else await manager.StopAsync(item!.Id); }
        if (request.Command is "start" or "restart")
        { if (request.All) await manager.StartAllAsync(cancellationToken: token); else await manager.StartAsync(item!.Id, token); }
        var items = (item is null ? manager.Config.StartupItems() : [item]).Select(x =>
        {
            var state = manager.Snapshot(x.Id);
            return new ControlItem(x.Id, x.Name, state.Status, state.Pid, state.Active, state.Detail, state.Health);
        }).ToArray();
        return new(true, "OK", items);
    }
}

public static class LocalControl
{
    public static string PipeName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return "LaunchDesk-Control-" + identity.User!.Value + "-" + process.SessionId;
        }
    }
    private const int RequestLimit = 16 * 1024;
    private const int ResponseLimit = 4 * 1024 * 1024;
    internal static async Task<T> ReadAsync<T>(Stream stream, int limit, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 || length > limit) throw new InvalidDataException("Invalid local-control frame size.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, ConfigurationStore.JsonOptions) ?? throw new InvalidDataException("Empty control frame.");
    }
    internal static async Task WriteAsync<T>(Stream stream, T value, int limit, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ConfigurationStore.JsonOptions);
        if (bytes.Length > limit) throw new InvalidDataException("Local-control response exceeds its size limit.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<ControlResponse> SendAsync(ControlRequest request, TimeSpan timeout, string? pipeName = null)
    {
        ControlCommands.Validate(request);
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(2));
            try { await pipe.ConnectAsync(connect.Token); }
            catch (OperationCanceledException) { throw new IOException("LaunchDesk is not available in this login session. Open the desktop application first."); }
        }
        await WriteAsync(pipe, request, RequestLimit, cancellation.Token);
        var response = await ReadAsync<ControlResponse>(pipe, ResponseLimit, cancellation.Token);
        if (response.Version != 1) throw new InvalidDataException("Unsupported control protocol version.");
        return response;
    }
    public sealed class Server : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly Func<ControlRequest, CancellationToken, Task<ControlResponse>> handler;
        private readonly string name;
        private readonly Task loop;
        public Server(Func<ControlRequest, CancellationToken, Task<ControlResponse>> handler, string? pipeName = null)
        { this.handler = handler; name = pipeName ?? PipeName; loop = ListenAsync(); }
        private async Task ListenAsync()
        {
            var clients = new List<Task>();
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    clients.RemoveAll(x => x.IsCompleted);
                    if (clients.Count >= 8) { await Task.WhenAny(clients).WaitAsync(cancellation.Token); continue; }
                    var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    try { await pipe.WaitForConnectionAsync(cancellation.Token); }
                    catch { pipe.Dispose(); throw; }
                    clients.Add(ServeAsync(pipe));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { await Task.WhenAll(clients); }
        }
        private async Task ServeAsync(NamedPipeServerStream pipe)
        {
            using (pipe)
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            {
                try
                {
                    read.CancelAfter(TimeSpan.FromSeconds(5));
                    var request = await ReadAsync<ControlRequest>(pipe, RequestLimit, read.Token);
                    ControlResponse response;
                    try { ControlCommands.Validate(request); response = await handler(request, cancellation.Token); }
                    catch (Exception error) { response = new(false, error.Message, []); }
                    using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    write.CancelAfter(TimeSpan.FromSeconds(5));
                    await WriteAsync(pipe, response, ResponseLimit, write.Token);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
            }
        }
        public void Stop() => cancellation.Cancel();
        public async ValueTask DisposeAsync() { Stop(); try { await loop; } finally { cancellation.Dispose(); } }
    }
}
