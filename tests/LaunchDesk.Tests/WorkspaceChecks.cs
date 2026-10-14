using System.Text.Json;
using System.Diagnostics;

internal static class WorkspaceChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        string Evidence(string name)
        {
            var report = Path.Combine(root, name + ".json"); File.WriteAllText(report, "[]"); File.WriteAllText(report + ".metadata.json", "{}"); return report;
        }
        await test("Test data policy: success removes only owned data after external evidence", () =>
        {
            var workspace = new TestWorkspace(); var report = Evidence("cleanup");
            File.WriteAllText(Path.Combine(workspace.Root, "data.txt"), "fixture");
            Check(workspace.Finish(report, true) == "removed" && !Directory.Exists(workspace.Root), "successful data retained");
            Check(File.Exists(report) && File.Exists(report + ".metadata.json"), "external evidence deleted");
            return Task.CompletedTask;
        });
        await test("Test data policy: incomplete and explicitly retained data survive", () =>
        {
            var workspace = new TestWorkspace(); var report = Evidence("keep");
            Check(workspace.Finish(report, false) == "retained-incomplete" && Directory.Exists(workspace.Root), "failure data lost");
            Check(workspace.Finish(report, true, true) == "retained-requested" && Directory.Exists(workspace.Root), "explicit keep ignored");
            Check(workspace.Finish(report, true) == "removed", "fixture cleanup failed");
            return Task.CompletedTask;
        });
        await test("Test data policy: inside reports and missing evidence block cleanup", () =>
        {
            var workspace = new TestWorkspace(); var inside = Path.Combine(workspace.Root, "results.json");
            File.WriteAllText(inside, "[]");
            Check(workspace.Finish(inside, true) == "retained-report-inside" && File.Exists(inside), "default report deleted");
            Check(workspace.Finish(Path.Combine(root, "missing.json"), true) == "retained-missing-evidence", "missing evidence accepted");
            Check(workspace.Finish(Evidence("outside"), true) == "removed", "fixture cleanup failed");
            return Task.CompletedTask;
        });
        await test("Test data policy: invalid ownership blocks deletion", () =>
        {
            var workspace = new TestWorkspace(); var report = Evidence("ownership");
            var marker = Path.Combine(workspace.Root, TestWorkspace.MarkerName); var saved = File.ReadAllText(marker);
            File.WriteAllText(marker, JsonSerializer.Serialize(new { key = "not-the-owner", ownerPid = Environment.ProcessId }));
            Check(workspace.Finish(report, true).StartsWith("retained-cleanup-error") && Directory.Exists(workspace.Root), "unowned directory deleted");
            File.WriteAllText(marker, saved); Check(workspace.Finish(report, true) == "removed", "fixture cleanup failed");
            return Task.CompletedTask;
        });
        await test("Test data policy: junction refuses traversal and preserves outside sentinel", async () =>
        {
            var workspace = new TestWorkspace(); var report = Evidence("junction");
            var outside = Path.Combine(root, "outside-sentinel"); Directory.CreateDirectory(outside);
            var sentinel = Path.Combine(outside, "sentinel.txt"); File.WriteAllText(sentinel, "outside must survive");
            var link = Path.Combine(workspace.Root, "junction");
            var info = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:TEST_LINK -Target $env:TEST_TARGET | Out-Null");
            info.Environment["TEST_LINK"] = link; info.Environment["TEST_TARGET"] = outside;
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10));
            }
            Check(process.ExitCode == 0, "junction creation failed: " + await error);
            Check(workspace.Finish(report, true).StartsWith("retained-cleanup-error") && File.ReadAllText(sentinel) == "outside must survive", "junction target traversed/deleted");
            // Only unlink the verified junction, never recursively delete its target.
            Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "fixture is not a junction");
            Directory.Delete(link);
            Check(workspace.Finish(report, true) == "removed" && File.Exists(sentinel), "safe fixture cleanup failed");
        });
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
