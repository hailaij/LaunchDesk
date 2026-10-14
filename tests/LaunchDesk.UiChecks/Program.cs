using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LaunchDesk;
using LaunchDesk.Core;
using LaunchDesk.UiTesting;

internal static class UiChecks
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        if (args is ["--resource-keys", var resourceReport]) return ResourceChecks.Run(resourceReport);
        if (args is ["--scale-ui", var scaleReport]) return ScaleUiChecks.Run(scaleReport);
        if (args.Length == 2 && args[0] == "--signal" && int.TryParse(args[1], out var pid))
        { var sent = NativeProcess.SendControlSignal(pid, out var detail); if (!sent) Console.Error.WriteLine(detail); Thread.Sleep(100); return sent ? 0 : 1; }
        if (args.Length == 2 && args[0] is "--ui-fault-loaded" or "--ui-fault-dispatcher" or "--ui-smoke" or "--ui-smoke-min" or "--ui-smoke-en" or "--ui-smoke-en-min")
            return FaultProbe.Run(args[0], Path.GetFullPath(args[1]));
        if (args.Length != 2 || args[0] is not ("--timer" or "--loaded" or "--control" or "--recovery-accept" or "--recovery-accept-en" or "--recovery-skip" or "--recovery-empty" or "--accessibility" or "--accessibility-en" or "--service-diagnostics")) return 2;
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
        if (args[0] == "--recovery-empty")
        {
            var failures = new List<string>();
            var dialog = new RecoveryDialog([new() { Name = "isolated" }], 0);
            dialog.Loaded += (_, _) =>
            {
                var choices = (Dictionary<Guid, System.Windows.Controls.CheckBox>)typeof(RecoveryDialog)
                    .GetField("choices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var grid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)dialog.Content).Child;
                var buttons = grid.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                var restore = buttons.Children.OfType<System.Windows.Controls.Button>().Last();
                if (!restore.IsEnabled) failures.Add("Initial selection disabled");
                choices.Values.Single().IsChecked = false;
                if (restore.IsEnabled || dialog.SelectedIds.Length != 0) failures.Add("Empty selection still enabled");
                choices.Values.Single().IsChecked = true;
                if (!restore.IsEnabled || dialog.SelectedIds.Length != 1) failures.Add("Re-selection disabled");
                var empty = new RecoveryDialog([], 1);
                empty.Show();
                var emptyGrid = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)empty.Content).Child;
                var emptyButtons = emptyGrid.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                if (emptyButtons.Children.OfType<System.Windows.Controls.Button>().Last().IsEnabled)
                    failures.Add("No candidates still enabled");
                empty.Close();
                dialog.Close(); app.Shutdown(failures.Count == 0 ? 0 : 1);
            };
            app.Run(dialog);
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = failures.Count == 0, failures }));
            return failures.Count == 0 ? 0 : 1;
        }
        var iconFont = (System.Windows.Media.FontFamily)app.Resources["IconFont"];
        if (iconFont.Source != "Segoe MDL2 Assets" || !new System.Windows.Media.Typeface(iconFont, System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal).TryGetGlyphTypeface(out var glyphs)) return 1;
        foreach (var code in new[] { 0xE70D, 0xE73E, 0xE76C, 0xE8B7, 0xE756, 0xE8B5, 0xEDE1, 0xE777, 0xE713,
            0xE8F4, 0xE768, 0xE71A, 0xE710, 0xE70F, 0xE8C8, 0xE74D, 0xE70E, 0xE721, 0xE72C, 0xE74E, 0xE894, 0xE122, 0xE712 })
            if (!glyphs.CharacterToGlyphMap.TryGetValue(code, out var index) || index == 0) throw new InvalidDataException($"Missing icon glyph: {code:X4}");
        var loaded = args[0] == "--loaded";
        if (loaded) config.Items.Add(new() { Name = "fault", Target = "cmd" });
        var pipe = "LaunchDesk-ui-test-" + Guid.NewGuid().ToString("N");
        MainWindow window = loaded ? new UiSmokeRunner(store, config, false, report + ".png", false, pipe).Window
            : new MainWindow(store, config, false, false, pipe);
        var escaped = false;
        app.DispatcherUnhandledException += (_, e) => { escaped = true; e.Handled = true; app.Shutdown(1); };
        if (args[0] == "--service-diagnostics")
        {
            var failures = new List<string>();
            window.Loaded += async (_, _) =>
            {
                try
                {
                    foreach (var scenario in new[] { (0, "Running", ""), (7, "", "isolated service failure"), (8, "fallback detail", ""), (9, "", "") })
                    {
                        Func<System.Diagnostics.ProcessStartInfo, System.Diagnostics.Process?> start = info =>
                        {
                            if (info.UseShellExecute || !info.RedirectStandardError || !info.RedirectStandardOutput)
                                throw new InvalidDataException("Status query unexpectedly requested elevation");
                            info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"); info.ArgumentList.Clear();
                            info.ArgumentList.Add("/d"); info.ArgumentList.Add("/c");
                            info.ArgumentList.Add($"{(scenario.Item2.Length > 0 ? "echo " + scenario.Item2 + "&" : "")}{(scenario.Item3.Length > 0 ? "echo " + scenario.Item3 + " 1>&2&" : "")}exit /b {scenario.Item1}");
                            return System.Diagnostics.Process.Start(info);
                        };
                        var dialog = (ServiceDialog)Activator.CreateInstance(typeof(ServiceDialog), BindingFlags.Instance | BindingFlags.NonPublic,
                            null, [start], null)!;
                        await (Task)typeof(ServiceDialog).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
                        var text = ((System.Windows.Controls.TextBlock)typeof(ServiceDialog).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Text;
                        var expected = scenario.Item3.Length > 0 ? scenario.Item3 : scenario.Item2;
                        if (!text.Contains(expected, StringComparison.Ordinal)) failures.Add("Service status discarded detail: " + expected + "; actual=" + text);
                        if (scenario.Item1 != 0 && !text.Contains($"({scenario.Item1})", StringComparison.Ordinal)) failures.Add("Service status lost failure exit code");
                    }
                    foreach (var policy in new[] { false, true })
                    {
                        var serviceRoot = Path.Combine(root, policy ? "service-policy" : "service-load");
                        Directory.CreateDirectory(serviceRoot);
                        if (policy) new ConfigurationStore(serviceRoot).Save(new() { Items = [new() { Target = "echo unused", ShowWindow = true }] });
                        else File.WriteAllText(Path.Combine(serviceRoot, "config.json"), "{broken");
                        var type = Assembly.Load("LaunchDesk.Host").GetType("LaunchDesk.Host.ManagedService", true)!;
                        using var service = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [serviceRoot], null)!;
                        Exception? original = null;
                        try { type.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [Array.Empty<string>()]); }
                        catch (TargetInvocationException error) { original = error.InnerException; }
                        var path = new ErrorDiagnostics(serviceRoot).PathName;
                        if (original is null || !File.Exists(path) || !File.ReadAllText(path).Contains(original.Message, StringComparison.Ordinal))
                            failures.Add("Synchronous service startup missing original diagnostics: policy=" + policy);
                        if ((int)type.GetProperty("ExitCode")!.GetValue(service)! != 1) failures.Add("Service startup failure exit code lost");
                    }
                    var blockedRoot = Path.Combine(root, "service-blocked-diagnostics");
                    Directory.CreateDirectory(blockedRoot); File.WriteAllText(Path.Combine(blockedRoot, "diagnostics"), "blocked");
                    new ConfigurationStore(blockedRoot).Save(new() { Items = [new() { Target = "echo unused", ShowWindow = true }] });
                    var blockedType = Assembly.Load("LaunchDesk.Host").GetType("LaunchDesk.Host.ManagedService", true)!;
                    using var blockedService = (IDisposable)Activator.CreateInstance(blockedType, BindingFlags.Instance | BindingFlags.NonPublic, null, [blockedRoot], null)!;
                    try { blockedType.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(blockedService, [Array.Empty<string>()]); failures.Add("Policy failure was swallowed"); }
                    catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { }
                }
                catch (Exception error) { failures.Add(error.ToString()); }
                finally { await (Task)typeof(MainWindow).GetMethod("ExitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [failures.Count == 0 ? 0 : 1, null])!; }
            };
            var exit = app.Run(window);
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = failures.Count == 0, failures, cleanExit = exit == 0, root,
                scope = "Injected non-elevated status process and isolated OnStart failures; no SCM registration or UAC" }));
            return failures.Count == 0 && exit == 0 ? 0 : 1;
        }
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
                    UiSmokeRunner.Capture(editor, report + ".png");
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
                    UiSmokeRunner.Capture(dialog, report + ".png");
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
