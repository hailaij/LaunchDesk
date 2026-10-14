using System.Text;
using LaunchDesk.Core;

internal static class ProjectionBoundaryChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (var merged in new[] { false, true })
        foreach (var length in new[] { 499980, 499999, 500000 })
            await test($"Log projection boundary: length={length}, merged={merged}", () =>
            {
                var id = Guid.NewGuid(); using var log = new LogStore(root, id, new());
                var text = string.Concat(Enumerable.Repeat("中文🙂", length / 4)) + new string('x', length % 4);
                log.Write("stdout", text, Encoding.UTF8.GetBytes(text));
                var record = log.Snapshot().Single();
                var prefix = $"[{record.Time:HH:mm:ss.fff}] " + (merged ? "[来源中文🙂] " : "") + "[stdout] ";
                var before = Path.Combine(root, id + "-before.jsonl"); var after = Path.Combine(root, id + "-after.jsonl");
                log.Export(before);
                var projection = new LogProjection();
                var lines = projection.Update([(id, "来源中文🙂", log)], merged, null, "");
                var typed = projection.UpdateLines([(id, "来源中文🙂", log)], merged, null, "");
                Check(typed.Select(x => x.Text).SequenceEqual(lines) && typed[0].Channel == "stdout"
                    && typed[0].PrefixLength == prefix.Length, "typed projection lost prefix/channel contract");
                Check(lines[0].StartsWith(prefix, StringComparison.Ordinal), $"prefix damaged: merged={merged}, length={length}");
                Check(lines[0].Contains("[... truncated ...] ", StringComparison.Ordinal), "truncation not marked");
                Check(lines.Sum(x => x.Length + 1) <= 500001, "display budget exceeded");
                Check(ValidUtf16(lines[0]), "truncation split emoji");
                Check(projection.Update([(id, "来源中文🙂", log)], merged, null, "").SequenceEqual(lines)
                    && projection.FormattedRecords == 0, "cached projection changed after trimming");
                log.Export(after);
                Check(File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)), "projection changed persisted export");
                Check(log.Snapshot().Single().Text == text && record.RawBytes!.SequenceEqual(Encoding.UTF8.GetBytes(text)), "original changed");
                return Task.CompletedTask;
            });
        foreach (var sizes in new[] { (249995, 250000), (40, 499960) })
            await test($"Log projection boundary: merged sizes={sizes.Item1}/{sizes.Item2}", () =>
            {
                var a = Guid.NewGuid(); var b = Guid.NewGuid();
                using var first = new LogStore(root, a, new()); using var second = new LogStore(root, b, new());
                first.Write("stdout", new string('中', sizes.Item1)); second.Write("stderr", new string('文', sizes.Item2));
                (Guid Id, string Name, LogStore Store)[] sources = [(a, "A中文🙂", first), (b, "B中文🙂", second)];
                var before = Path.Combine(root, a + "-merged-before.jsonl"); var after = Path.Combine(root, a + "-merged-after.jsonl");
                LogStore.ExportMerged(sources, before);
                var projection = new LogProjection(); var lines = projection.Update(sources, true, null, "");
                var aPrefix = $"[{first.Snapshot()[0].Time:HH:mm:ss.fff}] [A中文🙂] [stdout] ";
                var bPrefix = $"[{second.Snapshot()[0].Time:HH:mm:ss.fff}] [B中文🙂] [stderr] ";
                Check(lines.Where(x => x.Length > 0).All(x => x.StartsWith(aPrefix) || x.StartsWith(bPrefix)), "merged source prefix damaged");
                Check(lines.Any(x => x.StartsWith(bPrefix)) && lines.Sum(x => x.Length + 1) <= 500001, "newer merged output lost or over budget");
                if (sizes.Item1 > 40) Check(lines[0].Contains("[... truncated ...] "), "merged clipping not marked");
                else Check(lines.All(x => !x.StartsWith(aPrefix)), "insufficient prefix budget must omit whole old line");
                LogStore.ExportMerged(sources, after);
                Check(File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)), "merged export changed");
                return Task.CompletedTask;
            });
        await test("Log projection boundary: oversized record persists outside display", () =>
        {
            var oversizedId = Guid.NewGuid(); using var oversized = new LogStore(root, oversizedId, new());
            var large = new string('中', 500001); oversized.Write("stdout", large);
            var oversizedExport = Path.Combine(root, oversizedId + "-oversized.jsonl"); oversized.Export(oversizedExport);
            Check(new LogProjection().Update([(oversizedId, "oversized", oversized)], false, null, "").Length == 0, "existing whole-record retention changed");
            Check(System.Text.Json.JsonSerializer.Deserialize<LogRecord>(File.ReadLines(oversizedExport).Single())!.Text == large, "oversized disk record lost");
            return Task.CompletedTask;
        });
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }

    private static bool ValidUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) { if (++i == text.Length || !char.IsLowSurrogate(text[i])) return false; }
            else if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }
}
