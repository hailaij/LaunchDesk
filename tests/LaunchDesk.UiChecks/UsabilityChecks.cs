using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LaunchDesk;
using LaunchDesk.Core;
using LaunchDesk.UiTesting;

internal static class UsabilityChecks
{
    public static void VerifyList(MainWindow window, string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var grid = (DataGrid)typeof(MainWindow).GetField("ItemsGrid", flags)!.GetValue(window)!;
        var item = grid.Items.Cast<ItemRow>().First(x => !x.IsFolder);
        grid.SelectedItem = item; grid.ScrollIntoView(item); grid.UpdateLayout();
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
        var actions = LogView.VisualChildren<StackPanel>(row).Single(x => x.Children.OfType<Button>().Any(b => b.Tag?.ToString() == "Run"));
        if (actions.Opacity != 1) throw new InvalidDataException("Selected row actions not emphasized");
        var last = actions.Children.OfType<Button>().Last();
        DependencyObject parent = last;
        while (parent is not DataGridCell) parent = VisualTreeHelper.GetParent(parent);
        var cell = (DataGridCell)parent;
        var bounds = last.TransformToAncestor(cell).TransformBounds(new Rect(last.RenderSize));
        if (bounds.Right > cell.ActualWidth + 1) throw new InvalidDataException("Last action clipped inside cell");
        var folder = grid.Items.Cast<ItemRow>().First(x => x.IsFolder);
        if (folder.MemberCount != grid.Items.Cast<ItemRow>().Count(x => x.Item.FolderId == folder.Item.Id)
            || !folder.Kind.Contains(folder.MemberCount.ToString())) throw new InvalidDataException("Folder member count missing");
        if (item.Item.FolderId.HasValue && item.NameIndent.Left < 24) throw new InvalidDataException("Grouped child indentation lost");
        var more = (Button)typeof(MainWindow).GetField("MoreButton", flags)!.GetValue(window)!;
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            if (!more.ContextMenu.Items.OfType<MenuItem>().Single(x => x.Tag?.ToString() == "Duplicate").IsEnabled)
                throw new InvalidDataException("Duplicate command disabled for selected launch item");
        }
        finally { more.ContextMenu.IsOpen = false; }
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", selectedActionsEmphasized = true,
            lastActionFitsCell = true, folderMemberCount = true, indentation = true, selectedMenuCommands = true }));
    }
    public static async Task Run(MainWindow window, string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var grid = (DataGrid)typeof(MainWindow).GetField("ItemsGrid", flags)!.GetValue(window)!;
        void Compact() => typeof(MainWindow).GetMethod("UpdateCompactLayout", flags)!.Invoke(window, null);
        var language = UiText.Language; var width = window.Width;
        var savedWidth = grid.Columns[0].Width;
        var evidence = new List<string>();
        void Check(bool passed, string name) { if (!passed) throw new InvalidDataException(name); evidence.Add(name); }
        try
        {
            grid.Columns[0].Width = 187;
            Compact(); window.Width = 1000; await Task.Delay(50); Compact();
            Check(grid.Columns[0].Width.Value == 187, "User width survives refresh and breakpoint change");
            UiText.Apply("zh-CN"); Compact(); var chinese = grid.Columns[2].Width.Value;
            UiText.Apply("en-US"); Compact();
            Check(grid.Columns[2].Width.Value == 120 && chinese == 110, "Language updates automatic width in same compact band");
            Check(grid.Columns[0].Width.Value == 187, "Language preserves manually resized width");
            Check(LogView.VisualChildren<GridSplitter>(window).Single().Background is not null, "Splitter discoverable");
            var more = (Button)typeof(MainWindow).GetField("MoreButton", flags)!.GetValue(window)!;
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(more.ContextMenu.IsOpen && more.ContextMenu.Items.OfType<MenuItem>().Count() == 8, "More menu exposes eight existing commands");
            Check(more.ContextMenu.Items.OfType<MenuItem>().Where(x => x.Tag is not null).All(x => !x.IsEnabled), "Selection commands disabled for empty list");
            more.ContextMenu.IsOpen = false;
            var manager = (ProcessManager)typeof(MainWindow).GetField("manager", flags)!.GetValue(window)!;
            var queue = (System.Collections.Concurrent.ConcurrentQueue<string>)typeof(ProcessManager).GetField("maintenanceWarnings", flags)!.GetValue(manager)!;
            queue.Enqueue("Isolated cleanup warning fixture");
            typeof(MainWindow).GetMethod("Refresh", flags)!.Invoke(window, null);
            var warnings = (Button)typeof(MainWindow).GetField("WarningsButton", flags)!.GetValue(window)!;
            Check(warnings.Visibility == Visibility.Visible, "Cleanup warning exposed without modal or selected item");
            queue.TryDequeue(out _);
            typeof(MainWindow).GetMethod("Refresh", flags)!.Invoke(window, null);
            Check(warnings.Visibility == Visibility.Collapsed, "Cleanup warning clears when no warning exists");
            foreach (var lang in new[] { "zh-CN", "en-US" })
            {
                UiText.Apply(lang);
                foreach (var invalid in new[] { "abc", "2147483648", "-1", "366" })
                {
                    try { FormLayout.Number(new TextBox { Text = invalid }, UiText.T("日志保留（天）"), 1, 365); throw new Exception("Accepted invalid number"); }
                    catch (InvalidDataException error) { Check(error.Message.Contains(UiText.T("日志保留（天）")) && error.Message.Contains("365"), "Field-specific validation " + lang + " " + invalid); }
                }
                Check(FormLayout.Number(new TextBox { Text = "365" }, "days", 1, 365) == 365, "Numeric inclusive boundary " + lang);
                var item = new ItemDialog(new LaunchItem { Name = "test", Target = "echo test", Kind = LaunchKind.Command }, [])
                    { Owner = window, Width = 620, Height = 500 };
                var settings = new SettingsDialog(new Preferences(), false, Path.GetTempPath()) { Owner = window, Width = 480, Height = 400 };
                try
                {
                    item.Show(); settings.Show(); await Task.Delay(100); item.UpdateLayout(); settings.UpdateLayout();
                    var check = LogView.VisualChildren<Button>(item).Single(x => x.Content?.ToString() == UiText.T("启动前检查"));
                    Check(check.IsVisible && !HasAncestor<ScrollViewer>(check), "Fixed preflight footer " + lang);
                    var before = check.TransformToAncestor(item).Transform(new Point());
                    LogView.VisualChildren<ScrollViewer>(item).First().ScrollToEnd(); item.UpdateLayout();
                    Check(before == check.TransformToAncestor(item).Transform(new Point()), "Preflight position independent of scroll " + lang);
                    var categories = LogView.VisualChildren<TabControl>(item).Single();
                    Check(categories.Items.Count == 5, "Five configuration categories " + lang);
                    Check(!item.HasUnsavedChanges, "Opening editor does not mark dirty " + lang);
                    foreach (TabItem category in categories.Items)
                    {
                        category.IsSelected = true; item.UpdateLayout();
                        var categoryScroll = (ScrollViewer)category.Content;
                        Check(categoryScroll.ExtentWidth <= categoryScroll.ViewportWidth + 1, "Compact category has no horizontal overflow " + lang + " " + category.Header);
                    }
                    ((TabItem)categories.Items[1]).IsSelected = true; item.UpdateLayout();
                    Check(!item.HasUnsavedChanges, "Navigation does not mark dirty " + lang);
                    var order = LogView.VisualChildren<TextBox>(item).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == UiText.T("启动顺序"));
                    order.Text = "invalid";
                    Check(item.HasUnsavedChanges, "Field edits mark unsaved changes " + lang);
                    categories.SelectedIndex = 0; item.UpdateLayout();
                    try { FormLayout.Number(order, UiText.T("启动顺序"), 0, 9999); throw new InvalidDataException("Invalid numeric input accepted"); }
                    catch (InvalidDataException error) { Check(error.Message.Contains("9999") && categories.SelectedIndex == 1, "Validation selects invalid field category " + lang); }
                    order.Text = "0";
                    Check(!item.HasUnsavedChanges, "Restored field value clears dirty state " + lang);
                    LogView.VisualChildren<Grid>(item).Single(x => x.Children.Count > 0 && x.Children[0] is TextBlock label && label.Text == UiText.T("启动 / 停止")).BringIntoView();
                    item.UpdateLayout();
                    UiSmokeRunner.Capture(item, report + "." + lang + ".item.png");
                    UiSmokeRunner.Capture(settings, report + "." + lang + ".settings.png");
                }
                finally { settings.Close(); item.Close(); }
            }
            var log = new LogView { FontSize = 12 };
            log.SetResourceReference(Control.BackgroundProperty, "Terminal");
            log.SetResourceReference(Control.ForegroundProperty, "TerminalText");
            var logWindow = new Window { Owner = window, Width = 700, Height = 300, Content = log };
            try
            {
                Theme.Apply("Light"); logWindow.Show();
                var lines = Enumerable.Range(0, 3000).Select(i => new ProjectedLogLine("[stdout] ERROR " + i + " https://example.com/", 9, "stdout")).ToArray();
                log.SetProjectedLines(lines); await Task.Delay(100); logWindow.UpdateLayout();
                Check(log.RealizedLineCount is > 0 and < 100 && log.HasRenderedLink, "Typed log virtualization and links");
                log.SelectRow(2, false, false); var selected = log.SelectedItem;
                log.SetProjectedLines(lines.Concat([new ProjectedLogLine("[stderr] failure", 9, "stderr")]).ToArray());
                Check(ReferenceEquals(selected, log.SelectedItem), "Typed append preserves selected row identity");
                var rendered = LogView.VisualChildren<LogLineView>(log).First(x => x.Channel == "stdout" && !x.IsRowSelected);
                var prefix = ((Paragraph)rendered.Document.Blocks.FirstBlock).Inlines.OfType<Run>().First();
                Check(ReferenceEquals(prefix.Foreground, Application.Current.Resources["TerminalText"]), "Stdout ERROR remains stdout");
                rendered.Channel = "stderr";
                Check(ReferenceEquals(prefix.Foreground, Application.Current.Resources["TerminalText"]), "Stderr is a transport channel, not error severity");
                Theme.ApplyHighContrast(Application.Current.Resources);
                Check(ReferenceEquals(prefix.Foreground, SystemColors.WindowTextBrush), "High contrast channel fallback");
                var selectedContainer = (ListBoxItem)ItemsControl.ContainerFromElement(log, rendered);
                log.SelectRow(log.ItemContainerGenerator.IndexFromContainer(selectedContainer), false, false);
                Check(ReferenceEquals(prefix.Foreground, SystemColors.HighlightTextBrush), "High contrast selection foreground");
                var link = ((Paragraph)rendered.Document.Blocks.FirstBlock).Inlines.OfType<Hyperlink>().Single();
                Check(ReferenceEquals(link.Foreground, SystemColors.HighlightTextBrush) && link.ToolTip!.ToString()!.Contains("Ctrl"), "Selected link contrast and tooltip");
                UiSmokeRunner.Capture(logWindow, report + ".high-contrast.png");
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    Theme.Apply(theme);
                    var samples = new[] { "INFO[2026-10-15 21:07:47] start HTTP server @ 0.0.0.0:5244", "WARN[2026-10-15 21:07:47] init tool aria2 failed", "ERROR connection failed" };
                    log.SetProjectedLines(samples.Select(x => new ProjectedLogLine("[stderr] " + x, 9, "stderr")).ToArray());
                    log.UnselectAll(); logWindow.UpdateLayout();
                    var stderrRows = LogView.VisualChildren<LogLineView>(log).Where(x => x.Channel == "stderr").ToArray();
                    Check(stderrRows.Length == samples.Length, "Alist stderr rows rendered " + theme);
                    foreach (var stderrRow in stderrRows)
                    {
                        var run = ((Paragraph)stderrRow.Document.Blocks.FirstBlock).Inlines.OfType<Run>().First();
                        Check(ReferenceEquals(run.Foreground, Application.Current.Resources["TerminalText"]), "Stderr severity is not inferred " + theme);
                        Check(stderrRow.Value.StartsWith("[stderr] ") && samples.Any(stderrRow.Value.EndsWith), "Original stderr content retained " + theme);
                    }
                    UiSmokeRunner.Capture(logWindow, report + ".stderr-" + theme + ".png");
                }
            }
            finally { logWindow.Close(); Theme.Apply("Light"); }
            Check(ExitCodeDisplay.Format(NativeProcess.ForcedExitCode) == "0xE04C4401" && ExitCodeDisplay.Format(1) == "1", "Forced versus ordinary exit formatting");
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", checks = evidence, highContrast = "simulated resource policy, not OS acceptance" }));
        }
        finally { UiText.Apply(language); window.Width = width; Compact(); grid.Columns[0].Width = savedWidth; }
    }
    private static bool HasAncestor<T>(DependencyObject value) where T : DependencyObject
    {
        while (VisualTreeHelper.GetParent(value) is { } parent) { if (parent is T) return true; value = parent; }
        return false;
    }
}
