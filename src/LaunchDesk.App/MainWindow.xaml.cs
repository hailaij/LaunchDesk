using static LaunchDesk.UiText;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchDesk.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LaunchDesk;

public sealed class ItemRow(LaunchItem item, bool isFolder = false, bool collapsed = false) : INotifyPropertyChanged
{
    public string CollapseGlyph => collapsed ? "\uE76C" : "\uE70D";
    public bool IsFolder { get; } = isFolder;
    public string Glyph => IsFolder ? "\uE8B7" : "\uE756";
    public Thickness NameIndent => new(!IsFolder && Item.FolderId.HasValue ? 16 : 0, 0, 0, 0);
    public bool CanRun { get; private set; } = true;
    public bool CanStop { get; private set; }
    public bool CanModify { get; private set; } = true;
    public LaunchItem Item { get; } = item;
    public string Name => Item.Name;
    public string StartupOrder => IsFolder ? "" : Item.StartupOrder.ToString();
    public bool IsRenaming { get; private set; }
    public string RenameText { get; set; } = item.Name;
    public Visibility NameVisibility => IsRenaming ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditorVisibility => IsRenaming ? Visibility.Visible : Visibility.Collapsed;
    public void SetRenaming(bool value)
    {
        if (value) RenameText = Name;
        IsRenaming = value; PropertyChanged?.Invoke(this, new(null));
    }
    public string Kind => IsFolder ? T("文件夹") : Item.Kind switch { LaunchKind.Batch => "BAT / CMD", LaunchKind.Command => T("命令"), LaunchKind.Executable => T("程序"), LaunchKind.PowerShell => "PowerShell", _ => "Shell" };
    public string Target => Item.Target + " " + Item.Arguments;
    public string AutoStart => Item.AutoStart ? T("是") : "";
    public string Status { get; private set; } = T("未启动");
    public string Detail { get; private set; } = "";
    public string Pid { get; private set; } = "";
    public string ExitCode { get; private set; } = "";
    public string Duration { get; private set; } = "";
    public Brush StatusBrush { get; private set; } = Brushes.Gray;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh(RuntimeSnapshot state, bool folderStarting = false)
    {
        Detail = ErrorText(state.Detail);
        CanRun = !state.Active && !folderStarting; CanStop = state.Active; CanModify = !state.Active && !folderStarting;
        Status = state.Status switch { RunStatus.Starting => state.Detail == "等待服务就绪" ? T("等待就绪") : T("启动中"), RunStatus.Running => T("运行中"), RunStatus.Stopping => T("停止中"), RunStatus.Exited => T("正常退出"), RunStatus.Failed => state.Detail.StartsWith("启动前检查") ? T("检查失败") : T("异常退出"), RunStatus.Restarting => T("等待重启"), _ => T("未启动") };
        StatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.Status switch
        { RunStatus.Running => Theme.IsDark ? "#76D9AD" : "#13795B", RunStatus.Failed => Theme.IsDark ? "#FF9292" : "#BD3434",
            RunStatus.Starting or RunStatus.Restarting or RunStatus.Stopping => Theme.IsDark ? "#EEC77C" : "#946400", _ => Theme.IsDark ? "#ADB4BE" : "#636B76" }));
        Pid = state.Pid?.ToString() ?? ""; ExitCode = state.ExitCode?.ToString() ?? "";
        Duration = state.Active && state.Started is { } time ? (DateTimeOffset.Now - time).ToString(@"hh\:mm\:ss") : "";
        PropertyChanged?.Invoke(this, new(null));
    }
    public void RefreshFolder(RuntimeSnapshot[] states, bool starting)
    {
        var active = states.Count(x => x.Active);
        Status = starting ? T("启动中") : F("{0}/{1} 运行", active, states.Length);
        CanRun = !starting && states.Length > 0 && active < states.Length;
        CanStop = starting || active > 0; CanModify = !starting;
        StatusBrush = (Brush)System.Windows.Application.Current.FindResource(active > 0 ? "Accent" : "Muted");
        PropertyChanged?.Invoke(this, new(null));
    }
}

