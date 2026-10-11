namespace LaunchDesk.Core;

public static class LaunchItemFactory
{
    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".bat" or ".cmd" or ".exe" or ".ps1" or ".sh";
    public static LaunchItem FromFile(string path, Guid? folderId = null)
    {
        path = Path.GetFullPath(path);
        if (!Supports(path) || !File.Exists(path)) throw new InvalidDataException("请拖入存在的 BAT、CMD、EXE、PS1 或 SH 文件。");
        return new()
        {
            Name = Path.GetFileNameWithoutExtension(path), Target = path, WorkingDirectory = Path.GetDirectoryName(path)!, FolderId = folderId,
            Kind = Path.GetExtension(path).ToLowerInvariant() switch
            { ".bat" or ".cmd" => LaunchKind.Batch, ".ps1" => LaunchKind.PowerShell, ".sh" => LaunchKind.Shell, _ => LaunchKind.Executable }
        };
    }
}
