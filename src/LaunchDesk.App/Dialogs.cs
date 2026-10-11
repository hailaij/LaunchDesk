using System.Windows;
using System.Windows.Controls;
using LaunchDesk.Core;
using Microsoft.Win32;

namespace LaunchDesk;

internal static class FormLayout
{
    public static void Section(Panel panel, string title)
    {
        var heading = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 16) };
        panel.Children.Add(heading);
    }
    public static void Row(Panel panel, string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(112) }); grid.ColumnDefinitions.Add(new());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 10, 0) });
        Grid.SetColumn(control, 1); grid.Children.Add(control); panel.Children.Add(grid);
    }
    public static TextBox Text(string value, bool multiline = false) => new()
    {
        Text = value, AcceptsReturn = multiline, Height = multiline ? 82 : 34,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
    };
    public static DockPanel Browse(TextBox text, Action pick)
    {
        var panel = new DockPanel();
        var button = new Button { Content = "…", Width = 36, ToolTip = "浏览", Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => pick(); DockPanel.SetDock(button, Dock.Right); panel.Children.Add(button); panel.Children.Add(text); return panel;
    }
    public static void Finish(Window window, Panel form, Action save)
    {
        window.SetResourceReference(Window.IconProperty, "AppIcon");
        var grid = new Grid();
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80 }; cancel.Click += (_, _) => window.Close();
        var commit = new Button { Content = "保存", IsDefault = true, MinWidth = 80, Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton") };
        commit.Click += (_, _) => save(); buttons.Children.Add(cancel); buttons.Children.Add(commit);
        Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        var surface = new Border { Padding = new Thickness(24), Child = grid };
        surface.SetResourceReference(Border.BackgroundProperty, "Canvas"); window.Content = surface;
    }
}

