using System.Reflection;
using System.Text.Json;
using LaunchDesk.Core;

internal static class ReliabilityChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Seed(LogStore store, string name, int days = 0)
        {
            Directory.CreateDirectory(store.DirectoryPath);
            var path = Path.Combine(store.DirectoryPath, name + ".jsonl");
            File.WriteAllText(path, JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stdout", "history\n")) + "\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-days)); return path;
        }
        await test("Reliability: 10 MiB retention keeps small recent history", () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new() { LogLimitMiB = 10 });
            var path = Seed(store, "recent"); store.Write("stdout", "new\n");
            Check(File.Exists(path), "Recent history deleted at legal minimum limit"); return Task.CompletedTask;
        });
        await test("Reliability: locked expired history does not fail writes", () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new() { LogDays = 1 });
            var path = Seed(store, "locked", 5);
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            store.Write("stdout", "still writing\n");
            Check(store.Snapshot().Any(x => x.Text.Contains("still writing")), "Write lost");
            Check(store.CleanupWarnings.Length > 0, "Cleanup failure not reported");
            held.Dispose(); store.Maintain(); Check(!File.Exists(path), "Retry did not delete unlocked history"); return Task.CompletedTask;
        });
        await test("Reliability: corrupt config recovers newest valid backup", () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "recover"));
            var config = new Configuration { Items = [new() { Name = "saved", Target = "cmd" }] };
            store.Save(config); store.Save(config);
            var backupDir = Path.Combine(store.Root, "backups");
            File.WriteAllText(Path.Combine(backupDir, "config-newest-invalid.json"), "{");
            File.WriteAllText(store.ConfigPath, "{broken");
            Check(store.Load().Items.Single().Name == "saved", "Backup not restored");
            Check(Directory.GetFiles(store.Root, "config.corrupt-*.json").Any(x => File.ReadAllText(x) == "{broken"), "Corrupt original not preserved");
            return Task.CompletedTask;
        });
        await test("Reliability: export iterator does not block log writer", async () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new()); store.Write("stdout", "first\n");
            var method = typeof(LogStore).GetMethod("RetainedRecords", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var iterator = ((IEnumerable<LogRecord>)method.Invoke(store, null)!).GetEnumerator();
            Check(iterator.MoveNext(), "Missing snapshot record");
            var write = Task.Run(() => store.Write("stdout", "while exporting\n"));
            var done = write.Wait(500);
            iterator.Dispose(); await write;
            Check(done, "Export holds write lock across yield");
        });
        await test("Reliability: UI snapshot independent of disk lock", async () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new()); store.Write("stdout", "first\n");
            var field = typeof(LogStore).GetField("diskGate", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(LogStore).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var sync = field.GetValue(store)!;
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var disk = Task.Run(() => { lock (sync) { entered.Set(); release.Wait(); } }); entered.Wait();
            var snapshot = Task.Run(() => store.Snapshot());
            var done = await Task.WhenAny(snapshot, Task.Delay(500)) == snapshot;
            release.Set(); await disk; await snapshot;
            Check(done, "UI snapshot waits on disk lock");
        });
        await test("Reliability: invalid backups preserve original, future schema not downgraded", () =>
        {
            var store = new ConfigurationStore(Path.Combine(root, "bad-backups"));
            var config = new Configuration { Items = [new() { Name = "old", Target = "cmd" }] };
            store.BackupNow(config);
            File.WriteAllText(store.ConfigPath, "{\"schemaVersion\":2}");
            try { store.Load(); throw new Exception("Future schema downgraded"); } catch (InvalidDataException) { }
            Check(File.ReadAllText(store.ConfigPath).Contains(":2"), "Future config overwritten");
            var invalid = new ConfigurationStore(Path.Combine(root, "no-valid-backups"));
            Directory.CreateDirectory(Path.Combine(invalid.Root, "backups"));
            File.WriteAllText(Path.Combine(invalid.Root, "backups", "config-bad.json"), "null");
            File.WriteAllText(invalid.ConfigPath, "{bad");
            try { invalid.Load(); throw new Exception("Invalid backup accepted"); } catch (JsonException) { }
            Check(File.ReadAllText(invalid.ConfigPath) == "{bad", "Invalid original overwritten");
            File.WriteAllText(store.ConfigPath, "null");
            Check(store.Load().Items.Single().Name == "old" && store.RecoveredFrom!.Contains("manual"), "Manual fallback failed");
            return Task.CompletedTask;
        });
        await test("Reliability: export snapshot bounded across writes and history deletion", () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new()); store.Write("stdout", "first\n");
            var method = typeof(LogStore).GetMethod("RetainedRecords", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var iterator = ((IEnumerable<LogRecord>)method.Invoke(store, null)!).GetEnumerator();
            Check(iterator.MoveNext(), "Initial snapshot empty");
            store.Write("stdout", "second\n"); store.DeleteHistory(); store.Write("stdout", "fresh\n");
            Check(!iterator.MoveNext(), "Snapshot includes post-capture records");
            Check(store.Snapshot().Single().Text == "fresh\n", "Writer not rebuilt after delete"); return Task.CompletedTask;
        });
        await test("Reliability: size pruning keeps newest history, protects active writer", () =>
        {
            using var store = new LogStore(root, Guid.NewGuid(), new() { LogLimitMiB = 10 });
            Directory.CreateDirectory(store.DirectoryPath);
            var paths = Enumerable.Range(0, 4).Select(i => Path.Combine(store.DirectoryPath, $"size-{i}.jsonl")).ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                File.WriteAllText(paths[i], JsonSerializer.Serialize(new LogRecord(DateTimeOffset.Now, "stdout", new string('x', 3 * 1024 * 1024))) + "\n");
                File.SetLastWriteTimeUtc(paths[i], DateTime.UtcNow.AddMinutes(i - 10));
            }
            store.Write("stdout", "active\n");
            Check(File.Exists(paths[^1]) && !File.Exists(paths[0]), "Newest retention wrong");
            Check(Directory.GetFiles(store.DirectoryPath).Sum(x => new FileInfo(x).Length) < 10 * 1024 * 1024L, "Size remains over limit");
            var active = Directory.GetFiles(store.DirectoryPath).Single(x => !Path.GetFileName(x).StartsWith("size-"));
            File.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-30)); store.Maintain();
            Check(File.Exists(active), "Active writer deleted"); return Task.CompletedTask;
        });
        await test("Reliability: idle history cleaned at startup without launching services", async () =>
        {
            var isolated = Path.Combine(root, "idle-maintenance");
            using var store = new LogStore(isolated, Guid.NewGuid(), new()); var path = Seed(store, "expired", 5);
            await using var manager = new ProcessManager(new() { Preferences = new() { LogDays = 1 } }, isolated);
            for (var i = 0; i < 50 && File.Exists(path); i++) await Task.Delay(20);
            Check(!File.Exists(path), "Idle expired file remains");
        });
    }
}
