using System.Reflection;
using System.IO;
using System.Windows;
using LaunchDesk;
using LaunchDesk.Core;

internal static class FaultProbe
{
    public static int Run(string mode, string report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        var root = Path.Combine(Path.GetDirectoryName(report)!, "ui-smoke-data", Guid.NewGuid().ToString("N"));
        var config = new Configuration { Preferences = new() { CheckUpdatesOnStartup = false } };
        if (mode.Contains("-en", StringComparison.Ordinal)) config.Preferences.Language = "en-US";
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow(new(root), config, false, report, false, "LaunchDesk-fixture-" + Guid.NewGuid().ToString("N"));
        app.ConfigureErrors(new(root), window);
        if (mode.EndsWith("-min", StringComparison.Ordinal)) { window.Width = 940; window.Height = 640; }
        if (mode.StartsWith("--ui-fault-", StringComparison.Ordinal))
        {
            // Replace only the loaded test callback; cleanup and dispatcher error handling remain production code.
            var loaded = typeof(MainWindow).GetMethod("OnLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, loaded);
            window.Loaded += async (_, _) =>
            {
                try
                {
                    var manager = (ProcessManager)typeof(MainWindow).GetField("manager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var item = new LaunchItem { Name = "isolated-fatal-probe", Kind = LaunchKind.Executable,
                        Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Arguments = "/d /c ping -t 127.0.0.1", StopTimeoutSeconds = 1 };
                    config.Items.Add(item); await manager.StartAsync(item.Id);
                    var temporary = report + ".pid.tmp";
                    File.WriteAllText(temporary, manager.Snapshot(item.Id).Pid!.Value.ToString()); File.Move(temporary, report + ".pid");
                    File.WriteAllText(report + ".diagnostics-path", new ErrorDiagnostics(root).PathName);
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
                    while (!File.Exists(report + ".observed"))
                    {
                        if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Fault fixture was not observed");
                        await Task.Delay(50);
                    }
                    if (mode == "--ui-fault-dispatcher")
                    { _ = window.Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Injected dispatcher fault"))); return; }
                    throw new InvalidOperationException("Injected loaded fault after await");
                }
                catch (Exception error)
                {
                    await (Task)typeof(MainWindow).GetMethod("FailAndExitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [error])!;
                }
            };
        }
        return app.Run(window);
    }
}
