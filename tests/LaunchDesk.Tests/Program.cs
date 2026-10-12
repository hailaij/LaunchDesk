using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args is ["--log-benchmark", var benchmarkReport])
{
    await LogBenchmarks.Run(benchmarkReport); return 0;
}
var self = Environment.ProcessPath!;
ProcessStartInfo Self(params string[] args)
{
    var info = new ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
    if (Path.GetFileNameWithoutExtension(self) == "dotnet") info.ArgumentList.Add(typeof(LaunchItem).Assembly.Location.Replace("LaunchDesk.Core.dll", "LaunchDesk.Tests.dll"));
    foreach (var arg in args) info.ArgumentList.Add(arg); return info;
}
LaunchItem Worker(string arguments) => new() { Name = "test", Kind = LaunchKind.Executable, Target = self,
    Arguments = (Path.GetFileNameWithoutExtension(self) == "dotnet" ? LaunchCommand.Quote(typeof(LaunchItem).Assembly.Location.Replace("LaunchDesk.Core.dll", "LaunchDesk.Tests.dll")) + " " : "") + arguments,
    WorkingDirectory = AppContext.BaseDirectory, StopTimeoutSeconds = 1 };
void PublishMarker(string path, string value)
{
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(temporary, value);
    File.Move(temporary, path, true);
}
if (args.Length > 0 && args[0] != "--report")
{
    if (args[0] == "--elevated")
    {
        await ElevationBroker.RunHelperAsync(args[1], args[2], int.Parse(args[3]), long.Parse(args[4]), _ => Task.CompletedTask, requireAdministrator: false);
        return 0;
    }
    if (args[0] == "--broker-owner")
    {
        using var process = await ElevationBroker.StartAsync(Worker("--worker sleep"), self, CancellationToken.None,
            info => Process.Start(Self(info.ArgumentList.ToArray())));
        PublishMarker(args[1], process.Id.ToString());
        await Task.Delay(Timeout.Infinite); return 0;
    }
    if (args[0] == "--journal-owner")
    {
        var journal = new SessionJournal(args[1]); journal.Begin();
        var item = Worker("--worker sleep"); var config = new Configuration { Items = [item] };
        new ConfigurationStore(args[1]).Save(config);
        await using var manager = new ProcessManager(config, args[1]) { Session = journal };
        await manager.StartAsync(item.Id);
        PublishMarker(args[2], JsonSerializer.Serialize(new ProductChecks.OwnerMarker(item.Id, manager.Snapshot(item.Id).Pid!.Value)));
        await Task.Delay(Timeout.Infinite); return 0;
    }
    if (args[0] == "--install-data-probe")
    {
        var data = new ConfigurationStore(args[1]).Load();
        if (data.Items.Count != int.Parse(args[2]) || data.Folders.Count != 0) return 1;
        if (data.Items.Count == 0 && (File.Exists(Path.Combine(args[1], "config.json"))
            || Directory.Exists(Path.Combine(args[1], "logs")))) return 1;
        Console.WriteLine("Isolated installed-data load verified"); return 0;
    }
    if (args[0] == "--save-worker")
    {
        var writer = new ConfigurationStore(args[1]);
        for (var i = 0; i < 100; i++) writer.Save(new() { Items = [new() { Name = args[2] + i, Target = "not-executed" }] });
        return 0;
    }
    if (args[0] == "--signal") { var sent = NativeProcess.SendControlSignal(int.Parse(args[1])); Thread.Sleep(100); return sent ? 0 : 1; }
    if (args[0] == "--input-probe")
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), "LaunchDesk-input-probe-" + Guid.NewGuid().ToString("N"));
        var item = Worker("--worker sleep");
        await using var manager = new ProcessManager(new() { Items = [item] }, probeRoot);
        await manager.StartAsync(item.Id);
        var write = manager.SendInputAsync(item.Id, new string('x', 100_000));
        var done = await Task.WhenAny(write, Task.Delay(3000)) == write;
        await manager.StopAsync(item.Id);
        try { await write; } catch (IOException) { }
        Console.WriteLine("Blocked input bounded without external stop: " + done); return done ? 0 : 1;
    }
    if (args[0] == "--maintenance-probe")
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), "LaunchDesk-maintenance-probe-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(probeRoot, "logs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "expired.jsonl"); File.WriteAllText(path, "expired"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-5));
        await using var manager = new ProcessManager(new() { Preferences = new() { LogDays = 1 } }, probeRoot);
        await Task.Delay(1000); var cleaned = !File.Exists(path);
        Console.WriteLine("Idle history cleaned without process launch: " + cleaned); return cleaned ? 0 : 1;
    }
    if (args[0] == "--worker")
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        switch (args[1])
        {
            case "health-ready":
                while (!File.Exists(args[2])) await Task.Delay(20);
                Console.Write("READY"); Console.Out.Flush(); await Task.Delay(Timeout.Infinite); break;
            case "args": Console.Write(JsonSerializer.Serialize(args.Skip(2).ToArray())); return 0;
            case "flood-stop":
            case "flood":
                using (var output = Console.OpenStandardOutput())
                using (var error = Console.OpenStandardError())
                {
                    for (var i = 0; i < 12000; i++)
                    {
                        output.Write(Encoding.UTF8.GetBytes($"OUT:{i}:中文🙂:{new string('x', 64)}\n"));
                        error.Write(Encoding.UTF8.GetBytes($"ERR:{i}:错误:{new string('y', 64)}\n"));
                    }
                    foreach (var value in Encoding.UTF8.GetBytes("尾部🙂无换行")) output.WriteByte(value);
                    error.Write(Encoding.UTF8.GetBytes("stderr-tail"));
                }
                if (args[1] == "flood-stop") _ = Console.ReadLine();
                return 0;
            case "input": Console.Write("READY"); Console.Out.Flush(); var input = Console.ReadLine(); Console.Write("GOT:" + input); return 0;
            case "stop-prompt":
                Console.Write("READY"); Console.Out.Flush();
                while (!File.Exists(args[2])) await Task.Delay(20);
                await Task.Delay(400); Console.Write("(Y/N)?"); Console.Out.Flush();
                if (args.Length > 3) return 0;
                Console.Write("GOT:" + Console.ReadLine()); return 0;
            case "gbk":
                using (var output = Console.OpenStandardOutput())
                    foreach (var value in Encoding.GetEncoding("gbk").GetBytes("中文GBK尾部")) output.WriteByte(value);
                return 0;
            case "env": Console.Write(Environment.GetEnvironmentVariable("LAUNCHDESK_TEST") + "|" + Environment.CurrentDirectory); return 0;
            case "spawn":
                using (var child = Process.Start(Self("--worker", "sleep"))!)
                { File.WriteAllText(args[2], Environment.ProcessId + "," + child.Id); Thread.Sleep(Timeout.Infinite); }
                break;
            case "detach":
                using (var child = Process.Start(Self("--worker", "sleep"))!) File.WriteAllText(args[2], child.Id.ToString());
                return 0;
            case "sleep": Thread.Sleep(Timeout.Infinite); break;
            case "ready-log":
                await Task.Delay(400); Console.Write("REA"); Console.Out.Flush(); await Task.Delay(100); Console.Write("DY"); Console.Out.Flush();
                await Task.Delay(Timeout.Infinite); break;
            case "ready-server":
                await Task.Delay(400);
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, int.Parse(args[2]));
                listener.Start();
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    var stream = client.GetStream(); var request = new byte[4096];
                    if (await stream.ReadAsync(request) == 0) continue;
                    var status = args.Length > 3 ? args[3] : "200";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"));
                }
            case "fail": Console.Error.Write("failure"); return 7;
        }
        return 0;
    }
    if (args[0] == "--owner")
    {
        using var native = new NativeProcess(Worker("--worker spawn " + LaunchCommand.Quote(args[1])));
        while (!File.Exists(args[1])) await Task.Delay(50);
        File.WriteAllText(args[1] + ".owner", Environment.ProcessId.ToString());
        Thread.Sleep(Timeout.Infinite); return 0;
    }
}

