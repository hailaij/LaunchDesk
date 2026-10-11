using static LaunchDesk.UiText;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LaunchDesk.Core;

namespace LaunchDesk;

public sealed class UpdateDialog : Window
{
    private readonly ReleaseUpdate release;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Button download = new() { Content = T("自动下载"), MinWidth = 100 };
    private readonly Button cancel = new() { Content = T("取消下载"), Visibility = Visibility.Collapsed };
    private readonly Button openFolder = new() { Content = T("打开下载目录"), Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private VerifiedDownload? result;
    private bool downloading;
    public UpdateDialog(ReleaseUpdate release)
    {
        this.release = release;
        Title = T("LaunchDesk 更新"); Width = 560; Height = 460; MinWidth = 440; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var content = new StackPanel();
        FormLayout.Section(content, T("发现新版本 ") + release.Tag);
        content.Children.Add(new TextBlock { Text = T("当前版本 v") + typeof(App).Assembly.GetName().Version!.ToString(3), Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBox { Text = release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(status); content.Children.Add(progress);
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        download.Style = (Style)Application.Current.FindResource("PrimaryButton");
        download.IsEnabled = release.Installer is not null && AuthenticodeVerifier.IsConfigured;
        status.Text = !AuthenticodeVerifier.IsConfigured ? T("发行者证书尚未配置，应用内下载已禁用。可打开发布页面查看。")
            : release.Installer is null ? T("发布包缺少安装文件或校验信息，应用内下载已禁用。") : T("可下载并验证更新安装包。");
        download.Click += async (_, _) => await DownloadAsync();
        cancel.Click += (_, _) => { cancellation.Cancel(); cancel.IsEnabled = false; status.Text = T("正在取消并清理临时文件…"); };
        openFolder.Click += (_, _) => { if (result is not null) Open(Path.GetDirectoryName(result.Path)!); };
        var page = new Button { Content = T("查看发布页面") }; page.Click += (_, _) => Open(release.Page.AbsoluteUri);
        var later = new Button { Content = T("稍后"), IsCancel = true }; later.Click += (_, _) => Close();
        foreach (var button in new[] { download, cancel, openFolder, page, later }) actions.Children.Add(button);
        content.Children.Add(actions);
        var surface = new Border { Padding = new Thickness(24), Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        surface.SetResourceReference(Border.BackgroundProperty, "Canvas"); Content = surface;
        Closing += (_, _) => cancellation.Cancel();
    }
    private async Task DownloadAsync()
    {
        if (downloading) return;
        downloading = true; download.IsEnabled = false; cancel.Visibility = progress.Visibility = Visibility.Visible;
        status.Text = T("正在下载更新…");
        var reporting = new Progress<DownloadProgress>(value =>
        {
            if (!IsVisible || cancellation.IsCancellationRequested) return;
            progress.Value = value.Received * 100d / value.Total;
            status.Text = value.Received == value.Total ? T("下载完成，正在验证 SHA-256 和发行者签名…") : F("正在下载 {0:F1} / {1:F1} MiB", value.Received / 1048576d, value.Total / 1048576d);
        });
        try
        {
            result = await new UpdateDownloader().DownloadAsync(release, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk-updates"), reporting, cancellation.Token);
            if (!IsVisible) return;
            status.Text = T("下载和签名验证通过，发行者：") + result.Publisher + T("。当前程序保持运行，安装需手动确认。");
            openFolder.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { if (IsVisible) status.Text = T("下载已取消或超时，临时文件已清理；旧版本保持不变。"); }
        catch (Exception error) { if (IsVisible) status.Text = T("更新未完成：") + ErrorText(error.Message) + T("\n旧版本和配置未被替换。"); }
        finally
        {
            downloading = false; cancel.Visibility = progress.Visibility = Visibility.Collapsed;
            if (result is null && !cancellation.IsCancellationRequested) download.IsEnabled = release.Installer is not null && AuthenticodeVerifier.IsConfigured;
        }
    }
    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("无法打开"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
