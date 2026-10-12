using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LaunchDesk.Core;

internal static class HostingChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test, Func<string, LaunchItem> worker, Func<string[], ProcessStartInfo> self)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
        Task<IManagedProcess> Start(LaunchItem item, Action<Process>? capture = null) => ElevationBroker.StartAsync(item, Environment.ProcessPath!, CancellationToken.None,
            info => { var process = Process.Start(self(info.ArgumentList.ToArray()))!; capture?.Invoke(process); return process; });
        await test("Hosting: service rejects GUI visibility and elevation without changing desktop config", () =>
        {
            foreach (var elevation in new[] { false, true })
            {
                var item = worker("--worker sleep"); item.RequireElevation = elevation; item.ShowWindow = !elevation;
                var rejected = false; try { ServicePolicy.Validate(new() { Items = [item] }); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && item.RequireElevation == elevation && item.ShowWindow == !elevation, "Service policy changed desktop item");
            }
            var background = new LaunchItem { Kind = LaunchKind.Command, Target = "echo service" };
            ServicePolicy.Validate(new() { Items = [background] });
            var guiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/LaunchDesk.App/bin/Release/net10.0-windows/LaunchDesk.exe"));
            var guiRejected = false;
            try { ServicePolicy.Validate(new() { Items = [new() { Kind = LaunchKind.Executable, Target = guiPath }] }); }
            catch (InvalidDataException) { guiRejected = true; }
            Check(guiRejected, "GUI executable accepted for Session 0");
            var store = new ConfigurationStore(Path.Combine(root, "hosting-config")); background.RequireElevation = true;
            store.Save(new() { Items = [background] }); Check(store.Load().Items.Single().RequireElevation, "Elevation flag lost");
            return Task.CompletedTask;
        });
        await test("Hosting: service executable validation uses launch working directory and PATH", () =>
        {
            var directory = Path.Combine(root, "hosting-relative"); Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "worker.exe"); File.Copy(Environment.ProcessPath!, target);
            var item = new LaunchItem { Kind = LaunchKind.Executable, Target = "worker.exe", WorkingDirectory = directory };
            ServicePolicy.Validate(new() { Items = [item] });
            item.WorkingDirectory = AppContext.BaseDirectory; item.Environment["PATH"] = directory;
            ServicePolicy.Validate(new() { Items = [item] });
            return Task.CompletedTask;
        });
        await test("Hosting: explicit UAC request and cancelled consent", async () =>
        {
            var info = ElevationBroker.StartInfo("helper.exe", "pipe", "nonce", 42, 123);
            Check(info.UseShellExecute && info.Verb == "runas" && info.ArgumentList[0] == "--elevated", "UAC bypass");
            var cancelled = false;
            try { using var unused = await ElevationBroker.StartAsync(worker("--worker sleep"), Environment.ProcessPath!, CancellationToken.None, _ => throw new Win32Exception(1223)); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Cancelled UAC was not cancelled");
        });
        await test("Hosting: isolated broker preserves stdout stderr and exit", async () =>
        {
            using var process = await Start(worker("--worker flood"));
            using var stdout = new MemoryStream(); using var stderr = new MemoryStream();
            var output = process.Output.CopyToAsync(stdout); var error = process.Error.CopyToAsync(stderr);
            await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromSeconds(30));
            Check(process.ExitCode == 0 && Encoding.UTF8.GetString(stdout.ToArray()).Contains("尾部🙂无换行")
                && Encoding.UTF8.GetString(stderr.ToArray()).EndsWith("stderr-tail"), "Broker truncated logs");
        });
        await test("Hosting: isolated broker stdin and forced job cleanup", async () =>
        {
            using (var process = await Start(worker("--worker input")))
            {
                using var text = new StreamReader(process.Output, Encoding.UTF8);
                await process.Input.WriteAsync(Encoding.UTF8.GetBytes("hello\n")); await process.Input.FlushAsync();
                var output = text.ReadToEndAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check((await output).Contains("GOT:hello"), "Broker stdin lost");
            }
            using (var process = await Start(worker("--worker sleep")))
            {
                var pid = process.Id; process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(!Alive(pid) && process.ForcedTermination, "Elevated job leaked");
            }
        });
        await test("Hosting: isolated helper and manager crashes clean child processes", async () =>
        {
            Process? helper = null;
            using (var process = await Start(worker("--worker sleep"), p => helper = p))
            {
                var pid = process.Id; helper!.Kill(); await helper.WaitForExitAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException) { }
                for (var i = 0; i < 100 && Alive(pid); i++) await Task.Delay(20);
                Check(!Alive(pid), "Helper crash leaked job");
            }
            var marker = Path.Combine(root, "broker-owner-pid.txt");
            using var owner = Process.Start(self(["--broker-owner", marker]))!;
            try
            {
                for (var i = 0; i < 200 && !File.Exists(marker); i++) await Task.Delay(25);
                Check(File.Exists(marker), "Broker owner did not start");
                var pid = int.Parse(File.ReadAllText(marker)); owner.Kill(); await owner.WaitForExitAsync();
                for (var i = 0; i < 200 && Alive(pid); i++) await Task.Delay(25);
                Check(!Alive(pid), "Manager crash leaked helper job");
            }
            finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
        });
    }
}
