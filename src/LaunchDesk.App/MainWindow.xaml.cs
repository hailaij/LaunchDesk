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
    public string Kind => IsFolder ? "文件夹" : Item.Kind switch { LaunchKind.Batch => "BAT / CMD", LaunchKind.Command => "命令", LaunchKind.Executable => "程序", LaunchKind.PowerShell => "PowerShell", _ => "Shell" };
    public string Target => Item.Target + " " + Item.Arguments;
    public string AutoStart => Item.AutoStart ? "是" : "";
    public string Status { get; private set; } = "未启动";
    public string Pid { get; private set; } = "";
    public string ExitCode { get; private set; } = "";
    public string Duration { get; private set; } = "";
    public Brush StatusBrush { get; private set; } = Brushes.Gray;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh(RuntimeSnapshot state, bool folderStarting = false)
    {
        CanRun = !state.Active && !folderStarting; CanStop = state.Active; CanModify = !state.Active && !folderStarting;
        Status = state.Status switch { RunStatus.Starting => "启动中", RunStatus.Running => "运行中", RunStatus.Stopping => "停止中", RunStatus.Exited => "正常退出", RunStatus.Failed => "异常退出", RunStatus.Restarting => "等待重启", _ => "未启动" };
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
        Status = starting ? "启动中" : $"{active}/{states.Length} 运行";
        CanRun = !starting && states.Length > 0 && active < states.Length;
        CanStop = starting || active > 0; CanModify = !starting;
        StatusBrush = (Brush)System.Windows.Application.Current.FindResource(active > 0 ? "Accent" : "Muted");
        PropertyChanged?.Invoke(this, new(null));
    }
}

