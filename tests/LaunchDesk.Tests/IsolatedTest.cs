using System.Text;
using System.Text.Json;
using LaunchDesk.Core;

internal static class IsolatedTest
{
    public static async Task<string> Run(LaunchItem item, TimeSpan timeout, bool requireSuccess = true)
    {
        using var process = new NativeProcess(item);
        using var output = new StreamReader(process.Output, Encoding.UTF8);
        using var error = new StreamReader(process.Error, Encoding.UTF8);
        var stdout = output.ReadToEndAsync(); var stderr = error.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            var text = await stdout + await stderr;
            if (requireSuccess && process.ExitCode != 0) throw new InvalidDataException("Isolated process failed: " + process.ExitCode + ": " + text);
            return text;
        }
        finally
        {
            // The job owns descendants even after their original parent has exited.
            process.Kill();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    public static async Task<TestResult> RunCase(LaunchItem item, string report, string name)
    {
        var output = await Run(item, TimeSpan.FromSeconds(120), requireSuccess: false);
        if (!File.Exists(report)) throw new InvalidDataException("Isolated test produced no report: " + output);
        if (!File.Exists(report + ".metadata.json")) throw new InvalidDataException("Isolated test provenance missing: " + output);
        var results = JsonSerializer.Deserialize<TestResult[]>(await File.ReadAllTextAsync(report))!;
        if (results.Length != 1 || results[0].name != name) throw new InvalidDataException("Isolated test inventory mismatch: " + output);
        return results[0];
    }
}
