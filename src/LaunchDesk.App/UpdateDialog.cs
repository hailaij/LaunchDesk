using static LaunchDesk.UiText;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LaunchDesk.Core;

namespace LaunchDesk;

public sealed class UpdateDialog : Window
{
    public UpdateDialog(ReleaseUpdate release)
    {
        Title = T("LaunchDesk 更新"); Width = 560; Height = 390; MinWidth = 440; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var content = new StackPanel();
        FormLayout.Section(content, T("发现新版本 ") + release.Tag);
        content.Children.Add(new TextBlock { Text = T("当前版本 v") + typeof(App).Assembly.GetName().Version!.ToString(3), Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBox { Text = release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock { Text = T("请在浏览器中下载并手动安装更新。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var page = new Button { Content = T("打开下载页"), Style = (Style)Application.Current.FindResource("PrimaryButton") };
        page.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(UpdateChecker.ReleasePage(release.Tag).AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("无法打开"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        var later = new Button { Content = T("稍后"), IsCancel = true }; later.Click += (_, _) => Close();
        actions.Children.Add(page); actions.Children.Add(later); content.Children.Add(actions);
        var surface = new Border { Padding = new Thickness(24), Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        surface.SetResourceReference(Border.BackgroundProperty, "Canvas"); Content = surface;
    }
}
