using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class TestEvidence
{
    public static async Task WriteAsync(string report, DateTimeOffset startedAt, string? filter)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Test evidence requires the repository root.");
        async Task<string> Run(string command, params string[] arguments)
        {
            var info = new ProcessStartInfo(command) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (process.ExitCode != 0) throw new IOException("Evidence command failed: " + command + ": " + await error);
            await error; return (await output).TrimEnd();
        }
        var paths = (await Run("git", "ls-files", "--cached", "--others", "--exclude-standard", "--", "src", "tests", "scripts", "tools", "build", ".github", "Directory.Build.props", "Directory.Build.targets", "global.json", "LaunchDesk.slnx"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r')).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        var hashTimer = Stopwatch.StartNew();
        var hashAllocated = GC.GetAllocatedBytesForCurrentThread();
        var hashes = paths.Where(x => File.Exists(Path.Combine(root, x))).Select(x =>
        {
            using var input = File.OpenRead(Path.Combine(root, x));
            return new { path = x, sha256 = Convert.ToHexString(SHA256.HashData(input)) };
        }).ToArray();
        hashTimer.Stop();
        var sourceHashCost = new { files = hashes.Length, elapsedMs = hashTimer.Elapsed.TotalMilliseconds,
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - hashAllocated, mode = "stream", completeInventory = true };
        var metadata = new
        {
            schemaVersion = 1, startedAt, finishedAt = DateTimeOffset.UtcNow, commit = await Run("git", "rev-parse", "HEAD"),
            dirtyFiles = (await Run("git", "status", "--porcelain")).Split('\n', StringSplitOptions.RemoveEmptyEntries), filter,
            sdk = await Run("dotnet", "--version"), runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runner = Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") ?? "local", image = Environment.GetEnvironmentVariable("ImageOS"),
            imageVersion = Environment.GetEnvironmentVariable("ImageVersion"), workflowCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            reportSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(report))), sourceHashes = hashes, sourceHashCost
        };
        await File.WriteAllTextAsync(report + ".metadata.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }
}
