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
        if (args.Length != 2 || args[0] is not ("--timer" or "--loaded")) return 2;
        var report = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var root = Path.Combine(Path.GetTempPath(), "LaunchDesk-ui-check-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigurationStore(root);
        var config = new Configuration { Preferences = new() { CheckUpdatesOnStartup = false } };
        var app = new App(); app.InitializeComponent();
        var loaded = args[0] == "--loaded";
        if (loaded) config.Items.Add(new() { Name = "fault", Target = "cmd" });
        var window = new MainWindow(store, config, false, loaded ? report + ".png" : null, false);
        var escaped = false;
        app.DispatcherUnhandledException += (_, e) => { escaped = true; e.Handled = true; app.Shutdown(1); };
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
