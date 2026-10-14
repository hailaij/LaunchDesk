namespace LaunchDesk.Core;

public readonly record struct ProjectedLogLine(string Text, int PrefixLength, string Channel);

// Formatting is cached by immutable record identity; disk capture is never affected
// by display retention, filtering, pause, or collection notifications.
public sealed class LogProjection
{
    private sealed record Cached(string Name, bool Merged, ProjectedLogLine[] Lines);
    private readonly Dictionary<LogRecord, Cached> cache = new(ReferenceEqualityComparer.Instance);
    private sealed class Source
    {
        public long Cursor = -1;
        public readonly Queue<(long Cursor, LogRecord Record)> Records = new();
    }
    private readonly Dictionary<LogStore, Source> buffers = new();
    public int FormattedRecords { get; private set; }
    public int ReadRecords { get; private set; }
    public int CachedRecords => cache.Count;

    public string[] Update((Guid Id, string Name, LogStore Store)[] sources, bool merged, string? channel, string search)
        => UpdateLines(sources, merged, channel, search).Select(x => x.Text).ToArray();

    public ProjectedLogLine[] UpdateLines((Guid Id, string Name, LogStore Store)[] sources, bool merged, string? channel, string search)
    {
        FormattedRecords = 0; ReadRecords = 0;
        foreach (var removed in buffers.Keys.Where(x => !sources.Any(s => s.Store == x)).ToArray()) buffers.Remove(removed);
        foreach (var source in sources)
        {
            if (!buffers.TryGetValue(source.Store, out var buffer)) buffers[source.Store] = buffer = new();
            var delta = source.Store.ReadSince(buffer.Cursor);
            if (delta.Reset) buffer.Records.Clear();
            while (buffer.Records.TryPeek(out var oldest) && oldest.Cursor < delta.FirstCursor) buffer.Records.Dequeue();
            var next = delta.Cursor - delta.Records.Length + 1;
            foreach (var record in delta.Records) buffer.Records.Enqueue((next++, record));
            buffer.Cursor = delta.Cursor; ReadRecords += delta.Records.Length;
        }
        var snapshot = LogStore.MergeSnapshots(sources.Select(x => (x.Id, x.Name, buffers[x.Store].Records.Select(r => r.Record).ToArray())).ToArray());
        var retained = snapshot.Select(x => x.Record).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var record in cache.Keys.Where(x => !retained.Contains(x)).ToArray()) cache.Remove(record);
        var output = new List<ProjectedLogLine>();
        foreach (var entry in snapshot)
        {
            var record = entry.Record;
            if (!cache.TryGetValue(record, out var formatted) || formatted.Name != entry.Name || formatted.Merged != merged)
            {
                var content = LogStore.DisplayText(record.Text);
                content = merged ? content.TrimEnd('\n') : content.EndsWith('\n') ? content[..^1] : content;
                var prefix = $"[{record.Time:HH:mm:ss.fff}] " + (merged ? $"[{entry.Name}] " : "") + $"[{record.Channel}] ";
                var lines = record.Text.Length == 0 ? [] : content.Split('\n').Select((line, index) =>
                {
                    var label = merged || index == 0 ? prefix : "";
                    return new ProjectedLogLine(label + line.TrimEnd('\r'), label.Length, record.Channel);
                }).ToArray();
                cache[record] = formatted = new(entry.Name, merged, lines); FormattedRecords++;
            }
            if (channel is null || record.Channel == channel) output.AddRange(formatted.Lines);
        }
        var characters = output.Sum(x => x.Text.Length + 1);
        var start = 0;
        while (start < output.Count && characters - output[start].Text.Length - 1 >= 500_000)
            characters -= output[start++].Text.Length + 1;
        if (start > 0) output.RemoveRange(0, start);
        if (characters > 500_000 && output.Count > 0)
        {
            const string marker = "[... truncated ...] ";
            var first = output[0];
            var budget = 500_000 - (characters - first.Text.Length - 1);
            if (budget < first.PrefixLength + marker.Length + 1) output.RemoveAt(0);
            else
            {
                var cut = first.Text.Length - (budget - first.PrefixLength - marker.Length - 1);
                // Only trim content; never split the source prefix or a UTF-16 surrogate pair.
                if (cut < first.Text.Length && char.IsLowSurrogate(first.Text[cut]) && char.IsHighSurrogate(first.Text[cut - 1])) cut++;
                output[0] = new(first.Text[..first.PrefixLength] + marker + first.Text[cut..], first.PrefixLength, first.Channel);
            }
        }
        if (!string.IsNullOrWhiteSpace(search)) return output.Where(x => x.Text.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (output.Count > 0) output.Add(new("", 0, ""));
        return output.ToArray();
    }
}
