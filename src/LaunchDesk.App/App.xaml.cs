namespace LaunchDesk;
public partial class App : System.Windows.Application
{
    private LaunchDesk.Core.ErrorDiagnostics? diagnostics;
    private MainWindow? managedWindow;
    public void ConfigureErrors(LaunchDesk.Core.ErrorDiagnostics errors, MainWindow window)
    {
        diagnostics = errors; managedWindow = window;
        DispatcherUnhandledException += async (_, e) =>
        {
            e.Handled = true;
            managedWindow.Diagnostics.Write("DispatcherUnhandledException", e.Exception);
            await managedWindow.FailAndExitAsync(e.Exception);
        };
        TaskScheduler.UnobservedTaskException += Unobserved;
        AppDomain.CurrentDomain.UnhandledException += Unhandled;
        Exit += (_, _) =>
        {
            TaskScheduler.UnobservedTaskException -= Unobserved;
            AppDomain.CurrentDomain.UnhandledException -= Unhandled;
        };
    }
    private void Unobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    { (managedWindow?.Diagnostics ?? diagnostics)?.Write("UnobservedTaskException", e.Exception); e.SetObserved(); }
    private void Unhandled(object sender, UnhandledExceptionEventArgs e)
    { if (e.ExceptionObject is Exception error) (managedWindow?.Diagnostics ?? diagnostics)?.Write("UnhandledException", error); }
}
