using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LaunchDesk.Core;
using Microsoft.Win32.SafeHandles;

internal static class DataReliabilityChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test, Func<string[], ProcessStartInfo> self)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Configuration Small(string name) => new() { Items = [new() { Name = name, Target = "not-executed" }] };
        const int maxConfigBytes = 4 * 1024 * 1024;
        await test("Data reliability: exact byte limit roundtrip and oversized writes rejected", () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "size-limit"));
            var config = Small("boundary");
            var overhead = JsonSerializer.SerializeToUtf8Bytes(config, ConfigurationStore.JsonOptions).Length;
            config.Items[0].Arguments = new string('x', maxConfigBytes - overhead);
            store.Save(config);
            Check(new FileInfo(store.ConfigPath).Length == maxConfigBytes, "exact boundary");
            Check(store.Load().Items[0].Arguments == config.Items[0].Arguments, "boundary reload");
            var before = File.ReadAllBytes(store.ConfigPath);
            config.Items[0].Arguments += "中";
            var export = Path.Combine(store.Root, "export.json"); File.WriteAllText(export, "sentinel");
            foreach (var write in new Action[] { () => store.Save(config), () => store.Export(config, export), () => store.BackupNow(config) })
            {
                var rejected = false; try { write(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "oversized write accepted");
            }
            Check(File.ReadAllBytes(store.ConfigPath).SequenceEqual(before), "committed configuration changed");
            Check(store.Load().Items[0].Name == "boundary" && store.RecoveredFrom is null, "unexpected backup rollback");
            Check(!Directory.Exists(Path.Combine(store.Root, "backups")), "rejected save created backup");
            Check(File.ReadAllText(export) == "sentinel", "rejected export overwrote file");
            Check(Directory.GetFiles(store.Root, "*.tmp").Length == 0, "temporary leak");
            var fresh = new ConfigurationStore(Path.Combine(root, "size-fresh"));
            try { fresh.Save(config); } catch (InvalidDataException) { }
            Check(!File.Exists(fresh.ConfigPath), "fresh oversized save committed");
            return Task.CompletedTask;
        });
        await test("Data reliability: failed save cleans only its own temporary file", () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "temp-owner")); store.Save(Small("original"));
            var foreign = store.ConfigPath + ".tmp"; File.WriteAllText(foreign, "writer-A");
            var uniqueForeign = store.ConfigPath + ".foreign.tmp"; File.WriteAllText(uniqueForeign, "writer-C");
            using (var locked = new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var rejected = false; try { store.Save(Small("failed-B")); } catch (IOException) { rejected = true; }
                Check(rejected, "locked commit unexpectedly succeeded");
            }
            Check(File.ReadAllText(foreign) == "writer-A" && File.ReadAllText(uniqueForeign) == "writer-C", "foreign temp deleted");
            Check(Directory.GetFiles(store.Root, "*.tmp").Length == 2, "own temp leaked");
            Check(store.Load().Items[0].Name == "original", "failed commit altered configuration");
            store.Save(Small("success")); Check(store.Load().Items[0].Name == "success", "failed save retained mutex");
            var locator = Path.Combine(store.Root, "location.json"); DataLocation.Save(store.Root, locator);
            var locatorForeign = locator + ".tmp"; File.WriteAllText(locatorForeign, "other writer");
            using (var locked = new FileStream(locator, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var rejected = false;
                try { DataLocation.Save(root, locator); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rejected = true; }
                Check(rejected, "locked locator commit unexpectedly succeeded");
            }
            Check(DataLocation.Resolve(root, false, locator) == store.Root && File.ReadAllText(locatorForeign) == "other writer", "locator ownership");
            Check(Directory.GetFiles(store.Root, "location.json.*.tmp").Length == 0, "locator own temp leaked");
            return Task.CompletedTask;
        });
        await test("Data reliability: concurrent process saves commit readable snapshots", async () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "parallel-save"));
            using var first = Process.Start(self(["--save-worker", store.Root, "A"]))!;
            using var second = Process.Start(self(["--save-worker", store.Root, "B"]))!;
            try
            {
                await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(45));
                Check(first.ExitCode == 0 && second.ExitCode == 0, "concurrent writer failed");
                Check(store.Load().Items[0].Name is "A99" or "B99", "final write lost or unreadable");
                Check(Directory.GetFiles(store.Root, "*.tmp").Length == 0, "temporary files left");
                var backups = Directory.GetFiles(Path.Combine(store.Root, "backups"), "*.json");
                Check(backups.Length == 20, "backup retention");
                foreach (var backup in backups) Check(ConfigurationStore.Read(backup).Items.Count == 1, "invalid backup");
            }
            finally
            {
                foreach (var process in new[] { first, second }) if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
            }
        });
        await test("Data reliability: migration copy failure reports retained staging", () =>
        {
            var sandbox = Path.Combine(root, "copy-rollback"); var store = new ConfigurationStore(Path.Combine(sandbox, "source"));
            var config = Small("original"); store.Save(config); var before = File.ReadAllBytes(store.ConfigPath);
            var logs = Path.Combine(store.Root, "logs"); Directory.CreateDirectory(logs);
            var file = Path.Combine(logs, "locked"); File.WriteAllText(file, "sentinel");
            var destination = Path.Combine(sandbox, "destination"); Directory.CreateDirectory(destination);
            IOException? failure = null;
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                try { DataLocation.CopyTo(store, config, destination); } catch (IOException error) { failure = error; }
            var staging = Directory.GetDirectories(sandbox, "destination.migration-*").Single();
            Check(failure is not null && failure.Message.Contains(staging), "missing recovery path");
            Check(Directory.Exists(destination) && !Directory.EnumerateFileSystemEntries(destination).Any(), "destination changed");
            Check(File.ReadAllBytes(store.ConfigPath).SequenceEqual(before), "source changed");
            return Task.CompletedTask;
        });
        await test("Data reliability: final migration move failure restores empty destination", async () =>
        {
            var sandbox = Path.Combine(root, "move-rollback"); var store = new ConfigurationStore(Path.Combine(sandbox, "source"));
            var config = Small("original"); store.Save(config); var before = File.ReadAllBytes(store.ConfigPath);
            var logs = Path.Combine(store.Root, "logs"); Directory.CreateDirectory(logs);
            for (var i = 0; i < 64; i++) File.WriteAllBytes(Path.Combine(logs, i + ".bin"), new byte[512 * 1024]);
            var destination = Path.Combine(sandbox, "destination"); Directory.CreateDirectory(destination);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var hold = Task.Run(async () =>
            {
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var staging = Directory.GetDirectories(sandbox, "destination.migration-*").FirstOrDefault();
                    if (staging is not null)
                    {
                        var handle = CreateFile(staging, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                        if (!handle.IsInvalid) return handle;
                        handle.Dispose();
                    }
                    await Task.Delay(1, timeout.Token);
                }
            });
            var copy = Task.Run(() => DataLocation.CopyTo(store, config, destination));
            using var held = await hold;
            IOException? failure = null; try { await copy; } catch (IOException error) { failure = error; }
            var retained = Directory.GetDirectories(sandbox, "destination.migration-*").Single();
            Check(failure is not null && failure.Message.Contains(retained) && failure.InnerException is IOException, "missing move failure evidence");
            Check(Directory.Exists(destination) && !Directory.EnumerateFileSystemEntries(destination).Any(), "selected directory not restored");
            Check(ConfigurationStore.Read(Path.Combine(retained, "config.json")).Items[0].Name == "original", "staging not recoverable");
            Check(File.ReadAllBytes(store.ConfigPath).SequenceEqual(before), "source changed");
        });
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
