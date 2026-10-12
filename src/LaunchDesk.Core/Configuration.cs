using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDesk.Core;

public enum LaunchKind { Batch, Executable, Command, PowerShell, Shell }
public enum RunStatus { Stopped, Starting, Running, Stopping, Exited, Failed, Restarting }
public enum ReadinessKind { None, Tcp, Http, Log }

public sealed class ReadinessOptions
{
    public ReadinessKind Kind { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3000;
    public string Url { get; set; } = "http://127.0.0.1:3000/";
    public string Keyword { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class LaunchItem
{
    public Guid? FolderId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新启动项";
    public string Note { get; set; } = "";
    public LaunchKind Kind { get; set; } = LaunchKind.Batch;
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Interpreter { get; set; } = "";
    public string Encoding { get; set; } = "auto";
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Guid> Dependencies { get; set; } = [];
    public bool AutoStart { get; set; }
    public bool AutoRestart { get; set; }
    public bool ShowWindow { get; set; }
    public int DelaySeconds { get; set; }
    public int StartupOrder { get; set; }
    public int StopTimeoutSeconds { get; set; } = 10;
    public string StopInput { get; set; } = "";
    public int StopInputDelaySeconds { get; set; }
    public bool SendStopSignalBeforeInput { get; set; }
    public ReadinessOptions Readiness { get; set; } = new();
}

public sealed class Preferences
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public bool StartAtLogin { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;
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
    public IEnumerable<LaunchItem> StartupItems() => Items.OrderBy(x => x.StartupOrder);
    public void Rename(Guid id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || name.Contains('\0')) throw new InvalidDataException("名称须为 1–100 个字符。");
        if (Folders.FirstOrDefault(x => x.Id == id) is { } folder)
        {
            if (Folders.Any(x => x.Id != id && x.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("文件夹名称重复。");
            folder.Name = name;
        }
        else Items.Single(x => x.Id == id).Name = name;
    }
    public void DissolveFolder(Guid id)
    {
        Folders.RemoveAll(x => x.Id == id);
        foreach (var item in Items.Where(x => x.FolderId == id)) item.FolderId = null;
    }
    public void MoveItem(Guid id, Guid? folderId, Guid? beforeId = null)
    {
        if (folderId is { } folder && !Folders.Any(x => x.Id == folder)) throw new InvalidOperationException("目标文件夹不存在。");
        var item = Items.Single(x => x.Id == id);
        if (beforeId == id) return;
        var before = beforeId.HasValue ? Items.Single(x => x.Id == beforeId) : null;
        if (before is not null && before.FolderId != folderId) throw new InvalidOperationException("排序目标不属于该文件夹。");
        Items.Remove(item); item.FolderId = folderId;
        if (before is null) Items.Add(item); else Items.Insert(Items.IndexOf(before), item);
    }
    public void MoveFolder(Guid id, Guid? beforeId = null)
    {
        var folder = Folders.Single(x => x.Id == id);
        if (beforeId == id) return;
        var before = beforeId.HasValue ? Folders.Single(x => x.Id == beforeId) : null;
        Folders.Remove(folder);
        if (before is null) Folders.Add(folder); else Folders.Insert(Folders.IndexOf(before), folder);
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
    public string? RecoveredFrom { get; private set; }
    public string? CorruptArchive { get; private set; }
    public ConfigurationStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
    public Configuration Load()
    {
        RecoveredFrom = CorruptArchive = null;
        if (!File.Exists(ConfigPath)) return new();
        try { return Read(ConfigPath); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            // Future schemas must not be replaced with an older backup.
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in document.RootElement.EnumerateObject())
                        if (property.Name.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Number
                            && property.Value.TryGetInt32(out var version) && version > 1) throw;
            }
            catch (JsonException) { }
            var backupRoot = Path.Combine(Root, "backups");
            var paths = new[] { backupRoot, Path.Combine(backupRoot, "manual") }
                .Where(Directory.Exists).SelectMany(path => new DirectoryInfo(path).GetFiles("config-*.json"))
                .OrderByDescending(x => x.LastWriteTimeUtc);
            foreach (var path in paths)
            {
                Configuration restored;
                try { restored = Read(path.FullName); }
                catch (Exception backupError) when (backupError is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { continue; }
                var archive = Path.Combine(Root, $"config.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.Copy(ConfigPath, archive, false);
                Save(restored);
                RecoveredFrom = path.FullName; CorruptArchive = archive;
                return restored;
            }
            throw;
        }
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
        if (config.Preferences.Language is not ("zh-CN" or "en-US"))
            throw new InvalidDataException("语言设置无效。");
        if (config.Folders.Count > 1000 || config.Folders.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Name.Contains('\0'))
            || config.Folders.Select(x => x.Id).Distinct().Count() != config.Folders.Count
            || config.Folders.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Folders.Count)
            throw new InvalidDataException("文件夹名称为空、重复、过长或 ID 无效。");
        if (config.Items.Count > 1000 || config.Items.Any(x => x is null) ||
            config.Items.Select(x => x.Id).Distinct().Count() != config.Items.Count)
            throw new InvalidDataException("启动项为空、过多或 ID 重复。");
        foreach (var item in config.Items)
        {
            if (item.Readiness is not { } readiness || !Enum.IsDefined(readiness.Kind)
                || readiness.TimeoutSeconds is < 1 or > 300
                || readiness.Host is null || readiness.Url is null || readiness.Keyword is null
                || new[] { readiness.Host, readiness.Url, readiness.Keyword }.Any(x => x.Contains('\0')))
                throw new InvalidDataException("就绪检测配置无效：" + item.Name);
            if (readiness.Kind == ReadinessKind.Tcp && (string.IsNullOrWhiteSpace(readiness.Host) || readiness.Port is < 1 or > 65535))
                throw new InvalidDataException("TCP 就绪检测需要主机和有效端口。");
            if (readiness.Kind == ReadinessKind.Http && (!Uri.TryCreate(readiness.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0))
                throw new InvalidDataException("HTTP 就绪检测需要不含用户名密码的 http/https 地址。");
            if (readiness.Kind == ReadinessKind.Log && (string.IsNullOrEmpty(readiness.Keyword) || readiness.Keyword.Length > 4096))
                throw new InvalidDataException("日志就绪关键词长度须为 1–4096 个字符。");
            if (config.Folders.Any(x => x.Id == item.Id)) throw new InvalidDataException("文件夹与启动项 ID 重复。");
            if (item.FolderId is { } folder && !config.Folders.Any(x => x.Id == folder))
                throw new InvalidDataException("启动项所属文件夹不存在：" + item.Name);
            if (item.Id == Guid.Empty || !Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.Target) || item.Environment is null || item.Dependencies is null
                || item.DelaySeconds is < 0 or > 3600 || item.StopTimeoutSeconds is < 1 or > 300 || item.StartupOrder is < 0 or > 9999
                || item.StopInputDelaySeconds is < 0 or > 300 || item.Note is null || item.Note.Length > 1000)
                throw new InvalidDataException("启动项配置无效：" + item.Name);
            foreach (var value in new[] { item.Name, item.Note, item.Target, item.Arguments, item.WorkingDirectory, item.Interpreter, item.StopInput })
                if (value is null || value.Contains('\0')) throw new InvalidDataException("配置包含空值或 NUL。");
            _ = OutputDecoder.Resolve(item.Encoding);
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
    public string BackupNow(Configuration config)
    {
        Validate(config);
        var directory = Path.Combine(Root, "backups", "manual"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"config-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, config, JsonOptions); stream.Flush(true);
        return path;
    }
}
