using System.Text.Json;

namespace LaunchDesk.Core;

public sealed record SessionState(int Version, bool Dirty, Guid[] ActiveItems);

public sealed class SessionJournal(string root)
{
    private const int Limit = 1024 * 1024;
    private readonly object sync = new();
    private readonly HashSet<Guid> active = [];
    private bool frozen;
    public string PathName { get; } = Path.Combine(Path.GetFullPath(root), "session.json");
    public SessionState? ReadPrevious()
    {
        if (!File.Exists(PathName)) return null;
        using var stream = File.OpenRead(PathName);
        if (stream.Length > Limit) throw new InvalidDataException("Session journal exceeds its size limit: " + PathName);
        var state = JsonSerializer.Deserialize<SessionState>(stream);
        if (state is null || state.Version != 1 || state.ActiveItems is null || state.ActiveItems.Length > 20000
            || state.ActiveItems.Any(x => x == Guid.Empty) || state.ActiveItems.Distinct().Count() != state.ActiveItems.Length
            || (!state.Dirty && state.ActiveItems.Length != 0))
            throw new InvalidDataException("Invalid session journal; original preserved: " + PathName);
        return state;
    }
    public void Begin() { lock (sync) { active.Clear(); frozen = false; Write(true); } }
    public void MarkActive(Guid id)
    {
        lock (sync)
        {
            if (frozen) throw new InvalidOperationException("Session is closing.");
            if (!active.Add(id)) return;
            try { Write(true); } catch { active.Remove(id); throw; }
        }
    }
    public void MarkInactive(Guid id)
    {
        lock (sync)
        {
            if (frozen || !active.Remove(id)) return;
            try { Write(true); } catch { active.Add(id); throw; }
        }
    }
    // Preserve the pre-cleanup running set when a fatal error triggers process teardown.
    public void Freeze() { lock (sync) frozen = true; }
    public void MarkClean()
    {
        lock (sync) { Write(false); active.Clear(); frozen = true; }
    }
    private void Write(bool dirty)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new SessionState(1, dirty, dirty ? active.Order().ToArray() : []));
        if (bytes.Length > Limit) throw new InvalidDataException("Session journal exceeds its size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, PathName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static LaunchItem[] RecoveryItems(SessionState state, Configuration config) => config.StartupItems()
        .Where(x => state.ActiveItems.Contains(x.Id)).ToArray();
}
