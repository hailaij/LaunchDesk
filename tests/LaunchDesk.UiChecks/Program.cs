using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LaunchDesk;
using LaunchDesk.Core;

internal static class UiChecks
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        if (args.Length != 2 || args[0] is not ("--timer" or "--loaded" or "--control" or "--recovery-accept" or "--recovery-accept-en" or "--recovery-skip" or "--accessibility" or "--accessibility-en")) return 2;
        var report = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var root = Path.Combine(Path.GetTempPath(), "LaunchDesk-ui-check-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigurationStore(root);
        var config = new Configuration { Preferences = new() { CheckUpdatesOnStartup = false } };
        var product = args[0] is "--control" or "--recovery-accept" or "--recovery-accept-en" or "--recovery-skip";
        var recoveryMode = args[0].StartsWith("--recovery-", StringComparison.Ordinal);
        var acceptRecovery = args[0] is "--recovery-accept" or "--recovery-accept-en";
        if (args[0].EndsWith("-en", StringComparison.Ordinal)) config.Preferences.Language = "en-US";
        if (args[0].StartsWith("--accessibility", StringComparison.Ordinal))
        {
            var folder = new LaunchFolder { Name = "Accessible folder" }; config.Folders.Add(folder);
            config.Items.Add(new() { Name = "Accessible item", Kind = LaunchKind.Command, Target = "echo isolated", FolderId = folder.Id });
        }
        if (product)
        {
            config.Items.Add(new() { Name = "CLI 中文 服务", Kind = LaunchKind.Executable, Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /q /k", StopInput = "exit", StopTimeoutSeconds = 1 });
            config.Items.Add(new() { Name = "other", Kind = LaunchKind.Executable, Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /q /k", StopInput = "exit", StopTimeoutSeconds = 1, AutoStart = recoveryMode });
            store.Save(config);
            if (recoveryMode)
            {
                var journal = new SessionJournal(root); journal.Begin(); journal.MarkActive(config.Items[0].Id); journal.MarkActive(Guid.NewGuid());
            }
        }
        var app = new App(); app.InitializeComponent();
        var iconFont = (System.Windows.Media.FontFamily)app.Resources["IconFont"];
        if (iconFont.Source != "Segoe MDL2 Assets" || !new System.Windows.Media.Typeface(iconFont, System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal).TryGetGlyphTypeface(out var glyphs)) return 1;
        foreach (var code in new[] { 0xE70D, 0xE73E, 0xE76C, 0xE8B7, 0xE756, 0xE8B5, 0xEDE1, 0xE777, 0xE713,
            0xE8F4, 0xE768, 0xE71A, 0xE710, 0xE70F, 0xE8C8, 0xE74D, 0xE70E, 0xE721, 0xE72C, 0xE74E, 0xE894, 0xE122 })
            if (!glyphs.CharacterToGlyphMap.TryGetValue(code, out var index) || index == 0) throw new InvalidDataException($"Missing icon glyph: {code:X4}");
        var loaded = args[0] == "--loaded";
        if (loaded) config.Items.Add(new() { Name = "fault", Target = "cmd" });
        var pipe = "LaunchDesk-ui-test-" + Guid.NewGuid().ToString("N");
        var window = new MainWindow(store, config, false, loaded ? report + ".png" : null, false, pipe);
        var escaped = false;
        app.DispatcherUnhandledException += (_, e) => { escaped = true; e.Handled = true; app.Shutdown(1); };
        if (args[0].StartsWith("--accessibility", StringComparison.Ordinal))
        {
            var accessibilityProbe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            var passed = false; string? failure = null;
            accessibilityProbe.Tick += async (_, _) =>
            {
                accessibilityProbe.Stop();
                ItemDialog? editor = null;
                try
                {
                    IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
                    {
                        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                        {
                            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
                            foreach (var descendant in Descendants(child)) yield return descendant;
                        }
                    }
                    void Named(System.Windows.Controls.Control control)
                    {
                        if (control is System.Windows.Controls.TextBox &&
                            System.Windows.Automation.AutomationProperties.GetLabeledBy(control) is not null &&
                            string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(control)))
                            throw new InvalidDataException("Form input must expose a name independently of label peer realization.");
                        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                        if (string.IsNullOrWhiteSpace(peer?.GetName()) || peer.GetName().Any(c => c is >= '\uE000' and <= '\uF8FF'))
                            throw new InvalidDataException("Unnamed/icon-only automation control: " + control.GetType().Name + " name=" + control.Name + " tooltip=" + control.ToolTip + " content=" + (control as System.Windows.Controls.ContentControl)?.Content + " peer=" + peer?.GetName());
                    }
                    var buttons = Descendants(window).OfType<System.Windows.Controls.Button>().ToArray();
                    foreach (var button in buttons) Named(button);
                    var grid = Descendants(window).OfType<System.Windows.Controls.DataGrid>().Single(); Named(grid);
                    var logs = Descendants(window).OfType<LogView>().Single(); Named(logs);
                    void Key(System.Windows.UIElement target, System.Windows.Input.Key key)
                    {
                        var input = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                            System.Windows.PresentationSource.FromVisual(window), 0, key)
                            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                        target.RaiseEvent(input);
                    }
                    grid.SelectedItem = grid.Items[0]; grid.Focus(); Key(grid, System.Windows.Input.Key.Left);
                    if (grid.Items.Count != 1) throw new InvalidDataException("Keyboard folder collapse failed");
                    Key(grid, System.Windows.Input.Key.Right); grid.UpdateLayout();
                    if (grid.Items.Count != 2) throw new InvalidDataException("Keyboard folder expand failed");
                    var row = (ItemRow)grid.Items[1]; grid.SelectedItem = row;
                    grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(row, grid.Columns[0]);
                    Key(grid, System.Windows.Input.Key.F2);
                    if (!row.IsRenaming) throw new InvalidDataException("Keyboard inline rename failed");
                    row.SetRenaming(false);
                    grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(row, grid.Columns.Single(x => x.Header?.ToString() is "备注" or "Note"));
                    Key(grid, System.Windows.Input.Key.F2);
                    if (!row.IsEditingNote) throw new InvalidDataException("Keyboard inline note failed");
                    row.SetEditingNote(false); grid.UpdateLayout();
                    logs.Text = "Accessible log text"; logs.UpdateLayout();
                    var logRow = (System.Windows.Controls.ListBoxItem)logs.ItemContainerGenerator.ContainerFromIndex(0);
                    if (System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(logRow)?.GetName() != "Accessible log text")
                        throw new InvalidDataException("Log automation lost text");
                    foreach (var input in Descendants(window).OfType<System.Windows.Controls.TextBox>().Where(x => x.IsVisible)) Named(input);
                    editor = new ItemDialog(new() { Target = "echo isolated" }, []) { Owner = window };
                    editor.Show(); editor.UpdateLayout();
                    foreach (var input in Descendants(editor).OfType<System.Windows.Controls.TextBox>().Where(x => x.IsVisible && x.Name != "PART_EditableTextBox")) Named(input);
                    if (buttons.Any(x => x.FocusVisualStyle is null)) throw new InvalidDataException("Missing keyboard focus visual");
                    typeof(MainWindow).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [editor, report + ".png"]);
                    passed = true;
                }
                catch (Exception error) { failure = error.ToString(); }
                finally { editor?.Close(); await (Task)typeof(MainWindow).GetMethod("ExitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [passed ? 0 : 1, null])!; }
            };
            window.Loaded += (_, _) => accessibilityProbe.Start();
            var result = app.Run(window);
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed, failure, cleanExit = result == 0, escaped, scope = "Automation peer names and focus; manual screen reader/Win10 acceptance deferred" }));
            return passed && !escaped && result == 0 ? 0 : 1;
        }
        if (product)
        {
            var productProbe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            var decision = !recoveryMode; var running = false; var passed = false; string? failure = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            productProbe.Tick += async (_, _) =>
            {
                if (!decision && app.Windows.OfType<RecoveryDialog>().FirstOrDefault() is { } dialog)
                {
                    typeof(MainWindow).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [dialog, report + ".png"]);
                    dialog.DialogResult = acceptRecovery; decision = true; return;
                }
                if (running) return;
                if (DateTimeOffset.UtcNow > deadline) { failure = "Product UI timed out"; productProbe.Stop(); app.Shutdown(1); return; }
                if (!decision || typeof(MainWindow).GetField("controlServer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null) return;
                var manager = (ProcessManager)typeof(MainWindow).GetField("manager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                if (acceptRecovery && manager.Snapshot(config.Items[0].Id).Status != RunStatus.Running) return;
                running = true; productProbe.Stop();
                try
                {
                    if (manager.Snapshot(config.Items[1].Id).Active) throw new Exception("Unselected auto-start item ran during recovery.");
                    if (args[0] == "--recovery-skip" && manager.Snapshot(config.Items[0].Id).Active) throw new Exception("Skip recovery started a service.");
                    using var output = new StringWriter(); using var error = new StringWriter();
                    Task<ControlResponse> Send(ControlRequest request, TimeSpan timeout) => LocalControl.SendAsync(request, timeout, pipe);
                    if (await ControlCli.RunAsync(["start", "CLI 中文 服务", "--json"], output, error, Send) != 0) throw new Exception(error.ToString());
                    if (!JsonSerializer.Deserialize<ControlResponse>(output.ToString(), ConfigurationStore.JsonOptions)!.Items.Single().Active) throw new Exception("CLI JSON did not report running item.");
                    if (await ControlCli.RunAsync(["restart", "CLI 中文 服务"], output, error, Send) != 0) throw new Exception(error.ToString());
                    if (await ControlCli.RunAsync(["stop", "--all"], output, error, Send) != 0 || config.Items.Any(x => manager.Snapshot(x.Id).Active)) throw new Exception("CLI stop did not stop items.");
                    passed = true;
                }
                catch (Exception error) { failure = error.ToString(); }
                finally { await (Task)typeof(MainWindow).GetMethod("ExitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [passed ? 0 : 1, null])!; }
            };
            productProbe.Start(); app.Run(window);
            var clean = new SessionJournal(root).ReadPrevious() is { Dirty: false, ActiveItems.Length: 0 };
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed, cleanExit = clean, failure, root }));
            return passed && clean && !escaped ? 0 : 1;
        }
        if (loaded)
        {
            app.Run(window);
            File.WriteAllText(report, JsonSerializer.Serialize(new { loadedExceptionEscaped = escaped, diagnosticsWritten = File.Exists(Path.Combine(root, "diagnostics", "errors.log")), root }));
            return !escaped && File.Exists(Path.Combine(root, "diagnostics", "errors.log")) ? 0 : 1;
        }
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var manager = (ProcessManager)typeof(MainWindow).GetField("manager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var item = new LaunchItem { Name = "duplicate-frame-fault", Target = "cmd" };
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var phase = 0; var ticks = 0;
        probe.Tick += (_, _) =>
        {
            if (phase == 0) { config.Items.Add(item); config.Items.Add(item); phase = 1; return; }
            if (phase == 2)
            {
                if (++ticks < 8) return;
                var text = (System.Windows.Controls.TextBlock)typeof(MainWindow).GetField("SummaryText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!;
                var resumed = timer.IsEnabled && text.Text != "sentinel";
                File.WriteAllText(report, JsonSerializer.Serialize(new { timerResumedAfterError = resumed, diagnosticsWritten = File.Exists(Path.Combine(root, "diagnostics", "errors.log")), root }));
                manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); Environment.Exit(resumed ? 0 : 1);
            }
            if (++ticks < 10 && timer.IsEnabled) return;
            var enabled = timer.IsEnabled;
            config.Items.Clear();
            if (enabled)
            {
                var text = (System.Windows.Controls.TextBlock)typeof(MainWindow).GetField("SummaryText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!;
                text.Text = "sentinel"; phase = 2; ticks = 0; return;
            }
            File.WriteAllText(report, JsonSerializer.Serialize(new { timerResumedAfterError = enabled, diagnosticsWritten = File.Exists(Path.Combine(root, "diagnostics", "errors.log")), root }));
            // Baseline's modal error runs a nested dispatcher; terminate only this isolated test host.
            manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Environment.Exit(enabled ? 0 : 1);
        };
        window.Loaded += (_, _) => probe.Start();
        app.Run(window); return 1;
    }
}
