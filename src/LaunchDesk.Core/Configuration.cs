using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDesk.Core;

public enum LaunchKind { Batch, Executable, Command, PowerShell, Shell }
public enum RunStatus { Stopped, Starting, Running, Stopping, Exited, Failed, Restarting }

public sealed class LaunchItem
{
    public Guid? FolderId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新启动项";
    public LaunchKind Kind { get; set; } = LaunchKind.Batch;
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Interpreter { get; set; } = "";
    public string Encoding { get; set; } = "utf-8";
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Guid> Dependencies { get; set; } = [];
    public bool AutoStart { get; set; }
    public bool AutoRestart { get; set; }
    public bool ShowWindow { get; set; }
    public int DelaySeconds { get; set; }
    public int StopTimeoutSeconds { get; set; } = 10;
    public string StopInput { get; set; } = "";
}

public sealed class Preferences
{
    public string Theme { get; set; } = "System";
    public bool StartAtLogin { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public int LogDays { get; set; } = 7;
    public int LogLimitMiB { get; set; } = 200;
}

public sealed class LaunchFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新文件夹";
    public override string ToString() => Name;
}

public sealed class Configuration
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Preferences { get; set; } = new();
    public List<LaunchItem> Items { get; set; } = [];
    public List<LaunchFolder> Folders { get; set; } = [];
    public void DissolveFolder(Guid id)
    {
        Folders.RemoveAll(x => x.Id == id);
        foreach (var item in Items.Where(x => x.FolderId == id)) item.FolderId = null;
    }
}

public sealed class ConfigurationStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };
    public string Root { get; }
    public string ConfigPath => Path.Combine(Root, "config.json");
    public ConfigurationStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
    public Configuration Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        return Read(ConfigPath);
    }
    public static Configuration Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("配置文件超过 4 MiB。");
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("配置为空。");
        Validate(config);
        return config;
    }
    public static LaunchItem Clone(LaunchItem item) => JsonSerializer.Deserialize<LaunchItem>(
        JsonSerializer.Serialize(item, JsonOptions), JsonOptions)!;
    public static void Validate(Configuration config)
    {
        if (config.SchemaVersion != 1 || config.Preferences is null || config.Items is null || config.Folders is null)
            throw new InvalidDataException("不支持的配置格式。");
        if (config.Preferences.LogDays is < 1 or > 365 || config.Preferences.LogLimitMiB is < 10 or > 10240)
            throw new InvalidDataException("日志保留范围无效。");
        if (config.Preferences.Theme is not ("System" or "Light" or "Dark"))
            throw new InvalidDataException("主题设置无效。");
        if (config.Folders.Count > 1000 || config.Folders.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Name.Contains('\0'))
            || config.Folders.Select(x => x.Id).Distinct().Count() != config.Folders.Count
            || config.Folders.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Folders.Count)
            throw new InvalidDataException("文件夹名称为空、重复、过长或 ID 无效。");
        if (config.Items.Count > 1000 || config.Items.Any(x => x is null) ||
            config.Items.Select(x => x.Id).Distinct().Count() != config.Items.Count)
            throw new InvalidDataException("启动项为空、过多或 ID 重复。");
        foreach (var item in config.Items)
        {
            if (config.Folders.Any(x => x.Id == item.Id)) throw new InvalidDataException("文件夹与启动项 ID 重复。");
            if (item.FolderId is { } folder && !config.Folders.Any(x => x.Id == folder))
                throw new InvalidDataException("启动项所属文件夹不存在：" + item.Name);
            if (item.Id == Guid.Empty || !Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.Target) || item.Environment is null || item.Dependencies is null
                || item.DelaySeconds is < 0 or > 3600 || item.StopTimeoutSeconds is < 1 or > 300)
                throw new InvalidDataException("启动项配置无效：" + item.Name);
            foreach (var value in new[] { item.Name, item.Target, item.Arguments, item.WorkingDirectory, item.Interpreter, item.StopInput })
                if (value is null || value.Contains('\0')) throw new InvalidDataException("配置包含空值或 NUL。");
            _ = System.Text.Encoding.GetEncoding(item.Encoding);
            if (item.Environment.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Contains('=') ||
                x.Key.Contains('\0') || x.Value is null || x.Value.Contains('\0')))
                throw new InvalidDataException("环境变量格式无效。");
            if (item.Dependencies.Any(id => id == item.Id || !config.Items.Any(x => x.Id == id)))
                throw new InvalidDataException("依赖不存在或引用自身：" + item.Name);
        }
        var visiting = new HashSet<Guid>(); var visited = new HashSet<Guid>();
        void Visit(LaunchItem item)
        {
            if (visited.Contains(item.Id)) return;
            if (!visiting.Add(item.Id)) throw new InvalidDataException("启动项依赖存在循环。");
            foreach (var id in item.Dependencies) Visit(config.Items.Single(x => x.Id == id));
            visiting.Remove(item.Id); visited.Add(item.Id);
        }
        foreach (var item in config.Items) Visit(item);
    }
    public void Save(Configuration config)
    {
        Validate(config);
        var temp = ConfigPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, config, JsonOptions); stream.Flush(true);
            }
            if (File.Exists(ConfigPath))
            {
                var backupDir = Path.Combine(Root, "backups"); Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir, $"config-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json");
                File.Replace(temp, ConfigPath, backup);
                try
                {
                    foreach (var old in new DirectoryInfo(backupDir).GetFiles("*.json").OrderByDescending(x => x.LastWriteTimeUtc).Skip(20))
                    { try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            else File.Move(temp, ConfigPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Export(Configuration config, string destination) =>
        File.WriteAllText(destination, JsonSerializer.Serialize(config, JsonOptions), new System.Text.UTF8Encoding(false));
}
