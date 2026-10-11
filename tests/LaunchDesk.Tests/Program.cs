using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
if (args.Length > 0 && args[0] != "--report")
{
    if (args[0] == "--signal") { var sent = NativeProcess.SendControlSignal(int.Parse(args[1])); Thread.Sleep(100); return sent ? 0 : 1; }
    if (args[0] == "--worker")
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        switch (args[1])
        {
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
                return 0;
            case "input": Console.Write("READY"); Console.Out.Flush(); var input = Console.ReadLine(); Console.Write("GOT:" + input); return 0;
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
var results = new List<object>();
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
    var time = Stopwatch.StartNew();
    try { await action(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true, elapsedMs = time.ElapsedMilliseconds }); }
    catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error); results.Add(new { name, passed = false, error = error.ToString() }); }
}
async Task Signal(int pid)
{ using var helper = Process.Start(Self("--signal", pid.ToString()))!; await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }

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
    Assert(File.ReadAllText(export).Contains("尾部🙂无换行"), "export tail");
    manager.Logs(item.Id).ClearPanel(); Assert(manager.Logs(item.Id).Snapshot().Length == 0, "clear panel");
    Assert(Directory.GetFiles(manager.Logs(item.Id).DirectoryPath).Length > 0, "clear retains disk history");
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
    await using var manager = new ProcessManager(new() { Items = [item] }, root, Signal);
    await manager.StartAsync(item.Id); await Until(() => !manager.Snapshot(item.Id).Active);
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
var reportPath = args.Length == 2 && args[0] == "--report" ? args[1] : Path.Combine(root, "results.json");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Evidence: " + root);
return results.Any(x => x.GetType().GetProperty("passed")?.GetValue(x) is false) ? 1 : 0;
