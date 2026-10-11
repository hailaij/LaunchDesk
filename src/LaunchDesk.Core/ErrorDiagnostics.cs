using System.Text;

namespace LaunchDesk.Core;

public sealed class ErrorDiagnostics(string root)
{
    private readonly object gate = new();
    public string PathName => Path.Combine(root, "diagnostics", "errors.log");
    public bool Write(string context, Exception error)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 2 * 1024 * 1024)
                {
                    for (var i = 2; i >= 1; i--)
                        if (File.Exists(PathName + "." + i)) File.Move(PathName + "." + i, PathName + "." + (i + 1), true);
                    File.Move(PathName, PathName + ".1", true);
                }
                File.AppendAllText(PathName, $"[{DateTimeOffset.Now:O}] [{context}]\n{error}\n", new UTF8Encoding(false));
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