var root = Path.Combine(Path.GetTempPath(), "LaunchDesk-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<TestResult>();
var startedAt = DateTimeOffset.UtcNow;
var filterIndex = Array.IndexOf(args, "--filter");
var filter = filterIndex >= 0 && filterIndex + 1 < args.Length ? args[filterIndex + 1] : null;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Until(Func<bool> condition, int seconds = 15)
{
    var stopwatch = Stopwatch.StartNew();
    while (!condition()) { if (stopwatch.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Condition timed out"); await Task.Delay(50); }
}
bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
IEnumerable<string> ReadLog(string file)
{
    using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    while (reader.ReadLine() is { } line) yield return line;
}
async Task Test(string name, Func<Task> action)
{
    if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
    var time = Stopwatch.StartNew();
    try { await action(); Console.WriteLine("PASS " + name); results.Add(new(name, "passed", time.ElapsedMilliseconds)); }
    catch (TestSkippedException error) { Console.WriteLine("SKIP " + name + ": " + error.Message); results.Add(new(name, "skipped", time.ElapsedMilliseconds, reason: error.Message)); }
    catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error); results.Add(new(name, "failed", time.ElapsedMilliseconds, error: error.ToString())); }
}
async Task Signal(int pid)
{ using var helper = Process.Start(Self("--signal", pid.ToString()))!; await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }

await ReliabilityChecks.Run(root, Test);
await DataReliabilityChecks.Run(root, Test, Self);
await HealthChecks.Run(root, Test, Worker);
await ProductChecks.Run(root, Test, Worker, Self);
await HostingChecks.Run(root, Test, Worker, Self);
await CorrectnessChecks.Run(root, Test, Worker);
await ProjectionBoundaryChecks.Run(root, Test);
await Test("Incremental log cursors handle retention clear and concurrent writes", async () =>
{
    using var log = new LogStore(Path.Combine(root, "delta"), Guid.NewGuid(), new());
    Assert(log.ReadSince(-1) is { Reset: true, Records.Length: 0 }, "initial empty reset");
    log.Write("stdout", "one\n"); var first = log.ReadSince(-1);
    log.Write("stderr", "two\n"); var next = log.ReadSince(first.Cursor);
    Assert(!next.Reset && next.Records.Length == 1 && next.Records[0].Text == "two\n", "append only");
    Assert(log.ReadSince(next.Cursor).Records.Length == 0, "idle delta");
    await Task.Run(() => Parallel.For(0, 3100, i => log.Write("stdout", $"record-{i}\n")));
    var overflow = log.ReadSince(next.Cursor);
    Assert(overflow.Reset && overflow.Records.Length == 3000, "missed retention requires reset");
    for (var i = 0; i < 7000; i++)
    {
        log.Write("stdout", $"wrapped-{i}\n");
        var delta = log.ReadSince(overflow.Cursor + i);
        Assert(!delta.Reset && delta.Records.Length == 1 && delta.Records[0].Text == $"wrapped-{i}\n", "indexed ring wrap delta");
    }
    Assert(log.Snapshot().First().Text == "wrapped-4000\n" && log.Snapshot().Last().Text == "wrapped-6999\n", "ring retained order");
    overflow = log.ReadSince(-1);
    log.ClearPanel(); var cleared = log.ReadSince(overflow.Cursor);
    Assert(cleared.Reset && cleared.Records.Length == 0, "clear is visible even without new records");
    log.Write("stdout", "after-clear");
    Assert(log.ReadSince(cleared.Cursor).Records.Single().Text == "after-clear", "resume after clear");
    var filesBefore = Directory.GetFiles(log.DirectoryPath, "*.jsonl").Length;
    typeof(LogStore).GetField("currentDate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(log, DateTime.Today.AddDays(-1));
    log.Write("stdout", "next-day");
    Assert(Directory.GetFiles(log.DirectoryPath, "*.jsonl").Length == filesBefore + 1, "cached previous day rotates file");
});
await Test("Incremental log projection reuses records and matches merged snapshots", () =>
{
    var a = Guid.NewGuid(); var b = Guid.NewGuid();
    using var first = new LogStore(Path.Combine(root, "projection"), a, new());
    using var second = new LogStore(Path.Combine(root, "projection"), b, new());
    (Guid Id, string Name, LogStore Store)[] sources = [(a, "A", first), (b, "B", second)];
    var projection = new LogProjection();
    first.Write("stdout", "\u001b[31mone\u001b[0m\n\n"); second.Write("stderr", "two\n");
    var initial = projection.Update(sources, true, null, "");
    Assert(projection.ReadRecords == 2 && projection.FormattedRecords == 2, "initial formatting");
    Assert(initial.Any(x => x.Contains("[A] [stdout] one")) && initial.Any(x => x.Contains("[B] [stderr] two")), "merged labels and ANSI");
    projection.Update(sources, true, null, "");
    Assert(projection.ReadRecords == 0 && projection.FormattedRecords == 0, "idle does not read or format history");
    first.Write("stdout", "three\n"); projection.Update(sources, true, null, "");
    Assert(projection.ReadRecords == 1 && projection.FormattedRecords == 1, "only new record formatted");
    var filtered = projection.Update(sources, true, "stderr", "two");
    Assert(filtered.Length == 1 && filtered[0].Contains("two") && projection.FormattedRecords == 0, "filter cached text");
    var renamed = projection.Update([(a, "Renamed", first)], true, null, "");
    Assert(renamed.Any(x => x.Contains("[Renamed]")) && renamed.All(x => !x.Contains("[B]")), "rename and membership");
    first.ClearPanel(); Assert(projection.Update([(a, "Renamed", first)], true, null, "").Length == 0, "clear removes cached records");
    for (var i = 0; i < 3100; i++) first.Write("stdout", $"r-{i}\n");
    projection.Update([(a, "A", first)], false, null, "");
    first.Write("stdout", "tail\n"); var rolling = projection.Update([(a, "A", first)], false, null, "");
    Assert(projection.ReadRecords == 1 && projection.FormattedRecords == 1 && projection.CachedRecords == 3000, "bounded rolling cache");
    Assert(rolling.Length == 3001 && rolling[^2].EndsWith("tail") && !rolling.Any(x => x.EndsWith("r-100")), "rolling rows current");
    return Task.CompletedTask;
});
await Test("Windows legacy output encoding and UTF-8 chunk boundaries", async () =>
{
    var gbk = Encoding.GetEncoding("gbk");
    foreach (var (encoding, text) in new[] { (Encoding.UTF8, "中文🙂尾部"), (gbk, "中文GBK尾部") })
    {
        var bytes = encoding.GetBytes(text);
        for (var split = 0; split <= bytes.Length; split++)
        {
            var decoder = new OutputDecoder("auto", gbk);
            var actual = decoder.Decode(bytes.AsSpan(0, split)) + decoder.Decode(bytes.AsSpan(split)) + decoder.Decode([], true);
            Assert(actual == text, "auto decoding at split " + split);
        }
        var incremental = new OutputDecoder("auto", gbk);
        var result = string.Concat(bytes.Select(b => incremental.Decode(new[] { b }))) + incremental.Decode([], true);
        Assert(result == text, "byte-by-byte decoding");
        var explicitDecoder = new OutputDecoder(encoding.CodePage == 65001 ? "utf-8" : "gbk", gbk);
        var explicitResult = string.Concat(bytes.Select(b => explicitDecoder.Decode(new[] { b }))) + explicitDecoder.Decode([], true);
        Assert(explicitResult == text && explicitDecoder.Decode([], true) == "", "pooled explicit decoder tail and repeat flush");
    }
    var incomplete = new OutputDecoder("auto", gbk);
    Assert(incomplete.Decode(new byte[] { 0xE4 }) == "", "partial UTF-8 buffered");
    Assert(incomplete.Decode([], true).Length > 0, "incomplete tail not lost");
    Assert(OutputDecoder.Resolve("system").CodePage == OutputDecoder.SystemEncoding.CodePage, "system code page");
    var item = Worker("--worker gbk"); item.Encoding = "gbk";
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => !manager.Snapshot(item.Id).Active);
    var records = manager.Logs(item.Id).Snapshot().Where(x => x.Channel == "stdout").ToArray();
    Assert(string.Concat(records.Select(x => x.Text)) == "中文GBK尾部", "native pipe decoding");
    Assert(records.SelectMany(x => x.RawBytes ?? []).SequenceEqual(gbk.GetBytes("中文GBK尾部")), "raw legacy bytes retained");
    var automatic = Worker("--worker input"); automatic.Encoding = "auto";
    await using var manager2 = new ProcessManager(new() { Items = [automatic] }, root, Signal);
    await manager2.StartAsync(automatic.Id);
    await Until(() => manager2.Logs(automatic.Id).Snapshot().Any(x => x.Text.Contains("READY")));
    await manager2.SendInputAsync(automatic.Id, "你好🙂");
    await Until(() => !manager2.Snapshot(automatic.Id).Active);
    Assert(string.Concat(manager2.Logs(automatic.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)) == "READYGOT:你好🙂", "auto native UTF-8 output and input");
});
await Test("Reliability: blocked stdin has bounded failure and releases lifecycle", async () =>
{
    var item = Worker("--worker sleep");
    await using var manager = new ProcessManager(new() { Items = [item] }, root);
    await manager.StartAsync(item.Id);
    var pid = manager.Snapshot(item.Id).Pid!.Value;
    try { await manager.SendInputAsync(item.Id, new string('x', 100_000)).WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Blocked input accepted"); }
    catch (IOException error) { Assert(error.Message.Contains("超时"), "explicit timeout"); }
    await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!Alive(pid) && !manager.Snapshot(item.Id).Active, "timeout releases gate and kills tree");
});
await Test("Reliability: small buffered input flush cannot hang", async () =>
{
    var item = Worker("--worker sleep");
    await using var manager = new ProcessManager(new() { Items = [item] }, root);
    await manager.StartAsync(item.Id); var timedOut = false;
    for (var i = 0; i < 32; i++)
    {
        try { await manager.SendInputAsync(item.Id, new string('x', 1000)).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (IOException error) { timedOut = error.Message.Contains("超时"); break; }
    }
    await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(5));
    Assert(timedOut && !manager.Snapshot(item.Id).Active, "Flush timeout is bounded");
});

await Test("Language preferences round-trip without changing user data", () =>
{
    var config = new Configuration { Items = [new() { Name = "中文名称", Target = "中文路径", Arguments = "原始参数" }] };
    config.Preferences.Language = "en-US";
    var copy = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config, ConfigurationStore.JsonOptions), ConfigurationStore.JsonOptions)!;
    Assert(copy.Preferences.Language == "en-US" && copy.Items[0].Name == "中文名称" && copy.Items[0].Arguments == "原始参数", "locale must not mutate user data");
    Assert(JsonSerializer.Deserialize<Preferences>("{}")!.Language == "zh-CN", "legacy language default");
    copy.Preferences.Language = "bad";
    try { ConfigurationStore.Validate(copy); throw new Exception("invalid locale accepted"); } catch (InvalidDataException) { }
    return Task.CompletedTask;
});
await Test("Installer metadata is pinned to expected GitHub asset", () =>
{
    var hash = new string('a', 64);
    var json = $$"""{"tag_name":"v9.0.0","draft":false,"prerelease":false,"assets":[{"name":"LaunchDesk-v9.0.0-win-x64-setup.exe","size":42,"digest":"sha256:{{hash}}","browser_download_url":"https://evil.example/installer.exe"}]}""";
    var release = UpdateChecker.Parse(json)!;
    Assert(release.Installer?.DownloadUri.Host == "github.com" && release.Installer.Size == 42, "ignore untrusted metadata URLs");
    Assert(UpdateChecker.Parse(json.Replace("sha256:", "sha1:"))!.Installer is null, "unknown digest rejected");
    Assert(UpdateChecker.Parse(json.Replace("\"size\":42", "\"size\":0"))!.Installer is null, "empty asset rejected");
    foreach (var url in new[] { "http://github.com/a", "https://evil.example/a", "https://github.com:444/a", "https://user@github.com/a", "https://github.com/a#fragment" })
        try { UpdateDownloader.ValidateDownloadUri(new(url)); throw new Exception("unsafe URL accepted"); } catch (InvalidDataException) { }
    return Task.CompletedTask;
});
await Test("Verified downloads require both hash and signature, failures clean up", async () =>
{
    var bytes = Encoding.UTF8.GetBytes("isolated fake installer, never executed");
    var asset = new ReleaseAsset("LaunchDesk-v9.0.0-win-x64-setup.exe", bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), new Uri("https://github.com/hailaij/LaunchDesk/releases/download/v9.0.0/LaunchDesk-v9.0.0-win-x64-setup.exe"));
    var release = new ReleaseUpdate(new(9, 0, 0), "v9.0.0", "", new Uri("https://github.com/hailaij/LaunchDesk/releases/tag/v9.0.0")) { Installer = asset };
    var cache = Path.Combine(root, "update-cache");
    var old = Path.Combine(root, "old-version.txt"); await File.WriteAllTextAsync(old, "unchanged");
    var verifier = new TestSignatureVerifier();
    using var client = new HttpClient(new DownloadResponseHandler(bytes));
    var result = await new UpdateDownloader(client, verifier).DownloadAsync(release, cache);
    Assert(verifier.Calls == 1 && File.ReadAllBytes(result.Path).SequenceEqual(bytes), "verified promotion");
    foreach (var mode in new[] { "hash", "signature", "truncated", "oversized", "network", "redirect", "cancel", "midstream" })
    {
        var failingCache = Path.Combine(root, "download-failure-" + mode);
        using var failingClient = new HttpClient(new DownloadResponseHandler(bytes, mode));
        var signer = new TestSignatureVerifier { Reject = mode == "signature" };
        var failingRelease = mode == "hash" ? release with { Installer = asset with { Sha256 = new string('0', 64) } } : release;
        using var cancel = new CancellationTokenSource(); if (mode == "cancel") cancel.Cancel();
        var failed = false;
        try { await new UpdateDownloader(failingClient, signer).DownloadAsync(failingRelease, failingCache, cancellationToken: cancel.Token); }
        catch (Exception) { failed = true; }
        Assert(failed && (!Directory.Exists(failingCache) || !Directory.EnumerateFileSystemEntries(failingCache).Any()), mode + " rollback");
        Assert(File.ReadAllText(old) == "unchanged", "old version unchanged");
        if (mode == "hash") Assert(signer.Calls == 0, "hash precedes trust check");
    }
});
await Test("Unconfigured publisher blocks download before network access", async () =>
{
    var handler = new DownloadResponseHandler([1]); using var client = new HttpClient(handler);
    var release = new ReleaseUpdate(new(9, 0, 0), "v9.0.0", "", new Uri("https://github.com/hailaij/LaunchDesk/releases"));
    try { await new UpdateDownloader(client, new AuthenticodeVerifier("")).DownloadAsync(release, Path.Combine(root, "unconfigured")); throw new Exception("missing publisher accepted"); }
    catch (InvalidDataException) { }
    Assert(handler.Calls == 0, "no network request without trust pin");
});
await SigningChecks.Run(root, Test);
await Test("Rename and stable startup priority preserve dependencies", async () =>
{
    var folder = new LaunchFolder { Name = "group" };
    var later = Worker("--worker sleep"); later.StartupOrder = 20; later.FolderId = folder.Id;
    var first = Worker("--worker sleep"); first.StartupOrder = 1; first.FolderId = folder.Id;
    var tie = Worker("--worker sleep"); tie.StartupOrder = 20; tie.FolderId = folder.Id;
    var config = new Configuration { Items = [later, first, tie], Folders = [folder] };
    Assert(config.StartupItems().Select(x => x.Id).SequenceEqual(new[] { first.Id, later.Id, tie.Id }), "stable priority");
    config.Rename(later.Id, "renamed"); config.Rename(folder.Id, "renamed group");
    Assert(later.Name == "renamed" && folder.Name == "renamed group", "in-place metadata rename");
    var invalid = false; try { config.Rename(first.Id, " "); } catch (InvalidDataException) { invalid = true; }
    Assert(invalid && first.Name == "test", "invalid rename preserves name");
    await using var manager = new ProcessManager(config, Path.Combine(root, "startup-priority"), Signal);
    await manager.StartFolderAsync(folder.Id);
    Assert(manager.Snapshot(first.Id).Started <= manager.Snapshot(later.Id).Started && manager.Snapshot(later.Id).Started <= manager.Snapshot(tie.Id).Started, "folder start priority");
    await manager.StopAllAsync();
    first.Dependencies = [tie.Id];
    await manager.StartAllAsync();
    Assert(manager.Snapshot(tie.Id).Started <= manager.Snapshot(first.Id).Started, "dependencies override numeric priority");
    await manager.StopAllAsync();
});
await Test("Merged logs label sources and preserve raw exported records", () =>
{
    var a = Guid.NewGuid(); var b = Guid.NewGuid();
    using var first = new LogStore(Path.Combine(root, "merged"), a, new());
    using var second = new LogStore(Path.Combine(root, "merged"), b, new());
    first.Write("stdout", "first\n", [1, 2]); second.Write("stderr", "second\n", [3, 4]); first.Write("stdout", "third\n");
    (Guid Id, string Name, LogStore Store)[] sources = [(a, "A", first), (b, "B", second)];
    var snapshot = LogStore.SnapshotMerged(sources);
    Assert(snapshot.Select(x => x.Record.Text.Trim()).SequenceEqual(new[] { "first", "second", "third" }), "time merge");
    var export = Path.Combine(root, "merged.jsonl"); LogStore.ExportMerged(sources, export);
    var records = File.ReadAllLines(export).Select(x => JsonDocument.Parse(x)).ToArray();
    try
    {
        Assert(records.Length == 3 && records[1].RootElement.GetProperty("SourceName").GetString() == "B", "source in export");
        Assert(records[0].RootElement.GetProperty("RawBytes").GetBytesFromBase64().SequenceEqual(new byte[] { 1, 2 }), "raw preserved");
    }
    finally { foreach (var record in records) record.Dispose(); }
    first.ClearPanel(); second.ClearPanel();
    Assert(LogStore.SnapshotMerged(sources).Length == 0 && File.ReadAllLines(export).Length == 3, "panel clear preserves history");
    return Task.CompletedTask;
});
await Test("Log URLs accept HTTP only and trim surrounding punctuation", () =>
{
    var text = "链接：http://127.0.0.1:4000/pro/ https://example.com/a(b))。 file:///C:/x javascript:alert(1) https://user:secret@example.com/";
    var links = LogLinks.Find(text).ToArray();
    Assert(links.Length == 2 && text.Substring(links[0].Start, links[0].Length) == "http://127.0.0.1:4000/pro/", "localhost link");
    Assert(text.Substring(links[1].Start, links[1].Length) == "https://example.com/a(b)", "balanced punctuation");
    return Task.CompletedTask;
});
await Test("Data migration retains config logs backups and safe locator", () =>
{
    var old = new ConfigurationStore(Path.Combine(root, "old-data")); var config = new Configuration { Items = [Worker("--worker sleep")] }; old.Save(config);
    var backup = old.BackupNow(config);
    using (var log = new LogStore(old.Root, config.Items[0].Id, config.Preferences)) log.Write("stdout", "retained\n");
    var destination = Path.Combine(root, "new-data"); var migrated = DataLocation.CopyTo(old, config, destination);
    Assert(migrated.Load().Items[0].Id == config.Items[0].Id && File.Exists(old.ConfigPath), "configuration and original retained");
    Assert(File.Exists(Path.Combine(destination, Path.GetRelativePath(old.Root, backup))) && Directory.GetFiles(Path.Combine(destination, "logs"), "*.jsonl", SearchOption.AllDirectories).Length == 1, "backups and logs copied");
    var locator = Path.Combine(root, "location.json"); DataLocation.Save(destination, locator);
    Assert(DataLocation.Resolve(root, false, locator) == destination && DataLocation.Resolve(root, true, locator) == Path.Combine(root, "data"), "persisted location and portable precedence");
    var failed = false; try { DataLocation.CopyTo(old, config, destination); } catch (InvalidOperationException) { failed = true; }
    Assert(failed && migrated.Load().Items.Count == 1, "never overwrite destination");
    failed = false; try { DataLocation.ValidateDestination(old.Root, Path.Combine(old.Root, "nested")); } catch (InvalidOperationException) { failed = true; }
    Assert(failed, "reject recursive copy");
    return Task.CompletedTask;
});
await Test("Launch preflight detects resource, interpreter and npm mistakes", () =>
{
    var directory = Path.Combine(root, "preflight"); Directory.CreateDirectory(directory);
    var missing = new LaunchItem { Target = Path.Combine(directory, "missing.bat"), Kind = LaunchKind.Batch };
    var failed = false; try { LaunchPreflight.Check(missing); } catch (FileNotFoundException) { failed = true; }
    Assert(failed, "missing resource");
    missing.WorkingDirectory = Path.Combine(directory, "missing"); failed = false;
    try { LaunchPreflight.Check(missing); } catch (DirectoryNotFoundException) { failed = true; }
    Assert(failed, "missing directory");
    var npm = new LaunchItem { Kind = LaunchKind.Command, Target = "npm run start", WorkingDirectory = directory };
    failed = false; try { LaunchPreflight.Check(npm); } catch (InvalidDataException) { failed = true; }
    Assert(failed, "missing package");
    File.WriteAllText(Path.Combine(directory, "package.json"), """{"scripts":{"test":"echo ok"}}""");
    failed = false; try { LaunchPreflight.Check(npm); } catch (InvalidDataException error) { failed = error.Message.Contains("scripts.start"); }
    Assert(failed, "missing npm start script");
    File.WriteAllText(Path.Combine(directory, "package.json"), """{"scripts":{"start":"echo ok"}}"""); LaunchPreflight.Check(npm);
    var script = Path.Combine(directory, "test.sh"); File.WriteAllText(script, "echo ok");
    failed = false; try { LaunchPreflight.Check(new() { Kind = LaunchKind.Shell, Target = script }); } catch (InvalidOperationException) { failed = true; }
    Assert(failed, "shell interpreter required");
    var spaced = Path.Combine(directory, "file with space.cmd"); File.WriteAllText(spaced, "@echo ok");
    LaunchPreflight.Check(new() { Kind = LaunchKind.Command, Target = spaced, WorkingDirectory = directory });
    return Task.CompletedTask;
});
await Test("Preflight failure prevents dependency startup", async () =>
{
    var dependency = Worker("--worker sleep");
    var item = new LaunchItem { Kind = LaunchKind.Batch, Target = Path.Combine(root, "not-found.cmd"), Dependencies = [dependency.Id] };
    await using var manager = new ProcessManager(new() { Items = [dependency, item] }, Path.Combine(root, "preflight-dependencies"), Signal);
    var failed = false;
    try { await manager.StartAsync(item.Id); } catch (FileNotFoundException) { failed = true; }
    Assert(failed && !manager.Snapshot(dependency.Id).Active && manager.Snapshot(item.Id).Status == RunStatus.Failed, "fail before dependency");
});
await Test("Manual backups retain config and survive automatic pruning", () =>
{
    var store = new ConfigurationStore(Path.Combine(root, "manual-backups")); var config = new Configuration { Items = [Worker("--worker sleep")] };
    var path = store.BackupNow(config); var name = config.Items[0].Name;
    for (var i = 0; i < 23; i++) { config.Items[0].Name = "changed" + i; store.Save(config); }
    Assert(File.Exists(path) && ConfigurationStore.Read(path).Items[0].Name == name, "manual backup retained");
    Assert(Directory.GetFiles(Path.Combine(store.Root, "backups"), "*.json").Length <= 20, "automatic limit intact");
    return Task.CompletedTask;
});
await Test("Drag model grouping ordering and file import preserve resources", () =>
{
    var first = new LaunchFolder { Name = "first" }; var second = new LaunchFolder { Name = "second" };
    var a = Worker("--worker sleep"); var b = Worker("--worker sleep"); var c = Worker("--worker sleep");
    a.FolderId = first.Id; b.FolderId = first.Id; c.FolderId = second.Id;
    var config = new Configuration { Folders = [first, second], Items = [a, b, c] };
    config.MoveItem(b.Id, first.Id, a.Id); Assert(config.Items.IndexOf(b) < config.Items.IndexOf(a), "reorder");
    config.MoveItem(a.Id, second.Id, c.Id); Assert(a.FolderId == second.Id && config.Items.IndexOf(a) < config.Items.IndexOf(c), "move group");
    config.MoveItem(a.Id, null); Assert(a.FolderId is null, "ungroup");
    config.MoveFolder(second.Id, first.Id); Assert(config.Folders[0] == second, "reorder groups");
    var path = Path.Combine(root, "import.cmd"); File.WriteAllText(path, "@echo imported");
    var imported = LaunchItemFactory.FromFile(path, first.Id);
    Assert(imported.Kind == LaunchKind.Batch && imported.FolderId == first.Id && !imported.AutoStart && File.Exists(path), "no move or launch");
    Assert(!LaunchItemFactory.Supports("unknown.txt"), "unsupported extension");
    ConfigurationStore.Validate(config); return Task.CompletedTask;
});
await Test("Readiness legacy defaults and validation", () =>
{
    var legacy = JsonSerializer.Deserialize<LaunchItem>("{}", ConfigurationStore.JsonOptions)!;
    Assert(legacy.Readiness.Kind == ReadinessKind.None, "legacy readiness disabled");
    var item = Worker("--worker sleep"); item.Readiness = new() { Kind = ReadinessKind.Http, Url = "file:///C:/invalid" };
    var failed = false; try { ConfigurationStore.Validate(new() { Items = [item] }); } catch (InvalidDataException) { failed = true; }
    Assert(failed, "reject non HTTP URI");
    item.Readiness = new() { Kind = ReadinessKind.Tcp, Port = 0 }; failed = false;
    try { ConfigurationStore.Validate(new() { Items = [item] }); } catch (InvalidDataException) { failed = true; }
    Assert(failed, "reject port"); return Task.CompletedTask;
});
await Test("Readiness waits for split current-run logs and dependencies", async () =>
{
    var dependency = Worker("--worker ready-log"); dependency.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "READY", TimeoutSeconds = 5 };
    var parent = Worker("--worker sleep"); parent.Dependencies = [dependency.Id];
    await using var manager = new ProcessManager(new() { Items = [dependency, parent] }, Path.Combine(root, "ready-log"), Signal);
    var pending = manager.StartAsync(parent.Id);
    await Until(() => manager.Snapshot(dependency.Id).Pid.HasValue);
    Assert(manager.Snapshot(dependency.Id).Status == RunStatus.Starting && !manager.Snapshot(parent.Id).Active, "dependent waits");
    await pending; Assert(manager.Snapshot(parent.Id).Status == RunStatus.Running, "dependent started after match");
    await manager.StopAllAsync();
});
await Test("TCP and HTTP readiness with delayed local server", async () =>
{
    foreach (var kind in new[] { ReadinessKind.Tcp, ReadinessKind.Http })
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var item = Worker("--worker ready-server " + port);
        item.Readiness = new() { Kind = kind, Port = port, Url = "http://127.0.0.1:" + port + "/", TimeoutSeconds = 8 };
        await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "ready-" + kind), Signal);
        await manager.StartAsync(item.Id); Assert(manager.Snapshot(item.Id).Status == RunStatus.Running, "network readiness");
        await manager.StopAllAsync();
    }
});
await Test("Readiness rejects HTTP errors and early process exit", async () =>
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var server = Worker("--worker ready-server " + port + " 503");
    server.Readiness = new() { Kind = ReadinessKind.Http, Url = "http://127.0.0.1:" + port + "/", TimeoutSeconds = 2 };
    var earlyExit = Worker("--worker fail");
    earlyExit.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "NEVER", TimeoutSeconds = 10 };
    await using var manager = new ProcessManager(new() { Items = [server, earlyExit] }, Path.Combine(root, "ready-failures"), Signal);
    var start = manager.StartAsync(server.Id); await Until(() => manager.Snapshot(server.Id).Pid.HasValue);
    var pid = manager.Snapshot(server.Id).Pid!.Value; var failed = false;
    try { await start; } catch (TimeoutException) { failed = true; }
    await Until(() => !manager.Snapshot(server.Id).Active);
    Assert(failed && !Alive(pid), "HTTP 503 is not ready and process is cleaned");
    failed = false;
    try { await manager.StartAsync(earlyExit.Id); } catch (InvalidOperationException) { failed = true; }
    await Until(() => !manager.Snapshot(earlyExit.Id).Active);
    Assert(failed && manager.Snapshot(earlyExit.Id).Status == RunStatus.Failed, "early exit is not ready");
});
await Test("Readiness timeout ignores old logs and cleans process tree", async () =>
{
    var item = Worker("--worker sleep"); item.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "OLD-READY", TimeoutSeconds = 1 };
    await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "ready-timeout"), Signal);
    manager.Logs(item.Id).Write("stdout", "OLD-READY\n");
    var start = manager.StartAsync(item.Id); await Until(() => manager.Snapshot(item.Id).Pid.HasValue);
    var pid = manager.Snapshot(item.Id).Pid!.Value; var failed = false;
    try { await start; } catch (TimeoutException) { failed = true; }
    await Until(() => !manager.Snapshot(item.Id).Active);
    Assert(failed && manager.Snapshot(item.Id).Status == RunStatus.Failed && !Alive(pid), "timeout terminates process");
});
await Test("Stop cancels pending readiness and blocks dependent launch", async () =>
{
    var dependency = Worker("--worker sleep"); dependency.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "NEVER", TimeoutSeconds = 30 };
    var parent = Worker("--worker sleep"); parent.Dependencies = [dependency.Id];
    await using var manager = new ProcessManager(new() { Items = [dependency, parent] }, Path.Combine(root, "ready-cancel"), Signal);
    var pending = manager.StartAsync(parent.Id); await Until(() => manager.Snapshot(dependency.Id).Pid.HasValue);
    var pid = manager.Snapshot(dependency.Id).Pid!.Value; await manager.StopAllAsync();
    try { await pending; } catch (OperationCanceledException) { }
    Assert(!Alive(pid) && !manager.Snapshot(parent.Id).Active && manager.Snapshot(dependency.Id).Status == RunStatus.Stopped, "cancel before dependent");
});
await Test("Release update stable version comparison and safe links", () =>
{
    var update = UpdateChecker.Parse("""{"tag_name":"v0.10.0","draft":false,"prerelease":false,"body":"notes","html_url":"https://untrusted.invalid"}""")!;
    Assert(update.IsNewerThan(new Version(0, 2, 1)), "numeric version comparison");
    Assert(!update.IsNewerThan(new Version(0, 10, 0, 0)), "same version is not newer");
    Assert(!update.IsNewerThan(new Version(1, 0, 0)), "older release is not newer");
    Assert(update.Page.AbsoluteUri == "https://github.com/hailaij/LaunchDesk/releases/tag/v0.10.0", "fixed release host");
    Assert(update.Notes == "notes", "release notes");
    Assert(UpdateChecker.Parse("""{"tag_name":"v2.0.0-beta","draft":false,"prerelease":true}""") is null, "skip prerelease");
    Assert(UpdateChecker.Parse("""{"tag_name":"v2.0.0","draft":true,"prerelease":false}""") is null, "skip draft");
    var rejected = false;
    try { UpdateChecker.Parse("""{"tag_name":"../../invalid","draft":false,"prerelease":false}"""); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "reject invalid tag");
    return Task.CompletedTask;
});
await Test("Release update HTTP results, rate limits and cancellation", async () =>
{
    using var client = new HttpClient(new UpdateResponseHandler());
    var update = await UpdateChecker.CheckAsync(client: client);
    Assert(update?.Tag == "v0.3.0", "successful response");
    var handler = new UpdateResponseHandler { Status = System.Net.HttpStatusCode.NotFound };
    using var missingClient = new HttpClient(handler);
    Assert(await UpdateChecker.CheckAsync(client: missingClient) is null, "no stable release");
    handler.Status = System.Net.HttpStatusCode.Forbidden;
    var limited = false;
    try { await UpdateChecker.CheckAsync(client: missingClient); }
    catch (HttpRequestException error) { limited = error.StatusCode == System.Net.HttpStatusCode.Forbidden; }
    Assert(limited, "rate limit surfaced");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var cancelled = false;
    try { await UpdateChecker.CheckAsync(cancellation.Token, client); }
    catch (OperationCanceledException) { cancelled = true; }
    Assert(cancelled, "cancelled request");
});
await Test("Configuration atomic save, backup, import, cycles", () =>
{
    var item = Worker("--worker sleep"); var config = new Configuration { Items = [item] };
    var store = new ConfigurationStore(Path.Combine(root, "config")); store.Save(config); item.Name = "renamed"; store.Save(config);
    Assert(store.Load().Items[0].Name == "renamed", "save/load");
    Assert(Directory.GetFiles(Path.Combine(store.Root, "backups")).Length == 1, "backup");
    var backup = ConfigurationStore.Read(Directory.GetFiles(Path.Combine(store.Root, "backups"))[0]); Assert(backup.Items[0].Name == "test", "backup content");
    var second = Worker("--worker sleep"); config.Items.Add(second); item.Dependencies.Add(second.Id); second.Dependencies.Add(item.Id);
    var threw = false; try { ConfigurationStore.Validate(config); } catch (InvalidDataException) { threw = true; }
    Assert(threw, "cycles must be rejected"); return Task.CompletedTask;
});
await Test("Theme preference compatibility, roundtrip, validation", () =>
{
    var legacy = JsonSerializer.Deserialize<Configuration>("{\"Preferences\":{\"LogDays\":7,\"LogLimitMiB\":200}}", ConfigurationStore.JsonOptions)!;
    ConfigurationStore.Validate(legacy); Assert(legacy.Preferences.Theme == "System", "legacy defaults to system");
    var store = new ConfigurationStore(Path.Combine(root, "themes"));
    foreach (var theme in new[] { "System", "Light", "Dark" })
    { legacy.Preferences.Theme = theme; store.Save(legacy); Assert(store.Load().Preferences.Theme == theme, "theme roundtrip"); }
    legacy.Preferences.Theme = "invalid";
    var threw = false; try { ConfigurationStore.Validate(legacy); } catch (InvalidDataException) { threw = true; }
    Assert(threw, "invalid theme rejected"); return Task.CompletedTask;
});
await Test("Folder configuration roundtrip, legacy, dissolve, validation", () =>
{
    var legacy = JsonSerializer.Deserialize<Configuration>("{}", ConfigurationStore.JsonOptions)!;
    ConfigurationStore.Validate(legacy); Assert(legacy.Folders.Count == 0, "legacy folders empty");
    var folder = new LaunchFolder { Name = "Services" }; var empty = new LaunchFolder { Name = "Empty" };
    var item = Worker("--worker sleep"); item.FolderId = folder.Id;
    var config = new Configuration { Folders = [folder, empty], Items = [item] };
    var store = new ConfigurationStore(Path.Combine(root, "folders")); store.Save(config);
    var loaded = store.Load(); Assert(loaded.Folders.Count == 2 && loaded.Items[0].FolderId == folder.Id, "folder roundtrip");
    loaded.DissolveFolder(folder.Id); store.Save(loaded);
    Assert(store.Load().Items[0].FolderId is null && loaded.Items[0].Id == item.Id, "dissolve keeps item");
    loaded.Items[0].FolderId = Guid.NewGuid();
    var threw = false; try { ConfigurationStore.Validate(loaded); } catch (InvalidDataException) { threw = true; }
    Assert(threw, "dangling folder rejected"); return Task.CompletedTask;
});
await Test("Folder start, stop isolation, dependencies, cancellation", async () =>
{
    var folder = new LaunchFolder { Name = "Group" }; var otherFolder = new LaunchFolder { Name = "Other" };
    var dependency = Worker("--worker sleep"); dependency.FolderId = otherFolder.Id;
    var first = Worker("--worker sleep"); first.FolderId = folder.Id; first.Dependencies.Add(dependency.Id);
    var second = Worker("--worker sleep"); second.FolderId = folder.Id;
    var unrelated = Worker("--worker sleep");
    var config = new Configuration { Folders = [folder, otherFolder], Items = [first, second, dependency, unrelated] };
    await using var manager = new ProcessManager(config, root, Signal);
    await manager.StartFolderAsync(folder.Id);
    Assert(manager.Snapshot(first.Id).Active && manager.Snapshot(second.Id).Active && manager.Snapshot(dependency.Id).Active, "members and explicit dependency start");
    Assert(!manager.Snapshot(unrelated.Id).Active, "unrelated not started");
    await manager.StopFolderAsync(folder.Id);
    Assert(!manager.Snapshot(first.Id).Active && !manager.Snapshot(second.Id).Active && manager.Snapshot(dependency.Id).Active, "stop only folder members");
    first.Dependencies.Clear(); first.DelaySeconds = 5;
    using var cancellation = new CancellationTokenSource();
    var pending = manager.StartFolderAsync(folder.Id, cancellation.Token);
    await Until(() => manager.Snapshot(first.Id).Active); cancellation.Cancel();
    await manager.StopFolderAsync(folder.Id);
    var canceled = false; try { await pending; } catch (OperationCanceledException) { canceled = true; }
    Assert(canceled && !manager.Snapshot(first.Id).Active && !manager.Snapshot(second.Id).Active, "cancel pending batch without later launch");
    config.DissolveFolder(folder.Id); Assert(config.Items.Count == 4 && manager.Snapshot(dependency.Id).Active, "dissolve never stops other processes");
});
await Test("Dual-stream flood, split UTF-8, unterminated tail, export", async () =>
{
    var item = Worker("--worker flood"); var config = new Configuration { Items = [item] };
    await using var manager = new ProcessManager(config, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => !manager.Snapshot(item.Id).Active, 45);
    Assert(manager.Snapshot(item.Id).ExitCode == 0, "exit code");
    var records = Directory.GetFiles(manager.Logs(item.Id).DirectoryPath, "*.jsonl").Order()
        .SelectMany(ReadLog).Select(x => JsonSerializer.Deserialize<LogRecord>(x)!).ToArray();
    var expectedOut = new StringBuilder(); var expectedErr = new StringBuilder();
    for (var i = 0; i < 12000; i++) { expectedOut.Append($"OUT:{i}:中文🙂:{new string('x', 64)}\n"); expectedErr.Append($"ERR:{i}:错误:{new string('y', 64)}\n"); }
    expectedOut.Append("尾部🙂无换行"); expectedErr.Append("stderr-tail");
    Assert(string.Concat(records.Where(x => x.Channel == "stdout").Select(x => x.Text)) == expectedOut.ToString(), "stdout exact match");
    Assert(string.Concat(records.Where(x => x.Channel == "stderr").Select(x => x.Text)) == expectedErr.ToString(), "stderr exact match");
    Assert(records.Where(x => x.Channel == "stdout").SelectMany(x => x.RawBytes ?? []).SequenceEqual(Encoding.UTF8.GetBytes(expectedOut.ToString())), "stdout raw bytes exact match");
    var export = Path.Combine(root, "export.txt"); manager.Logs(item.Id).Export(export);
    var expectedExport = string.Concat(records.Select(record =>
        $"[{record.Time:yyyy-MM-dd HH:mm:ss.fff}] [{record.Channel}] " + record.Text
        + (record.Text.EndsWith('\n') ? "" : Environment.NewLine)));
    Assert(File.ReadAllText(export) == expectedExport, "export preserves every record, including split tail");
    manager.Logs(item.Id).ClearPanel(); Assert(manager.Logs(item.Id).Snapshot().Length == 0, "clear panel");
    Assert(Directory.GetFiles(manager.Logs(item.Id).DirectoryPath).Length > 0, "clear retains disk history");
    var stopping = Worker("--worker flood-stop"); stopping.StopInput = "exit"; stopping.StopTimeoutSeconds = 15;
    await using var stopManager = new ProcessManager(new() { Items = [stopping] }, root, Signal);
    await stopManager.StartAsync(stopping.Id);
    await stopManager.StopAsync(stopping.Id);
    var stoppedRecords = Directory.GetFiles(stopManager.Logs(stopping.Id).DirectoryPath, "*.jsonl").Order()
        .SelectMany(ReadLog).Select(x => JsonSerializer.Deserialize<LogRecord>(x)!).ToArray();
    Assert(stopManager.Snapshot(stopping.Id).ExitCode == 0, "stop while producing output exits gracefully");
    Assert(string.Concat(stoppedRecords.Where(x => x.Channel == "stdout").Select(x => x.Text)) == expectedOut.ToString(), "stop drains stdout and decoder tail");
    Assert(string.Concat(stoppedRecords.Where(x => x.Channel == "stderr").Select(x => x.Text)) == expectedErr.ToString(), "stop drains stderr");
    Assert(stoppedRecords.Where(x => x.Channel == "stdout").SelectMany(x => x.RawBytes ?? []).SequenceEqual(Encoding.UTF8.GetBytes(expectedOut.ToString())), "stop retains raw bytes");
});
await Test("Stop input delay and notes compatibility, validation, roundtrip", () =>
{
    var legacy = JsonSerializer.Deserialize<LaunchItem>("{}")!;
    Assert(legacy.Note == "" && legacy.StopInputDelaySeconds == 0 && !legacy.SendStopSignalBeforeInput, "legacy behavior");
    var item = Worker("--worker input"); item.Note = "备注 / note"; item.StopInputDelaySeconds = 2; item.SendStopSignalBeforeInput = true;
    var copy = ConfigurationStore.Clone(item);
    Assert(copy.Note == item.Note && copy.StopInputDelaySeconds == 2 && copy.SendStopSignalBeforeInput, "roundtrip");
    foreach (var delay in new[] { -1, 301 })
    {
        copy.StopInputDelaySeconds = delay;
        try { ConfigurationStore.Validate(new() { Items = [copy] }); throw new Exception("invalid delay accepted"); } catch (InvalidDataException) { }
    }
    copy.StopInputDelaySeconds = 0; copy.Note = new string('x', 1001);
    try { ConfigurationStore.Validate(new() { Items = [copy] }); throw new Exception("long note accepted"); } catch (InvalidDataException) { }
    return Task.CompletedTask;
});
await Test("Signal then delayed stop input, timeout starts after input", async () =>
{
    var marker = Path.Combine(root, "stop-signal");
    var item = Worker("--worker stop-prompt " + LaunchCommand.Quote(marker));
    item.StopInput = "Y"; item.StopInputDelaySeconds = 2; item.SendStopSignalBeforeInput = true;
    var signals = 0;
    await using var manager = new ProcessManager(new() { Items = [item] }, root, _ => { signals++; File.WriteAllText(marker, "signal"); return Task.CompletedTask; });
    await manager.StartAsync(item.Id); await Until(() => manager.Logs(item.Id).Snapshot().Any(x => x.Text.Contains("READY")));
    var started = DateTimeOffset.Now;
    await manager.StopAsync(item.Id);
    var records = manager.Logs(item.Id).Snapshot();
    Assert(signals == 1 && records.Any(x => x.Channel == "stdout" && x.Text.Contains("GOT:Y")), "signal and response");
    Assert(records.Single(x => x.Channel == "stdin").Time - started >= TimeSpan.FromSeconds(1.9), "not sent early");
    Assert(manager.Snapshot(item.Id).ExitCode == 0 && !records.Any(x => x.Text.Contains("优雅停止超时")), "delay does not exhaust stop timeout");
});
await Test("Stop delay ends early on process exit without sending input", async () =>
{
    var marker = Path.Combine(root, "stop-early-exit");
    var item = Worker("--worker stop-prompt " + LaunchCommand.Quote(marker) + " exit");
    item.StopInput = "Y"; item.StopInputDelaySeconds = 10; item.SendStopSignalBeforeInput = true;
    await using var manager = new ProcessManager(new() { Items = [item] }, root, _ => { File.WriteAllText(marker, "signal"); return Task.CompletedTask; });
    await manager.StartAsync(item.Id); await Until(() => manager.Logs(item.Id).Snapshot().Any(x => x.Text.Contains("READY")));
    var watch = Stopwatch.StartNew(); await manager.StopAsync(item.Id);
    Assert(watch.Elapsed.TotalSeconds < 5 && manager.Snapshot(item.Id).ExitCode == 0, "early exit skips delay");
    Assert(!manager.Logs(item.Id).Snapshot().Any(x => x.Channel == "stdin"), "no late input");
});
await Test("Interactive input, environment, working directory", async () =>
{
    var item = Worker("--worker input"); await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => manager.Logs(item.Id).Snapshot().Any(x => x.Text.Contains("READY")));
    await manager.SendInputAsync(item.Id, "你好🙂"); await Until(() => !manager.Snapshot(item.Id).Active);
    Assert(string.Concat(manager.Logs(item.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)) == "READYGOT:你好🙂", "input exact match");
    var env = Worker("--worker env"); env.Environment["LAUNCHDESK_TEST"] = "abc=中文"; env.WorkingDirectory = root;
    await using var manager2 = new ProcessManager(new() { Items = [env] }, root, Signal);
    await manager2.StartAsync(env.Id); await Until(() => !manager2.Snapshot(env.Id).Active);
    Assert(string.Concat(manager2.Logs(env.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)) == "abc=中文|" + root, "environment and cwd");
    var gbk = Worker("--worker gbk"); gbk.Encoding = "gbk";
    await using var manager3 = new ProcessManager(new() { Items = [gbk] }, root, Signal);
    await manager3.StartAsync(gbk.Id); await Until(() => !manager3.Snapshot(gbk.Id).Active);
    Assert(string.Concat(manager3.Logs(gbk.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)) == "中文GBK尾部", "GBK decoding");
});
await Test("BAT relative path, paths with spaces, pause input", async () =>
{
    var folder = Path.Combine(root, "batch folder"); Directory.CreateDirectory(folder);
    await File.WriteAllTextAsync(Path.Combine(folder, "marker.txt"), "relative-marker");
    var script = Path.Combine(folder, "launch.cmd"); await File.WriteAllTextAsync(script, "@echo off\r\nchcp 65001 >nul\r\ntype .\\marker.txt\r\necho ARG:%~1\r\npause\r\necho DONE\r\n");
    var item = new LaunchItem { Name = "bat", Kind = LaunchKind.Batch, Target = script, Arguments = "\"hello world\"", StopTimeoutSeconds = 1 };
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => string.Concat(manager.Logs(item.Id).Snapshot().Select(x => x.Text)).Contains("ARG:hello world"));
    await manager.SendInputAsync(item.Id, ""); await Until(() => !manager.Snapshot(item.Id).Active);
    Assert(string.Concat(manager.Logs(item.Id).Snapshot().Select(x => x.Text)).Contains("DONE"), "pause completion");
});
await Test("npm run start in isolated project", async () =>
{
    var folder = Path.Combine(root, "npm sample"); Directory.CreateDirectory(folder);
    await File.WriteAllTextAsync(Path.Combine(folder, "package.json"), "{\"private\":true,\"scripts\":{\"start\":\"node -e \\\"process.stdout.write('npm-ok');process.stderr.write('npm-err')\\\"\"}}");
    var item = new LaunchItem { Name = "npm", Kind = LaunchKind.Command, Target = "npm", Arguments = "run start", WorkingDirectory = folder };
    // Keep this process fixture offline; npm's update notifier can outlive the test script.
    item.Environment["NPM_CONFIG_UPDATE_NOTIFIER"] = "false";
    item.Environment["NPM_CONFIG_AUDIT"] = "false";
    item.Environment["NPM_CONFIG_FUND"] = "false";
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id);
    try { await Until(() => !manager.Snapshot(item.Id).Active, 30); }
    catch (TimeoutException error)
    {
        throw new TimeoutException("npm fixture did not exit: " + JsonSerializer.Serialize(manager.Snapshot(item.Id))
            + "\n" + string.Concat(manager.Logs(item.Id).Snapshot().Select(x => x.Text)), error);
    }
    Assert(manager.Snapshot(item.Id).ExitCode == 0, "npm exit");
    var all = manager.Logs(item.Id).Snapshot(); Assert(string.Concat(all.Where(x => x.Channel == "stdout").Select(x => x.Text)).Contains("npm-ok"), "npm stdout");
    Assert(string.Concat(all.Where(x => x.Channel == "stderr").Select(x => x.Text)).Contains("npm-err"), "npm stderr");
});
await Test("Process tree stop, restart, repeated launches", async () =>
{
    var marker = Path.Combine(root, "tree.txt"); var item = Worker("--worker spawn " + LaunchCommand.Quote(marker));
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    for (var i = 0; i < 3; i++)
    {
        if (File.Exists(marker)) File.Delete(marker);
        await manager.StartAsync(item.Id); await Until(() => File.Exists(marker));
        var pids = File.ReadAllText(marker).Split(',').Select(int.Parse).ToArray();
        await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(8));
        await Until(() => pids.All(pid => !Alive(pid))); Assert(manager.Snapshot(item.Id).Status == RunStatus.Stopped, "stop state");
    }
});
await Test("Root exit preserves tracked child until stop", async () =>
{
    var marker = Path.Combine(root, "detached.txt"); var item = Worker("--worker detach " + LaunchCommand.Quote(marker));
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => File.Exists(marker)); var pid = int.Parse(File.ReadAllText(marker));
    await Task.Delay(200); Assert(Alive(pid) && manager.Snapshot(item.Id).Active, "child remains tracked");
    await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(8)); await Until(() => !Alive(pid));
});
await Test("Owner forced termination kills root and grandchild", async () =>
{
    var marker = Path.Combine(root, "owner.txt"); using var owner = Process.Start(Self("--owner", marker))!;
    try
    {
        await Until(() => File.Exists(marker + ".owner")); var pids = File.ReadAllText(marker).Split(',').Select(int.Parse).ToArray();
        owner.Kill(); await owner.WaitForExitAsync(); await Until(() => pids.All(pid => !Alive(pid)));
    }
    finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
});
await Test("Dependency order, pending startup cancellation", async () =>
{
    var a = Worker("--worker sleep"); a.Name = "dependency"; var b = Worker("--worker sleep"); b.Dependencies.Add(a.Id);
    await using var manager = new ProcessManager(new() { Items = [b, a] }, root, Signal);
    await manager.StartAsync(b.Id); Assert(manager.Snapshot(a.Id).Active && manager.Snapshot(b.Id).Active, "dependencies");
    await manager.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(10));
    a.DelaySeconds = 10; var pending = manager.StartAsync(a.Id); await Task.Delay(100); await manager.StopAsync(a.Id);
    try { await pending; } catch (TaskCanceledException) { }
    Assert(!manager.Snapshot(a.Id).Active, "cancel pending");
});
await Test("Startup failure surfaces and log failure kills process", async () =>
{
    var item = Worker("--worker sleep"); item.Target = Path.Combine(root, "does-not-exist.exe");
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    var failed = false; try { await manager.StartAsync(item.Id); } catch (FileNotFoundException) { failed = true; }
    await Until(() => !manager.Snapshot(item.Id).Active); Assert(failed && manager.Snapshot(item.Id).Status == RunStatus.Failed, "startup failure");
    var blocked = Worker("--worker sleep"); var logsRoot = Path.Combine(root, "blocked"); Directory.CreateDirectory(logsRoot); File.WriteAllText(Path.Combine(logsRoot, "logs"), "not a directory");
    await using var manager2 = new ProcessManager(new() { Items = [blocked] }, logsRoot, Signal);
    try { await manager2.StartAsync(blocked.Id); } catch (IOException) { }
    await Until(() => !manager2.Snapshot(blocked.Id).Active); Assert(manager2.Snapshot(blocked.Id).Status == RunStatus.Failed, "disk failure status");
});
await Test("Log retention, ANSI display, persistent history", () =>
{
    var id = Guid.NewGuid(); using var logs = new LogStore(root, id, new() { LogLimitMiB = 10, LogDays = 1 });
    Directory.CreateDirectory(logs.DirectoryPath); var old = Path.Combine(logs.DirectoryPath, "old.jsonl"); File.WriteAllText(old, "old"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
    logs.Write("stdout", "\x1b[31mred\x1b[0m"); Assert(!File.Exists(old), "age retention"); Assert(LogStore.DisplayText(logs.Snapshot()[0].Text) == "red", "ANSI rendering");
    logs.Dispose(); using var restored = new LogStore(root, id, new()); restored.LoadRecent(); Assert(restored.Snapshot().Length == 1, "load history"); return Task.CompletedTask;
});
await Test("Concurrent starts, bulk cancellation, blocked input stop", async () =>
{
    var item = Worker("--worker sleep"); await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await Task.WhenAll(manager.StartAsync(item.Id), manager.StartAsync(item.Id));
    var blocked = manager.SendInputAsync(item.Id, new string('x', 100_000)); await Task.Delay(100);
    await manager.StopAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(8));
    try { await blocked.WaitAsync(TimeSpan.FromSeconds(3)); } catch (IOException) { }
    Assert(!manager.Snapshot(item.Id).Active, "blocked input must not prevent stop");
    item.DelaySeconds = 10; var other = Worker("--worker sleep"); manager.Config.Items.Add(other);
    var bulk = manager.StartAllAsync(); await Task.Delay(150); await manager.StopAllAsync(); await bulk;
    Assert(!manager.Snapshot(item.Id).Active && !manager.Snapshot(other.Id).Active, "bulk stop cancels queued launches");
});
await Test("Auto restart stops after five consecutive exits", async () =>
{
    var item = Worker("--worker fail"); item.AutoRestart = true;
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => !manager.Snapshot(item.Id).Active, 45);
    Assert(manager.Snapshot(item.Id).Detail.Contains("5"), "restart cap");
    Assert(manager.Logs(item.Id).Snapshot().Count(x => x.Channel == "system" && x.Text.StartsWith("启动 PID")) == 5, "exactly five launches");
});
await Test("PowerShell and Git Bash isolated scripts", async () =>
{
    var psPath = Path.Combine(root, "worker.ps1");
    await File.WriteAllTextAsync(psPath, "param([string]$Name)\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n[Console]::Write('ps-' + $Name)\n");
    var item = new LaunchItem { Name = "ps", Kind = LaunchKind.PowerShell, Target = psPath, Interpreter = "powershell.exe", Arguments = "-Name 中文", WorkingDirectory = root };
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => !manager.Snapshot(item.Id).Active);
    Assert(manager.Snapshot(item.Id).ExitCode == 0, "PowerShell exit");
    Assert(string.Concat(manager.Logs(item.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)) == "ps-中文", "PowerShell text");
    var git = LaunchCommand.Resolve("git.exe", root); var bash = Path.Combine(Directory.GetParent(Path.GetDirectoryName(git)!)!.FullName, "bin", "bash.exe");
    if (!File.Exists(bash)) bash = Path.Combine(Path.GetDirectoryName(git)!, "bash.exe");
    var shPath = Path.Combine(root, "worker.sh"); await File.WriteAllTextAsync(shPath, "printf 'shell-ok\\n'\n");
    var shell = new LaunchItem { Name = "shell", Kind = LaunchKind.Shell, Target = shPath, Interpreter = bash, WorkingDirectory = root };
    await using var manager2 = new ProcessManager(new() { Items = [shell] }, root, Signal);
    await manager2.StartAsync(shell.Id); await Until(() => !manager2.Snapshot(shell.Id).Active);
    Assert(manager2.Snapshot(shell.Id).ExitCode == 0, "Shell exit");
    Assert(string.Concat(manager2.Logs(shell.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text)).Contains("shell-ok"), "Shell text");
});
var reportPath = args.Length >= 2 && args[0] == "--report" ? args[1] : Path.Combine(root, "results.json");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
await TestEvidence.WriteAsync(reportPath, startedAt, filter);
Console.WriteLine("Evidence: " + root);
Console.WriteLine($"Summary: {results.Count(x => x.status == "passed")} passed, {results.Count(x => x.status == "failed")} failed, {results.Count(x => x.status == "skipped")} skipped");
return results.Count == 0 ? 2 : results.Any(x => x.status == "failed") || (args.Contains("--fail-on-skip") && results.Any(x => x.status == "skipped")) ? 1 : 0;

