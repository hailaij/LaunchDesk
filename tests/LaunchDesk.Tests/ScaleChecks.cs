using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

internal static class ScaleChecks
{
    internal static string Line(int index)
    {
        var prefix = $"D{index:D8}:中文🙂:";
        return prefix + new string('x', 1023 - Encoding.UTF8.GetByteCount(prefix)) + "\n";
    }
    internal static async Task WriteWorker(int mib)
    {
        var stopping = Task.Run(Console.ReadLine);
        using var stream = Console.OpenStandardOutput();
        for (var i = 0; i < mib * 1024; i++) await stream.WriteAsync(Encoding.UTF8.GetBytes(Line(i)));
        Console.WriteLine("BURST-END"); Console.Out.Flush();
        var tick = 0;
        while (!stopping.IsCompleted) { Console.WriteLine("TICK:" + tick++); Console.Out.Flush(); await Task.WhenAny(stopping, Task.Delay(1000)); }
        Console.Write("TAIL:中文🙂"); Console.Out.Flush();
    }

    internal static async Task Run(string report, int seconds, int mib, Func<string, LaunchItem> worker)
    {
        if (seconds < 30 || seconds > 86400 || mib < 1 || mib > 1024) throw new ArgumentOutOfRangeException(nameof(seconds));
        var started = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        var root = Path.Combine(Path.GetTempPath(), "LaunchDesk-scale-" + Guid.NewGuid().ToString("N"));
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.AvailableFreeSpace < mib * 1024L * 1024 * 8 + 512 * 1024 * 1024) throw new IOException("Insufficient isolated test disk space");
        var item = worker("--worker scale " + mib); item.StopInput = "exit"; item.StopTimeoutSeconds = 10;
        var config = new Configuration { Preferences = new() { LogLimitMiB = Math.Max(1024, mib * 4) } };
        config.Items.Add(item);
        for (var i = 1; i < 1000; i++) { var entry = worker("--worker sleep"); entry.Name = "配置中文🙂-" + i; config.Items.Add(entry); }
        var store = new ConfigurationStore(root); store.Save(config); var loaded = store.Load();
        if (loaded.Items.Count != 1000 || !loaded.Items.Select(x => (x.Id, x.Name)).SequenceEqual(config.Items.Select(x => (x.Id, x.Name))))
            throw new InvalidDataException("1000-item roundtrip failed");
        var samples = new List<object>(); var projection = new LogProjection();
        var watch = Stopwatch.StartNew(); int pid;
        long warmMemory = 0; int warmHandles = 0; long lastMemory = 0; int lastHandles = 0;
        await using (var manager = new ProcessManager(loaded, root))
        {
            foreach (var entry in loaded.Items) _ = manager.Snapshot(entry.Id);
            await manager.StartAsync(item.Id); pid = manager.Snapshot(item.Id).Pid!.Value;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                projection.Update([(item.Id, item.Name, manager.Logs(item.Id))], false, null, "");
                using var process = Process.GetCurrentProcess(); process.Refresh();
                lastMemory = process.PrivateMemorySize64; lastHandles = process.HandleCount;
                var retained = manager.Logs(item.Id).Snapshot().Length;
                if (projection.CachedRecords > 3000 || retained > 3000) throw new InvalidDataException("Unbounded record cache");
                if (watch.Elapsed.TotalSeconds >= seconds / 2 && warmMemory == 0) { warmMemory = lastMemory; warmHandles = lastHandles; }
                samples.Add(new { seconds = watch.Elapsed.TotalSeconds, privateBytes = lastMemory, managedBytes = GC.GetTotalMemory(false),
                    handles = lastHandles, cachedRecords = projection.CachedRecords, retainedRecords = retained, formatted = projection.FormattedRecords });
                if (samples.Count % 15 == 0)
                    await File.WriteAllTextAsync(report + ".progress.json", JsonSerializer.Serialize(new { root, samples }));
                if (!manager.Snapshot(item.Id).Active) throw new InvalidDataException("Scale worker exited unexpectedly");
                await Task.Delay(1000);
            }
            await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(20));
            var export = Path.Combine(root, "complete.jsonl"); manager.Logs(item.Id).Export(export, out var skipped);
            if (skipped != 0) throw new InvalidDataException("Export skipped records");
            using var actual = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var burst = false;
            foreach (var recordLine in File.ReadLines(export))
            {
                var record = JsonSerializer.Deserialize<LogRecord>(recordLine)!;
                if (record.Channel != "stdout") continue;
                actual.AppendData(Encoding.UTF8.GetBytes(record.Text));
            }
            // Verify the burst stream exactly, including UTF-8 and sequence ordering across capture chunks.
            var index = 0; var ticks = 0; var pending = new StringBuilder();
            foreach (var recordLine in File.ReadLines(export))
            {
                var record = JsonSerializer.Deserialize<LogRecord>(recordLine)!;
                if (record.Channel != "stdout") continue;
                pending.Append(record.Text);
                while (true)
                {
                    var text = pending.ToString(); var newline = text.IndexOf('\n'); if (newline < 0) break;
                    var line = text[..(newline + 1)]; pending.Remove(0, newline + 1);
                    if (line.StartsWith('D')) { if (line != Line(index++)) throw new InvalidDataException("Lost/reordered burst data"); }
                    else if (line.TrimEnd('\r', '\n') == "BURST-END") burst = true;
                    else if (line.TrimEnd('\r', '\n') == "TICK:" + ticks) ticks++;
                    else throw new InvalidDataException("Unexpected stdout line: " + line);
                }
            }
            var tail = pending.ToString() == "TAIL:中文🙂";
            if (index != mib * 1024 || !tail || !burst) throw new InvalidDataException("Capture or stop drain incomplete");
            if (warmMemory == 0 || lastMemory - warmMemory > 128 * 1024 * 1024L || lastHandles - warmHandles > 32)
                throw new InvalidDataException("Steady-state resource growth exceeded budget");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { status = "passed", root, configurationItems = 1000,
                requestedSeconds = seconds, elapsedSeconds = watch.Elapsed.TotalSeconds, payloadBytes = mib * 1024L * 1024,
                logFileBytes = Directory.GetFiles(manager.Logs(item.Id).DirectoryPath).Sum(x => new FileInfo(x).Length),
                exportBytes = new FileInfo(export).Length, stdoutSha256 = Convert.ToHexString(actual.GetHashAndReset()),
                exactBurstLines = index, exactTickLines = ticks, tailDrained = tail, skippedRecords = skipped, privateGrowthBytes = lastMemory - warmMemory,
                handleGrowth = lastHandles - warmHandles, samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        try { using var process = Process.GetProcessById(pid); if (!process.HasExited) throw new InvalidDataException("Scale worker survived disposal"); }
        catch (ArgumentException) { }
        await TestEvidence.WriteAsync(report, started, "scale-soak");
    }
}
