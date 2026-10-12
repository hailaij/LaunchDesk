using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using static LaunchDesk.UiText;

namespace LaunchDesk;

public sealed class ServiceDialog : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel actions = new();
    private readonly Func<ProcessStartInfo, Process?> start;
    public ServiceDialog() : this(Process.Start) { }
    internal ServiceDialog(Func<ProcessStartInfo, Process?> startProcess)
    {
        start = startProcess;
        Title = T("Windows 服务管理"); Width = 560; Height = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(status); panel.Children.Add(actions);
        foreach (var entry in new[] { ("install", T("安装服务")), ("configure", T("替换服务配置")), ("start", T("启动服务")), ("stop", T("停止服务")), ("uninstall", T("卸载服务")) })
        {
            var button = new Button { Content = entry.Item2, Margin = new Thickness(0, 8, 0, 0) };
            button.Click += async (_, _) => await ExecuteAsync(entry.Item1);
            actions.Children.Add(button);
        }
        var refresh = new Button { Content = T("刷新"), Margin = new Thickness(0, 8, 0, 0) };
        refresh.Click += async (_, _) => await RefreshAsync(); panel.Children.Add(refresh);
        Content = panel; Loaded += async (_, _) => await RefreshAsync();
    }
    private async Task ExecuteAsync(string command)
    {
        string? path = null;
        if (command is "install" or "configure")
        {
            var picker = new OpenFileDialog { Filter = T("JSON 配置 (*.json)|*.json") };
            if (picker.ShowDialog(this) != true) return;
            path = picker.FileName;
        }
        if (MessageBox.Show(this, T("此操作需要管理员授权。服务使用独立配置和 LocalService 账号；不会更改桌面配置。是否继续？"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        actions.IsEnabled = false;
        try
        {
            var info = new ProcessStartInfo(Program.HostPath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            info.ArgumentList.Add("service"); info.ArgumentList.Add(command);
            if (path is not null) info.ArgumentList.Add(path);
            info.ArgumentList.Add("--interactive");
            using var helper = start(info) ?? throw new IOException(T("服务操作失败"));
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0) status.Text = T("服务操作失败"); else await RefreshAsync();
        }
        catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { actions.IsEnabled = true; }
    }
    private async Task RefreshAsync()
    {
        try
        {
            var info = new ProcessStartInfo(Program.HostPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("service"); info.ArgumentList.Add("status");
            using var helper = start(info) ?? throw new IOException(T("服务操作失败"));
            var output = helper.StandardOutput.ReadToEndAsync(); var error = helper.StandardError.ReadToEndAsync();
            await helper.WaitForExitAsync();
            var stdout = (await output).Trim(); var stderr = (await error).Trim();
            status.Text = helper.ExitCode == 0 ? T("服务状态：") + stdout :
                T("服务操作失败") + $" ({helper.ExitCode})\n" + (stderr.Length > 0 ? stderr : stdout);
        }
        catch (Exception error) { status.Text = ErrorText(error.Message); }
    }
}