public partial class MainWindow : Window
{
    private ConfigurationStore store;
    private Configuration config;
    private ProcessManager manager;
    private readonly ObservableCollection<ItemRow> rows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private bool portable;
    private readonly string? smokePath;
    private readonly bool autostart;
    private bool exiting;
    private readonly CancellationTokenSource startupUpdateCancellation = new();
    private ReleaseUpdate? pendingUpdate;
    private int lastRevision = -1;
    private Guid? lastSelected;
    private readonly HashSet<Guid> collapsedFolders = [];
    private readonly Dictionary<Guid, CancellationTokenSource> folderStarts = [];
    private Point dragStart;
    private ItemRow? dragRow;
    private const string RowDragFormat = "LaunchDesk.InternalRow";
    private ItemRow? Selected => ItemsGrid.SelectedItem as ItemRow;
    public MainWindow(ConfigurationStore store, Configuration config, bool portable, string? smokePath, bool autostart)
    {
        this.store = store; this.config = config; this.portable = portable; this.smokePath = smokePath; this.autostart = autostart;
        manager = new(config, store.Root, Program.SignalAsync);
        UiText.Apply(config.Preferences.Language);
        InitializeComponent();
        Theme.Apply(config.Preferences.Theme);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        ItemsGrid.ItemsSource = rows; ChannelFilter.SelectedIndex = 0;
        var version = typeof(App).Assembly.GetName().Version!.ToString(3);
        Title = "LaunchDesk v" + version;
        SetResourceReference(IconProperty, "AppIcon");
        ModeText.Text = (portable ? T("便携模式") : T("用户模式")) + " · v" + version;
        timer.Tick += (_, _) => { try { Refresh(); } catch (Exception error) { timer.Stop(); ShowError(error); } }; timer.Start();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateCompactLayout();
    }
    private void UpdateCompactLayout()
    {
        var visibility = ActualWidth < 1100 ? Visibility.Collapsed : Visibility.Visible;
        PidColumn.Visibility = visibility; ExitCodeColumn.Visibility = visibility;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (smokePath is not null)
        {
            CreateTray();
            if (Icon is null || trayIcon is null) throw new InvalidOperationException(T("应用图标测试失败。"));
            Populate();
            if (config.Items.Count != 0 || config.Folders.Count != 0 || LogBox.Text.Length != 0)
                throw new InvalidOperationException(T("首次启动空白数据测试失败。"));
            Capture(this, Path.ChangeExtension(smokePath, null) + "-empty.png");
            config.Items.AddRange([
                new() { Name = T("聊天服务"), Kind = LaunchKind.Batch, Target = @"C:\Scripts\chat-service.cmd" },
                new() { Name = T("Node 服务"), Kind = LaunchKind.Command, Target = "npm", Arguments = "run start", WorkingDirectory = @"C:\Services\node" },
                new() { Name = T("后台任务"), Kind = LaunchKind.PowerShell, Target = @"C:\Scripts\worker.ps1" }
            ]);
            var folder = new LaunchFolder { Name = T("聊天工具") }; config.Folders.Add(folder); config.Items[0].FolderId = folder.Id;
            Populate();
            var folderRow = rows.Single(x => x.IsFolder);
            ToggleFolderClick(new Button { DataContext = folderRow }, new RoutedEventArgs(Button.ClickEvent));
            if (rows.Any(x => x.Item.Id == config.Items[0].Id)) throw new InvalidOperationException(T("文件夹收起测试失败。"));
            ToggleFolderClick(new Button { DataContext = rows.Single(x => x.IsFolder) }, new RoutedEventArgs(Button.ClickEvent));
            ItemSearch.Text = T("聊天工具");
            if (rows.Count != 2) throw new InvalidOperationException(T("文件夹搜索测试失败。"));
            ItemSearch.Clear(); ItemsGrid.SelectedItem = rows.Single(x => x.Item.Id == config.Items[0].Id);
            manager.Logs(config.Items[0].Id).Write("stdout", T("[12:00:01] 服务已连接\n[12:00:02] 启动完成，等待任务\n"));
            manager.Logs(config.Items[0].Id).Write("stderr", T("[12:00:03] 测试警告：连接重试\n"));
            await Task.Delay(700); Theme.Apply("Light"); Refresh();
            if (!LogBox.Text.Contains(T("服务已连接"))) throw new InvalidOperationException(T("日志界面测试失败。"));
            ChannelFilter.SelectedIndex = 2;
            if (!LogBox.Text.Contains(T("测试警告")) || LogBox.Text.Contains(T("服务已连接"))) throw new InvalidOperationException(T("通道过滤测试失败。"));
            ChannelFilter.SelectedIndex = 0; LogSearch.Text = T("启动完成");
            if (!LogBox.Text.Contains(T("启动完成")) || LogBox.Text.Contains(T("测试警告"))) throw new InvalidOperationException(T("日志搜索测试失败。"));
            LogSearch.Clear(); PauseLog.IsChecked = true;
            var paused = LogBox.Text; manager.Logs(config.Items[0].Id).Write("stdout", T("暂停期间捕获\n")); Refresh();
            if (LogBox.Text != paused) throw new InvalidOperationException(T("暂停显示测试失败。"));
            PauseLog.IsChecked = false;
            if (!LogBox.Text.Contains(T("暂停期间捕获"))) throw new InvalidOperationException(T("恢复显示测试失败。"));
            Capture(this, smokePath);
            var editor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = this };
            editor.Show(); await Task.Delay(250); Capture(editor, Path.ChangeExtension(smokePath, null) + "-editor.png"); editor.Close();
            var settings = new SettingsDialog(config.Preferences, portable, store.Root, () => store.BackupNow(config)) { Owner = this };
            settings.Show(); await Task.Delay(250); Capture(settings, Path.ChangeExtension(smokePath, null) + "-settings.png"); settings.Close();
            var about = new AboutDialog { Owner = this };
            about.Show(); await Task.Delay(250); Capture(about, Path.ChangeExtension(smokePath, null) + "-about.png"); about.Close();
            var updateAbout = new AboutDialog { Owner = this };
            updateAbout.ApplyUpdateResult(new ReleaseUpdate(new Version(9, 0, 0), "v9.0.0", T("界面测试更新说明，不执行下载或安装。"), new Uri("https://github.com/hailaij/LaunchDesk/releases")));
            updateAbout.Show(); await Task.Delay(250); Capture(updateAbout, Path.ChangeExtension(smokePath, null) + "-update.png"); updateAbout.Close();
            Theme.Apply("Dark"); Refresh(); await Task.Delay(250); Capture(this, Path.ChangeExtension(smokePath, null) + "-dark.png");
            var darkEditor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = this };
            darkEditor.Show(); await Task.Delay(250); Capture(darkEditor, Path.ChangeExtension(smokePath, null) + "-editor-dark.png"); darkEditor.Close();
            var darkSettings = new SettingsDialog(config.Preferences, portable, store.Root, () => store.BackupNow(config)) { Owner = this };
            darkSettings.Show(); await Task.Delay(250); Capture(darkSettings, Path.ChangeExtension(smokePath, null) + "-settings-dark.png"); darkSettings.Close();
            var darkAbout = new AboutDialog { Owner = this };
            darkAbout.Show(); await Task.Delay(250); Capture(darkAbout, Path.ChangeExtension(smokePath, null) + "-about-dark.png"); darkAbout.Close();
            var child = rows.Single(x => x.Item.FolderId == folder.Id);
            ApplyRowDrop(child, null);
            if (config.Items.Single(x => x.Id == child.Item.Id).FolderId is not null) throw new InvalidOperationException(T("拖拽移出分组测试失败。"));
            ApplyRowDrop(rows.Single(x => x.Item.Id == child.Item.Id), rows.Single(x => x.IsFolder));
            if (config.Items.Single(x => x.Id == child.Item.Id).FolderId != folder.Id) throw new InvalidOperationException(T("拖拽移入分组测试失败。"));
            var backup = store.BackupNow(config);
            if (ConfigurationStore.Read(backup).Items.Count != config.Items.Count) throw new InvalidOperationException(T("手动备份界面测试失败。"));
            var readyItem = ConfigurationStore.Clone(child.Item); readyItem.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "READY" };
            var readinessEditor = new ItemDialog(readyItem, config.Items, config.Folders) { Owner = this };
            readinessEditor.Show();
            if (readinessEditor.Content is Border { Child: Grid formGrid } && formGrid.Children[0] is ScrollViewer formScroll) formScroll.ScrollToEnd();
            await Task.Delay(250); Capture(readinessEditor, Path.ChangeExtension(smokePath, null) + "-readiness.png"); readinessEditor.Close();
            var secondMember = config.Items.Single(x => x.Kind == LaunchKind.Command); secondMember.FolderId = folder.Id;
            manager.Logs(secondMember.Id).Write("stdout", T("第二项输出 http://127.0.0.1:4000/pro/\n"));
            Populate(); ItemsGrid.SelectedItem = rows.Single(x => x.IsFolder); Refresh();
            if (!LogBox.Text.Contains(T("[聊天服务]")) || !LogBox.Text.Contains(T("[Node 服务]")) || !LogBox.Text.Contains(T("第二项输出")))
                throw new InvalidOperationException(T("文件夹合并日志测试失败。"));
            if (!LogBox.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(x => x.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Any())
                throw new InvalidOperationException(T("日志链接标记测试失败。"));
            Capture(this, Path.ChangeExtension(smokePath, null) + "-merged.png");
            var renameRow = rows.Single(x => x.Item.Id == secondMember.Id);
            ItemsGrid.SelectedItem = renameRow; renameRow.SetRenaming(true);
            await Task.Delay(250); Capture(this, Path.ChangeExtension(smokePath, null) + "-rename.png");
            renameRow.RenameText = T("原位重命名"); CommitName(renameRow);
            if (store.Load().Items.Single(x => x.Id == secondMember.Id).Name != T("原位重命名"))
                throw new InvalidOperationException(T("原位重命名持久化测试失败。"));
            var updatePrompt = new UpdateDialog(new(new Version(9, 0, 0), "v9.0.0", T("更新界面测试：不会访问网络或执行安装。"), new Uri("https://github.com/hailaij/LaunchDesk/releases/tag/v9.0.0"))) { Owner = this };
            updatePrompt.Show(); await Task.Delay(250); Capture(updatePrompt, Path.ChangeExtension(smokePath, null) + "-update-prompt.png"); updatePrompt.Close();
            var originalLanguage = config.Preferences.Language;
            var names = config.Items.Select(x => x.Name).ToArray();
            var rawLog = string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text));
            UiText.Apply(originalLanguage == "en-US" ? "zh-CN" : "en-US"); Populate(); Refresh();
            await Task.Delay(100); UpdateLayout();
            if (ItemsGrid.Columns[0].Header?.ToString() != T("名称") || AutoScroll.Content?.ToString() != T("自动滚动") || !names.SequenceEqual(config.Items.Select(x => x.Name))
                || rawLog != string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text)))
                throw new InvalidOperationException("Live language switch failed. Header=" + ItemsGrid.Columns[0].Header + "; expected=" + T("名称")
                    + "; names=" + names.SequenceEqual(config.Items.Select(x => x.Name)) + "; logs=" + (rawLog == string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text))));
            Capture(this, Path.ChangeExtension(smokePath, null) + "-language-switch.png");
            UiText.Apply(originalLanguage); Populate(); Refresh();
            await ExitAsync(); return;
        }
        Populate(); CreateTray();
        if (config.Preferences.StartMinimized || autostart) Hide();
        if (config.Preferences.CheckUpdatesOnStartup) _ = CheckAtStartupAsync();
        await Execute(() => manager.StartAllAsync(true));
    }
    private async Task CheckAtStartupAsync()
    {
        try
        {
            var release = await UpdateChecker.CheckAsync(startupUpdateCancellation.Token);
            if (exiting || release is null || !release.IsNewerThan(typeof(App).Assembly.GetName().Version!)) return;
            pendingUpdate = release; ShowPendingUpdate();
        }
        catch (Exception) { /* Background update failures must not interfere with application startup. */ }
    }
    private void ShowPendingUpdate()
    {
        if (exiting || !IsVisible || WindowState == WindowState.Minimized || pendingUpdate is not { } release) return;
        pendingUpdate = null;
        new UpdateDialog(release) { Owner = this }.Show();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var content = (FrameworkElement)window.Content;
        image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private void CreateTray()
    {
        tray?.ContextMenuStrip?.Dispose(); tray?.Dispose(); trayIcon?.Dispose();
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LaunchDesk.ico"))!.Stream;
        trayIcon = new System.Drawing.Icon(iconStream, 32, 32);
        tray = new Forms.NotifyIcon { Text = Title, Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false, ShowCheckMargin = false,
            Font = new System.Drawing.Font("Microsoft YaHei UI", 10),
            Padding = new Forms.Padding(6),
            Renderer = new Forms.ToolStripProfessionalRenderer(new TrayMenuColors()) { RoundedEdges = false }
        };
        menu.Opening += (_, _) =>
        {
            menu.ForeColor = Theme.IsDark ? System.Drawing.Color.FromArgb(235, 238, 242) : System.Drawing.Color.FromArgb(32, 36, 40);
            foreach (Forms.ToolStripItem item in menu.Items) item.Padding = new Forms.Padding(12, 7, 12, 7);
        };
        menu.Items.Add(T("打开 LaunchDesk"), null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add(T("全部启动"), null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(() => manager.StartAllAsync())));
        menu.Items.Add(T("全部停止"), null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(StopEverything)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("退出"), null, (_, _) => Dispatcher.InvokeAsync(async () => await ExitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
    }
    public void Restore() { if (exiting) return; Show(); WindowState = WindowState.Normal; Activate(); ShowPendingUpdate(); }
    private void Populate(Guid? select = null)
    {
        var headers = new[] { "名称", "类型", "状态", "PID", "运行时长", "顺序", "退出码", "资源 / 命令", "自启", "操作" };
        for (var index = 0; index < headers.Length; index++) ItemsGrid.Columns[index].Header = T(headers[index]);
        ItemsGrid.Columns[2].Width = UiText.Language == "en-US" ? 140 : 110;
        select ??= Selected?.Item.Id;
        rows.Clear();
        bool Match(LaunchItem item) => ItemSearch.Text.Length == 0 || item.Name.Contains(ItemSearch.Text, StringComparison.OrdinalIgnoreCase)
            || item.Target.Contains(ItemSearch.Text, StringComparison.OrdinalIgnoreCase);
        foreach (var folder in config.Folders)
        {
            var matchesFolder = folder.Name.Contains(ItemSearch.Text, StringComparison.OrdinalIgnoreCase);
            var children = config.Items.Where(x => x.FolderId == folder.Id && (matchesFolder || Match(x))).ToArray();
            if (!matchesFolder && children.Length == 0) continue;
            rows.Add(new(new LaunchItem { Id = folder.Id, Name = folder.Name }, true, collapsedFolders.Contains(folder.Id)));
            if (!collapsedFolders.Contains(folder.Id) || ItemSearch.Text.Length > 0)
                foreach (var item in children) rows.Add(new(item));
        }
        foreach (var item in config.Items.Where(x => x.FolderId is null && Match(x))) rows.Add(new(item));
        ItemsGrid.SelectedItem = rows.FirstOrDefault(x => x.Item.Id == select) ?? rows.FirstOrDefault();
        EmptyItems.Text = config.Items.Count == 0 ? T("暂无启动项") : T("没有匹配的启动项");
        EmptyItems.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lastRevision = -1; Refresh();
    }
    private void Refresh()
    {
        if (exiting) return;
        foreach (var row in rows)
            if (row.IsFolder) row.RefreshFolder(config.Items.Where(x => x.FolderId == row.Item.Id).Select(x => manager.Snapshot(x.Id)).ToArray(), folderStarts.ContainsKey(row.Item.Id));
            else row.Refresh(manager.Snapshot(row.Item.Id), row.Item.FolderId is { } folderId && folderStarts.ContainsKey(folderId));
        SummaryText.Text = F("{0} 项 · {1} 运行中", config.Items.Count, config.Items.Count(x => manager.Snapshot(x.Id).Status == RunStatus.Running));
        if (Selected is not { } selected) { SelectedTitle.Text = T("日志"); LogBox.Clear(); StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = false; return; }
        var members = selected.IsFolder ? config.Items.Where(x => x.FolderId == selected.Item.Id).ToArray() : [selected.Item];
        if (selected.IsFolder)
        {
            SelectedTitle.Text = selected.Name + T(" · 合并日志");
            StartButton.IsEnabled = selected.CanRun; StopButton.IsEnabled = selected.CanStop; RestartButton.IsEnabled = false;
        }
        else
        {
            SelectedTitle.Text = selected.Name + T(" · 日志");
            var state = manager.Snapshot(selected.Item.Id);
            StartButton.IsEnabled = !state.Active; StopButton.IsEnabled = state.Active; RestartButton.IsEnabled = true;
            if (state.Detail.Length > 0) StatusText.Text = selected.Name + ": " + ErrorText(state.Detail);
        }
        if (lastSelected != selected.Item.Id)
        {
            lastSelected = selected.Item.Id; lastRevision = -1;
            foreach (var member in members) manager.Logs(member.Id).LoadRecent();
        }
        var revision = members.Aggregate(0, (sum, x) => unchecked(sum + manager.Logs(x.Id).Revision));
        LogBox.Opacity = PauseLog.IsChecked == true ? 0.65 : 1;
        if (PauseLog.IsChecked == true || lastRevision == revision) return;
        lastRevision = revision;
        var channel = (ChannelFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var text = new StringBuilder();
        var records = LogStore.SnapshotMerged(members.Select(x => (x.Id, x.Name, manager.Logs(x.Id))).ToArray());
        foreach (var entry in records.Where(x => ChannelFilter.SelectedIndex == 0 || x.Record.Channel == channel))
        {
            var record = entry.Record;
            if (record.Text.Length == 0) continue;
            var content = LogStore.DisplayText(record.Text);
            var lines = selected.IsFolder ? content.TrimEnd('\n').Split('\n') : [content];
            foreach (var line in lines)
            {
                text.Append('[').Append(record.Time.ToString("HH:mm:ss.fff")).Append("] ");
                if (selected.IsFolder) text.Append('[').Append(entry.Name).Append("] ");
                text.Append('[').Append(record.Channel).Append("] ").Append(line);
                if (!line.EndsWith('\n')) text.AppendLine();
            }
        }
        var value = text.ToString();
        if (value.Length > 500_000) value = value[^500_000..];
        if (!string.IsNullOrWhiteSpace(LogSearch.Text)) value = string.Join(Environment.NewLine,
            value.Split('\n').Where(x => x.Contains(LogSearch.Text, StringComparison.OrdinalIgnoreCase)));
        var verticalOffset = LogBox.VerticalOffset;
        var horizontalOffset = LogBox.HorizontalOffset;
        LogBox.Text = value;
        if (AutoScroll.IsChecked == true) LogBox.ScrollToEnd();
        else { LogBox.ScrollToVerticalOffset(verticalOffset); LogBox.ScrollToHorizontalOffset(horizontalOffset); }
    }
    private async Task Execute(Func<Task> action)
    {
        if (exiting) return;
        try { await action(); StatusText.Text = T("就绪"); }
        catch (Exception error) { if (!exiting) ShowError(error); }
        Refresh();
    }
    private void ShowError(Exception error)
    {
        StatusText.Text = ErrorText(error.Message);
        MessageBox.Show(this, error is AggregateException aggregate ? string.Join("\n", aggregate.InnerExceptions.Select(x => ErrorText(x.Message))) : ErrorText(error.Message),
            T("操作未完成"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private bool CanEdit(LaunchItem item)
    {
        if (!manager.Snapshot(item.Id).Active && !(item.FolderId is { } folder && folderStarts.ContainsKey(folder))) return true;
        MessageBox.Show(this, T("请先停止该启动项。"), "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Information); return false;
    }
    private void SaveOrRollback(Action change)
    {
        var previous = System.Text.Json.JsonSerializer.Serialize(config, ConfigurationStore.JsonOptions);
        try { change(); store.Save(config); Populate(); }
        catch (Exception error)
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<Configuration>(previous, ConfigurationStore.JsonOptions)!;
            config.Items = restored.Items; config.Folders = restored.Folders; ShowError(error); Populate();
        }
    }
    private void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ItemDialog(new() { Name = T("新启动项"), FolderId = Selected?.IsFolder == true ? Selected.Item.Id : Selected?.Item.FolderId }, config.Items, config.Folders) { Owner = this };
        if (dialog.ShowDialog() == true) SaveOrRollback(() => config.Items.Add(dialog.Item));
    }
    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    private void NameClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: ItemRow row }) return;
        e.Handled = true; dragRow = null;
        row.SetRenaming(true);
    }
    private void NameEditorVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox editor && editor.IsVisible)
            Dispatcher.BeginInvoke(() => { if (editor.IsVisible) { editor.Focus(); editor.SelectAll(); } });
    }
    private void CommitName(ItemRow row)
    {
        if (!row.IsRenaming) return;
        var name = row.RenameText;
        row.SetRenaming(false);
        SaveOrRollback(() => config.Rename(row.Item.Id, name));
    }
    private void NameEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ItemRow row }) return;
        if (e.Key == Key.Enter) { e.Handled = true; CommitName(row); }
        else if (e.Key == Key.Escape) { e.Handled = true; row.SetRenaming(false); ItemsGrid.Focus(); }
    }
    private void NameEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemRow row }) CommitName(row);
    }
    private void ItemsDragStart(object s, MouseButtonEventArgs e)
    {
        dragRow = FindParent<Button>(e.OriginalSource as DependencyObject) is null && FindParent<TextBox>(e.OriginalSource as DependencyObject) is null
            ? FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as ItemRow : null;
        dragStart = e.GetPosition(ItemsGrid);
    }
    private void ItemsDragMove(object s, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragRow is not { } row) return;
        var point = e.GetPosition(ItemsGrid);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragRow = null;
        if (!row.CanModify) return;
        DragDrop.DoDragDrop(ItemsGrid, new DataObject(RowDragFormat, row), DragDropEffects.Move);
    }
    private void ItemsDragOver(object s, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (e.Data.GetData(RowDragFormat) is ItemRow row && row.CanModify)
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length is > 0 and <= 50
            && files.All(LaunchItemFactory.Supports)) e.Effects = DragDropEffects.Copy;
    }
    private void ItemsDrop(object s, DragEventArgs e)
    {
        e.Handled = true; dragRow = null;
        var targetRow = FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as ItemRow;
        var targetFolder = targetRow?.IsFolder == true ? targetRow.Item.Id : targetRow?.Item.FolderId;
        if (e.Data.GetData(RowDragFormat) is ItemRow source)
        {
            ApplyRowDrop(source, targetRow);
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length is < 1 or > 50) return;
        if (targetFolder is { } target && folderStarts.ContainsKey(target)) return;
        foreach (var path in paths)
        {
            try
            {
                var item = LaunchItemFactory.FromFile(path, targetFolder);
                var dialog = new ItemDialog(item, config.Items, config.Folders) { Owner = this };
                if (dialog.ShowDialog() != true) continue;
                SaveOrRollback(() => config.Items.Add(dialog.Item));
            }
            catch (Exception error) { ShowError(error); }
        }
    }
    private void ApplyRowDrop(ItemRow source, ItemRow? targetRow)
    {
        var targetFolder = targetRow?.IsFolder == true ? targetRow.Item.Id : targetRow?.Item.FolderId;
        if (source.IsFolder)
        {
            if (folderStarts.ContainsKey(source.Item.Id)) return;
            SaveOrRollback(() => config.MoveFolder(source.Item.Id, targetFolder));
        }
        else
        {
            if (!CanEdit(source.Item) || targetFolder is { } folder && folderStarts.ContainsKey(folder)) return;
            SaveOrRollback(() => config.MoveItem(source.Item.Id, targetFolder, targetRow?.IsFolder == false ? targetRow.Item.Id : null));
        }
    }
    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ItemsGrid) && e.OriginalSource is DependencyObject source)
            for (var current = source; current is not null && current is not DataGrid; current = VisualTreeHelper.GetParent(current))
                if (current is Button) return;
        if (Selected is not { } row) return;
        if (row.IsFolder) { EditFolder(config.Folders.Single(x => x.Id == row.Item.Id)); return; }
        if (!CanEdit(row.Item)) return;
        var dialog = new ItemDialog(ConfigurationStore.Clone(row.Item), config.Items, config.Folders) { Owner = this };
        if (dialog.ShowDialog() == true) SaveOrRollback(() => config.Items[config.Items.FindIndex(x => x.Id == row.Item.Id)] = dialog.Item);
    }
    private void DuplicateClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsFolder: false } row) return;
        var clone = ConfigurationStore.Clone(row.Item); clone.Id = Guid.NewGuid(); clone.Name += T(" 副本"); clone.AutoStart = false;
        SaveOrRollback(() => config.Items.Add(clone));
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (row.IsFolder)
        {
            if (folderStarts.ContainsKey(row.Item.Id)) { ShowError(new InvalidOperationException(T("请先停止文件夹的启动操作。"))); return; }
            if (MessageBox.Show(this, T("解散文件夹“") + row.Name + T("”？启动项、运行进程和日志均保留。"), T("删除文件夹"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                SaveOrRollback(() => config.DissolveFolder(row.Item.Id));
            return;
        }
        if (!CanEdit(row.Item)) return;
        if (config.Items.Any(x => x.Dependencies.Contains(row.Item.Id))) { ShowError(new InvalidOperationException(T("其他启动项依赖该项，请先移除依赖。"))); return; }
        if (MessageBox.Show(this, T("删除“") + row.Name + T("”？日志将保留。"), T("删除启动项"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SaveOrRollback(() => config.Items.Remove(row.Item));
    }
    private void AddFolderClick(object s, RoutedEventArgs e) => EditFolder(null);
    private void EditFolder(LaunchFolder? folder)
    {
        var dialog = new FolderDialog(folder?.Name ?? T("新文件夹")) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        SaveOrRollback(() => { if (folder is null) config.Folders.Add(new() { Name = dialog.FolderName }); else folder.Name = dialog.FolderName; });
    }
    private void ToggleFolderClick(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.DataContext is not ItemRow { IsFolder: true } row) return;
        if (!collapsedFolders.Add(row.Item.Id)) collapsedFolders.Remove(row.Item.Id);
        e.Handled = true; Populate(row.Item.Id);
    }
    private void RowActionClick(object s, RoutedEventArgs e)
    {
        if (s is not Button { DataContext: ItemRow row } button) return;
        e.Handled = true; ItemsGrid.SelectedItem = row;
        switch (button.Tag)
        { case "Run": StartClick(s, e); break; case "Stop": StopClick(s, e); break; case "Edit": EditClick(s, e); break; case "Delete": DeleteClick(s, e); break; }
    }
    private async Task RunFolder(Guid id)
    {
        if (folderStarts.ContainsKey(id)) return;
        using var cancellation = new CancellationTokenSource(); folderStarts.Add(id, cancellation); Refresh();
        try { await manager.StartFolderAsync(id, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { folderStarts.Remove(id); Refresh(); }
    }
    private async Task StopEverything()
    {
        foreach (var pending in folderStarts.Values.ToArray()) pending.Cancel();
        await manager.StopAllAsync();
    }
    private void Move(int offset)
    {
        if (Selected is not { } row) return;
        if (row.IsFolder)
        {
            var folderIndex = config.Folders.FindIndex(x => x.Id == row.Item.Id); var targetIndex = folderIndex + offset;
            if (targetIndex < 0 || targetIndex >= config.Folders.Count) return;
            SaveOrRollback(() => (config.Folders[folderIndex], config.Folders[targetIndex]) = (config.Folders[targetIndex], config.Folders[folderIndex])); return;
        }
        var members = config.Items.Where(x => x.FolderId == row.Item.FolderId).ToList();
        var memberIndex = members.IndexOf(row.Item) + offset;
        if (memberIndex < 0 || memberIndex >= members.Count) return;
        var index = config.Items.IndexOf(row.Item); var next = config.Items.IndexOf(members[memberIndex]);
        if (next < 0 || next >= config.Items.Count) return;
        SaveOrRollback(() => (config.Items[index], config.Items[next]) = (config.Items[next], config.Items[index]));
    }
    private void MoveUpClick(object s, RoutedEventArgs e) => Move(-1);
    private void MoveDownClick(object s, RoutedEventArgs e) => Move(1);
    private async void StartClick(object s, RoutedEventArgs e) { if (Selected is { } row) await Execute(() => row.IsFolder ? RunFolder(row.Item.Id) : manager.StartAsync(row.Item.Id)); }
    private async void StopClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (row.IsFolder && folderStarts.TryGetValue(row.Item.Id, out var pending)) pending.Cancel();
        await Execute(() => row.IsFolder ? manager.StopFolderAsync(row.Item.Id) : manager.StopAsync(row.Item.Id));
    }
    private async void RestartClick(object s, RoutedEventArgs e) { if (Selected is { IsFolder: false } row) await Execute(async () => { await manager.StopAsync(row.Item.Id); await manager.StartAsync(row.Item.Id); }); }
    private async void StartAllClick(object s, RoutedEventArgs e) => await Execute(() => manager.StartAllAsync());
    private async void StopAllClick(object s, RoutedEventArgs e) => await Execute(StopEverything);
    private async void SendInputClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { IsFolder: false } row) return;
        var input = InputText.Text; await Execute(() => manager.SendInputAsync(row.Item.Id, input)); InputText.Clear();
    }
    private void InputKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; SendInputClick(s, e); } }
    private void SelectionChanged(object s, SelectionChangedEventArgs e) { if (LogBox is not null) { lastRevision = -1; PauseLog.IsChecked = false; Refresh(); } }
    private void ItemSearchChanged(object s, TextChangedEventArgs e) { if (ItemsGrid is not null) Populate(); }
    private void LogFilterChanged(object s, RoutedEventArgs e) { if (LogBox is not null) { lastRevision = -1; Refresh(); } }
    private void LogSearchChanged(object s, TextChangedEventArgs e) { if (LogBox is not null) { lastRevision = -1; Refresh(); } }
    private LaunchItem[] SelectedMembers() => Selected is { } row
        ? row.IsFolder ? config.Items.Where(x => x.FolderId == row.Item.Id).ToArray() : [row.Item] : [];
    private void ClearPanelClick(object s, RoutedEventArgs e)
    {
        foreach (var item in SelectedMembers()) manager.Logs(item.Id).ClearPanel();
        lastRevision = -1; Refresh();
    }
    private void DeleteHistoryClick(object s, RoutedEventArgs e)
    {
        var members = SelectedMembers(); if (members.Length == 0) return;
        if (MessageBox.Show(this, F("永久删除所选范围内 {0} 个启动项的历史日志？", members.Length), T("删除日志"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { foreach (var item in members) manager.Logs(item.Id).DeleteHistory(); lastRevision = -1; Refresh(); } catch (Exception error) { ShowError(error); }
    }
    private async void ExportLogClick(object s, RoutedEventArgs e)
    {
        var members = SelectedMembers(); if (members.Length == 0) return;
        var merged = Selected?.IsFolder == true;
        var dialog = new SaveFileDialog { Filter = T("文本日志 (*.txt)|*.txt|原始记录 (*.jsonl)|*.jsonl"), FileName = "LaunchDesk-log.txt" };
        var sources = members.Select(x => (x.Id, x.Name, Store: manager.Logs(x.Id))).ToArray();
        if (dialog.ShowDialog(this) == true) await Execute(() => Task.Run(() =>
        {
            if (merged) LogStore.ExportMerged(sources, dialog.FileName);
            else sources[0].Store.Export(dialog.FileName);
        }));
    }
    private void ExportConfigClick(object s, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = T("JSON 配置 (*.json)|*.json"), FileName = "LaunchDesk-config.json" };
        if (dialog.ShowDialog(this) == true) { try { store.Export(config, dialog.FileName); } catch (Exception error) { ShowError(error); } }
    }
    private async Task Import(string path)
    {
        if (folderStarts.Count > 0 || config.Items.Any(x => manager.Snapshot(x.Id).Active)) throw new InvalidOperationException(T("导入前请停止全部启动项。"));
        var imported = ConfigurationStore.Read(path);
        if (MessageBox.Show(this, F("替换当前配置为 {0} 个启动项？原配置将自动备份。导入项不会立即启动。", imported.Items.Count), T("导入配置"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // Login startup is a local preference, not an imported configuration's side effect.
        imported.Preferences.StartAtLogin = config.Preferences.StartAtLogin;
        store.Save(imported); await manager.DisposeAsync(); config = imported; manager = new(config, store.Root, Program.SignalAsync); Theme.Apply(config.Preferences.Theme);
        UiText.Apply(config.Preferences.Language); Populate(); CreateTray(); lastRevision = -1;
        ModeText.Text = (portable ? T("便携模式") : T("用户模式")) + " · v" + typeof(App).Assembly.GetName().Version!.ToString(3);
    }
    private async void ImportClick(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = T("JSON 配置 (*.json)|*.json") };
        if (dialog.ShowDialog(this) == true) await Execute(() => Import(dialog.FileName));
    }
    private async void RestoreBackupClick(object s, RoutedEventArgs e)
    {
        var directory = Path.Combine(store.Root, "backups"); Directory.CreateDirectory(directory);
        var dialog = new OpenFileDialog { Filter = T("JSON 备份 (*.json)|*.json"), InitialDirectory = directory };
        if (dialog.ShowDialog(this) == true) await Execute(() => Import(dialog.FileName));
    }
    private async void SettingsClick(object s, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(config.Preferences, portable, store.Root, () =>
        {
            var path = store.BackupNow(config);
            MessageBox.Show(T("备份完成：\n") + path, T("配置备份"), MessageBoxButton.OK, MessageBoxImage.Information);
        }) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var previous = new Preferences { Language = config.Preferences.Language, Theme = config.Preferences.Theme, StartAtLogin = config.Preferences.StartAtLogin, StartMinimized = config.Preferences.StartMinimized,
                MinimizeToTray = config.Preferences.MinimizeToTray, CheckUpdatesOnStartup = config.Preferences.CheckUpdatesOnStartup, LogDays = config.Preferences.LogDays, LogLimitMiB = config.Preferences.LogLimitMiB };
            var destinationRoot = dialog.DataRoot;
            var changingRoot = !Path.TrimEndingDirectorySeparator(destinationRoot).Equals(Path.TrimEndingDirectorySeparator(store.Root), StringComparison.OrdinalIgnoreCase);
            if (changingRoot && (folderStarts.Count > 0 || config.Items.Any(x => manager.Snapshot(x.Id).Active)))
                throw new InvalidOperationException(T("更改数据目录前请停止全部启动项。"));
            if (changingRoot && MessageBox.Show(this, T("将配置、日志与备份复制到新目录，并立即切换。原目录保留，不覆盖目标已有数据。继续？"), T("切换数据目录"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var p = config.Preferences;
            p.Language = dialog.Preferences.Language;
            p.Theme = dialog.Preferences.Theme;
            p.StartAtLogin = dialog.Preferences.StartAtLogin; p.StartMinimized = dialog.Preferences.StartMinimized;
            p.MinimizeToTray = dialog.Preferences.MinimizeToTray; p.LogDays = dialog.Preferences.LogDays; p.LogLimitMiB = dialog.Preferences.LogLimitMiB;
            p.CheckUpdatesOnStartup = dialog.Preferences.CheckUpdatesOnStartup;
            try
            {
                store.Save(config);
                var targetStore = store;
                if (changingRoot)
                {
                    IsEnabled = false;
                    try
                    {
                        await manager.DisposeAsync();
                        targetStore = await Task.Run(() => DataLocation.CopyTo(store, config, destinationRoot));
                    }
                    catch { manager = new(config, store.Root, Program.SignalAsync); throw; }
                    finally { IsEnabled = true; }
                }
                try
                {
                    var flag = Path.Combine(AppContext.BaseDirectory, "portable.flag");
                    if (!dialog.Portable) DataLocation.Save(destinationRoot);
                    if (dialog.Portable != portable)
                    {
                        if (dialog.Portable) File.WriteAllText(flag, "LaunchDesk portable mode\n");
                        else if (File.Exists(flag)) File.Delete(flag);
                    }
                }
                catch { if (changingRoot) manager = new(config, store.Root, Program.SignalAsync); throw; }
                store = targetStore; portable = dialog.Portable;
                if (changingRoot) manager = new(config, store.Root, Program.SignalAsync);
            }
            catch { config.Preferences.Language = previous.Language; config.Preferences.Theme = previous.Theme; config.Preferences.StartAtLogin = previous.StartAtLogin; config.Preferences.StartMinimized = previous.StartMinimized;
                config.Preferences.MinimizeToTray = previous.MinimizeToTray; config.Preferences.LogDays = previous.LogDays; config.Preferences.LogLimitMiB = previous.LogLimitMiB;
                config.Preferences.CheckUpdatesOnStartup = previous.CheckUpdatesOnStartup;
                store.Save(config); throw; }
            try
            {
                using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (p.StartAtLogin) run.SetValue("LaunchDesk", LaunchCommand.Quote(Environment.ProcessPath!) + " --autostart");
                else run.DeleteValue("LaunchDesk", false);
            }
            catch (Exception error) { ShowError(new IOException(T("设置已保存，但登录自启注册失败：") + ErrorText(error.Message), error)); }
            UiText.Apply(config.Preferences.Language);
            lastRevision = -1;
            ModeText.Text = (portable ? T("便携模式") : T("用户模式")) + " · v" + typeof(App).Assembly.GetName().Version!.ToString(3);
            Populate(); CreateTray();
            StatusText.Text = T("就绪");
            if (changingRoot) { lastSelected = null; lastRevision = -1; Populate(); }
            Theme.Apply(config.Preferences.Theme); Refresh();
        }
        catch (Exception error) { ShowError(error); }
    }
    private void WindowClosing(object? s, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (config.Preferences.Theme == "System" && !exiting)
            Dispatcher.BeginInvoke(() => { if (!exiting) { Theme.Apply("System"); Refresh(); } });
    }
    private void WindowStateChanged(object? s, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && config.Preferences.MinimizeToTray && smokePath is null) Hide();
        else if (WindowState == WindowState.Normal && smokePath is null) ShowPendingUpdate();
    }
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true; startupUpdateCancellation.Cancel(); SystemEvents.UserPreferenceChanged -= SystemThemeChanged; timer.Stop(); IsEnabled = false; StatusText.Text = T("正在停止进程并保存日志…");
        foreach (var pending in folderStarts.Values.ToArray()) pending.Cancel();
        try { await manager.DisposeAsync(); }
        catch (Exception error) { MessageBox.Show(this, ErrorText(error.Message), T("退出清理失败"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { tray?.Dispose(); trayIcon?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(); }
    }
}
