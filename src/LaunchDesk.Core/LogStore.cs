using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public sealed record LogRecord(DateTimeOffset Time, string Channel, string Text, byte[]? RawBytes = null);
public sealed partial class LogStore : IDisposable
{
    private readonly object gate = new();
    private readonly object diskGate = new();
    private readonly Queue<string> cleanupWarnings = new();
    private readonly Queue<LogRecord> memory = new();
    private readonly Preferences preferences;
    private StreamWriter? writer;
    private string? currentFile;
    private long bytes;
    private int characters;
    private int revision;
    private static long sequence;
    private long changeStamp;
    private bool recentLoaded;
    private bool disposed;
    public string DirectoryPath { get; }
    public int Revision { get { lock (gate) return revision; } }
    public long ChangeStamp => Interlocked.Read(ref changeStamp);
    public string[] CleanupWarnings { get { lock (gate) return cleanupWarnings.ToArray(); } }
    public LogStore(string root, Guid id, Preferences preferences)
    { DirectoryPath = Path.Combine(root, "logs", id.ToString("N")); this.preferences = preferences; }
    public void Write(string channel, string text, byte[]? rawBytes = null)
    {
        if (text.Length == 0 && rawBytes is not { Length: > 0 }) return;
        lock (diskGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var record = new LogRecord(DateTimeOffset.Now, channel, text, rawBytes);
            var serialized = JsonSerializer.Serialize(record);
            if (writer is null || bytes > Math.Min(10 * 1024 * 1024L, preferences.LogLimitMiB * 1024 * 1024L / 2)
                || File.GetLastWriteTime(currentFile!).Date != DateTime.Today)
            {
                writer?.Dispose(); writer = null;
                Directory.CreateDirectory(DirectoryPath); Cleanup();
                currentFile = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".jsonl");
                writer = new StreamWriter(new FileStream(currentFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                bytes = 0;
            }
            // Disk is the source of truth; a failed write must propagate to the supervisor.
            writer.WriteLine(serialized); writer.Flush(); bytes += Encoding.UTF8.GetByteCount(serialized) + 2;
            lock (gate)
            {
                memory.Enqueue(record); characters += text.Length; revision++;
                Interlocked.Exchange(ref changeStamp, Interlocked.Increment(ref sequence));
                while (memory.Count > 3000 || characters > 500_000)
                    characters -= memory.Dequeue().Text.Length;
            }
        }
    }
    private void Cleanup()
    {
        var reserve = Math.Min(10 * 1024 * 1024L, preferences.LogLimitMiB * 1024 * 1024L / 2);
        CleanupDirectory(DirectoryPath, preferences, null, reserve, Warn);
    }
    private void Warn(string warning)
    {
        lock (gate) { cleanupWarnings.Enqueue(warning); while (cleanupWarnings.Count > 20) cleanupWarnings.Dequeue(); }
    }
    public void Maintain()
    { lock (diskGate) { if (!disposed) CleanupDirectory(DirectoryPath, preferences, currentFile, 0, Warn); } }
    internal static void CleanupDirectory(string directory, Preferences preferences, string? activeFile = null, long reserve = 0, Action<string>? warn = null)
    {
        try
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            var files = new DirectoryInfo(directory).GetFiles("*.jsonl").OrderBy(x => x.LastWriteTimeUtc).ToArray();
            long total = files.Sum(x => x.Length);
            var budget = Math.Max(1024 * 1024L, preferences.LogLimitMiB * 1024 * 1024L - reserve);
            foreach (var file in files)
            {
                if (file.FullName.Equals(activeFile, StringComparison.OrdinalIgnoreCase) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-preferences.LogDays) && total <= budget) continue;
                try { var length = file.Length; file.Delete(); total -= length; }
                catch (IOException error) { warn?.Invoke(file.FullName + ": " + error.Message); }
                catch (UnauthorizedAccessException error) { warn?.Invoke(file.FullName + ": " + error.Message); }
            }
        }
        catch (IOException error) { warn?.Invoke(error.Message); }
        catch (UnauthorizedAccessException error) { warn?.Invoke(error.Message); }
    }
    public LogRecord[] Snapshot() { lock (gate) return memory.ToArray(); }
    public void ClearPanel() { lock (gate) { memory.Clear(); characters = 0; recentLoaded = true; revision++; Interlocked.Exchange(ref changeStamp, Interlocked.Increment(ref sequence)); } }
    public void DeleteHistory()
    {
        lock (diskGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            writer?.Dispose(); writer = null;
            if (Directory.Exists(DirectoryPath))
                foreach (var path in Directory.GetFiles(DirectoryPath, "*.jsonl")) File.Delete(path);
            ClearPanel();
        }
    }
    public void Export(string destination) => Export(destination, out _);
    public void Export(string destination, out int skippedRecords)
    {
        if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("导出位置不能位于原始日志目录内。");
        var skipped = 0;
        WriteExport(destination, output =>
        {
            foreach (var record in ReadRecords(() => skipped++))
            {
                if (Path.GetExtension(destination).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)) { output.WriteLine(JsonSerializer.Serialize(record)); continue; }
                output.Write($"[{record.Time:yyyy-MM-dd HH:mm:ss.fff}] [{record.Channel}] "); output.Write(record.Text);
                if (!record.Text.EndsWith('\n')) output.WriteLine();
            }
        });
        skippedRecords = skipped;
    }
    public void LoadRecent()
    {
        long initialStamp;
        lock (gate)
        {
            if (recentLoaded) return;
            if (memory.Count > 0 || !Directory.Exists(DirectoryPath)) { recentLoaded = true; return; }
            initialStamp = changeStamp;
        }
        var loaded = new Queue<LogRecord>(); var count = 0;
        foreach (var record in ReadRecords(recentOnly: true))
        {
            loaded.Enqueue(record); count += record.Text.Length;
            while (loaded.Count > 3000 || count > 500_000) count -= loaded.Dequeue().Text.Length;
        }
        lock (gate)
        {
            // A concurrent writer owns the live view; do not duplicate captured records.
            if (disposed || memory.Count > 0 || changeStamp != initialStamp) return;
            recentLoaded = true;
            foreach (var record in loaded) memory.Enqueue(record);
            characters = count;
            revision++;
            Interlocked.Exchange(ref changeStamp, Interlocked.Increment(ref sequence));
        }
    }
    public static (Guid Id, string Name, LogRecord Record)[] SnapshotMerged((Guid Id, string Name, LogStore Store)[] sources)
    {
        var queue = new PriorityQueue<(Guid Id, string Name, LogRecord Record), (DateTimeOffset Time, long Sequence)>();
        long sequence = 0;
        foreach (var source in sources)
            foreach (var record in source.Store.Snapshot())
            {
                queue.Enqueue((source.Id, source.Name, record), (record.Time, sequence++));
                if (queue.Count > 3000) queue.Dequeue();
            }
        var records = new List<(Guid Id, string Name, LogRecord Record)>();
        while (queue.TryDequeue(out var record, out _)) records.Add(record);
        var characters = 0; var start = records.Count;
        while (start > 0 && characters + records[start - 1].Record.Text.Length <= 500_000)
            characters += records[--start].Record.Text.Length;
        return records.Skip(start).ToArray();
    }
    private IEnumerable<LogRecord> RetainedRecords() => ReadRecords();
    private IEnumerable<LogRecord> ReadRecords(Action? skipped = null, bool recentOnly = false)
    {
        foreach (var line in RetainedLines(recentOnly))
        {
            LogRecord? record = null;
            try { record = JsonSerializer.Deserialize<LogRecord>(line); }
            catch (JsonException) { }
            if (record?.Text is null || record.Channel is null) { skipped?.Invoke(); continue; }
            yield return record;
        }
    }
    private List<StreamReader> CaptureReaders(bool recentOnly)
    {
        lock (diskGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            writer?.Flush();
            var files = Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.jsonl").Order().ToArray() : [];
            var readers = new List<StreamReader>();
            try
            {
                foreach (var file in recentOnly ? files.TakeLast(2) : files)
                {
                    FileStream stream;
                    try { stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
                    catch (FileNotFoundException) { continue; }
                    readers.Add(new StreamReader(new SnapshotStream(stream, stream.Length)));
                }
                return readers;
            }
            catch { foreach (var reader in readers) reader.Dispose(); throw; }
        }
    }
    private IEnumerable<string> RetainedLines(bool recentOnly = false)
    {
        var readers = CaptureReaders(recentOnly);
        try { foreach (var reader in readers) while (reader.ReadLine() is { } line) yield return line; }
        finally { foreach (var reader in readers) reader.Dispose(); }
    }
    private sealed class SnapshotStream(FileStream file, long remaining) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        { var read = file.Read(buffer, offset, (int)Math.Min(count, remaining)); remaining -= read; return read; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
    }
    public static void ExportMerged((Guid Id, string Name, LogStore Store)[] sources, string destination) => ExportMerged(sources, destination, out _);
    public static void ExportMerged((Guid Id, string Name, LogStore Store)[] sources, string destination, out int skippedRecords)
    {
        if (sources.Any(x => Path.GetFullPath(destination).StartsWith(Path.GetFullPath(x.Store.DirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("导出位置不能位于原始日志目录内。");
        // Each source is a bounded file snapshot; enumeration never holds its write lock.
        var ordered = sources.OrderBy(x => x.Store.DirectoryPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var skipped = 0;
        var readers = ordered.Select(x => x.Store.ReadRecords(() => skipped++).GetEnumerator()).ToArray();
        try
        {
            var queue = new PriorityQueue<int, (DateTimeOffset Time, int Source)>();
            for (var i = 0; i < readers.Length; i++) if (readers[i].MoveNext()) queue.Enqueue(i, (readers[i].Current.Time, i));
            WriteExport(destination, output =>
            {
                while (queue.TryDequeue(out var index, out _))
                {
                    var record = readers[index].Current; var source = ordered[index];
                    if (Path.GetExtension(destination).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
                        output.WriteLine(JsonSerializer.Serialize(new { SourceId = source.Id, SourceName = source.Name, record.Time, record.Channel, record.Text, record.RawBytes }));
                    else
                    {
                        output.Write($"[{record.Time:yyyy-MM-dd HH:mm:ss.fff}] [{source.Name}] [{record.Channel}] "); output.Write(record.Text);
                        if (!record.Text.EndsWith('\n')) output.WriteLine();
                    }
                    if (readers[index].MoveNext()) queue.Enqueue(index, (readers[index].Current.Time, index));
                }
            });
        }
        finally { foreach (var reader in readers.Reverse()) reader.Dispose(); }
        skippedRecords = skipped;
    }
    private static void WriteExport(string destination, Action<StreamWriter> write)
    {
        destination = Path.GetFullPath(destination);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false))) write(output);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string DisplayText(string value) => Ansi().Replace(value, "");
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))")]
    private static partial Regex Ansi();
    public void Dispose()
    {
        lock (diskGate)
        {
            disposed = true;
            try { writer?.Dispose(); }
            finally { writer = null; lock (gate) { memory.Clear(); characters = 0; } }
        }
    }
}
