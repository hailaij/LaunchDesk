using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public static class LaunchPreflight
{
    public static void Check(LaunchItem item)
    {
        var command = LaunchCommand.From(item);
        if (item.Kind != LaunchKind.Command) return;
        var text = Environment.ExpandEnvironmentVariables(item.Target) + " " + item.Arguments;
        // Complex cmd expressions belong to cmd.exe; do not reinterpret their syntax.
        if (text.IndexOfAny(['&', '|', '<', '>', '^', '%', '(', ')']) >= 0) return;
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        if (File.Exists(target)) text = LaunchCommand.Quote(target) + " " + item.Arguments;
        var tokens = Regex.Matches(text, "\"([^\"]*)\"|([^\\s\"]+)")
            .Select(x => x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value).ToArray();
        if (tokens.Length == 0) throw new InvalidDataException("启动命令为空。");
        var builtins = new[] { "echo", "cd", "chdir", "dir", "set", "setlocal", "endlocal", "call", "start", "exit", "pause", "title", "cls", "rem", "if", "for", "pushd", "popd", "type", "copy", "del", "erase", "ren", "rename", "md", "mkdir", "rd", "rmdir", "move", "path", "prompt", "assoc", "ftype", "ver", "vol", "date", "time", "verify", "color", "break", "shift", "goto" };
        if (!builtins.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
            _ = LaunchCommand.Resolve(tokens[0], command.Directory, item.Environment.FirstOrDefault(x => x.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase)).Value);
        if (!Path.GetFileNameWithoutExtension(tokens[0]).Equals("npm", StringComparison.OrdinalIgnoreCase)) return;
        var directory = command.Directory;
        var prefix = Array.FindIndex(tokens, x => x is "--prefix" or "-C");
        if (prefix >= 0)
        {
            if (prefix + 1 >= tokens.Length) throw new InvalidDataException("npm --prefix 缺少目录。");
            directory = Path.GetFullPath(tokens[prefix + 1], directory);
        }
        var run = Array.FindIndex(tokens, x => x is "run" or "run-script");
        var script = run >= 0 && run + 1 < tokens.Length && !tokens[run + 1].StartsWith('-') ? tokens[run + 1]
            : tokens.Skip(1).FirstOrDefault() is "start" or "stop" or "restart" or "test" ? tokens[1] : null;
        if (script is null) return;
        var packagePath = Path.Combine(directory, "package.json");
        if (!File.Exists(packagePath)) throw new InvalidDataException("npm 工作目录没有 package.json：" + directory);
        if (new FileInfo(packagePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("package.json 超过 4 MiB。");
        using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
        var exists = package.RootElement.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object
            && scripts.TryGetProperty(script, out var entry) && entry.ValueKind == JsonValueKind.String;
        if (!exists && tokens.Contains("--if-present")) return;
        if (!exists && script == "start" && File.Exists(Path.Combine(directory, "server.js"))) return;
        if (!exists) throw new InvalidDataException("package.json 未定义 scripts." + script + "；请检查工作目录或启动命令。");
    }
}