public sealed class ItemDialog : Window
{
    public LaunchItem Item { get; }
    public ItemDialog(LaunchItem item, List<LaunchItem> all, List<LaunchFolder>? folders = null)
    {
        folders ??= [];
        Item = item; Title = "启动项配置"; Width = 760; Height = 760; MinWidth = 620; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel();
        FormLayout.Section(form, "基本信息");
        var name = FormLayout.Text(item.Name); FormLayout.Row(form, "名称", name);
        var folderOptions = new[] { new LaunchFolder { Id = Guid.Empty, Name = "未分组" } }.Concat(folders).ToArray();
        var folder = new ComboBox { ItemsSource = folderOptions, DisplayMemberPath = "Name", SelectedItem = folderOptions.FirstOrDefault(x => x.Id == item.FolderId) ?? folderOptions[0] };
        FormLayout.Row(form, "所属文件夹", folder);
        var kinds = new[] { "BAT / CMD", "可执行程序", "命令（cmd）", "PowerShell 脚本", "Shell 脚本" };
        var kind = new ComboBox { ItemsSource = kinds, SelectedIndex = (int)item.Kind }; FormLayout.Row(form, "类型", kind);
        var target = FormLayout.Text(item.Target);
        FormLayout.Row(form, "资源 / 命令", FormLayout.Browse(target, () =>
        {
            var dialog = new OpenFileDialog { Filter = "启动资源|*.bat;*.cmd;*.exe;*.ps1;*.sh|所有文件|*.*" };
            if (dialog.ShowDialog(this) == true)
            {
                target.Text = dialog.FileName;
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                kind.SelectedIndex = ext switch { ".bat" or ".cmd" => 0, ".ps1" => 3, ".sh" => 4, _ => 1 };
            }
        }));
        var args = FormLayout.Text(item.Arguments); FormLayout.Row(form, "启动参数", args);
        var directory = FormLayout.Text(item.WorkingDirectory);
        FormLayout.Row(form, "工作目录", FormLayout.Browse(directory, () =>
        { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) directory.Text = dialog.FolderName; }));
        var interpreter = FormLayout.Text(item.Interpreter);
        FormLayout.Row(form, "解释器路径", FormLayout.Browse(interpreter, () =>
        { var dialog = new OpenFileDialog { Filter = "解释器|*.exe;*.cmd|所有文件|*.*" }; if (dialog.ShowDialog(this) == true) interpreter.Text = dialog.FileName; }));
        FormLayout.Section(form, "日志与环境");
        var encoding = new ComboBox { ItemsSource = new[] { "utf-8", "gbk", "utf-16", "windows-1252" }, Text = item.Encoding, IsEditable = true };
        FormLayout.Row(form, "日志 / 输入编码", encoding);
        var env = FormLayout.Text(string.Join(Environment.NewLine, item.Environment.Select(x => x.Key + "=" + x.Value)), true);
        FormLayout.Row(form, "环境变量", env);
        FormLayout.Section(form, "启动与停止");
        var dependencies = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 90, DisplayMemberPath = "Name", ItemsSource = all.Where(x => x.Id != item.Id).ToArray() };
        foreach (var dependency in all.Where(x => item.Dependencies.Contains(x.Id))) dependencies.SelectedItems.Add(dependency);
        FormLayout.Row(form, "依赖启动项", dependencies);
        var numeric = new StackPanel { Orientation = Orientation.Horizontal };
        var delay = FormLayout.Text(item.DelaySeconds.ToString()); delay.Width = 70;
        var timeout = FormLayout.Text(item.StopTimeoutSeconds.ToString()); timeout.Width = 70;
        numeric.Children.Add(delay); numeric.Children.Add(new TextBlock { Text = "秒延迟", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 20, 0) });
        numeric.Children.Add(timeout); numeric.Children.Add(new TextBlock { Text = "秒停止超时", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        FormLayout.Row(form, "启动 / 停止", numeric);
        var stop = FormLayout.Text(item.StopInput); FormLayout.Row(form, "退出输入（可选）", stop);
        var options = new WrapPanel();
        var auto = new CheckBox { Content = "随管理器启动", IsChecked = item.AutoStart };
        var restart = new CheckBox { Content = "退出后自动重启", IsChecked = item.AutoRestart };
        var visible = new CheckBox { Content = "显示目标窗口", IsChecked = item.ShowWindow };
        options.Children.Add(auto); options.Children.Add(restart); options.Children.Add(visible); FormLayout.Row(form, "选项", options);
        FormLayout.Finish(this, form, () =>
        {
            try
            {
                item.Name = name.Text.Trim(); item.Kind = (LaunchKind)kind.SelectedIndex; item.Target = target.Text.Trim();
                var folderId = ((LaunchFolder)folder.SelectedItem).Id; item.FolderId = folderId == Guid.Empty ? null : folderId;
                item.Arguments = args.Text; item.WorkingDirectory = directory.Text.Trim(); item.Interpreter = interpreter.Text.Trim(); item.Encoding = encoding.Text.Trim();
                item.DelaySeconds = int.Parse(delay.Text); item.StopTimeoutSeconds = int.Parse(timeout.Text); item.StopInput = stop.Text;
                item.AutoStart = auto.IsChecked == true; item.AutoRestart = restart.IsChecked == true; item.ShowWindow = visible.IsChecked == true;
                item.Dependencies = dependencies.SelectedItems.Cast<LaunchItem>().Select(x => x.Id).ToList();
                item.Environment = new(StringComparer.OrdinalIgnoreCase);
                foreach (var line in env.Text.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var split = line.IndexOf('='); if (split < 1) throw new InvalidDataException("环境变量应为每行 NAME=VALUE。");
                    item.Environment.Add(line[..split].Trim(), line[(split + 1)..]);
                }
                ConfigurationStore.Validate(new() { Folders = folders, Items = all.Where(x => x.Id != item.Id).Append(item).ToList() });
                _ = LaunchCommand.From(item);
                DialogResult = true;
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "配置无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
    }
}

public sealed class FolderDialog : Window
{
    public string FolderName { get; private set; } = "";
    public FolderDialog(string name)
    {
        Title = "文件夹"; Width = 440; Height = 220; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel(); var input = FormLayout.Text(name);
        FormLayout.Row(form, "名称", input);
        FormLayout.Finish(this, form, () =>
        {
            var value = input.Text.Trim();
            if (value.Length is < 1 or > 100 || value.Contains('\0')) { MessageBox.Show(this, "文件夹名称须为 1–100 个字符。", "名称无效"); return; }
            FolderName = value; DialogResult = true;
        });
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}

public sealed class SettingsDialog : Window
{
    public Preferences Preferences { get; private set; } = new();
    public bool Portable { get; private set; }
    public SettingsDialog(Preferences preferences, bool portable)
    {
        Title = "设置"; Width = 550; Height = 570; MinWidth = 480; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel();
        FormLayout.Section(form, "外观");
        var themes = new[] { "System", "Light", "Dark" };
        var theme = new ComboBox { ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = Array.IndexOf(themes, preferences.Theme) };
        FormLayout.Row(form, "主题", theme);
        FormLayout.Section(form, "启动与窗口");
        var login = new CheckBox { Content = "用户登录后启动 LaunchDesk", IsChecked = preferences.StartAtLogin };
        var minimized = new CheckBox { Content = "启动时收起到托盘", IsChecked = preferences.StartMinimized };
        var tray = new CheckBox { Content = "最小化时收起到托盘", IsChecked = preferences.MinimizeToTray };
        var mode = new CheckBox { Content = "便携模式", IsChecked = portable };
        FormLayout.Row(form, "启动", login); FormLayout.Row(form, "窗口", minimized); FormLayout.Row(form, "", tray); FormLayout.Row(form, "数据目录", mode);
        FormLayout.Section(form, "日志保留");
        var days = FormLayout.Text(preferences.LogDays.ToString()); FormLayout.Row(form, "日志保留（天）", days);
        var size = FormLayout.Text(preferences.LogLimitMiB.ToString()); FormLayout.Row(form, "每项上限（MiB）", size);
        FormLayout.Section(form, "关于与开源");
        var about = new Button { Content = "关于 LaunchDesk", HorizontalAlignment = HorizontalAlignment.Left };
        about.Click += (_, _) => new AboutDialog { Owner = this }.ShowDialog();
        FormLayout.Row(form, "LaunchDesk", about);
        FormLayout.Finish(this, form, () =>
        {
            try
            {
                var value = new Preferences { Theme = themes[theme.SelectedIndex], StartAtLogin = login.IsChecked == true, StartMinimized = minimized.IsChecked == true,
                    MinimizeToTray = tray.IsChecked == true, LogDays = int.Parse(days.Text), LogLimitMiB = int.Parse(size.Text) };
                ConfigurationStore.Validate(new() { Preferences = value }); Preferences = value; Portable = mode.IsChecked == true; DialogResult = true;
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
    }
}

public sealed class AboutDialog : Window
{
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly TextBlock updateStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
    private readonly TextBox releaseNotes = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 140,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
    private readonly Button checkUpdate = new() { Content = "检查更新", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button openRelease = new() { Content = "打开下载页", HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private ReleaseUpdate? release;
    public AboutDialog()
    {
        Title = "关于 LaunchDesk"; Width = 560; Height = 680; MinWidth = 480; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var form = new StackPanel();
        FormLayout.Section(form, "LaunchDesk");
        var version = typeof(App).Assembly.GetName().Version!.ToString(3);
        form.Children.Add(new TextBlock { Text = "版本 " + version + "  ·  Windows x64", Margin = new Thickness(0, 0, 0, 16) });
        AddText(form, "Windows 启动项管理器");
        AddText(form, "Copyright (c) 2026 LaunchDesk contributors");
        FormLayout.Section(form, "版本更新");
        checkUpdate.Click += async (_, _) => await CheckForUpdatesAsync();
        openRelease.Click += (_, _) => { if (release is not null) OpenPage(release.Page); };
        form.Children.Add(checkUpdate); form.Children.Add(updateStatus); form.Children.Add(releaseNotes); form.Children.Add(openRelease);
        Closed += (_, _) => updateCancellation.Cancel();
        FormLayout.Section(form, "开源项目");
        AddText(form, "作者 / 维护者：hailaij 与社区贡献者");
        AddText(form, "源代码许可：MIT License");
        AddLink(form, "源代码仓库", "https://github.com/hailaij/LaunchDesk");
        AddLink(form, "问题反馈", "https://github.com/hailaij/LaunchDesk/issues");
        AddLink(form, "版本发布", "https://github.com/hailaij/LaunchDesk/releases");
        FormLayout.Section(form, "第三方组件");
        AddText(form, "使用 Microsoft .NET 10、WPF 与 WinForms。源代码没有第三方 NuGet PackageReference 依赖。自包含发行包的运行时许可和声明位于 runtime-notices 目录。");
        var licenses = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in new[] { ("MIT 许可证", "LICENSE"), ("第三方声明", "THIRD-PARTY-NOTICES.md") })
        {
            var button = new Button { Content = entry.Item1 };
            button.Click += (_, _) => ShowNotice(entry.Item1, entry.Item2);
            licenses.Children.Add(button);
        }
        form.Children.Add(licenses);
        var grid = new Grid(); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var close = new Button { Content = "关闭", IsCancel = true, IsDefault = true, MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        close.Click += (_, _) => Close(); Grid.SetRow(close, 1); grid.Children.Add(close);
        var surface = new Border { Padding = new Thickness(24), Child = grid };
        surface.SetResourceReference(Border.BackgroundProperty, "Canvas"); Content = surface;
    }
    private static void AddText(Panel form, string text) => form.Children.Add(new TextBlock
    { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
    private void AddLink(Panel form, string label, string url)
    {
        var text = new TextBlock { Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        var link = new System.Windows.Documents.Hyperlink { NavigateUri = new Uri(url) };
        link.Inlines.Add(label);
        link.RequestNavigate += (_, e) =>
        {
            OpenPage(e.Uri);
            e.Handled = true;
        };
        text.Inlines.Add(link); form.Children.Add(text);
    }
    private void OpenPage(Uri uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(this, "无法打开浏览器：" + error.Message, "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async Task CheckForUpdatesAsync()
    {
        checkUpdate.IsEnabled = false; release = null;
        releaseNotes.Visibility = openRelease.Visibility = Visibility.Collapsed;
        updateStatus.Text = "正在查询 GitHub 正式发布版本…";
        try
        {
            var result = await UpdateChecker.CheckAsync(updateCancellation.Token);
            if (!updateCancellation.IsCancellationRequested) ApplyUpdateResult(result);
        }
        catch (OperationCanceledException)
        { if (!updateCancellation.IsCancellationRequested) updateStatus.Text = "检查超时，请稍后重试。"; }
        catch (System.Net.Http.HttpRequestException error)
        {
            updateStatus.Text = error.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests
                ? "GitHub 请求受限，请稍后重试或打开版本发布页面。"
                : "无法连接 GitHub，请检查网络后重试。";
        }
        catch (Exception) { updateStatus.Text = "发布信息无法解析，请打开版本发布页面查看。"; }
        finally { checkUpdate.IsEnabled = true; }
    }
    internal void ApplyUpdateResult(ReleaseUpdate? result)
    {
        var current = typeof(App).Assembly.GetName().Version!;
        if (result is null) { updateStatus.Text = "暂无可用的正式发布版本。"; return; }
        if (!result.IsNewerThan(current))
        {
            updateStatus.Text = result.Version == new Version(current.Major, current.Minor, current.Build)
                ? "当前已是最新正式版本（" + result.Tag + "）。" : "当前版本高于最新正式发布版本（" + result.Tag + "）。";
            return;
        }
        release = result;
        updateStatus.Text = "发现新版本 " + result.Tag;
        releaseNotes.Text = result.Notes; releaseNotes.Visibility = openRelease.Visibility = Visibility.Visible;
    }
    private void ShowNotice(string title, string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        try
        {
            var notice = new Window { Title = title, Width = 700, Height = 520, MinWidth = 420, MinHeight = 300,
                Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            notice.SetResourceReference(IconProperty, "AppIcon");
            notice.Content = new TextBox { Text = File.ReadAllText(path), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
            notice.ShowDialog();
        }
        catch (Exception error) { MessageBox.Show(this, "无法读取许可文件：" + error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
