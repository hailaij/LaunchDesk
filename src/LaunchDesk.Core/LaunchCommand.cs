namespace LaunchDesk.Core;

public sealed record LaunchCommand(string FileName, string Arguments, string Directory)
{
    public static string Quote(string value)
    {
        // Windows argv parsing doubles trailing slashes and slashes preceding a quote.
        var result = new System.Text.StringBuilder("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    internal static string CommandTarget(string target, string directory)
    {
        // cmd uses its own quoting grammar, not the CRT argv escaping above.
        var path = Path.IsPathRooted(target) ? target : Path.Combine(directory, target);
        return File.Exists(path) ? "\"" + Path.GetFullPath(path) + "\"" : target;
    }
    public static string Resolve(string name, string directory, string? pathOverride = null)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
        {
            var path = Path.GetFullPath(name, directory);
            if (File.Exists(path)) return path;
            throw new FileNotFoundException("找不到启动资源。", path);
        }
        var suffixes = Path.HasExtension(name) ? new[] { "" } : new[] { "", ".exe", ".cmd", ".bat" };
        foreach (var root in new[] { directory }.Concat((pathOverride ?? System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
            foreach (var suffix in suffixes)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var candidate = Path.Combine(root.Trim('"'), name + suffix);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        throw new FileNotFoundException("找不到解释器或可执行程序：" + name);
    }
    public static LaunchCommand From(LaunchItem item)
    {
        var target = System.Environment.ExpandEnvironmentVariables(item.Target);
        var directory = string.IsNullOrWhiteSpace(item.WorkingDirectory)
            ? (Path.IsPathRooted(target) ? Path.GetDirectoryName(target)! : System.Environment.CurrentDirectory)
            : System.Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
        directory = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException("工作目录不存在：" + directory);
        string Find(string value) => Resolve(value, directory, item.Environment.FirstOrDefault(x => x.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase)).Value);
        var args = item.Arguments;
        return item.Kind switch
        {
            LaunchKind.Batch => new(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
                "/d /s /c \"\"" + Find(target) + "\"" + (args.Length > 0 ? " " + args : "") + "\"", directory),
            LaunchKind.Command => new(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
                "/d /s /c \"" + CommandTarget(target, directory) +
                (args.Length > 0 ? " " + args : "") + "\"", directory),
            LaunchKind.PowerShell => new(Find(string.IsNullOrWhiteSpace(item.Interpreter) ? "powershell.exe" : item.Interpreter),
                "-NoLogo -NoProfile -File " + Quote(Find(target)) + " " + args, directory),
            LaunchKind.Shell => new(Find(string.IsNullOrWhiteSpace(item.Interpreter) ? throw new InvalidOperationException("Shell 脚本需要指定解释器路径。") : item.Interpreter),
                Quote(Find(target)) + " " + args, directory),
            _ => new(Find(target), args, directory)
        };
    }
}
