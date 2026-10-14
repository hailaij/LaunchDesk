using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

internal static class LogBenchmarks
{
    public static async Task Run(string report)
    {
        var started = DateTimeOffset.UtcNow;
        var workspace = new TestWorkspace("log-benchmark"); var root = workspace.Root;
        var metrics = new List<object>();
        void Measure(string name, int count, Action action)
        {
            action();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var time = Stopwatch.StartNew();
            for (var i = 0; i < count; i++) action();
            metrics.Add(new { name, count, elapsedMs = time.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
        }
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("中文🙂 benchmark output\n", 200)));
        foreach (var encoding in new[] { "utf-8", "auto" })
        {
            var decoder = new OutputDecoder(encoding);
            Measure("decode-" + encoding, 10000, () => { _ = decoder.Decode(bytes); });
        }
        foreach (var flushEach in new[] { true, false })
        {
            using var writer = new StreamWriter(Path.Combine(root, "flush-" + flushEach), false, new UTF8Encoding(false));
            Measure("writer-flush-" + flushEach, 10000, () => { writer.WriteLine("stdout benchmark 中文"); if (flushEach) writer.Flush(); });
            writer.Flush();
        }
        using (var log = new LogStore(root, Guid.NewGuid(), new() { LogLimitMiB = 200 }))
        {
            Measure("persist-record", 10000, () => log.Write("stdout", "完整日志🙂\n"));
            var cursor = log.ReadSince(-1).Cursor;
            Measure("read-tail-1", 10000, () => { _ = log.ReadSince(cursor - 1); });
            Measure("read-idle", 10000, () => { _ = log.ReadSince(cursor); });
            log.Export(Path.Combine(root, "export.jsonl"));
            if (File.ReadLines(Path.Combine(root, "export.jsonl")).Count() != 10001)
                throw new InvalidDataException("Benchmark lost persisted records");
        }
        var path = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { root, started, metrics,
            integrity = "10001 records exported without loss", scope = "Single-thread warmed microbenchmarks; no fsync/power-loss guarantee" }, new JsonSerializerOptions { WriteIndented = true }));
        await TestEvidence.WriteAsync(path, started, "log-benchmark");
        Console.WriteLine("Benchmark data: " + workspace.Finish(path, true));
    }
}
