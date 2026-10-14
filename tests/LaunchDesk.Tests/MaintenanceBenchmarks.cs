using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

internal static class MaintenanceBenchmarks
{
    public static async Task Run(string report)
    {
        var started = DateTimeOffset.UtcNow;
        var workspace = new TestWorkspace("log-benchmark"); var root = workspace.Root;
        var rounds = new List<object>();
        var text = JsonSerializer.Serialize(new LogRecord(started, "stdout", string.Concat(Enumerable.Repeat("中文🙂 output\n", 100))));
        for (var round = 0; round < 7; round++)
        {
            foreach (var position in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var path = Path.Combine(root, "counter-" + position);
                using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
                long bytes = 0; const int count = 2000;
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                for (var i = 0; i < count; i++)
                {
                    writer.WriteLine(text); writer.Flush();
                    bytes = position ? writer.BaseStream.Position : bytes + Encoding.UTF8.GetByteCount(text) + Encoding.UTF8.GetByteCount(writer.NewLine);
                }
                timer.Stop();
                if (bytes != writer.BaseStream.Length) throw new InvalidDataException("Counter differs from actual disk length");
                rounds.Add(new { round, mode = position ? "position" : "byte-count", operations = count, elapsedMs = timer.Elapsed.TotalMilliseconds,
                    allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, bytes });
            }
            using var first = File.OpenRead(Path.Combine(root, "counter-False")); using var second = File.OpenRead(Path.Combine(root, "counter-True"));
            if (!SHA256.HashData(first).SequenceEqual(SHA256.HashData(second))) throw new InvalidDataException("Counter comparison changed serialized output");
        }
        var pathName = Path.GetFullPath(report); Directory.CreateDirectory(Path.GetDirectoryName(pathName)!);
        await File.WriteAllTextAsync(pathName, JsonSerializer.Serialize(new { started, root, rounds,
            scope = "Seven alternating paired flush-per-record runs; equal bytes verified. Not fsync or endurance evidence." }, new JsonSerializerOptions { WriteIndented = true }));
        await TestEvidence.WriteAsync(pathName, started, "maintenance-benchmark");
        var metadata = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(pathName + ".metadata.json"));
        var paths = metadata.GetProperty("sourceHashes").EnumerateArray().Select(x => x.GetProperty("path").GetString()!).ToArray();
        var hashRounds = new List<object>();
        for (var round = 0; round < 7; round++)
        {
            string[]? expected = null;
            foreach (var stream in new[] { round % 2 == 0, round % 2 != 0 })
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                var hashes = paths.Select(path =>
                {
                    if (!stream) return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                    using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
                }).ToArray();
                timer.Stop();
                if (expected is not null && !hashes.SequenceEqual(expected)) throw new InvalidDataException("Source hash comparison differs");
                expected = hashes;
                hashRounds.Add(new { round, mode = stream ? "stream" : "read-all-bytes", files = paths.Length,
                    elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
            }
        }
        var hashReport = pathName + ".source-hashing.json";
        await File.WriteAllTextAsync(hashReport, JsonSerializer.Serialize(new { paths.Length, hashRounds, sourceHashesPreserved = true,
            scope = "Same complete source inventory, no caching or evidence reduction." }, new JsonSerializerOptions { WriteIndented = true }));
        await TestEvidence.WriteAsync(hashReport, started, "source-hashing-benchmark");
        Console.WriteLine("Maintenance data: " + workspace.Finish(pathName, true));
    }
}
