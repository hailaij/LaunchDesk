using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LaunchDesk.Core;

internal static class ProductChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test, Func<string, LaunchItem> worker, Func<string[], ProcessStartInfo> self)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Until(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
        string Pipe() => "LaunchDesk-product-test-" + Guid.NewGuid().ToString("N");
        await test("Product: local pipe identity queries release native handles", () =>
        {
            var expected = LocalControl.PipeName;
            GC.Collect(); GC.WaitForPendingFinalizers();
            using var process = Process.GetCurrentProcess(); var before = process.HandleCount;
            for (var i = 0; i < 200; i++) Check(LocalControl.PipeName == expected, "Pipe identity changed");
            process.Refresh();
            Check(process.HandleCount - before < 20, "Pipe identity query leaked native handles");
            return Task.CompletedTask;
        });
        await test("Product: CLI parser JSON output and stable exit codes", async () =>
        {
            var calls = 0;
            Task<ControlResponse> Send(ControlRequest request, TimeSpan timeout)
            { calls++; Check(request.Target == "中文 服务" && timeout.TotalSeconds == 9, "CLI arguments changed"); return Task.FromResult(new ControlResponse(true, "OK", [])); }
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await ControlCli.RunAsync(["start", "中文 服务", "--json", "--timeout", "9"], output, error, Send) == 0, "CLI success code");
            Check(JsonSerializer.Deserialize<ControlResponse>(output.ToString(), ConfigurationStore.JsonOptions)!.Success, "JSON invalid");
            foreach (var args in new string[][] { ["start"], ["start", "x", "--all"], ["status", "--timeout", "0"], ["delete", "x"], ["start", "x", "--bad"] })
                Check(await ControlCli.RunAsync(args, output, error, Send) == 2, "invalid CLI code");
            Check(calls == 1, "invalid CLI contacted server");
            Check(await ControlCli.RunAsync(["list"], output, error, (_, _) => Task.FromResult(new ControlResponse(false, "no", []))) == 1, "server failure code");
            Check(await ControlCli.RunAsync(["list"], output, error, (_, _) => throw new IOException("offline")) == 3, "transport code");
            Check(await ControlCli.RunAsync(["list"], output, error, (_, _) => throw new OperationCanceledException()) == 4, "timeout code");
            Check(await ControlCli.RunAsync(["--help"], output, error, Send) == 0 && calls == 1, "help contacted server");
        });
        await test("Product: CLI validation unique names IDs and status", async () =>
        {
            var a = worker("--worker sleep"); a.Name = "带 空格"; var b = worker("--worker sleep"); b.Name = a.Name;
            await using var manager = new ProcessManager(new() { Items = [a, b] }, Path.Combine(root, "product-names"));
            var status = await ControlCommands.ExecuteAsync(new("list"), manager);
            Check(status.Items.Length == 2 && status.Items.All(x => !x.Active), "list lost items");
            var ambiguous = false;
            try { await ControlCommands.ExecuteAsync(new("start", a.Name), manager); } catch (InvalidOperationException) { ambiguous = true; }
            Check(ambiguous && !manager.Snapshot(a.Id).Active && !manager.Snapshot(b.Id).Active, "ambiguous name started service");
            await ControlCommands.ExecuteAsync(new("start", a.Id.ToString()), manager);
            Check(manager.Snapshot(a.Id).Active && !manager.Snapshot(b.Id).Active, "ID selected wrong service");
            await ControlCommands.ExecuteAsync(new("stop", a.Id.ToString()), manager);
            foreach (var bad in new ControlRequest[] { new("delete", a.Name), new("start"), new("start", a.Name, true), new("list", a.Name), new("status", Version: 2) })
            {
                var rejected = false; try { ControlCommands.Validate(bad); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid request accepted");
            }
        });
        await test("Product: local IPC start stop restart and handler errors", async () =>
        {
            var item = worker("--worker sleep"); item.Name = "ipc service";
            await using var manager = new ProcessManager(new() { Items = [item] }, Path.Combine(root, "product-ipc"));
            var pipe = Pipe(); await using var server = new LocalControl.Server((r, t) => ControlCommands.ExecuteAsync(r, manager, t), pipe);
            Task<ControlResponse> Send(ControlRequest request) => LocalControl.SendAsync(request, TimeSpan.FromSeconds(10), pipe);
            Check((await Send(new("list"))).Items.Single().Name == item.Name, "IPC list");
            var started = await Send(new("start", item.Name)); var pid = started.Items.Single().Pid;
            Check(started.Success && pid.HasValue, "IPC start");
            var restarted = await Send(new("restart", item.Name)); Check(restarted.Success && restarted.Items.Single().Pid != pid && !Alive(pid!.Value), "restart did not retire old process");
            Check(!(await Send(new("start", "missing"))).Success, "handler error reported as success");
            Check((await Send(new("stop", All: true))).Success && !manager.Snapshot(item.Id).Active, "IPC stop all");
        });
        await test("Product: concurrent IPC stop cancels pending start all", async () =>
        {
            var a = worker("--worker sleep"); var b = worker("--worker sleep"); a.DelaySeconds = 5;
            await using var manager = new ProcessManager(new() { Items = [a, b] }, Path.Combine(root, "product-concurrent"));
            var pipe = Pipe(); await using var server = new LocalControl.Server((r, t) => ControlCommands.ExecuteAsync(r, manager, t), pipe);
            var start = LocalControl.SendAsync(new("start", All: true), TimeSpan.FromSeconds(10), pipe);
            await Until(() => manager.Snapshot(a.Id).Active);
            await LocalControl.SendAsync(new("stop", All: true), TimeSpan.FromSeconds(10), pipe); await start;
            Check(!manager.Snapshot(a.Id).Active && !manager.Snapshot(b.Id).Active, "late launch after stop all");
        });
        await test("Product: IPC rejects oversized frames and survives disconnect timeout", async () =>
        {
            var pipe = Pipe(); await using var server = new LocalControl.Server((_, _) => Task.FromResult(new ControlResponse(true, "OK", [])), pipe);
            using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(2000); var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
                await client.WriteAsync(header); using var timeout = new CancellationTokenSource(3000);
                try { Check(await client.ReadAsync(new byte[1], timeout.Token) == 0, "oversized frame not closed"); } catch (IOException) { }
            }
            using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous)) await client.ConnectAsync(2000);
            Check((await LocalControl.SendAsync(new("status"), TimeSpan.FromSeconds(3), pipe)).Success, "server failed after disconnect");
            var absent = false;
            try { await LocalControl.SendAsync(new("list"), TimeSpan.FromSeconds(1), Pipe()); } catch (IOException) { absent = true; }
            Check(absent, "absent manager accepted");
        });
        await test("Product: journal lifecycle dependency set and clean exit", async () =>
        {
            var directory = Path.Combine(root, "product-journal"); var journal = new SessionJournal(directory); journal.Begin();
            var a = worker("--worker sleep"); var b = worker("--worker sleep"); b.Dependencies = [a.Id];
            await using var manager = new ProcessManager(new() { Items = [a, b] }, directory) { Session = journal };
            await manager.StartAsync(b.Id);
            var state = journal.ReadPrevious()!;
            Check(state.Dirty && state.ActiveItems.ToHashSet().SetEquals([a.Id, b.Id]), "running dependency set not durable");
            await manager.StopAsync(b.Id); Check(journal.ReadPrevious()!.ActiveItems.SequenceEqual([a.Id]), "stopped item retained");
            await manager.DisposeAsync(); journal.MarkClean();
            Check(journal.ReadPrevious() is { Dirty: false, ActiveItems.Length: 0 }, "clean exit dirty");
            Check(Directory.GetFiles(directory, "session.json.*.tmp").Length == 0, "temporary journal leaked");
        });
        await test("Product: journal failure prevents launch and corrupt originals survive", async () =>
        {
            var directory = Path.Combine(root, "product-journal-error"); var journal = new SessionJournal(directory); journal.Begin();
            var item = worker("--worker sleep"); await using var manager = new ProcessManager(new() { Items = [item] }, directory) { Session = journal };
            using (var held = new FileStream(journal.PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = false; try { await manager.StartAsync(item.Id); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
                Check(failed && !manager.Snapshot(item.Id).Active && manager.Snapshot(item.Id).Pid is null, "launch before durable journal");
            }
            foreach (var invalid in new[] { "{bad", "{\"Version\":2,\"Dirty\":true,\"ActiveItems\":[]}", "{\"Version\":1,\"Dirty\":true,\"ActiveItems\":null}" })
            {
                File.WriteAllText(journal.PathName, invalid); var failed = false;
                try { journal.ReadPrevious(); } catch (Exception error) when (error is JsonException or InvalidDataException) { failed = true; }
                Check(failed && File.ReadAllText(journal.PathName) == invalid, "invalid journal overwritten");
            }
        });
        await test("Product: fatal cleanup preserves recovery set and missing IDs are filtered", async () =>
        {
            var directory = Path.Combine(root, "product-freeze"); var journal = new SessionJournal(directory); journal.Begin();
            var a = worker("--worker sleep"); var b = worker("--worker sleep"); a.StartupOrder = 5;
            await using var manager = new ProcessManager(new() { Items = [a, b] }, directory) { Session = journal };
            await manager.StartAllAsync(); journal.Freeze(); await manager.DisposeAsync();
            var state = journal.ReadPrevious()!; Check(state.Dirty && state.ActiveItems.Length == 2, "fatal cleanup erased recovery intent");
            Check(SessionJournal.RecoveryItems(state, new() { Items = [a] }).Single().Id == a.Id, "deleted item restored");
            Check(SessionJournal.RecoveryItems(state, new() { Items = [a, b] })[0].Id == b.Id, "recovery ignores priority");
            var next = new SessionJournal(directory); next.Begin();
            Check(next.ReadPrevious()!.ActiveItems.Length == 0, "skip recovery did not clear prior intent");
        });
        await test("Product: forced owner termination preserves journal and kills tree", async () =>
        {
            var directory = Path.Combine(root, "product-crash"); Directory.CreateDirectory(directory); var marker = Path.Combine(directory, "owner.json");
            using var owner = Process.Start(self(["--journal-owner", directory, marker]))!;
            int pid = 0;
            try
            {
                await Until(() => File.Exists(marker));
                var info = JsonSerializer.Deserialize<OwnerMarker>(File.ReadAllText(marker))!; pid = info.Pid;
                Check(Alive(pid), "worker never ran"); owner.Kill(); await owner.WaitForExitAsync(); await Until(() => !Alive(pid));
                var state = new SessionJournal(directory).ReadPrevious()!;
                Check(state.Dirty && state.ActiveItems.SequenceEqual([info.Id]), "crash lost running set");
                var config = new ConfigurationStore(directory).Load();
                await using var recovered = new ProcessManager(config, directory); await recovered.StartAsync(info.Id);
                Check(recovered.Snapshot(info.Id).Pid != pid, "recovery adopted old PID"); await recovered.StopAllAsync();
            }
            finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
        });
    }
    public sealed record OwnerMarker(Guid Id, int Pid);
}