sealed class UpdateResponseHandler : HttpMessageHandler
{
    public System.Net.HttpStatusCode Status { get; set; } = System.Net.HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RequestUri?.AbsoluteUri != "https://api.github.com/repos/hailaij/LaunchDesk/releases/latest"
            || request.Headers.UserAgent.Count == 0) throw new InvalidOperationException("Unexpected update request");
        return Task.FromResult(new HttpResponseMessage(Status)
        { Content = new StringContent("""{"tag_name":"v0.3.0","draft":false,"prerelease":false,"body":"Release notes"}""") });
    }
}

sealed class TestSignatureVerifier : IUpdateSignatureVerifier
{
    public int Calls { get; private set; }
    public bool Reject { get; init; }
    public string Verify(string path) { Calls++; if (Reject) throw new InvalidDataException("test signature rejection"); return "isolated test publisher"; }
}
sealed class DownloadResponseHandler(byte[] bytes, string mode = "") : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; cancellationToken.ThrowIfCancellationRequested();
        if (mode == "network") throw new HttpRequestException("isolated network failure");
        if (mode == "redirect")
        {
            var redirect = new HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://evil.example/installer.exe"); return Task.FromResult(redirect);
        }
        var payload = mode == "truncated" ? bytes[..^1] : mode == "oversized" ? bytes.Concat(new byte[] { 0 }).ToArray() : bytes;
        HttpContent content = mode == "midstream" ? new StreamContent(new InterruptedStream(bytes)) : new ByteArrayContent(payload);
        content.Headers.ContentLength = payload.Length;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
    }
}
sealed class InterruptedStream : MemoryStream
{
    private bool read;
    private readonly int chunkSize;
    public InterruptedStream(byte[] bytes) : base(bytes) { chunkSize = bytes.Length / 2; }
    public override bool CanSeek => false;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (read) throw new IOException("isolated interruption after partial write");
        read = true; return base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    }
}
