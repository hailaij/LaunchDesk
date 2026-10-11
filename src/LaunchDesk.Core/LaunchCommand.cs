namespace LaunchDesk.Core;

public sealed record LaunchCommand(string FileName, string Arguments, string Directory)
{
    public static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    public static string Resolve(string name, string directory)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
        {
            var path = Path.GetFullPath(name, directory);
            if (File.Exists(path)) return path;
            throw new FileNotFoundException("找不到启动资源。", path);
        }
        var suffixes = Path.HasExtension(name) ? new[] { "" } : new[] { "", ".exe", ".cmd", ".bat" };
        foreach (var root in new[] { directory }.Concat((System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
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
        var directory = string.IsNullOrWhiteSpace(item.WorkingDirectory)
            ? (Path.IsPathRooted(item.Target) ? Path.GetDirectoryName(item.Target)! : System.Environment.CurrentDirectory)
            : System.Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException("工作目录不存在：" + directory);
        var target = System.Environment.ExpandEnvironmentVariables(item.Target);
        var args = item.Arguments;
        return item.Kind switch
        {
            LaunchKind.Batch => new(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
                "/d /s /c \"" + Quote(Resolve(target, directory)) + (args.Length > 0 ? " " + args : "") + "\"", directory),
            LaunchKind.Command => new(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"),
                "/d /s /c \"" + (target.Contains(' ') && File.Exists(target) ? Quote(target) : target) +
                (args.Length > 0 ? " " + args : "") + "\"", directory),
            LaunchKind.PowerShell => new(Resolve(string.IsNullOrWhiteSpace(item.Interpreter) ? "powershell.exe" : item.Interpreter, directory),
                "-NoLogo -NoProfile -File " + Quote(Resolve(target, directory)) + " " + args, directory),
            LaunchKind.Shell => new(Resolve(string.IsNullOrWhiteSpace(item.Interpreter) ? throw new InvalidOperationException("Shell 脚本需要指定解释器路径。") : item.Interpreter, directory),
                Quote(Resolve(target, directory)) + " " + args, directory),
            _ => new(Resolve(target, directory), args, directory)
        };
    }
}
