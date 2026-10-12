using System.Diagnostics;
using System.Text.Json;
using LaunchDesk.Core;

internal sealed class LifecycleProbe : IManagedProcess
{
    private readonly LaunchItem item;
    private readonly NativeProcess native;
    private readonly int id;
    private readonly bool trackReads = Environment.GetEnvironmentVariable("LAUNCHDESK_TRACK_PIPE_READS") == "1";
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    public LifecycleProbe(LaunchItem item)
    {
        this.item = item; native = new(item); id = native.Id;
    }
    private ReadProbe? output, error;
    private Task? exit;
    public Stream Output => trackReads ? output ??= new(native.Output) : native.Output;
    public Stream Error => trackReads ? error ??= new(native.Error) : native.Error;
    public Stream Input => native.Input;
    public int Id => id;
    public int ExitCode => native.ExitCode;
    public bool ForcedTermination => native.ForcedTermination;
    public Task WaitForExitAsync() => exit ??= native.WaitForExitAsync();
    public void Kill() => native.Kill();
    public void CloseInput() => native.CloseInput();
    public Task SignalAsync(Func<int, Task> fallback) => native.SignalAsync(fallback);
    public void Dispose() => native.Dispose();

    public string Capture(ProcessManager manager)
    {
        object root;
        try
        {
            using var process = Process.GetProcessById(Id);
            root = new { process.Id, process.ProcessName, process.HasExited,
                CpuMs = process.HasExited ? 0 : process.TotalProcessorTime.TotalMilliseconds,
                Threads = process.HasExited ? 0 : process.Threads.Count };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { root = new { Id, Unavailable = exception.Message }; }
        var evidence = new { At = DateTimeOffset.UtcNow, ElapsedMs = elapsed.ElapsedMilliseconds, TrackReads = trackReads,
            Command = LaunchCommand.From(item), Snapshot = manager.Snapshot(item.Id), Root = root,
            JobExitTask = exit?.Status.ToString(), StdoutBytes = output?.Bytes, StdoutEof = output?.Eof,
            StderrBytes = error?.Bytes, StderrEof = error?.Eof,
            Logs = manager.Logs(item.Id).Snapshot().Select(x => new { x.Channel, x.Text }) };
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        var directory = Environment.GetEnvironmentVariable("LAUNCHDESK_TEST_DIAGNOSTICS_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "lifecycle-" + item.Id + ".json"), json);
        }
        return json;
    }

    private sealed class ReadProbe(Stream stream) : Stream
    {
        public long Bytes;
        public volatile bool Eof;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref Bytes, count); if (count == 0) Eof = true; return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
