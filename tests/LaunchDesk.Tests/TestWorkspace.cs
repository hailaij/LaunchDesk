using System.Diagnostics;
using System.Text.Json;

internal sealed class TestWorkspace
{
    public const string MarkerName = ".launchdesk-test-workspace.json";
    private readonly string key = Guid.NewGuid().ToString("N");
    private readonly string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    public string Root { get; }
    public TestWorkspace(string category = "tests")
    {
        if (category is not ("tests" or "log-benchmark" or "scale")) throw new ArgumentException("Unknown test workspace category");
        Root = Path.Combine(temp, "LaunchDesk-" + category + "-" + key);
        Directory.CreateDirectory(Root);
        using var owner = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(Root, MarkerName), JsonSerializer.Serialize(new { key, category,
            ownerPid = owner.Id, ownerStartedUtcTicks = owner.StartTime.ToUniversalTime().Ticks, createdAt = DateTimeOffset.UtcNow }));
    }
    public string Finish(string report, bool completeSuccess, bool keep = false)
    {
        if (!completeSuccess) return "retained-incomplete";
        if (keep) return "retained-requested";
        if (Path.GetFullPath(report).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "retained-report-inside";
        if (!File.Exists(report) || !File.Exists(report + ".metadata.json")) return "retained-missing-evidence";
        try
        {
            foreach (var file in new[] { report, report + ".metadata.json" })
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked evidence refused");
                for (var parent = new FileInfo(file).Directory; parent is not null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked evidence directory refused");
            }
            if (!string.Equals(Path.GetDirectoryName(Root), temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(Root).EndsWith("-" + key, StringComparison.Ordinal)) throw new IOException("Workspace scope mismatch");
            var pending = new Stack<DirectoryInfo>(); pending.Push(new(Root));
            while (pending.TryPop(out var directory))
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked workspace refused");
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked workspace entry refused");
                    if (entry is DirectoryInfo child) pending.Push(child);
                }
            }
            var marker = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(Root, MarkerName)));
            if (marker.GetProperty("key").GetString() != key || marker.GetProperty("ownerPid").GetInt32() != Environment.ProcessId)
                throw new IOException("Workspace ownership mismatch");
            Directory.Delete(Root, recursive: true);
            return "removed";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return "retained-cleanup-error: " + error.Message; }
    }
}
