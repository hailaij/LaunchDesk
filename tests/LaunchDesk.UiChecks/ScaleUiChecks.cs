using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using DataGrid = System.Windows.Controls.DataGrid;
using LaunchDesk;
using LaunchDesk.Core;

internal static class ScaleUiChecks
{
    public static int Run(string report)
    {
        var root = Path.Combine(Path.GetTempPath(), "LaunchDesk-ui-scale-" + Guid.NewGuid().ToString("N"));
        var config = new Configuration { Preferences = new() { CheckUpdatesOnStartup = false } };
        for (var i = 0; i < 1000; i++) config.Items.Add(new() { Name = "规模中文🙂-" + i, Target = "not-executed" });
        var store = new ConfigurationStore(root); store.Save(config);
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow(store, store.Load(), false, null, false, "LaunchDesk-scale-ui-" + Guid.NewGuid().ToString("N"));
        var clock = Stopwatch.StartNew();
        window.Loaded += async (_, _) =>
        {
            var exitCode = 0; string? failure = null; int renderedStart = 0; int renderedEnd = 0;
            try
            {
                await Task.Delay(500); window.UpdateLayout();
                var grid = (DataGrid)typeof(MainWindow).GetField("ItemsGrid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!;
                int Rendered() => Enumerable.Range(0, grid.Items.Count).Count(i => grid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow);
                if (grid.Items.Count != 1000) throw new InvalidDataException("UI did not load 1000 items");
                renderedStart = Rendered(); grid.ScrollIntoView(grid.Items[999]);
                await Task.Delay(250); window.UpdateLayout(); renderedEnd = Rendered();
                if (renderedStart < 1 || renderedStart > 100 || renderedEnd < 1 || renderedEnd > 100
                    || grid.ItemContainerGenerator.ContainerFromIndex(999) is not DataGridRow)
                    throw new InvalidDataException("1000-item UI virtualization/scroll failed");
            }
            catch (Exception error) { exitCode = 1; failure = error.ToString(); }
            using var process = Process.GetCurrentProcess(); process.Refresh();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllText(report, JsonSerializer.Serialize(new { status = exitCode == 0 ? "passed" : "failed", root,
                configurationItems = 1000, renderedStart, renderedEnd, elapsedMs = clock.ElapsedMilliseconds,
                privateBytes = process.PrivateMemorySize64, handles = process.HandleCount, failure }));
            await (Task)typeof(MainWindow).GetMethod("ExitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [exitCode, null])!;
        };
        return app.Run(window);
    }
}
