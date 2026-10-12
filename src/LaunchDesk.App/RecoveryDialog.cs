using System.Windows;
using System.Windows.Controls;
using LaunchDesk.Core;
using static LaunchDesk.UiText;

namespace LaunchDesk;

public sealed class RecoveryDialog : Window
{
    private readonly Dictionary<Guid, CheckBox> choices = [];
    public Guid[] SelectedIds => choices.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray();
    public RecoveryDialog(LaunchItem[] items, int missing)
    {
        Title = T("恢复上次运行项"); Width = 700; Height = 480; MinWidth = 540; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = T("上次会话未正常退出。请选择要按当前配置重新启动的项目；依赖项也会启动。本次不会执行其他自启项。"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var list = new StackPanel();
        foreach (var item in items)
        {
            var box = new CheckBox { Content = new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap },
                IsChecked = true, ToolTip = item.Target, Margin = new Thickness(0, 8, 0, 8) };
            choices[item.Id] = box; list.Children.Add(box);
        }
        if (items.Length == 0) list.Children.Add(new TextBlock { Text = T("没有可恢复的启动项。"), TextWrapping = TextWrapping.Wrap });
        if (missing > 0) list.Children.Add(new TextBlock { Text = F("{0} 个已删除或不匹配的项目不会恢复。", missing), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var skip = new Button { Content = T("不恢复"), MinWidth = 90, IsCancel = true }; skip.Click += (_, _) => DialogResult = false;
        var restore = new Button { Content = T("恢复所选"), MinWidth = 100, IsEnabled = items.Length > 0,
            Style = (Style)Application.Current.FindResource("PrimaryButton") };
        foreach (var box in choices.Values)
        {
            box.Checked += (_, _) => restore.IsEnabled = SelectedIds.Length > 0;
            box.Unchecked += (_, _) => restore.IsEnabled = SelectedIds.Length > 0;
            box.Indeterminate += (_, _) => restore.IsEnabled = SelectedIds.Length > 0;
        }
        restore.Click += (_, _) => { if (SelectedIds.Length > 0) DialogResult = true; };
        buttons.Children.Add(skip); buttons.Children.Add(restore); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        var surface = new Border { Child = grid }; surface.SetResourceReference(Border.BackgroundProperty, "Canvas"); Content = surface;
    }
}
