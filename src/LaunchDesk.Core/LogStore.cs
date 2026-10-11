using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public sealed record LogRecord(DateTimeOffset Time, string Channel, string Text, byte[]? RawBytes = null);
public sealed partial class LogStore : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<LogRecord> memory = new();
    private readonly Preferences preferences;
    private StreamWriter? writer;
    private string? currentFile;
    private long bytes;
    private int characters;
    private int revision;
    private bool recentLoaded;
    public string DirectoryPath { get; }
    public int Revision { get { lock (gate) return revision; } }
    public LogStore(string root, Guid id, Preferences preferences)
    { DirectoryPath = Path.Combine(root, "logs", id.ToString("N")); this.preferences = preferences; }
    public void Write(string channel, string text, byte[]? rawBytes = null)
    {
        if (text.Length == 0 && rawBytes is not { Length: > 0 }) return;
        lock (gate)
        {
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
            memory.Enqueue(record); characters += text.Length; revision++;
            while (memory.Count > 3000 || characters > 500_000)
                characters -= memory.Dequeue().Text.Length;
        }
    }
    private void Cleanup()
    {
        var files = new DirectoryInfo(DirectoryPath).GetFiles("*.jsonl").OrderByDescending(x => x.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-preferences.LogDays)
                || total > preferences.LogLimitMiB * 1024 * 1024L - 10 * 1024 * 1024L) file.Delete();
        }
    }
    public LogRecord[] Snapshot() { lock (gate) return memory.ToArray(); }
    public void ClearPanel() { lock (gate) { memory.Clear(); characters = 0; recentLoaded = true; revision++; } }
    public void DeleteHistory()
    {
        lock (gate)
        {
            writer?.Dispose(); writer = null;
            if (Directory.Exists(DirectoryPath))
                foreach (var path in Directory.GetFiles(DirectoryPath, "*.jsonl")) File.Delete(path);
            ClearPanel();
        }
    }
    public void Export(string destination)
    {
        lock (gate)
        {
            writer?.Flush();
            var files = Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.jsonl").Order().ToArray() : [];
            if (Path.GetFullPath(destination).StartsWith(Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("导出位置不能位于原始日志目录内。");
            using var output = new StreamWriter(destination, false, new UTF8Encoding(false));
            foreach (var file in files)
            {
                using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                while (reader.ReadLine() is { } line)
                {
                    if (Path.GetExtension(destination).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)) { output.WriteLine(line); continue; }
                    var record = JsonSerializer.Deserialize<LogRecord>(line) ?? throw new InvalidDataException("日志记录无效。");
                    output.Write($"[{record.Time:yyyy-MM-dd HH:mm:ss.fff}] [{record.Channel}] "); output.Write(record.Text);
                    if (!record.Text.EndsWith('\n')) output.WriteLine();
                }
            }
        }
    }
    public void LoadRecent()
    {
        lock (gate)
        {
            if (recentLoaded) return;
            recentLoaded = true;
            if (memory.Count > 0 || !Directory.Exists(DirectoryPath)) return;
            foreach (var file in Directory.GetFiles(DirectoryPath, "*.jsonl").Order().TakeLast(2))
            {
                using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                while (reader.ReadLine() is { } line)
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<LogRecord>(line) is not { } record) continue;
                        memory.Enqueue(record); characters += record.Text.Length;
                        while (memory.Count > 3000 || characters > 500_000) characters -= memory.Dequeue().Text.Length;
                    }
                    catch (JsonException) { /* An interrupted final record cannot be recovered from JSON alone. */ }
                }
            }
            revision++;
        }
    }
    public static string DisplayText(string value) => Ansi().Replace(value, "");
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))")]
    private static partial Regex Ansi();
    public void Dispose() { lock (gate) { writer?.Dispose(); writer = null; } }
}
