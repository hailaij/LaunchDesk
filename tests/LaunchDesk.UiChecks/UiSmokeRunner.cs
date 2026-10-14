using static LaunchDesk.UiText;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchDesk.Core;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Forms = System.Windows.Forms;

namespace LaunchDesk.UiTesting;

internal sealed class UiSmokeRunner
{
    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private readonly string smokePath;
    public MainWindow Window { get; }
    private Dispatcher Dispatcher => Window.Dispatcher;
    private ImageSource Icon => Window.Icon;
    private void UpdateLayout() => Window.UpdateLayout();
    public RoutedEventHandler SmokeLoadedHandler => OnSmokeLoaded;
    public UiSmokeRunner(ConfigurationStore store, Configuration config, bool portable, string report, bool autostart, string? pipe = null)
    {
        smokePath = report; config.Preferences.MinimizeToTray = false;
        Window = new MainWindow(store, config, portable, autostart, pipe);
        Window.ExitErrorPresenter = (error, fatal) => File.WriteAllText(smokePath + ".error.txt", error.ToString());
        var loaded = typeof(MainWindow).GetMethod("OnLoaded", Access)!;
        Window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), Window, loaded);
        Window.Loaded += OnSmokeLoaded;
    }
    private T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Access)!.GetValue(Window)!;
    private object? Invoke(string name, params object?[] args)
    {
        try { return typeof(MainWindow).GetMethod(name, Access)!.Invoke(Window, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private async void OnSmokeLoaded(object sender, RoutedEventArgs args)
    {
        try { await RunSmokeAsync(); }
        catch (Exception error)
        {
            File.WriteAllText(smokePath + ".error.txt", error.ToString());
            await (Task)Invoke("FailAndExitAsync", error)!;
        }
    }
    private ConfigurationStore store => Field<ConfigurationStore>("store");
    private Configuration config => Field<Configuration>("config");
    private ProcessManager manager => Field<ProcessManager>("manager");
    private ObservableCollection<ItemRow> rows => Field<ObservableCollection<ItemRow>>("rows");
    private DispatcherTimer timer => Field<DispatcherTimer>("timer");
    private System.Drawing.Icon? trayIcon => Field<System.Drawing.Icon?>("trayIcon");
    private ErrorDiagnostics diagnostics => Field<ErrorDiagnostics>("diagnostics");
    private bool portable => Field<bool>("portable");
    private ItemRow? Selected => (ItemRow?)typeof(MainWindow).GetProperty("Selected", Access)!.GetValue(Window);
    private DataGrid ItemsGrid => Window.ItemsGrid;
    private LogView LogBox => Window.LogBox;
    private CheckBox PauseLog => Window.PauseLog;
    private CheckBox AutoScroll => Window.AutoScroll;
    private TextBox ItemSearch => Window.ItemSearch;
    private ComboBox ChannelFilter => Window.ChannelFilter;
    private TextBox LogSearch => Window.LogSearch;
    private long lastRevision { set => typeof(MainWindow).GetField("lastRevision", Access)!.SetValue(Window, value); }

    private void RefreshTick(Action refresh) => Invoke("RefreshTick", refresh);
    private void CreateTray() => Invoke("CreateTray");
    private void Populate(Guid? select = null) => Invoke("Populate", select);
    private void Refresh() => Invoke("Refresh");
    private void CommitName(ItemRow row) => Invoke("CommitName", row);
    private void CommitNote(ItemRow row) => Invoke("CommitNote", row);
    private void NoteEditorKeyDown(object sender, KeyEventArgs e) => Invoke("NoteEditorKeyDown", sender, e);
    private void NoteEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Invoke("NoteEditorLostFocus", sender, e);
    private void ApplyRowDrop(ItemRow source, ItemRow? targetRow) => Invoke("ApplyRowDrop", source, targetRow);
    private void ToggleFolderClick(object s, RoutedEventArgs e) => Invoke("ToggleFolderClick", s, e);
    private Task ExitAsync(int exitCode = 0, Exception? fatalError = null) => (Task)Invoke("ExitAsync", exitCode, fatalError)!;
    private async Task RunSmokeAsync()
    {
            CreateTray();
            if (Icon is null || trayIcon is null) throw new InvalidOperationException(T("应用图标测试失败。"));
            Populate();
            if (config.Items.Count != 0 || config.Folders.Count != 0 || LogBox.Text.Length != 0)
                throw new InvalidOperationException(T("首次启动空白数据测试失败。"));
            Capture(Window, Path.ChangeExtension(smokePath, null) + "-empty.png");
            var logProbe = new LogView();
            var payload = string.Concat(Enumerable.Range(0, 2000).Select(x => $"line-{x} {new string('x', 180)}\n"));
            var measure = Stopwatch.StartNew(); logProbe.Text = payload;
            var probeSource = logProbe.ItemsSource;
            for (var i = 0; i < 10; i++) logProbe.Text += $"next-{i} http://localhost:8080/\n";
            measure.Stop();
            File.WriteAllText(Path.ChangeExtension(smokePath, null) + "-log-performance.json", System.Text.Json.JsonSerializer.Serialize(new { elapsedMs = measure.ElapsedMilliseconds, sourceReused = ReferenceEquals(probeSource, logProbe.ItemsSource), characters = logProbe.Text.Length }));
            if (!ReferenceEquals(probeSource, logProbe.ItemsSource)) throw new InvalidOperationException("Log append replaces the source collection.");
            logProbe.Text = string.Join('\n', logProbe.Text.Split('\n').Skip(512)) + "tail\n";
            logProbe.Text = "http://loc"; logProbe.Text += "alhost:8080/";
            var lineProbe = new LogLineView { Value = logProbe.Text };
            if (lineProbe.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(x => x.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Single().NavigateUri.AbsoluteUri != "http://localhost:8080/")
                throw new InvalidOperationException("Partial log link append failed.");
            logProbe.Clear();
            config.Items.AddRange([
                new() { Name = T("聊天服务"), Kind = LaunchKind.Batch, Target = @"C:\Scripts\chat-service.cmd", Note = "Local service", StopInput = "Y", StopInputDelaySeconds = 2, SendStopSignalBeforeInput = true },
                new() { Name = T("Node 服务"), Kind = LaunchKind.Command, Target = "npm", Arguments = "run start", WorkingDirectory = @"C:\Services\node" },
                new() { Name = T("后台任务"), Kind = LaunchKind.PowerShell, Target = @"C:\Scripts\worker.ps1" }
            ]);
            var folder = new LaunchFolder { Name = T("聊天工具") }; config.Folders.Add(folder); config.Items[0].FolderId = folder.Id;
            Populate();
            var sortedView = CollectionViewSource.GetDefaultView(rows);
            sortedView.SortDescriptions.Add(new(nameof(ItemRow.IsFolder), ListSortDirection.Ascending));
            if (((ItemRow)ItemsGrid.Items[0]).IsFolder) throw new InvalidOperationException("Flat sorting reproduction failed.");
            Populate();
            if (ItemsGrid.CanUserSortColumns || sortedView.SortDescriptions.Count != 0 || !((ItemRow)ItemsGrid.Items[0]).IsFolder
                || ((ItemRow)ItemsGrid.Items[1]).Item.FolderId != folder.Id)
                throw new InvalidOperationException("Folder hierarchy recovery failed.");
            var otherFolder = new LaunchFolder { Name = "Folder reorder test" }; config.Folders.Add(otherFolder); Populate();
            ApplyRowDrop(rows.Single(x => x.IsFolder && x.Item.Id == otherFolder.Id), rows.Single(x => x.IsFolder && x.Item.Id == folder.Id));
            if (((ItemRow)ItemsGrid.Items[0]).Item.Id != otherFolder.Id || ((ItemRow)ItemsGrid.Items[1]).Item.Id != folder.Id
                || ((ItemRow)ItemsGrid.Items[2]).Item.FolderId != folder.Id)
                throw new InvalidOperationException("Folder drag separated members.");
            config.Folders.Remove(otherFolder); Populate();
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
            Capture(Window, smokePath);
            var healthRow = rows.First(x => !x.IsFolder);
            healthRow.Refresh(new(RunStatus.Running, 1234, null, DateTimeOffset.Now, "", true)
                { Health = new(HealthStatus.Unhealthy, DateTimeOffset.Now, "HTTP 503") });
            if (healthRow.HealthText != T("不健康") || !healthRow.HealthDetail.Contains("HTTP 503"))
                throw new InvalidOperationException("Health row binding failed");
            Capture(Window, Path.ChangeExtension(smokePath, null) + "-health.png"); Refresh();
            var healthPreview = ConfigurationStore.Clone(config.Items[0]);
            healthPreview.Health = new() { Enabled = true, ReuseReadiness = false, Kind = ReadinessKind.Http, AutoRestart = true };
            var healthEditor = new ItemDialog(healthPreview, config.Items, config.Folders) { Owner = Window };
            healthEditor.Show();
            if (healthEditor.Content is Border { Child: Grid healthGrid } && healthGrid.Children[0] is ScrollViewer { Content: StackPanel healthForm } healthScroll)
            {
                var healthSection = healthForm.Children.OfType<Grid>().Single(x => x.Children[0] is TextBlock label && label.Text == T("健康监控"));
                healthEditor.UpdateLayout(); healthScroll.ScrollToVerticalOffset(healthSection.TranslatePoint(new Point(0, 0), healthForm).Y - 30);
                await Task.Delay(250); Capture(healthEditor, Path.ChangeExtension(smokePath, null) + "-health-settings.png");
            }
            healthEditor.Close();
            var editor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = Window };
            editor.Show(); await Task.Delay(250); Capture(editor, Path.ChangeExtension(smokePath, null) + "-editor.png");
            if (editor.Content is Border { Child: Grid editorGrid } && editorGrid.Children[0] is ScrollViewer { Content: StackPanel editorForm })
            {
                editorForm.Children.OfType<Grid>().Single(x => x.Children[0] is TextBlock label && label.Text == T("退出信号")).BringIntoView();
                await Task.Delay(250); Capture(editor, Path.ChangeExtension(smokePath, null) + "-stop-options.png");
            }
            editor.Close();
            var settings = new SettingsDialog(config.Preferences, portable, store.Root, () => store.BackupNow(config)) { Owner = Window };
            settings.Show(); await Task.Delay(250); Capture(settings, Path.ChangeExtension(smokePath, null) + "-settings.png"); settings.Close();
            var about = new AboutDialog { Owner = Window };
            about.Show(); await Task.Delay(250); Capture(about, Path.ChangeExtension(smokePath, null) + "-about.png"); about.Close();
            var updateAbout = new AboutDialog { Owner = Window };
            updateAbout.ApplyUpdateResult(new ReleaseUpdate(new Version(9, 0, 0), "v9.0.0", T("界面测试更新说明，不执行下载或安装。"), new Uri("https://github.com/hailaij/LaunchDesk/releases")));
            updateAbout.Show(); await Task.Delay(250); Capture(updateAbout, Path.ChangeExtension(smokePath, null) + "-update.png"); updateAbout.Close();
            Theme.Apply("Dark"); Refresh(); await Task.Delay(250); Capture(Window, Path.ChangeExtension(smokePath, null) + "-dark.png");
            var darkEditor = new ItemDialog(ConfigurationStore.Clone(config.Items[0]), config.Items, config.Folders) { Owner = Window };
            darkEditor.Show(); await Task.Delay(250); Capture(darkEditor, Path.ChangeExtension(smokePath, null) + "-editor-dark.png"); darkEditor.Close();
            var darkSettings = new SettingsDialog(config.Preferences, portable, store.Root, () => store.BackupNow(config)) { Owner = Window };
            darkSettings.Show(); await Task.Delay(250); Capture(darkSettings, Path.ChangeExtension(smokePath, null) + "-settings-dark.png"); darkSettings.Close();
            var darkAbout = new AboutDialog { Owner = Window };
            darkAbout.Show(); await Task.Delay(250); Capture(darkAbout, Path.ChangeExtension(smokePath, null) + "-about-dark.png"); darkAbout.Close();
            var child = rows.Single(x => x.Item.FolderId == folder.Id);
            ApplyRowDrop(child, null);
            if (config.Items.Single(x => x.Id == child.Item.Id).FolderId is not null) throw new InvalidOperationException(T("拖拽移出分组测试失败。"));
            ApplyRowDrop(rows.Single(x => x.Item.Id == child.Item.Id), rows.Single(x => x.IsFolder));
            if (config.Items.Single(x => x.Id == child.Item.Id).FolderId != folder.Id) throw new InvalidOperationException(T("拖拽移入分组测试失败。"));
            var backup = store.BackupNow(config);
            if (ConfigurationStore.Read(backup).Items.Count != config.Items.Count) throw new InvalidOperationException(T("手动备份界面测试失败。"));
            var readyItem = ConfigurationStore.Clone(child.Item); readyItem.Readiness = new() { Kind = ReadinessKind.Log, Keyword = "READY" };
            var readinessEditor = new ItemDialog(readyItem, config.Items, config.Folders) { Owner = Window };
            readinessEditor.Show();
            if (readinessEditor.Content is Border { Child: Grid formGrid } && formGrid.Children[0] is ScrollViewer formScroll) formScroll.ScrollToEnd();
            await Task.Delay(250); Capture(readinessEditor, Path.ChangeExtension(smokePath, null) + "-readiness.png"); readinessEditor.Close();
            var secondMember = config.Items.Single(x => x.Kind == LaunchKind.Command); secondMember.FolderId = folder.Id;
            manager.Logs(secondMember.Id).Write("stdout", T("第二项输出 http://127.0.0.1:4000/pro/\n"));
            Populate(); ItemsGrid.SelectedItem = rows.Single(x => x.IsFolder); Refresh();
            if (!LogBox.Text.Contains(T("[聊天服务]")) || !LogBox.Text.Contains(T("[Node 服务]")) || !LogBox.Text.Contains(T("第二项输出")))
                throw new InvalidOperationException(T("文件夹合并日志测试失败。"));
            UpdateLayout();
            if (!LogBox.HasRenderedLink)
                throw new InvalidOperationException(T("日志链接标记测试失败。"));
            Capture(Window, Path.ChangeExtension(smokePath, null) + "-merged.png");
            var renameRow = rows.Single(x => x.Item.Id == secondMember.Id);
            ItemsGrid.SelectedItem = renameRow; renameRow.SetRenaming(true);
            await Task.Delay(250); Capture(Window, Path.ChangeExtension(smokePath, null) + "-rename.png");
            renameRow.RenameText = T("原位重命名"); CommitName(renameRow);
            if (store.Load().Items.Single(x => x.Id == secondMember.Id).Name != T("原位重命名"))
                throw new InvalidOperationException(T("原位重命名持久化测试失败。"));
            var noteRow = rows.Single(x => x.Item.Id == secondMember.Id);
            noteRow.SetEditingNote(true); noteRow.NoteText = "Inline note";
            await Task.Delay(250); Capture(Window, Path.ChangeExtension(smokePath, null) + "-note-edit.png");
            CommitNote(noteRow);
            if (store.Load().Items.Single(x => x.Id == secondMember.Id).Note != "Inline note")
                throw new InvalidOperationException("Inline note persistence failed.");
            noteRow = rows.Single(x => x.Item.Id == secondMember.Id);
            noteRow.SetEditingNote(true); noteRow.NoteText = "Discarded note";
            NoteEditorKeyDown(new TextBox { DataContext = noteRow }, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            if (noteRow.IsEditingNote || noteRow.Note != "Inline note") throw new InvalidOperationException("Note cancel failed.");
            noteRow.SetEditingNote(true); noteRow.NoteText = "Saved on blur";
            NoteEditorLostFocus(new TextBox { DataContext = noteRow }, new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, ItemsGrid));
            if (store.Load().Items.Single(x => x.Id == secondMember.Id).Note != "Saved on blur")
                throw new InvalidOperationException("Note focus-loss save failed.");
            var updatePrompt = new UpdateDialog(new(new Version(9, 0, 0), "v9.0.0", T("更新界面测试：不会访问网络或执行安装。"), new Uri("https://github.com/hailaij/LaunchDesk/releases/tag/v9.0.0"))) { Owner = Window };
            updatePrompt.Show(); await Task.Delay(250);
            var updateContent = (StackPanel)((ScrollViewer)((Border)updatePrompt.Content).Child).Content;
            var updateActions = updateContent.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
            if (updateActions.Length != 2 || updateActions[0].Content?.ToString() != T("打开下载页") || !updateActions[0].IsEnabled
                || updateActions[1].Content?.ToString() != T("稍后") || updateContent.Children.OfType<ProgressBar>().Any())
                throw new InvalidOperationException("Link-only update dialog contract failed.");
            Capture(updatePrompt, Path.ChangeExtension(smokePath, null) + "-update-prompt.png"); updatePrompt.Close();
            var originalLanguage = config.Preferences.Language;
            var names = config.Items.Select(x => x.Name).ToArray();
            var rawLog = string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text));
            UiText.Apply(originalLanguage == "en-US" ? "zh-CN" : "en-US"); Populate(); Refresh();
            await Task.Delay(100); UpdateLayout();
            if (ItemsGrid.Columns[0].Header?.ToString() != T("名称") || AutoScroll.Content?.ToString() != T("自动滚动") || !names.SequenceEqual(config.Items.Select(x => x.Name))
                || rawLog != string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text)))
                throw new InvalidOperationException("Live language switch failed. Header=" + ItemsGrid.Columns[0].Header + "; expected=" + T("名称")
                    + "; names=" + names.SequenceEqual(config.Items.Select(x => x.Name)) + "; logs=" + (rawLog == string.Concat(manager.Logs(secondMember.Id).Snapshot().Select(x => x.Text))));
            Capture(Window, Path.ChangeExtension(smokePath, null) + "-language-switch.png");
            UiText.Apply(originalLanguage); Populate(); Refresh();
            var liveTimings = new List<long>();
            for (var batch = 0; batch < 10; batch++)
            {
                manager.Logs(secondMember.Id).Write("stdout", string.Concat(Enumerable.Range(0, 1000).Select(x => $"stress-{batch}-{x} {new string('x', 160)}\n")));
                var liveMeasure = Stopwatch.StartNew(); Refresh();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                liveTimings.Add(liveMeasure.ElapsedMilliseconds);
                if (LogBox.Text.Length > 500_000 || liveMeasure.ElapsedMilliseconds > 5000) throw new InvalidOperationException("Live log stress exceeded display budget.");
                if (LogBox.VerticalOffset + LogBox.ViewportHeight < LogBox.ExtentHeight - 2) throw new InvalidOperationException("Auto-scroll did not reach the newest log lines.");
                if (LogBox.RealizedLineCount is <= 0 or > 100) throw new InvalidOperationException("Log rows are not virtualized.");
                foreach (var rendered in LogView.VisualChildren<LogLineView>(LogBox).Where(x => x.Value.StartsWith("stress-")))
                {
                    var run = rendered.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First().Inlines.OfType<System.Windows.Documents.Run>().First();
                    var firstRect = run.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                    var lastRect = run.ContentStart.GetPositionAtOffset(run.Text.Length - 1)!.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                    if (Math.Abs(firstRect.Y - lastRect.Y) > 1 || lastRect.X <= firstRect.X + 60)
                        throw new InvalidOperationException("Virtual log row wrapped or clipped its long text.");
                }
            }
            File.WriteAllText(Path.ChangeExtension(smokePath, null) + "-live-log-performance.json", System.Text.Json.JsonSerializer.Serialize(new { batches = 10, linesPerBatch = 1000, refreshAndLayoutMs = liveTimings, displayedCharacters = LogBox.Text.Length, renderedRows = LogBox.RealizedLineCount, totalRows = LogBox.Items.Count }));
            Capture(Window, Path.ChangeExtension(smokePath, null) + "-stress.png");
            await VerifyVirtualLogAsync();
            await VerifyIncrementalLogAsync();
            RefreshTick(() => throw new IOException("Injected transient refresh failure"));
            var recovered = false; RefreshTick(() => { Refresh(); recovered = true; });
            if (!recovered || !timer.IsEnabled || !File.ReadAllText(diagnostics.PathName).Contains("Injected transient refresh failure"))
                throw new InvalidOperationException("Refresh did not recover with diagnostics.");
            try { await Program.SignalAsync(int.MaxValue); throw new InvalidOperationException("Invalid PID signalled"); }
            catch (IOException error) { if (!error.Message.Contains("AttachConsole Win32=")) throw; }
            await ExitAsync(); return;
    }
    private async Task VerifyVirtualLogAsync()
    {
        var oldText = LogBox.Text;
        PauseLog.IsChecked = true;
        var timings = new List<long>();
        var payload = string.Concat(Enumerable.Range(0, 30000).Select(x => $"v-{x}\n"));
        var measure = Stopwatch.StartNew(); LogBox.Text = payload; LogBox.ScrollToEnd();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); timings.Add(measure.ElapsedMilliseconds);
        if (LogBox.RealizedLineCount is <= 0 or > 100 || LogBox.Items.Count != 30001)
            throw new InvalidOperationException("Short-line virtualization failed.");
        LogBox.ScrollToVerticalOffset(1500);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var anchorOffset = LogBox.VerticalOffset;
        LogBox.SelectRow(1500, false, false); LogBox.SelectRow(1502, true, false);
        if (LogBox.SelectedText != "v-1500\nv-1501\nv-1502") throw new InvalidOperationException("Virtualized row range selection failed.");
        LogBox.SelectRow(1500, false, false);
        LogBox.Text += "tail http://localhost:8080/\n";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(LogBox.VerticalOffset - anchorOffset) > 1 || LogBox.SelectedText != "v-1500")
            throw new InvalidOperationException($"Reading position or selection changed on append. Old={anchorOffset}, new={LogBox.VerticalOffset}, selected={LogBox.SelectedText}");
        LogBox.Text = string.Join('\n', LogBox.Text.Split('\n').Skip(500));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(LogBox.VerticalOffset - (anchorOffset - 500)) > 1 || LogBox.SelectedText != "v-1500")
            throw new InvalidOperationException("Rolling retention lost selected row/anchor.");
        LogBox.SelectAll();
        if (LogBox.SelectedText != LogBox.Text) throw new InvalidOperationException("Copy all excludes virtualized rows.");
        LogBox.UnselectAll(); LogBox.ScrollToEnd();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!LogBox.HasRenderedLink) throw new InvalidOperationException("Recycled rows lose links.");
        measure.Restart(); LogBox.Text = "replacement\n"; LogBox.ScrollToEnd();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); timings.Add(measure.ElapsedMilliseconds);
        if (LogBox.HasRenderedLink || LogBox.RealizedLineCount > 2) throw new InvalidOperationException("Recycled rows retain old links.");
        var selectable = LogView.VisualChildren<LogLineView>(LogBox).First(x => x.Value == "replacement"); selectable.Focus();
        var run = selectable.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First().Inlines.OfType<System.Windows.Documents.Run>().First();
        selectable.Selection.Select(run.ContentStart.GetPositionAtOffset(1)!, run.ContentStart.GetPositionAtOffset(5)!);
        if (LogBox.SelectedText != "epla") throw new InvalidOperationException("Character selection within a visible line failed.");
        LogBox.Focus();
        if (LogBox.SelectedText != "epla") throw new InvalidOperationException("Context-menu focus loses character selection.");
        selectable.Selection.Select(selectable.Document.ContentStart, selectable.Document.ContentStart); LogBox.Focus();
        File.WriteAllText(Path.ChangeExtension(smokePath!, null) + "-virtualization.json", System.Text.Json.JsonSerializer.Serialize(new { totalShortLines = 30000, updateAndLayoutMs = timings, selectionAndReadingPositionPreserved = true, virtualCopyAllPassed = true }));
        LogBox.Text = oldText; PauseLog.IsChecked = false; lastRevision = -1; Refresh();
    }
    private async Task VerifyIncrementalLogAsync()
    {
        PauseLog.IsChecked = true;
        AutoScroll.IsChecked = false;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var payload = Enumerable.Range(0, 3000).Select(x => $"incremental-{x} {new string('x', 80)}").ToList();
        LogBox.SetLines(payload.ToArray()); LogBox.ScrollToVerticalOffset(1500);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        LogBox.SelectRow(1500, false, false);
        var selected = LogBox.SelectedText; var resets = LogBox.ResetCount;
        var timings = new List<double>(); var allocations = new List<long>(); var maxRows = 0;
        for (var batch = 0; batch < 40; batch++)
        {
            payload.RemoveRange(0, 20);
            payload.AddRange(Enumerable.Range(0, 20).Select(x => $"new-{batch}-{x} http://localhost:8080/"));
            var before = GC.GetAllocatedBytesForCurrentThread(); var measure = Stopwatch.StartNew();
            LogBox.SetLines(payload.ToArray());
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            timings.Add(measure.Elapsed.TotalMilliseconds); maxRows = Math.Max(maxRows, LogBox.RealizedLineCount);
            if (LogBox.SelectedText != selected || Math.Abs(LogBox.VerticalOffset - (1500 - 20 * (batch + 1))) > 1)
                throw new InvalidOperationException($"Incremental retention changed selected text or reading anchor: batch={batch}, offset={LogBox.VerticalOffset}, expected={1500 - 20 * (batch + 1)}, selected={LogBox.SelectedText}, original={selected}.");
        }
        File.WriteAllText(Path.ChangeExtension(smokePath!, null) + "-incremental-performance.json",
            System.Text.Json.JsonSerializer.Serialize(new { batches = 40, retainedRows = payload.Count, rowsPerBatch = 20,
                refreshAndLayoutMs = timings, refreshAllocatedBytes = allocations, resets = LogBox.ResetCount - resets,
                maxRenderedRows = maxRows, selectionAndAnchorPreserved = true }));
        LogBox.SetLines(["repeat", "repeat", "unique", "tail"]); LogBox.SelectRow(2, false, false);
        resets = LogBox.ResetCount;
        LogBox.SetLines(["repeat", "unique", "tail", "fresh"]);
        if (LogBox.SelectedText != "unique" || LogBox.ResetCount != resets)
            throw new InvalidOperationException("Repeated log lines lost retained row identity.");
        AutoScroll.IsChecked = true; PauseLog.IsChecked = false; lastRevision = -1; Refresh();
    }

    public static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var content = (FrameworkElement)window.Content;
        image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }

}
