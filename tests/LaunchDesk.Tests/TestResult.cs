internal sealed record TestResult(string name, string status, long elapsedMs, string? error = null, string? reason = null)
{
    public bool? passed => status == "skipped" ? null : status == "passed";
    public bool skipped => status == "skipped";
}

internal sealed class TestSkippedException(string reason) : Exception(reason);
