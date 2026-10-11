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

public sealed class ItemRow(LaunchItem item) : INotifyPropertyChanged
{
    public LaunchItem Item { get; } = item;
    public string Name => Item.Name;
    public string Kind => Item.Kind switch { LaunchKind.Batch => "BAT / CMD", LaunchKind.Command => "命令", LaunchKind.Executable => "程序", LaunchKind.PowerShell => "PowerShell", _ => "Shell" };
    public string Target => Item.Target + " " + Item.Arguments;
    public string AutoStart => Item.AutoStart ? "是" : "";
    public string Status { get; private set; } = "未启动";
    public string Pid { get; private set; } = "";
    public string ExitCode { get; private set; } = "";
    public string Duration { get; private set; } = "";
    public Brush StatusBrush { get; private set; } = Brushes.Gray;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh(RuntimeSnapshot state)
    {
        Status = state.Status switch { RunStatus.Starting => "启动中", RunStatus.Running => "运行中", RunStatus.Stopping => "停止中", RunStatus.Exited => "正常退出", RunStatus.Failed => "异常退出", RunStatus.Restarting => "等待重启", _ => "未启动" };
        StatusBrush = state.Status switch { RunStatus.Running => new SolidColorBrush(Color.FromRgb(23, 123, 89)), RunStatus.Failed => Brushes.Firebrick, RunStatus.Starting or RunStatus.Restarting or RunStatus.Stopping => Brushes.DarkGoldenrod, _ => Brushes.Gray };
        Pid = state.Pid?.ToString() ?? ""; ExitCode = state.ExitCode?.ToString() ?? "";
        Duration = state.Active && state.Started is { } time ? (DateTimeOffset.Now - time).ToString(@"hh\:mm\:ss") : "";
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
    private readonly bool portable;
    private readonly string? smokePath;
    private readonly bool autostart;
    private bool exiting;
    private int lastRevision = -1;
    private Guid? lastSelected;
    private ItemRow? Selected => ItemsGrid.SelectedItem as ItemRow;
    public MainWindow(ConfigurationStore store, Configuration config, bool portable, string? smokePath, bool autostart)
    {
        this.store = store; this.config = config; this.portable = portable; this.smokePath = smokePath; this.autostart = autostart;
        manager = new(config, store.Root, Program.SignalAsync);
        InitializeComponent();
        ItemsGrid.ItemsSource = rows; ChannelFilter.SelectedIndex = 0;
        ModeText.Text = portable ? "便携模式 · v0.1.0" : "用户模式 · v0.1.0";
        timer.Tick += (_, _) => { try { Refresh(); } catch (Exception error) { timer.Stop(); ShowError(error); } }; timer.Start();
        Loaded += OnLoaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (smokePath is not null)
        {
            config.Items.AddRange([
                new() { Name = "聊天服务", Kind = LaunchKind.Batch, Target = @"C:\Scripts\chat-service.cmd" },
                new() { Name = "Node 服务", Kind = LaunchKind.Command, Target = "npm", Arguments = "run start", WorkingDirectory = @"C:\Services\node" },
                new() { Name = "后台任务", Kind = LaunchKind.PowerShell, Target = @"C:\Scripts\worker.ps1" }
            ]);
            Populate(); ItemsGrid.SelectedIndex = 0;
            manager.Logs(config.Items[0].Id).Write("stdout", "[12:00:01] 服务已连接\n[12:00:02] 启动完成，等待任务\n");
            manager.Logs(config.Items[0].Id).Write("stderr", "[12:00:03] 测试警告：连接重试\n");
            await Task.Delay(700); Refresh(); Capture(this, smokePath);
            var editor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items) { Owner = this };
            editor.Show(); await Task.Delay(250); Capture(editor, Path.ChangeExtension(smokePath, null) + "-editor.png"); editor.Close();
            var settings = new SettingsDialog(config.Preferences, portable) { Owner = this };
            settings.Show(); await Task.Delay(250); Capture(settings, Path.ChangeExtension(smokePath, null) + "-settings.png"); settings.Close();
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
        tray = new Forms.NotifyIcon { Text = "LaunchDesk", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 LaunchDesk", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("全部启动", null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(() => manager.StartAllAsync())));
        menu.Items.Add("全部停止", null, (_, _) => Dispatcher.InvokeAsync(async () => await Execute(manager.StopAllAsync)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.InvokeAsync(async () => await ExitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
    }
    public void Restore() { if (exiting) return; Show(); WindowState = WindowState.Normal; Activate(); }
    private void Populate(Guid? select = null)
    {
        select ??= Selected?.Item.Id;
        rows.Clear();
        foreach (var item in config.Items.Where(x => ItemSearch.Text.Length == 0 ||
            x.Name.Contains(ItemSearch.Text, StringComparison.OrdinalIgnoreCase) || x.Target.Contains(ItemSearch.Text, StringComparison.OrdinalIgnoreCase))) rows.Add(new(item));
        ItemsGrid.SelectedItem = rows.FirstOrDefault(x => x.Item.Id == select) ?? rows.FirstOrDefault();
        lastRevision = -1; Refresh();
    }
    private void Refresh()
    {
        if (exiting) return;
        foreach (var row in rows) row.Refresh(manager.Snapshot(row.Item.Id));
        SummaryText.Text = $"{config.Items.Count} 项 · {config.Items.Count(x => manager.Snapshot(x.Id).Status == RunStatus.Running)} 运行中";
        if (Selected is not { } selected) { SelectedTitle.Text = "日志"; LogBox.Clear(); StartButton.IsEnabled = StopButton.IsEnabled = RestartButton.IsEnabled = false; return; }
        SelectedTitle.Text = selected.Name + " · 日志";
        var state = manager.Snapshot(selected.Item.Id);
        StartButton.IsEnabled = !state.Active; StopButton.IsEnabled = state.Active; RestartButton.IsEnabled = true;
        if (state.Detail.Length > 0) StatusText.Text = selected.Name + "：" + state.Detail;
        var logs = manager.Logs(selected.Item.Id);
        if (lastSelected != selected.Item.Id) { lastSelected = selected.Item.Id; lastRevision = -1; logs.LoadRecent(); }
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
        LogBox.Text = value; LogBox.ScrollToEnd();
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
        if (!manager.Snapshot(item.Id).Active) return true;
        MessageBox.Show(this, "请先停止该启动项。", "LaunchDesk", MessageBoxButton.OK, MessageBoxImage.Information); return false;
    }
    private void SaveOrRollback(Action change)
    {
        var previous = System.Text.Json.JsonSerializer.Serialize(config, ConfigurationStore.JsonOptions);
        try { change(); store.Save(config); Populate(); }
        catch (Exception error)
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<Configuration>(previous, ConfigurationStore.JsonOptions)!;
            config.Items = restored.Items; ShowError(error); Populate();
        }
    }
    private void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ItemDialog(new(), config.Items) { Owner = this };
        if (dialog.ShowDialog() == true) SaveOrRollback(() => config.Items.Add(dialog.Item));
    }
    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || !CanEdit(row.Item)) return;
        var dialog = new ItemDialog(ConfigurationStore.Clone(row.Item), config.Items) { Owner = this };
        if (dialog.ShowDialog() == true) SaveOrRollback(() => config.Items[config.Items.FindIndex(x => x.Id == row.Item.Id)] = dialog.Item);
    }
    private void DuplicateClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        var clone = ConfigurationStore.Clone(row.Item); clone.Id = Guid.NewGuid(); clone.Name += " 副本"; clone.AutoStart = false;
        SaveOrRollback(() => config.Items.Add(clone));
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || !CanEdit(row.Item)) return;
        if (config.Items.Any(x => x.Dependencies.Contains(row.Item.Id))) { ShowError(new InvalidOperationException("其他启动项依赖该项，请先移除依赖。")); return; }
        if (MessageBox.Show(this, "删除“" + row.Name + "”？日志将保留。", "删除启动项", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SaveOrRollback(() => config.Items.Remove(row.Item));
    }
    private void Move(int offset)
    {
        if (Selected is not { } row) return;
        var index = config.Items.IndexOf(row.Item); var next = index + offset;
        if (next < 0 || next >= config.Items.Count) return;
        SaveOrRollback(() => (config.Items[index], config.Items[next]) = (config.Items[next], config.Items[index]));
    }
    private void MoveUpClick(object s, RoutedEventArgs e) => Move(-1);
    private void MoveDownClick(object s, RoutedEventArgs e) => Move(1);
    private async void StartClick(object s, RoutedEventArgs e) { if (Selected is { } row) await Execute(() => manager.StartAsync(row.Item.Id)); }
    private async void StopClick(object s, RoutedEventArgs e) { if (Selected is { } row) await Execute(() => manager.StopAsync(row.Item.Id)); }
    private async void RestartClick(object s, RoutedEventArgs e) { if (Selected is { } row) await Execute(async () => { await manager.StopAsync(row.Item.Id); await manager.StartAsync(row.Item.Id); }); }
    private async void StartAllClick(object s, RoutedEventArgs e) => await Execute(() => manager.StartAllAsync());
    private async void StopAllClick(object s, RoutedEventArgs e) => await Execute(manager.StopAllAsync);
    private async void SendInputClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        var input = InputText.Text; await Execute(() => manager.SendInputAsync(row.Item.Id, input)); InputText.Clear();
    }
    private void InputKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; SendInputClick(s, e); } }
    private void SelectionChanged(object s, SelectionChangedEventArgs e) { if (LogBox is not null) { lastRevision = -1; PauseLog.IsChecked = false; Refresh(); } }
    private void ItemSearchChanged(object s, TextChangedEventArgs e) { if (ItemsGrid is not null) Populate(); }
    private void LogFilterChanged(object s, SelectionChangedEventArgs e) { if (LogBox is not null) { lastRevision = -1; Refresh(); } }
    private void LogSearchChanged(object s, TextChangedEventArgs e) { if (LogBox is not null) { lastRevision = -1; Refresh(); } }
    private void ClearPanelClick(object s, RoutedEventArgs e) { if (Selected is { } row) { manager.Logs(row.Item.Id).ClearPanel(); Refresh(); } }
    private void DeleteHistoryClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (MessageBox.Show(this, "永久删除该项的历史日志？", "删除日志", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { manager.Logs(row.Item.Id).DeleteHistory(); Refresh(); } catch (Exception error) { ShowError(error); }
    }
    private async void ExportLogClick(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
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
        if (config.Items.Any(x => manager.Snapshot(x.Id).Active)) throw new InvalidOperationException("导入前请停止全部启动项。");
        var imported = ConfigurationStore.Read(path);
        if (MessageBox.Show(this, $"替换当前配置为 {imported.Items.Count} 个启动项？原配置将自动备份。导入项不会立即启动。", "导入配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // Login startup is a local preference, not an imported configuration's side effect.
        imported.Preferences.StartAtLogin = config.Preferences.StartAtLogin;
        store.Save(imported); await manager.DisposeAsync(); config = imported; manager = new(config, store.Root, Program.SignalAsync); Populate();
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
            var previous = new Preferences { StartAtLogin = config.Preferences.StartAtLogin, StartMinimized = config.Preferences.StartMinimized,
                MinimizeToTray = config.Preferences.MinimizeToTray, LogDays = config.Preferences.LogDays, LogLimitMiB = config.Preferences.LogLimitMiB };
            var destinationRoot = dialog.Portable ? Path.Combine(AppContext.BaseDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchDesk");
            var targetStore = new ConfigurationStore(destinationRoot);
            if (dialog.Portable != portable && File.Exists(targetStore.ConfigPath)
                && MessageBox.Show(this, "目标数据目录已有配置，是否备份后替换？", "切换数据目录", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var p = config.Preferences;
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
            catch { config.Preferences.StartAtLogin = previous.StartAtLogin; config.Preferences.StartMinimized = previous.StartMinimized;
                config.Preferences.MinimizeToTray = previous.MinimizeToTray; config.Preferences.LogDays = previous.LogDays; config.Preferences.LogLimitMiB = previous.LogLimitMiB;
                store.Save(config); throw; }
            if (dialog.Portable != portable) MessageBox.Show(this, "数据目录将在重启 LaunchDesk 后切换；历史日志保留在原目录。", "设置已保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) { ShowError(error); }
    }
    private void WindowClosing(object? s, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
    private void WindowStateChanged(object? s, EventArgs e) { if (WindowState == WindowState.Minimized && config.Preferences.MinimizeToTray && smokePath is null) Hide(); }
    private async Task ExitAsync()
    {
        if (exiting) return; exiting = true; timer.Stop(); IsEnabled = false; StatusText.Text = "正在停止进程并保存日志…";
        try { await manager.DisposeAsync(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "退出清理失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(); }
    }
}
