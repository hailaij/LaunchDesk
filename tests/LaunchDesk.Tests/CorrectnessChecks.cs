using System.Reflection;
using System.Text.Json;
using LaunchDesk.Core;

internal static class CorrectnessChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test, Func<string, LaunchItem> worker)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task<string> Output(LaunchItem item)
        {
            await using var manager = new ProcessManager(new() { Items = [item] }, root);
            await manager.StartAsync(item.Id);
            for (var i = 0; i < 200 && manager.Snapshot(item.Id).Active; i++) await Task.Delay(20);
            Check(!manager.Snapshot(item.Id).Active && manager.Snapshot(item.Id).ExitCode == 0, "Worker failed or did not exit");
            return string.Concat(manager.Logs(item.Id).Snapshot().Where(x => x.Channel == "stdout").Select(x => x.Text));
        }
        await test("Correctness: native argument roundtrip preserves quotes and trailing slashes", async () =>
        {
            var values = new[] { @"C:\", @"C:\space dir\", "", "a\"b", "a\\\"b", "中文 x", @"\\server\share\" };
            var item = worker("--worker args " + string.Join(' ', values.Select(LaunchCommand.Quote)));
            var parsed = JsonSerializer.Deserialize<string[]>(await Output(item))!;
            Check(values.SequenceEqual(parsed), "Windows argument quoting corrupts values");
        });
        await test("Correctness: spaced relative command uses configured working directory", async () =>
        {
            var dir = Path.Combine(root, "relative command"); Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "my worker.cmd"), "@echo relative-ok\r\n");
            var item = new LaunchItem { Name = "relative", Kind = LaunchKind.Command, Target = "my worker.cmd", WorkingDirectory = dir };
            LaunchPreflight.Check(item);
            Check((await Output(item)).Contains("relative-ok"), "Relative command not quoted in its working directory");
        });
        await test("Correctness: malformed npm package reports exact file and exception type", () =>
        {
            var dir = Path.Combine(root, "bad npm"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "package.json"); File.WriteAllText(path, "{");
            try { LaunchPreflight.Check(new() { Name = "bad", Kind = LaunchKind.Command, Target = "npm run start", WorkingDirectory = dir }); }
            catch (InvalidDataException error) { Check(error.Message.Contains(path) && error.InnerException is JsonException, "Missing JSON context"); return Task.CompletedTask; }
            throw new Exception("Malformed package not rejected with context");
        });
        await test("Correctness: removed item retires runtime and closes writer without deleting history", async () =>
        {
            var item = worker("--worker args resource"); var config = new Configuration { Items = [item] };
            await using var manager = new ProcessManager(config, root);
            await manager.StartAsync(item.Id); await manager.StopAsync(item.Id);
            var logs = manager.Logs(item.Id); config.Items.Remove(item);
            var remove = typeof(ProcessManager).GetMethod("RemoveRuntimeAsync");
            Check(remove is not null, "No runtime retirement path");
            await (Task)remove!.Invoke(manager, [item.Id])!;
            var registry = (System.Collections.IDictionary)typeof(ProcessManager).GetField("runtimes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            Check(registry.Count == 0, "Deleted runtime remains registered");
            foreach (var path in Directory.GetFiles(logs.DirectoryPath))
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            try { logs.Write("stdout", "resurrect"); throw new Exception("Retired store reopened"); } catch (ObjectDisposedException) { }
            Check(Directory.GetFiles(logs.DirectoryPath).Length > 0, "History removed");
        });
        await test("Correctness: manager force termination distinguishable from application exit 1", async () =>
        {
            var item = worker("--worker sleep");
            await using var manager = new ProcessManager(new() { Items = [item] }, root);
            await manager.StartAsync(item.Id); await manager.StopAsync(item.Id);
            Check(manager.Snapshot(item.Id).ExitCode is < 0, "Forced termination still uses ambiguous exit code 1");
        });
        await test("Correctness: corrupt log line skipped and valid export remains complete", () =>
        {
            using var logs = new LogStore(root, Guid.NewGuid(), new()); Directory.CreateDirectory(logs.DirectoryPath);
            File.WriteAllText(Path.Combine(logs.DirectoryPath, "broken.jsonl"),
                JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stdout", "before\n")) + "\n{broken\n" +
                JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stdout", "after\n")) + "\n");
            var export = Path.Combine(root, "corrupt-export.txt"); logs.Export(export);
            Check(File.ReadAllText(export).Contains("before") && File.ReadAllText(export).Contains("after"), "Valid records lost");
            return Task.CompletedTask;
        });
        await test("Correctness: export failure preserves existing destination", () =>
        {
            using var logs = new LogStore(root, Guid.NewGuid(), new()); Directory.CreateDirectory(logs.DirectoryPath);
            File.WriteAllText(Path.Combine(logs.DirectoryPath, "held.jsonl"), JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stdout", "new\n")) + "\n");
            var export = Path.Combine(root, "preserved-export.txt"); File.WriteAllText(export, "original");
            using var held = new FileStream(Directory.GetFiles(logs.DirectoryPath).Single(), FileMode.Open, FileAccess.Read, FileShare.None);
            try { logs.Export(export); throw new Exception("Locked input exported"); } catch (IOException) { }
            Check(File.ReadAllText(export) == "original", "Failed export truncated existing destination");
            return Task.CompletedTask;
        });
        await test("Correctness: literal cmd resources with ampersands and parentheses", async () =>
        {
            var dir = Path.Combine(root, "literal (commands)"); Directory.CreateDirectory(dir);
            var target = "my & worker.cmd"; File.WriteAllText(Path.Combine(dir, target), "@echo special-path-ok\r\n");
            Check((await Output(new() { Name = "literal", Kind = LaunchKind.Command, Target = target, WorkingDirectory = dir })).Contains("special-path-ok"), "cmd resource path interpreted as operators");
            Check((await Output(new() { Name = "expression", Kind = LaunchKind.Command, Target = "echo first & echo second", WorkingDirectory = dir })).Contains("second"), "Raw shell expression was rewritten");
        });
        await test("Correctness: native creation failures do not leak handles", () =>
        {
            var path = Path.Combine(root, "not-an-executable.exe"); File.WriteAllText(path, "invalid PE");
            void Fail()
            {
                try { using var native = new NativeProcess(new() { Kind = LaunchKind.Executable, Target = path }); throw new Exception("Invalid image started"); }
                catch (System.ComponentModel.Win32Exception error) { Check(error.NativeErrorCode is 193 or 216, "Unexpected native failure: " + error.NativeErrorCode + " " + error.Message); }
            }
            for (var i = 0; i < 5; i++) Fail();
            using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh(); var before = process.HandleCount;
            for (var i = 0; i < 30; i++) Fail();
            process.Refresh(); Check(process.HandleCount - before < 12, $"Native failure path leaks job/pipe handles: before={before}, after={process.HandleCount}"); return Task.CompletedTask;
        });
        await test("Correctness: removal racing startup cannot recreate deleted runtime", async () =>
        {
            var item = worker("--worker sleep"); item.DelaySeconds = 1;
            var config = new Configuration { Items = [item] };
            await using var manager = new ProcessManager(config, root);
            var starting = manager.StartAsync(item.Id); await Task.Delay(50); config.Items.Remove(item);
            await manager.RemoveRuntimeAsync(item.Id).WaitAsync(TimeSpan.FromSeconds(5));
            try { await starting; throw new Exception("Retired startup completed"); } catch (OperationCanceledException) { }
            try { manager.Logs(item.Id); throw new Exception("Deleted runtime resurrected"); } catch (KeyNotFoundException) { }
            await manager.RemoveRuntimeAsync(item.Id);
        });
        await test("Correctness: manager disposal stops orphaned config entries and is awaitably idempotent", async () =>
        {
            var item = worker("--worker sleep"); var config = new Configuration { Items = [item] };
            var manager = new ProcessManager(config, root); await manager.StartAsync(item.Id);
            var pid = manager.Snapshot(item.Id).Pid!.Value; config.Items.Clear();
            var first = manager.DisposeAsync().AsTask(); var second = manager.DisposeAsync().AsTask();
            Check(ReferenceEquals(first, second), "Repeated disposal detaches from cleanup");
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            try { using var alive = System.Diagnostics.Process.GetProcessById(pid); Check(alive.HasExited, "Orphaned runtime survived disposal"); } catch (ArgumentException) { }
        });
        await test("Correctness: shutdown keeps graceful stdin path working", async () =>
        {
            var item = worker("--worker input"); item.StopInput = "quit";
            var manager = new ProcessManager(new() { Items = [item] }, root); await manager.StartAsync(item.Id);
            var logs = manager.Logs(item.Id); var dir = logs.DirectoryPath;
            await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var records = Directory.GetFiles(dir).SelectMany(File.ReadLines).Select(x => JsonSerializer.Deserialize<LogRecord>(x)!).ToArray();
            Check(records.Any(x => x.Text.Contains("GOT:quit")) && !records.Any(x => x.Text.Contains("优雅停止超时")), "Shutdown rejected graceful stop input");
        });
        await test("Correctness: malformed root package and corrupt export records handled consistently", () =>
        {
            var dir = Path.Combine(root, "npm root array"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "package.json"), "[]");
            try { LaunchPreflight.Check(new() { Kind = LaunchKind.Command, Target = "npm start", WorkingDirectory = dir }); throw new Exception("Array root accepted"); }
            catch (InvalidDataException error) { Check(error.Message.Contains(Path.Combine(dir, "package.json")), "Missing root file path"); }
            using var logs = new LogStore(root, Guid.NewGuid(), new()); Directory.CreateDirectory(logs.DirectoryPath);
            File.WriteAllText(Path.Combine(logs.DirectoryPath, "partial.jsonl"), "null\n{}\n" + JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stderr", "valid")) + "\n{partial");
            var path = Path.Combine(root, "valid-merged.jsonl"); File.WriteAllText(path, "prior");
            LogStore.ExportMerged([(Guid.NewGuid(), "source", logs)], path, out var skipped);
            Check(skipped == 3 && File.ReadLines(path).Count() == 1 && File.ReadAllText(path).Contains("valid"), "Corrupt merged counts or JSONL output wrong");
            logs.LoadRecent(); Check(logs.Snapshot().Single().Text == "valid", "Corrupt history crashes recent-log loading");
            Check(!Directory.GetFiles(root, "*.tmp").Any(), "Export temp leaked"); return Task.CompletedTask;
        });
        await test("Correctness: diagnostics bounded and write failure never masks source error", () =>
        {
            var dir = Path.Combine(root, "diagnostics-check"); var diagnostics = new ErrorDiagnostics(dir);
            Check(diagnostics.Write("fixture", new IOException("original failure")), "Diagnostics missing");
            File.WriteAllText(diagnostics.PathName, new string('x', 2 * 1024 * 1024 + 1));
            Check(diagnostics.Write("rotate", new InvalidOperationException("next")) && File.Exists(diagnostics.PathName + ".1"), "Diagnostics not rotated");
            using var held = new FileStream(diagnostics.PathName, FileMode.Open, FileAccess.Read, FileShare.None);
            Check(!diagnostics.Write("blocked", new IOException("source")), "Locked diagnostics pretends success"); return Task.CompletedTask;
        });
        await test("Correctness: readiness keyword survives surrogate split and bounded tail", async () =>
        {
            var type = typeof(ProcessManager).Assembly.GetType("LaunchDesk.Core.ReadinessMonitor")!;
            var monitor = Activator.CreateInstance(type, new ReadinessOptions { Kind = ReadinessKind.Log, Keyword = "🙂READY", TimeoutSeconds = 1 })!;
            var observe = type.GetMethod("Observe")!;
            observe.Invoke(monitor, ["stdout", new string('x', 10000) + "\ud83d"]);
            observe.Invoke(monitor, ["stdout", "\ude42REA"]); observe.Invoke(monitor, ["stdout", "DY"]);
            await ((Task)type.GetMethod("WaitAsync")!.Invoke(monitor, [new TaskCompletionSource().Task, CancellationToken.None])!).WaitAsync(TimeSpan.FromSeconds(2));
        });
        await test("Correctness: repeated runtime retirement does not retain deleted entries", async () =>
        {
            var config = new Configuration(); await using var manager = new ProcessManager(config, root);
            for (var i = 0; i < 50; i++)
            {
                var item = worker("--worker args"); config.Items.Add(item);
                var logs = manager.Logs(item.Id); logs.Write("stdout", "retire " + i); config.Items.Remove(item);
                await manager.RemoveRuntimeAsync(item.Id);
                foreach (var path in Directory.GetFiles(logs.DirectoryPath)) using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            var registry = (System.Collections.IDictionary)typeof(ProcessManager).GetField("runtimes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            Check(registry.Count == 0, "Retired entries accumulate");
        });
        await test("Correctness: console signal failure includes native operation and error code", () =>
        {
            Check(!NativeProcess.SendControlSignal(int.MaxValue, out var detail) && detail.Contains("AttachConsole Win32="), "Console failure context erased"); return Task.CompletedTask;
        });
        await test("Correctness: HTTP redirect is not readiness success", async () =>
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var item = worker("--worker ready-server " + port + " 302");
            item.Readiness = new() { Kind = ReadinessKind.Http, Url = "http://127.0.0.1:" + port + "/", TimeoutSeconds = 2 };
            await using var manager = new ProcessManager(new() { Items = [item] }, root);
            try { await manager.StartAsync(item.Id); throw new Exception("302 reported ready"); } catch (TimeoutException) { }
            await manager.StopAsync(item.Id); Check(!manager.Snapshot(item.Id).Active, "Redirect timeout leaves worker alive");
        });
    }
}
