using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LaunchDesk.Core;
using Microsoft.Win32.SafeHandles;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args.FirstOrDefault() == "--save-worker")
{
    var store = new ConfigurationStore(args[1]);
    var errors = new List<string>(); var succeeded = 0;
    for (var i = 0; i < 200; i++)
    {
        try { store.Save(new() { Items = [new() { Name = args[2] + i, Target = "not-executed", Arguments = new string('x', 100000) }] }); succeeded++; }
        catch (IOException error) { errors.Add(error.GetType().Name + ":" + error.HResult.ToString("X") + ":" + error.Message); }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { succeeded, errors })); return 0;
}
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var root = Path.Combine(output, "sandbox-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var results = new List<object>();
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
Configuration Small(string name) => new() { Items = [new() { Name = name, Target = "not-executed" }] };
async Task Check(string name, Func<Task<object>> test)
{
    try { var evidence = await test(); results.Add(new { name, verification = "passed", evidence }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { results.Add(new { name, verification = "failed", error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error); }
}
await Check("D-1: original settings control flow, migration and locator failures", async () =>
{
    var evidence = new List<object>();
    foreach (var failCopy in new[] { true, false })
    {
        var sandbox = Path.Combine(root, failCopy ? "manager-copy" : "manager-locator"); Directory.CreateDirectory(sandbox);
        var harness = new SettingsHarness(sandbox);
        if (failCopy) File.WriteAllText(harness.Destination, "destination collision");
        // A directory at the locator filename makes writing fail without touching the user's locator.
        Directory.CreateDirectory(harness.Locator);
        try
        {
            Exception? fault = null;
            try { await harness.ApplyAsync(); } catch (IOException error) { fault = error; } catch (InvalidOperationException error) { fault = error; } catch (UnauthorizedAccessException error) { fault = error; }
            Require(fault is not null, "expected migration/locator failure");
            var active = harness.Managers.Count(x => !SettingsHarness.Maintenance(x).IsCompleted);
            Require(active == 1 && harness.Managers.Count == 2, "exactly one replacement manager remains");
            Require(harness.LocatorAttempted == !failCopy, "copy rethrow prevents entering locator branch");
            evidence.Add(new { failCopy, managersCreated = harness.Managers.Count, maintenanceTasksStillActive = active,
                harness.LocatorAttempted, error = fault!.Message, conclusion = "reported double replacement not reproduced" });
            for (var repeat = 0; repeat < 3; repeat++)
            {
                try { await harness.ApplyAsync(); } catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { }
                Require(harness.Managers.Count(x => !SettingsHarness.Maintenance(x).IsCompleted) == 1,
                    "repeated failed migration leaked a maintenance task");
            }
        }
        finally { foreach (var manager in harness.Managers) await manager.DisposeAsync(); }
        Require(harness.Managers.All(x => SettingsHarness.Maintenance(x).IsCompleted), "all test managers released");
    }
    var success = new SettingsHarness(Path.Combine(root, "manager-success"));
    try
    {
        await success.ApplyAsync();
        Require(success.Managers.Count == 2 && success.Managers.Count(x => !SettingsHarness.Maintenance(x).IsCompleted) == 1,
            "successful migration retained old manager");
        evidence.Add(new { success = true, managersCreated = success.Managers.Count, maintenanceTasksStillActive = 1 });
    }
    finally { foreach (var manager in success.Managers) await manager.DisposeAsync(); }
    Require(success.Managers.All(x => SettingsHarness.Maintenance(x).IsCompleted), "successful migration managers not released");
    return evidence;
});
if (args.Contains("--manager-only", StringComparer.Ordinal))
{
    await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { generatedAt = DateTimeOffset.UtcNow, root,
        scope = "Current source-guarded settings transaction, repeated copy/locator failures; no real dialog, registry or user locator", results }, new JsonSerializerOptions { WriteIndented = true }));
    return results.Any(x => JsonSerializer.SerializeToElement(x).GetProperty("verification").GetString() == "failed") ? 1 : 0;
}
await Check("D-10a: failed copy retains staging but preserves source and destination", () =>
{
    var sandbox = Path.Combine(root, "copy-failure"); var store = new ConfigurationStore(Path.Combine(sandbox, "source"));
    store.Save(Small("sentinel")); var before = File.ReadAllBytes(store.ConfigPath);
    var logs = Path.Combine(store.Root, "logs"); Directory.CreateDirectory(logs);
    var locked = Path.Combine(logs, "locked.bin"); File.WriteAllText(locked, "do not change");
    var destination = Path.Combine(sandbox, "destination"); Directory.CreateDirectory(destination);
    Exception? fault = null;
    using (var handle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        try { DataLocation.CopyTo(store, Small("sentinel"), destination); } catch (IOException error) { fault = error; }
    var staging = Directory.GetDirectories(sandbox, "destination.migration-*");
    Require(fault is not null && staging.Length == 1 && Directory.Exists(destination), "copy failure staging/destination evidence");
    Require(File.ReadAllBytes(store.ConfigPath).SequenceEqual(before), "original config unchanged");
    return Task.FromResult<object>(new { reproduced = true, staging, destinationPreserved = true, sourcePreserved = true, error = fault!.Message,
        qualification = "staging retention is documented in production source as recoverable, not silently activated" });
});
await Check("D-10b: failed final move removes the selected empty destination", async () =>
{
    var sandbox = Path.Combine(root, "move-failure"); var store = new ConfigurationStore(Path.Combine(sandbox, "source"));
    var config = Small("move-sentinel"); store.Save(config); var before = File.ReadAllBytes(store.ConfigPath);
    var logs = Path.Combine(store.Root, "logs"); Directory.CreateDirectory(logs);
    var payload = new byte[512 * 1024];
    for (var i = 0; i < 64; i++) File.WriteAllBytes(Path.Combine(logs, i + ".bin"), payload);
    var destination = Path.Combine(sandbox, "destination"); Directory.CreateDirectory(destination);
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var lockTask = Task.Run(async () =>
    {
        while (true)
        {
            cancel.Token.ThrowIfCancellationRequested();
            var staging = Directory.GetDirectories(sandbox, "destination.migration-*").FirstOrDefault();
            if (staging is not null)
            {
                var handle = Native.OpenDirectory(staging);
                if (!handle.IsInvalid) return handle;
                handle.Dispose();
            }
            await Task.Delay(1, cancel.Token);
        }
    });
    var copy = Task.Run(() => DataLocation.CopyTo(store, config, destination));
    using var directoryHandle = await lockTask;
    Exception? fault = null;
    try { await copy; } catch (IOException error) { fault = error; }
    var stagingPaths = Directory.GetDirectories(sandbox, "destination.migration-*");
    Require(fault is not null && !Directory.Exists(destination) && stagingPaths.Length == 1,
        $"final move fault evidence: error={fault?.Message ?? "none"}, destination={Directory.Exists(destination)}, staging={stagingPaths.Length}");
    Require(File.ReadAllBytes(store.ConfigPath).SequenceEqual(before), "source unchanged");
    return new { reproduced = true, destinationExists = false, sourcePreserved = true, stagingPaths, error = fault!.Message };
});
await Check("D-11a: failed competing save deletes another writer's temporary file", () =>
{
    var store = new ConfigurationStore(Path.Combine(root, "temp-ownership")); store.Save(Small("original"));
    var temporary = store.ConfigPath + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(Small("writer-A"), ConfigurationStore.JsonOptions));
    // Model writer A's closed-temp/before-replace gap, holding a delete-sharing read handle to force B's open to fail.
    using var owned = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    Exception? fault = null;
    try { store.Save(Small("writer-B")); } catch (IOException error) { fault = error; }
    var deleted = !File.Exists(temporary);
    Require(fault is not null && deleted && store.Load().Items[0].Name == "original", "failed B removed A temp but did not commit");
    return Task.FromResult<object>(new { reproduced = true, otherWriterTempDeleted = deleted, originalConfigPreserved = true, error = fault!.Message,
        qualification = "controlled interleaving, not two full concurrent Save calls" });
});
await Check("D-11b: two genuine save processes", async () =>
{
    var store = new ConfigurationStore(Path.Combine(root, "concurrent-save")); store.Save(Small("original"));
    Process Start(string name)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(info.FileName) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in new[] { "--save-worker", store.Root, name }) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }
    using var first = Start("A"); using var second = Start("B");
    try
    {
        var outA = first.StandardOutput.ReadToEndAsync(); var outB = second.StandardOutput.ReadToEndAsync();
        await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(45));
        Require(first.ExitCode == 0 && second.ExitCode == 0, "workers complete");
        var a = JsonSerializer.Deserialize<JsonElement>(await outA); var b = JsonSerializer.Deserialize<JsonElement>(await outB);
        var failures = a.GetProperty("errors").GetArrayLength() + b.GetProperty("errors").GetArrayLength();
        Require(failures > 0, "concurrent save contention reproduced");
        return new { reproduced = true, workerA = a, workerB = b, finalConfigReadable = store.Load().Items.Count == 1,
            qualification = "contention failures observed; error list distinguishes open/delete/replace failures" };
    }
    finally
    {
        foreach (var process in new[] { first, second }) if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
    }
});
await Check("D-12: successful oversized save rejected on read and rolled back on load", () =>
{
    var store = new ConfigurationStore(Path.Combine(root, "oversized")); store.Save(Small("old-valid"));
    var large = Small("new-lost"); large.Items[0].Arguments = new string('x', 5 * 1024 * 1024);
    store.Save(large); var length = new FileInfo(store.ConfigPath).Length;
    Exception? fault = null; try { ConfigurationStore.Read(store.ConfigPath); } catch (InvalidDataException error) { fault = error; }
    Require(length > 4 * 1024 * 1024 && fault is not null, "save succeeds but read rejects");
    var loaded = store.Load();
    Require(loaded.Items[0].Name == "old-valid" && store.RecoveredFrom is not null && File.Exists(store.CorruptArchive), "load restores old backup and archives new content");
    var fresh = new ConfigurationStore(Path.Combine(root, "oversized-no-backup")); fresh.Save(large);
    var refused = false; try { fresh.Load(); } catch (InvalidDataException) { refused = true; }
    Require(refused, "without backup startup load fails");
    return Task.FromResult<object>(new { reproduced = true, savedBytes = length, fault = fault!.Message, loadedName = loaded.Items[0].Name,
        store.RecoveredFrom, store.CorruptArchive, noBackupLoadRejected = refused });
});
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { generatedAt = DateTimeOffset.UtcNow,
    root, sourceCommit = "43449c5 plus uncommitted Windows 10 compatibility changes", results }, new JsonSerializerOptions { WriteIndented = true }));
return results.Any(x => JsonSerializer.SerializeToElement(x).GetProperty("verification").GetString() == "failed") ? 1 : 0;

internal static class Native
{
    public static SafeFileHandle OpenDirectory(string path) => CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
