using System.Reflection;
using System.Text.Json;
using LaunchDesk.Core;

internal sealed class SettingsHarness
{
    // Extracted settings transaction, guarded against source drift by Reproduce-DataLifecycle.ps1.
    // Only manager tracking and filesystem destinations are substituted; catch/rethrow order is unchanged.
    private readonly Configuration config = new();
    private ConfigurationStore store;
    private ProcessManager manager = null!;
    private bool portable;
    private ErrorDiagnostics diagnostics;
    private bool IsEnabled { get; set; } = true;
    private string BasePath { get; }
    public string Destination { get; }
    public string Locator { get; }
    public bool LocatorAttempted { get; private set; }
    public List<ProcessManager> Managers { get; } = [];
    public SettingsHarness(string root)
    {
        BasePath = root; store = new(Path.Combine(root, "source"));
        Destination = Path.Combine(root, "destination"); Locator = Path.Combine(root, "locator.json");
        diagnostics = new(store.Root); manager = NewManager();
    }
    private ProcessManager NewManager()
    { var result = new ProcessManager(config, store.Root); Managers.Add(result); return result; }
    private void SaveLocator(string root)
    { LocatorAttempted = true; DataLocation.Save(root, Locator); }
    public static Task Maintenance(ProcessManager manager) =>
        (Task)typeof(ProcessManager).GetField("maintenance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    public async Task ApplyAsync()
    {
        var previous = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(config.Preferences))!;
        var destinationRoot = Destination;
        var changingRoot = true;
        var dialog = new Dialog();
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
                    catch { manager = NewManager(); throw; }
                    finally { IsEnabled = true; }
                }
                try
                {
                    var flag = Path.Combine(BasePath, "portable.flag");
                    if (!dialog.Portable) SaveLocator(destinationRoot);
                    if (dialog.Portable != portable)
                    {
                        if (dialog.Portable) File.WriteAllText(flag, "LaunchDesk portable mode\n");
                        else if (File.Exists(flag)) File.Delete(flag);
                    }
                }
                catch { if (changingRoot) manager = NewManager(); throw; }
                store = targetStore; portable = dialog.Portable; diagnostics = new(store.Root);
                if (changingRoot) manager = NewManager();
            }
            catch { config.Preferences.Language = previous.Language; config.Preferences.Theme = previous.Theme; config.Preferences.StartAtLogin = previous.StartAtLogin; config.Preferences.StartMinimized = previous.StartMinimized;
                config.Preferences.MinimizeToTray = previous.MinimizeToTray; config.Preferences.LogDays = previous.LogDays; config.Preferences.LogLimitMiB = previous.LogLimitMiB;
                config.Preferences.CheckUpdatesOnStartup = previous.CheckUpdatesOnStartup;
                store.Save(config); throw; }
    }
    private sealed class Dialog { public bool Portable { get; } = false; }
}

