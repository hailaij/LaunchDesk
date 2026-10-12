namespace LaunchDesk.Core;

public interface IManagedProcess : IDisposable
{
    Stream Output { get; }
    Stream Error { get; }
    Stream Input { get; }
    int Id { get; }
    int ExitCode { get; }
    bool ForcedTermination { get; }
    Task WaitForExitAsync();
    Task SignalAsync(Func<int, Task> fallback);
    void Kill();
    void CloseInput();
}
