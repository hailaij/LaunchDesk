using System.Text.Json;

namespace LaunchDesk.Core;

public static class DataLocation
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk");
    public static string LocatorPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk-location.json");
    public static string Resolve(string basePath, bool portable, string? locatorPath = null)
    {
        if (portable) return Path.Combine(basePath, "data");
        var path = locatorPath ?? LocatorPath;
        if (!File.Exists(path)) return DefaultRoot;
        var root = JsonSerializer.Deserialize<string>(File.ReadAllText(path));
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new InvalidDataException("数据目录记录无效，原数据未被修改。");
        return Path.GetFullPath(root);
    }
    public static void Save(string root, string? locatorPath = null)
    {
        root = Path.GetFullPath(root);
        var path = locatorPath ?? LocatorPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, root); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void ValidateDestination(string source, string destination)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)) return;
        if (source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("新旧数据目录不能互相包含。");
        for (var directory = new DirectoryInfo(destination); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("数据目录不能经过符号链接或目录联接。");
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException("请选择空目录，以免覆盖已有数据。");
    }
    public static ConfigurationStore CopyTo(ConfigurationStore source, Configuration config, string destination)
    {
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        ValidateDestination(source.Root, destination);
        if (source.Root.Equals(destination, StringComparison.OrdinalIgnoreCase)) return source;
        var staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        var destinationRemoved = false;
        try
        {
            // Copy into an isolated sibling first. Failed copies remain recoverable and never become the active directory.
            void CopyDirectory(string from, string to)
            {
                if (File.GetAttributes(from).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("迁移不支持符号链接：" + from);
                Directory.CreateDirectory(to);
                foreach (var file in Directory.GetFiles(from))
                {
                    if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("迁移不支持链接文件：" + file);
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
                }
                foreach (var directory in Directory.GetDirectories(from)) CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
            }
            foreach (var name in new[] { "logs", "backups" })
                if (Directory.Exists(Path.Combine(source.Root, name))) CopyDirectory(Path.Combine(source.Root, name), Path.Combine(staging, name));
            new ConfigurationStore(staging).Save(config);
            ValidateDestination(source.Root, destination);
            if (Directory.Exists(destination)) { Directory.Delete(destination, false); destinationRemoved = true; }
            Directory.Move(staging, destination);
            return new ConfigurationStore(destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            string recovery = "";
            if (destinationRemoved && !Directory.Exists(destination) && !File.Exists(destination))
            {
                try { Directory.CreateDirectory(destination); }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                { recovery = "目标空目录恢复失败：" + restoreError.Message + "\n"; }
            }
            throw new IOException($"数据迁移失败，原数据目录未被修改：{source.Root}\n{recovery}暂存数据保留在：{staging}\n原因：{error.Message}", error);
        }
    }
}
