using static LaunchDesk.UiText;
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
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(UiText.Language == "en-US" ? 140 : 112) }); grid.ColumnDefinitions.Add(new());
        grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 10, 0) });
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
        var button = new Button { Content = "…", Width = 36, ToolTip = T("浏览"), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => pick(); DockPanel.SetDock(button, Dock.Right); panel.Children.Add(button); panel.Children.Add(text); return panel;
    }
    public static void Finish(Window window, Panel form, Action save)
    {
        window.SetResourceReference(Window.IconProperty, "AppIcon");
        var grid = new Grid();
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = T("取消"), IsCancel = true, MinWidth = 80 }; cancel.Click += (_, _) => window.Close();
        var commit = new Button { Content = T("保存"), IsDefault = true, MinWidth = 80, Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton") };
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
        Item = item; Title = T("启动项配置"); Width = 760; Height = 760; MinWidth = 620; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel();
        FormLayout.Section(form, T("基本信息"));
        var name = FormLayout.Text(item.Name); FormLayout.Row(form, T("名称"), name);
        var note = FormLayout.Text(item.Note); note.MaxLength = 1000; FormLayout.Row(form, T("备注"), note);
        var folderOptions = new[] { new LaunchFolder { Id = Guid.Empty, Name = T("未分组") } }.Concat(folders).ToArray();
        var folder = new ComboBox { ItemsSource = folderOptions, DisplayMemberPath = "Name", SelectedItem = folderOptions.FirstOrDefault(x => x.Id == item.FolderId) ?? folderOptions[0] };
        FormLayout.Row(form, T("所属文件夹"), folder);
        var kinds = new[] { "BAT / CMD", T("可执行程序"), T("命令（cmd）"), T("PowerShell 脚本"), T("Shell 脚本") };
        var kind = new ComboBox { ItemsSource = kinds, SelectedIndex = (int)item.Kind }; FormLayout.Row(form, T("类型"), kind);
        var target = FormLayout.Text(item.Target);
        FormLayout.Row(form, T("资源 / 命令"), FormLayout.Browse(target, () =>
        {
            var dialog = new OpenFileDialog { Filter = T("启动资源|*.bat;*.cmd;*.exe;*.ps1;*.sh|所有文件|*.*") };
            if (dialog.ShowDialog(this) == true)
            {
                target.Text = dialog.FileName;
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                kind.SelectedIndex = ext switch { ".bat" or ".cmd" => 0, ".ps1" => 3, ".sh" => 4, _ => 1 };
            }
        }));
        var args = FormLayout.Text(item.Arguments); FormLayout.Row(form, T("启动参数"), args);
        var directory = FormLayout.Text(item.WorkingDirectory);
        FormLayout.Row(form, T("工作目录"), FormLayout.Browse(directory, () =>
        { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) directory.Text = dialog.FolderName; }));
        var interpreter = FormLayout.Text(item.Interpreter);
        FormLayout.Row(form, T("解释器路径"), FormLayout.Browse(interpreter, () =>
        { var dialog = new OpenFileDialog { Filter = T("解释器|*.exe;*.cmd|所有文件|*.*") }; if (dialog.ShowDialog(this) == true) interpreter.Text = dialog.FileName; }));
        FormLayout.Section(form, T("日志与环境"));
        var encoding = new ComboBox { ItemsSource = new[] { "auto", "system", "utf-8", "gbk", "utf-16", "windows-1252" }, Text = item.Encoding, IsEditable = true };
        FormLayout.Row(form, T("日志 / 输入编码"), encoding);
        var env = FormLayout.Text(string.Join(Environment.NewLine, item.Environment.Select(x => x.Key + "=" + x.Value)), true);
        FormLayout.Row(form, T("环境变量"), env);
        FormLayout.Section(form, T("启动与停止"));
        var dependencies = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 90, DisplayMemberPath = "Name", ItemsSource = all.Where(x => x.Id != item.Id).ToArray() };
        foreach (var dependency in all.Where(x => item.Dependencies.Contains(x.Id))) dependencies.SelectedItems.Add(dependency);
        FormLayout.Row(form, T("依赖启动项"), dependencies);
        var startupOrder = FormLayout.Text(item.StartupOrder.ToString());
        startupOrder.ToolTip = T("0–9999，数字小的先启动；相同数字按列表顺序，显式依赖优先");
        FormLayout.Row(form, T("启动顺序"), startupOrder);
        var numeric = new StackPanel { Orientation = Orientation.Horizontal };
        var delay = FormLayout.Text(item.DelaySeconds.ToString()); delay.Width = 70;
        var timeout = FormLayout.Text(item.StopTimeoutSeconds.ToString()); timeout.Width = 70;
        numeric.Children.Add(delay); numeric.Children.Add(new TextBlock { Text = T("秒延迟"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 20, 0) });
        numeric.Children.Add(timeout); numeric.Children.Add(new TextBlock { Text = T("秒停止超时"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        FormLayout.Row(form, T("启动 / 停止"), numeric);
        var stop = FormLayout.Text(item.StopInput); FormLayout.Row(form, T("退出输入（可选）"), stop);
        var stopDelay = FormLayout.Text(item.StopInputDelaySeconds.ToString());
        stopDelay.ToolTip = T("0–300 秒；输入发送后才开始计算停止超时，进程提前退出则不再发送");
        FormLayout.Row(form, T("退出输入延迟（秒）"), stopDelay);
        var signalFirst = new CheckBox { Content = T("退出输入前先发送 Ctrl+C"), IsChecked = item.SendStopSignalBeforeInput };
        FormLayout.Row(form, T("退出信号"), signalFirst);
        var options = new WrapPanel();
        var auto = new CheckBox { Content = T("随管理器启动"), IsChecked = item.AutoStart };
        var restart = new CheckBox { Content = T("退出后自动重启"), IsChecked = item.AutoRestart };
        var visible = new CheckBox { Content = T("显示目标窗口"), IsChecked = item.ShowWindow };
        options.Children.Add(auto); options.Children.Add(restart); options.Children.Add(visible); FormLayout.Row(form, T("选项"), options);
        FormLayout.Section(form, T("服务就绪检测"));
        var readinessKind = new ComboBox { ItemsSource = new[] { T("关闭检测"), T("TCP 端口"), T("HTTP 成功响应"), T("日志关键词") }, SelectedIndex = (int)item.Readiness.Kind };
        FormLayout.Row(form, T("检测方式"), readinessKind);
        var host = FormLayout.Text(item.Readiness.Host); var port = FormLayout.Text(item.Readiness.Port.ToString());
        var url = FormLayout.Text(item.Readiness.Url); var keyword = FormLayout.Text(item.Readiness.Keyword);
        var readyTimeout = FormLayout.Text(item.Readiness.TimeoutSeconds.ToString());
        var tcpFields = new StackPanel(); FormLayout.Row(tcpFields, T("主机"), host); FormLayout.Row(tcpFields, T("端口"), port); form.Children.Add(tcpFields);
        var httpFields = new StackPanel(); FormLayout.Row(httpFields, T("HTTP 地址"), url); form.Children.Add(httpFields);
        var logFields = new StackPanel(); FormLayout.Row(logFields, T("日志关键词"), keyword); form.Children.Add(logFields);
        var timeoutFields = new StackPanel(); FormLayout.Row(timeoutFields, T("就绪超时（秒）"), readyTimeout); form.Children.Add(timeoutFields);
        void UpdateReadinessFields()
        {
            tcpFields.Visibility = readinessKind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            httpFields.Visibility = readinessKind.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            logFields.Visibility = readinessKind.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            timeoutFields.Visibility = readinessKind.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        readinessKind.SelectionChanged += (_, _) => UpdateReadinessFields(); UpdateReadinessFields();
        void ApplyFields()
        {
            item.Name = name.Text.Trim(); item.Kind = (LaunchKind)kind.SelectedIndex; item.Target = target.Text.Trim();
            item.Note = note.Text.Trim();
            var folderId = ((LaunchFolder)folder.SelectedItem).Id; item.FolderId = folderId == Guid.Empty ? null : folderId;
            item.Arguments = args.Text; item.WorkingDirectory = directory.Text.Trim(); item.Interpreter = interpreter.Text.Trim(); item.Encoding = encoding.Text.Trim();
            item.DelaySeconds = int.Parse(delay.Text); item.StopTimeoutSeconds = int.Parse(timeout.Text); item.StopInput = stop.Text;
            item.StopInputDelaySeconds = int.Parse(stopDelay.Text); item.SendStopSignalBeforeInput = signalFirst.IsChecked == true;
            item.StartupOrder = int.Parse(startupOrder.Text);
            item.AutoStart = auto.IsChecked == true; item.AutoRestart = restart.IsChecked == true; item.ShowWindow = visible.IsChecked == true;
            item.Dependencies = dependencies.SelectedItems.Cast<LaunchItem>().Select(x => x.Id).ToList();
            item.Readiness = new() { Kind = (ReadinessKind)readinessKind.SelectedIndex, Host = host.Text.Trim(),
                Port = readinessKind.SelectedIndex == 1 ? int.Parse(port.Text) : item.Readiness.Port, Url = url.Text.Trim(), Keyword = keyword.Text,
                TimeoutSeconds = readinessKind.SelectedIndex == 0 ? item.Readiness.TimeoutSeconds : int.Parse(readyTimeout.Text) };
            item.Environment = new(StringComparer.OrdinalIgnoreCase);
            foreach (var line in env.Text.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var split = line.IndexOf('='); if (split < 1) throw new InvalidDataException(T("环境变量应为每行 NAME=VALUE。"));
                item.Environment.Add(line[..split].Trim(), line[(split + 1)..]);
            }
            ConfigurationStore.Validate(new() { Folders = folders, Items = all.Where(x => x.Id != item.Id).Append(item).ToList() });
            LaunchPreflight.Check(item);
        }
        var check = new Button { Content = T("启动前检查"), HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += (_, _) =>
        {
            try { ApplyFields(); MessageBox.Show(this, T("检查通过。"), T("启动前检查"), MessageBoxButton.OK, MessageBoxImage.Information); }
            catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("启动前检查失败"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        FormLayout.Row(form, T("配置检查"), check);
        FormLayout.Finish(this, form, () =>
        {
            try
            {
                ApplyFields();
                DialogResult = true;
            }
            catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("配置无效"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
    }
}

public sealed class FolderDialog : Window
{
    public string FolderName { get; private set; } = "";
    public FolderDialog(string name)
    {
        Title = T("文件夹"); Width = 440; Height = 220; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel(); var input = FormLayout.Text(name);
        FormLayout.Row(form, T("名称"), input);
        FormLayout.Finish(this, form, () =>
        {
            var value = input.Text.Trim();
            if (value.Length is < 1 or > 100 || value.Contains('\0')) { MessageBox.Show(this, T("文件夹名称须为 1–100 个字符。"), T("名称无效")); return; }
            FolderName = value; DialogResult = true;
        });
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}

public sealed class SettingsDialog : Window
{
    public Preferences Preferences { get; private set; } = new();
    public bool Portable { get; private set; }
    public string DataRoot { get; private set; } = "";
    public SettingsDialog(Preferences preferences, bool portable, string? dataRoot = null, Action? backup = null)
    {
        Title = T("设置"); Width = 550; Height = 680; MinWidth = 480; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = new StackPanel();
        FormLayout.Section(form, T("外观"));
        var languages = new[] { "zh-CN", "en-US" };
        var language = new ComboBox { ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = preferences.Language == "en-US" ? 1 : 0 };
        FormLayout.Row(form, T("语言"), language);
        var themes = new[] { "System", "Light", "Dark" };
        var theme = new ComboBox { ItemsSource = new[] { T("跟随系统"), T("浅色"), T("深色") }, SelectedIndex = Array.IndexOf(themes, preferences.Theme) };
        FormLayout.Row(form, T("主题"), theme);
        FormLayout.Section(form, T("启动与窗口"));
        var login = new CheckBox { Content = T("用户登录后启动 LaunchDesk"), IsChecked = preferences.StartAtLogin };
        var minimized = new CheckBox { Content = T("启动时收起到托盘"), IsChecked = preferences.StartMinimized };
        var tray = new CheckBox { Content = T("最小化时收起到托盘"), IsChecked = preferences.MinimizeToTray };
        var mode = new CheckBox { Content = T("便携模式"), IsChecked = portable };
        FormLayout.Row(form, T("启动"), login); FormLayout.Row(form, T("窗口"), minimized); FormLayout.Row(form, "", tray); FormLayout.Row(form, T("数据目录"), mode);
        FormLayout.Section(form, T("数据与备份"));
        var root = dataRoot ?? (portable ? Path.Combine(AppContext.BaseDirectory, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk"));
        var dataPath = new TextBox { Text = root, TextWrapping = TextWrapping.Wrap, MinHeight = 52 };
        var originalUserRoot = portable ? DataLocation.Resolve(AppContext.BaseDirectory, false) : root;
        var picker = FormLayout.Browse(dataPath, () =>
        {
            var folder = new OpenFolderDialog { Title = T("选择空的数据目录") };
            if (folder.ShowDialog(this) == true) { mode.IsChecked = false; dataPath.Text = folder.FolderName; }
        });
        mode.Checked += (_, _) => { dataPath.Text = Path.Combine(AppContext.BaseDirectory, "data"); dataPath.IsReadOnly = true; };
        mode.Unchecked += (_, _) => { dataPath.IsReadOnly = false; dataPath.Text = originalUserRoot; };
        dataPath.IsReadOnly = portable;
        FormLayout.Row(form, T("当前目录"), picker);
        var dataButtons = new WrapPanel();
        foreach (var entry in new[] { (T("打开数据目录"), root), (T("打开备份目录"), Path.Combine(root, "backups")) })
        {
            var button = new Button { Content = entry.Item1 };
            button.Click += (_, _) =>
            {
                try { Directory.CreateDirectory(entry.Item2); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Item2) { UseShellExecute = true }); }
                catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("无法打开目录"), MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            dataButtons.Children.Add(button);
        }
        var backupButton = new Button { Content = T("立即备份配置"), IsEnabled = backup is not null };
        backupButton.Click += (_, _) =>
        {
            try { backup?.Invoke(); }
            catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("备份失败"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        dataButtons.Children.Add(backupButton); FormLayout.Row(form, T("管理"), dataButtons);
        FormLayout.Section(form, T("日志保留"));
        var days = FormLayout.Text(preferences.LogDays.ToString()); FormLayout.Row(form, T("日志保留（天）"), days);
        var size = FormLayout.Text(preferences.LogLimitMiB.ToString()); FormLayout.Row(form, T("每项上限（MiB）"), size);
        FormLayout.Section(form, T("关于与开源"));
        var automaticUpdates = new CheckBox { Content = T("启动时检查更新"), IsChecked = preferences.CheckUpdatesOnStartup };
        FormLayout.Row(form, T("更新"), automaticUpdates);
        var about = new Button { Content = T("关于 LaunchDesk"), HorizontalAlignment = HorizontalAlignment.Left };
        about.Click += (_, _) => new AboutDialog { Owner = this }.ShowDialog();
        FormLayout.Row(form, "LaunchDesk", about);
        FormLayout.Finish(this, form, () =>
        {
            try
            {
                var value = new Preferences { Language = languages[language.SelectedIndex], Theme = themes[theme.SelectedIndex], StartAtLogin = login.IsChecked == true, StartMinimized = minimized.IsChecked == true,
                    MinimizeToTray = tray.IsChecked == true, CheckUpdatesOnStartup = automaticUpdates.IsChecked == true, LogDays = int.Parse(days.Text), LogLimitMiB = int.Parse(size.Text) };
                ConfigurationStore.Validate(new() { Preferences = value });
                if (string.IsNullOrWhiteSpace(dataPath.Text) || !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(dataPath.Text.Trim())))
                    throw new InvalidDataException(T("数据目录必须是绝对路径。"));
                DataRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataPath.Text.Trim()));
                DataLocation.ValidateDestination(root, DataRoot);
                Preferences = value; Portable = mode.IsChecked == true; DialogResult = true;
            }
            catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("设置无效"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
    }
}

public sealed class AboutDialog : Window
{
    private readonly CancellationTokenSource updateCancellation = new();
    private readonly TextBlock updateStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) };
    private readonly TextBox releaseNotes = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 140,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
    private readonly Button checkUpdate = new() { Content = T("检查更新"), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button openRelease = new() { Content = T("打开下载页"), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private ReleaseUpdate? release;
    public AboutDialog()
    {
        Title = T("关于 LaunchDesk"); Width = 560; Height = 680; MinWidth = 480; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(IconProperty, "AppIcon");
        var form = new StackPanel();
        FormLayout.Section(form, "LaunchDesk");
        var version = typeof(App).Assembly.GetName().Version!.ToString(3);
        form.Children.Add(new TextBlock { Text = T("版本 ") + version + "  ·  Windows x64", Margin = new Thickness(0, 0, 0, 16) });
        AddText(form, T("Windows 启动项管理器"));
        AddText(form, "Copyright (c) 2026 LaunchDesk contributors");
        FormLayout.Section(form, T("版本更新"));
        checkUpdate.Click += async (_, _) => await CheckForUpdatesAsync();
        openRelease.Click += (_, _) => { if (release is not null) new UpdateDialog(release) { Owner = this }.ShowDialog(); };
        form.Children.Add(checkUpdate); form.Children.Add(updateStatus); form.Children.Add(releaseNotes); form.Children.Add(openRelease);
        Closed += (_, _) => updateCancellation.Cancel();
        FormLayout.Section(form, T("开源项目"));
        AddText(form, T("作者 / 维护者：hailaij 与社区贡献者"));
        AddText(form, T("源代码许可：MIT License"));
        AddLink(form, T("源代码仓库"), "https://github.com/hailaij/LaunchDesk");
        AddLink(form, T("问题反馈"), "https://github.com/hailaij/LaunchDesk/issues");
        AddLink(form, T("版本发布"), "https://github.com/hailaij/LaunchDesk/releases");
        FormLayout.Section(form, T("第三方组件"));
        AddText(form, T("使用 Microsoft .NET 10、WPF 与 WinForms。源代码没有第三方 NuGet PackageReference 依赖。自包含发行包的运行时许可和声明位于 runtime-notices 目录。"));
        var licenses = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var entry in new[] { (T("MIT 许可证"), "LICENSE"), (T("第三方声明"), "THIRD-PARTY-NOTICES.md") })
        {
            var button = new Button { Content = entry.Item1 };
            button.Click += (_, _) => ShowNotice(entry.Item1, entry.Item2);
            licenses.Children.Add(button);
        }
        form.Children.Add(licenses);
        var grid = new Grid(); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var close = new Button { Content = T("关闭"), IsCancel = true, IsDefault = true, MinWidth = 80,
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
        catch (Exception error) { MessageBox.Show(this, T("无法打开浏览器：") + ErrorText(error.Message), "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async Task CheckForUpdatesAsync()
    {
        checkUpdate.IsEnabled = false; release = null;
        releaseNotes.Visibility = openRelease.Visibility = Visibility.Collapsed;
        updateStatus.Text = T("正在查询 GitHub 正式发布版本…");
        try
        {
            var result = await UpdateChecker.CheckAsync(updateCancellation.Token);
            if (!updateCancellation.IsCancellationRequested) ApplyUpdateResult(result);
        }
        catch (OperationCanceledException)
        { if (!updateCancellation.IsCancellationRequested) updateStatus.Text = T("检查超时，请稍后重试。"); }
        catch (System.Net.Http.HttpRequestException error)
        {
            updateStatus.Text = error.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests
                ? T("GitHub 请求受限，请稍后重试或打开版本发布页面。")
                : T("无法连接 GitHub，请检查网络后重试。");
        }
        catch (Exception) { updateStatus.Text = T("发布信息无法解析，请打开版本发布页面查看。"); }
        finally { checkUpdate.IsEnabled = true; }
    }
    internal void ApplyUpdateResult(ReleaseUpdate? result)
    {
        var current = typeof(App).Assembly.GetName().Version!;
        if (result is null) { updateStatus.Text = T("暂无可用的正式发布版本。"); return; }
        if (!result.IsNewerThan(current))
        {
            updateStatus.Text = result.Version == new Version(current.Major, current.Minor, current.Build)
                ? T("当前已是最新正式版本（") + result.Tag + "）。" : T("当前版本高于最新正式发布版本（") + result.Tag + "）。";
            return;
        }
        release = result;
        openRelease.Content = T("查看更新 / 下载");
        updateStatus.Text = T("发现新版本 ") + result.Tag;
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
        catch (Exception error) { MessageBox.Show(this, T("无法读取许可文件：") + ErrorText(error.Message), title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