public partial class MainWindow : Window
{
    private readonly ConfigurationStore store;
    private Configuration config;
    private ProcessManager manager;
    private readonly ObservableCollection<ItemRow> rows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private readonly bool portable;
    private readonly string? smokePath;
    private readonly bool autostart;
    private bool exiting;
    private int lastRevision = -1;
    private Guid? lastSelected;
    private readonly HashSet<Guid> collapsedFolders = [];
    private readonly Dictionary<Guid, CancellationTokenSource> folderStarts = [];
    private ItemRow? Selected => ItemsGrid.SelectedItem as ItemRow;
    public MainWindow(ConfigurationStore store, Configuration config, bool portable, string? smokePath, bool autostart)
    {
        this.store = store; this.config = config; this.portable = portable; this.smokePath = smokePath; this.autostart = autostart;
        manager = new(config, store.Root, Program.SignalAsync);
        InitializeComponent();
        Theme.Apply(config.Preferences.Theme);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        ItemsGrid.ItemsSource = rows; ChannelFilter.SelectedIndex = 0;
        var version = typeof(App).Assembly.GetName().Version!.ToString(3);
        Title = "LaunchDesk v" + version;
        SetResourceReference(IconProperty, "AppIcon");
        ModeText.Text = (portable ? "便携模式" : "用户模式") + " · v" + version;
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
            if (Icon is null || trayIcon is null) throw new InvalidOperationException("应用图标测试失败。");
            Populate();
            if (config.Items.Count != 0 || config.Folders.Count != 0 || LogBox.Text.Length != 0)
                throw new InvalidOperationException("首次启动空白数据测试失败。");
            Capture(this, Path.ChangeExtension(smokePath, null) + "-empty.png");
            config.Items.AddRange([
                new() { Name = "聊天服务", Kind = LaunchKind.Batch, Target = @"C:\Scripts\chat-service.cmd" },
                new() { Name = "Node 服务", Kind = LaunchKind.Command, Target = "npm", Arguments = "run start", WorkingDirectory = @"C:\Services\node" },
                new() { Name = "后台任务", Kind = LaunchKind.PowerShell, Target = @"C:\Scripts\worker.ps1" }
            ]);
            var folder = new LaunchFolder { Name = "聊天工具" }; config.Folders.Add(folder); config.Items[0].FolderId = folder.Id;
            Populate();
            var folderRow = rows.Single(x => x.IsFolder);
            ToggleFolderClick(new Button { DataContext = folderRow }, new RoutedEventArgs(Button.ClickEvent));
            if (rows.Any(x => x.Item.Id == config.Items[0].Id)) throw new InvalidOperationException("文件夹收起测试失败。");
            ToggleFolderClick(new Button { DataContext = rows.Single(x => x.IsFolder) }, new RoutedEventArgs(Button.ClickEvent));
            ItemSearch.Text = "聊天工具";
            if (rows.Count != 2) throw new InvalidOperationException("文件夹搜索测试失败。");
            ItemSearch.Clear(); ItemsGrid.SelectedItem = rows.Single(x => x.Item.Id == config.Items[0].Id);
            manager.Logs(config.Items[0].Id).Write("stdout", "[12:00:01] 服务已连接\n[12:00:02] 启动完成，等待任务\n");
            manager.Logs(config.Items[0].Id).Write("stderr", "[12:00:03] 测试警告：连接重试\n");
            await Task.Delay(700); Theme.Apply("Light"); Refresh();
            if (!LogBox.Text.Contains("服务已连接")) throw new InvalidOperationException("日志界面测试失败。");
            ChannelFilter.SelectedIndex = 2;
            if (!LogBox.Text.Contains("测试警告") || LogBox.Text.Contains("服务已连接")) throw new InvalidOperationException("通道过滤测试失败。");
            ChannelFilter.SelectedIndex = 0; LogSearch.Text = "启动完成";
            if (!LogBox.Text.Contains("启动完成") || LogBox.Text.Contains("测试警告")) throw new InvalidOperationException("日志搜索测试失败。");
            LogSearch.Clear(); PauseLog.IsChecked = true;
            var paused = LogBox.Text; manager.Logs(config.Items[0].Id).Write("stdout", "暂停期间捕获\n"); Refresh();
            if (LogBox.Text != paused) throw new InvalidOperationException("暂停显示测试失败。");
            PauseLog.IsChecked = false;
            if (!LogBox.Text.Contains("暂停期间捕获")) throw new InvalidOperationException("恢复显示测试失败。");
            Capture(this, smokePath);
            var editor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = this };
            editor.Show(); await Task.Delay(250); Capture(editor, Path.ChangeExtension(smokePath, null) + "-editor.png"); editor.Close();
            var settings = new SettingsDialog(config.Preferences, portable) { Owner = this };
            settings.Show(); await Task.Delay(250); Capture(settings, Path.ChangeExtension(smokePath, null) + "-settings.png"); settings.Close();
            var about = new AboutDialog { Owner = this };
            about.Show(); await Task.Delay(250); Capture(about, Path.ChangeExtension(smokePath, null) + "-about.png"); about.Close();
            var updateAbout = new AboutDialog { Owner = this };
            updateAbout.ApplyUpdateResult(new ReleaseUpdate(new Version(9, 0, 0), "v9.0.0", "界面测试更新说明，不执行下载或安装。", new Uri("https://github.com/hailaij/LaunchDesk/releases")));
            updateAbout.Show(); await Task.Delay(250); Capture(updateAbout, Path.ChangeExtension(smokePath, null) + "-update.png"); updateAbout.Close();
            Theme.Apply("Dark"); Refresh(); await Task.Delay(250); Capture(this, Path.ChangeExtension(smokePath, null) + "-dark.png");
            var darkEditor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = this };
            darkEditor.Show(); await Task.Delay(250); Capture(darkEditor, Path.ChangeExtension(smokePath, null) + "-editor-dark.png"); darkEditor.Close();
            var darkSettings = new SettingsDialog(config.Preferences, portable) { Owner = this };
            darkSettings.Show(); await Task.Delay(250); Capture(darkSettings, Path.ChangeExtension(smokePath, null) + "-settings-dark.png"); darkSettings.Close();
            var darkAbout = new AboutDialog { Owner = this };
            darkAbout.Show(); await Task.Delay(250); Capture(darkAbout, Path.ChangeExtension(smokePath, null) + "-about-dark.png"); darkAbout.Close();
            await ExitAsync(); return;
        }
        Populate(); CreateTray();
        if (config.Preferences.StartMinimized || autostart) Hide();
        await Execute(() => manager.StartAllAsync(true));
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
        menu.Items.Add("打开 LaunchDesk", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("全部启动", null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(() => manager.StartAllAsync())));
        menu.Items.Add("全部停止", null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(StopEverything)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.InvokeAsync(async () => await ExitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
    }
    public void Restore() { if (exiting) return; Show(); WindowState = WindowState.Normal; Activate(); }
    private void Populate(Guid? select = null)
    {
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
        EmptyItems.Text = config.Items.Count == 0 ? "暂无启动项" : "没有匹配的启动项";
        EmptyItems.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lastRevision = -1; Refresh();
    }
    private void Refresh()
    {
        if (exiting) return;
        foreach (var row in rows)
            if (row.IsFolder) row.RefreshFolder(config.Items.Where(x => x.FolderId == row.Item.Id).Select(x => manager.Snapshot(x.Id)).ToArray(), folderStarts.ContainsKey(row.Item.Id));
            else row.Refresh(manager.Snapshot(row.Item.Id), row.Item.FolderId is { } folderId && folderStarts.ContainsKey(folderId));
        SummaryText.Text = $"{config.Items.Count} 项 · {config.Items.Count(x => manager.Snapshot(x.Id).Status == RunStatus.Running)} 运行中";
        if (Selected is not { } selected) { SelectedTitle.Text = "日志"; LogBox.Clear(); StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = false; return; }
        if (selected.IsFolder)
        {
            SelectedTitle.Text = selected.Name + " · " + selected.Status;
            LogBox.Clear(); StartButton.IsEnabled = selected.CanRun; StopButton.IsEnabled = selected.CanStop; RestartButton.IsEnabled = false;
            lastSelected = null; lastRevision = -1; return;
        }
        SelectedTitle.Text = selected.Name + " · 日志";
        var state = manager.Snapshot(selected.Item.Id);
        StartButton.IsEnabled = !state.Active; StopButton.IsEnabled = state.Active; RestartButton.IsEnabled = true;
        if (state.Detail.Length > 0) StatusText.Text = selected.Name + "：" + state.Detail;
        var logs = manager.Logs(selected.Item.Id);
        if (lastSelected != selected.Item.Id) { lastSelected = selected.Item.Id; lastRevision = -1; logs.LoadRecent(); }
        LogBox.Opacity = PauseLog.IsChecked == true ? 0.65 : 1;
        if (PauseLog.IsChecked == true || lastRevision == logs.Revision) return;
        lastRevision = logs.Revision;
        var channel = (ChannelFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var text = new StringBuilder();
        foreach (var record in logs.Snapshot().Where(x => ChannelFilter.SelectedIndex == 0 || x.Channel == channel))
        {
            if (record.Text.Length == 0) continue;
            text.Append('[').Append(record.Time.ToString("HH:mm:ss.fff")).Append("] [").Append(record.Channel).Append("] ").Append(LogStore.DisplayText(record.Text));
            if (!record.Text.EndsWith('\n')) text.AppendLine();
        }
        var value = text.ToString();
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
        try { await action(); StatusText.Text = "就绪"; }
        catch (Exception error) { if (!exiting) ShowError(error); }
        Refresh();
    }
    private void ShowError(Exception error)
    {
        StatusText.Text = error.Message;
        MessageBox.Show(this, error is AggregateException aggregate ? string.Join("\n", aggregate.InnerExceptions.Select(x => x.Message)) : error.Message,
            "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private bool CanEdit(LaunchItem item)
    {
        if (!manager.Snapshot(item.Id).Active && !(item.FolderId is { } folder && folderStarts.ContainsKey(folder))) return true;
        MessageBox.Show(this, "请先停止该启动项。", "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Information); return false;
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
        var dialog = new ItemDialog(new() { FolderId = Selected?.IsFolder == true ? Selected.Item.Id : Selected?.Item.FolderId }, config.Items, config.Folders) { Owner = this };
        if (dialog.ShowDialog() == true) SaveOrRollback(() => config.Items.Add(dialog.Item));
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
        var clone = ConfigurationStore.Clone(row.Item); clone.Id = Guid.NewGuid(); clone.Name += " 副本"; clone.AutoStart = false;
        SaveOrRollback(() => config.Items.Add(clone));
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (row.IsFolder)
        {
            if (folderStarts.ContainsKey(row.Item.Id)) { ShowError(new InvalidOperationException("请先停止文件夹的启动操作。")); return; }
            if (MessageBox.Show(this, "解散文件夹“" + row.Name + "”？启动项、运行进程和日志均保留。", "删除文件夹", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                SaveOrRollback(() => config.DissolveFolder(row.Item.Id));
            return;
        }
        if (!CanEdit(row.Item)) return;
        if (config.Items.Any(x => x.Dependencies.Contains(row.Item.Id))) { ShowError(new InvalidOperationException("其他启动项依赖该项，请先移除依赖。")); return; }
        if (MessageBox.Show(this, "删除“" + row.Name + "”？日志将保留。", "删除启动项", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SaveOrRollback(() => config.Items.Remove(row.Item));
    }
    private void AddFolderClick(object s, RoutedEventArgs e) => EditFolder(null);
    private void EditFolder(LaunchFolder? folder)
    {
        var dialog = new FolderDialog(folder?.Name ?? "新文件夹") { Owner = this };
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
    private void ClearPanelClick(object s, RoutedEventArgs e) { if (Selected is { IsFolder: false } row) { manager.Logs(row.Item.Id).ClearPanel(); Refresh(); } }
    private void DeleteHistoryClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { IsFolder: false } row) return;
        if (MessageBox.Show(this, "永久删除该项的历史日志？", "删除日志", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { manager.Logs(row.Item.Id).DeleteHistory(); Refresh(); } catch (Exception error) { ShowError(error); }
    }
    private async void ExportLogClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { IsFolder: false } row) return;
        var dialog = new SaveFileDialog { Filter = "文本日志 (*.txt)|*.txt|原始记录 (*.jsonl)|*.jsonl", FileName = "LaunchDesk-log.txt" };
        if (dialog.ShowDialog(this) == true) await Execute(() => Task.Run(() => manager.Logs(row.Item.Id).Export(dialog.FileName)));
    }
    private void ExportConfigClick(object s, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON 配置 (*.json)|*.json", FileName = "LaunchDesk-config.json" };
        if (dialog.ShowDialog(this) == true) { try { store.Export(config, dialog.FileName); } catch (Exception error) { ShowError(error); } }
    }
    private async Task Import(string path)
    {
        if (folderStarts.Count > 0 || config.Items.Any(x => manager.Snapshot(x.Id).Active)) throw new InvalidOperationException("导入前请停止全部启动项。");
        var imported = ConfigurationStore.Read(path);
        if (MessageBox.Show(this, $"替换当前配置为 {imported.Items.Count} 个启动项？原配置将自动备份。导入项不会立即启动。", "导入配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // Login startup is a local preference, not an imported configuration's side effect.
        imported.Preferences.StartAtLogin = config.Preferences.StartAtLogin;
        store.Save(imported); await manager.DisposeAsync(); config = imported; manager = new(config, store.Root, Program.SignalAsync); Theme.Apply(config.Preferences.Theme); Populate();
    }
    private async void ImportClick(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON 配置 (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true) await Execute(() => Import(dialog.FileName));
    }
    private async void RestoreBackupClick(object s, RoutedEventArgs e)
    {
        var directory = Path.Combine(store.Root, "backups"); Directory.CreateDirectory(directory);
        var dialog = new OpenFileDialog { Filter = "JSON 备份 (*.json)|*.json", InitialDirectory = directory };
        if (dialog.ShowDialog(this) == true) await Execute(() => Import(dialog.FileName));
    }
    private void SettingsClick(object s, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(config.Preferences, portable) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var previous = new Preferences { Theme = config.Preferences.Theme, StartAtLogin = config.Preferences.StartAtLogin, StartMinimized = config.Preferences.StartMinimized,
                MinimizeToTray = config.Preferences.MinimizeToTray, LogDays = config.Preferences.LogDays, LogLimitMiB = config.Preferences.LogLimitMiB };
            var destinationRoot = dialog.Portable ? Path.Combine(AppContext.BaseDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk");
            var targetStore = new ConfigurationStore(destinationRoot);
            if (dialog.Portable != portable && File.Exists(targetStore.ConfigPath)
                && MessageBox.Show(this, "目标数据目录已有配置，是否备份后替换？", "切换数据目录", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var p = config.Preferences;
            p.Theme = dialog.Preferences.Theme;
            p.StartAtLogin = dialog.Preferences.StartAtLogin; p.StartMinimized = dialog.Preferences.StartMinimized;
            p.MinimizeToTray = dialog.Preferences.MinimizeToTray; p.LogDays = dialog.Preferences.LogDays; p.LogLimitMiB = dialog.Preferences.LogLimitMiB;
            try
            {
                store.Save(config);
                if (dialog.Portable != portable)
                {
                    targetStore.Save(config);
                    var flag = Path.Combine(AppContext.BaseDirectory, "portable.flag");
                    if (dialog.Portable) File.WriteAllText(flag, "LaunchDesk portable mode\n"); else if (File.Exists(flag)) File.Delete(flag);
                }
                using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (p.StartAtLogin) run.SetValue("LaunchDesk", LaunchCommand.Quote(Environment.ProcessPath!) + " --autostart");
                else run.DeleteValue("LaunchDesk", false);
            }
            catch { config.Preferences.Theme = previous.Theme; config.Preferences.StartAtLogin = previous.StartAtLogin; config.Preferences.StartMinimized = previous.StartMinimized;
                config.Preferences.MinimizeToTray = previous.MinimizeToTray; config.Preferences.LogDays = previous.LogDays; config.Preferences.LogLimitMiB = previous.LogLimitMiB;
                store.Save(config); throw; }
            if (dialog.Portable != portable) MessageBox.Show(this, "数据目录将在重启 LaunchDesk 后切换；历史日志保留在原目录。", "设置已保存", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private void WindowStateChanged(object? s, EventArgs e) { if (WindowState == WindowState.Minimized && config.Preferences.MinimizeToTray && smokePath is null) Hide(); }
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true; SystemEvents.UserPreferenceChanged -= SystemThemeChanged; timer.Stop(); IsEnabled = false; StatusText.Text = "正在停止进程并保存日志…";
        foreach (var pending in folderStarts.Values.ToArray()) pending.Cancel();
        try { await manager.DisposeAsync(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "退出清理失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { tray?.Dispose(); trayIcon?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(); }
    }
}
