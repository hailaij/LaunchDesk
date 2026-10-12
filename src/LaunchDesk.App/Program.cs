using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Windows;
using LaunchDesk.Core;

namespace LaunchDesk;
public static class Program
{
    public static string HostPath => Path.Combine(AppContext.BaseDirectory, "LaunchDesk.Host.exe");
    public static Task<IManagedProcess> LaunchAsync(LaunchItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return item.RequireElevation ? ElevationBroker.StartAsync(item, HostPath, token)
            : Task.FromResult<IManagedProcess>(new NativeProcess(item));
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (args.Length == 2 && args[0] == "--signal" && int.TryParse(args[1], out var pid))
        { var sent = NativeProcess.SendControlSignal(pid, out var detail); if (!sent) Console.Error.WriteLine(detail); Thread.Sleep(100); return sent ? 0 : 1; }
        var smoke = args.Length == 2 && args[0] is "--ui-smoke" or "--ui-smoke-min" or "--ui-smoke-en" or "--ui-smoke-en-min" or "--ui-fault-loaded" or "--ui-fault-dispatcher";
        var name = "Local\\LaunchDesk-" + WindowsIdentity.GetCurrent().User!.Value + (smoke ? "-smoke-" + Guid.NewGuid() : "");
        using var mutex = new Mutex(true, name, out var created);
        // Installers must not replace files while the application owns managed processes.
        using var installerGuard = smoke ? null : new Mutex(false, "Local\\LaunchDesk-InstallerGuard");
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
        if (!created && !smoke)
        {
            var currentVersion = typeof(App).Assembly.GetName().Version!.ToString(3);
            foreach (var process in Process.GetProcessesByName("LaunchDesk"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (path is null) continue;
                        var runningVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "未知";
                        if (runningVersion.Split('+')[0] != currentVersion || !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                            MessageBox.Show($"已有 LaunchDesk 实例正在运行，当前启动不会替换它。\n\n运行中：{runningVersion}\n位置：{path}\n\n准备启动：v{currentVersion}\n位置：{Environment.ProcessPath}\n\n请先从旧实例的托盘菜单选择“退出”，再打开新版本。退出将停止旧实例管理的启动项。", "LaunchDesk 版本提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            activate.Set(); return 0;
        }
        var app = new App(); app.InitializeComponent();
        try
        {
            var basePath = AppContext.BaseDirectory;
            var portable = File.Exists(Path.Combine(basePath, "portable.flag"));
            var root = smoke ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "ui-smoke-data", Guid.NewGuid().ToString("N"))
                : DataLocation.Resolve(basePath, portable);
            var store = new ConfigurationStore(root);
            Configuration config;
            try { config = store.Load(); }
            catch (Exception error)
            {
                MessageBox.Show("配置读取失败，原文件已保留。请恢复备份或修正配置后重试。\n" + error.Message, "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            if (smoke && args[0].Contains("-en")) config.Preferences.Language = "en-US";
            if (store.RecoveredFrom is not null)
            {
                UiText.Apply(config.Preferences.Language);
                MessageBox.Show(UiText.F("已从备份恢复配置：{0}\n损坏原件已保留：{1}\n本次启动不会自动启动任何启动项，请检查恢复内容。", store.RecoveredFrom, store.CorruptArchive ?? ""), "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            var window = new MainWindow(store, config, portable, smoke ? args[1] : null, args.Contains("--autostart"));
            app.ConfigureErrors(new ErrorDiagnostics(root), window);
            if (smoke && args[0].StartsWith("--ui-fault-")) window.FaultProbe = args[0];
            if (smoke && args[0].EndsWith("-min")) { window.Width = 940; window.Height = 640; }
            var registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => app.Dispatcher.BeginInvoke(window.Restore), null, -1, false);
            try { return app.Run(window); } finally { registration.Unregister(null); }
        }
        catch (Exception error)
        {
            if (smoke) File.WriteAllText(Path.GetFullPath(args[1]) + ".error.txt", error.ToString());
            else MessageBox.Show(error.ToString(), "LaunchDesk 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally { if (created) mutex.ReleaseMutex(); }
    }
    public static async Task SignalAsync(int pid)
    {
        var path = Environment.ProcessPath!;
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        // Framework-dependent development launches may use the dotnet host.
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--signal"); start.ArgumentList.Add(pid.ToString());
        using var helper = Process.Start(start) ?? throw new IOException("无法启动退出信号助手。");
        var diagnostic = helper.StandardError.ReadToEndAsync();
        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { if (!helper.HasExited) helper.Kill(); await helper.WaitForExitAsync(); await diagnostic; throw; }
        var detail = await diagnostic;
        if (helper.ExitCode != 0) throw new IOException("Ctrl+C 信号发送失败：" + detail.Trim());
    }
}
