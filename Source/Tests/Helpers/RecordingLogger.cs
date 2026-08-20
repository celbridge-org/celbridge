namespace Celbridge.Tests.Helpers;

/// <summary>
/// The severity a recorded entry was logged at.
/// </summary>
public enum LogEntryLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// One entry a service wrote to its logger, holding the message template and the arguments separately as the
/// logger received them.
/// </summary>
public sealed record LogEntry(LogEntryLevel Level, string? Message, object?[] Arguments);

/// <summary>
/// Recording counterpart to NullLogger, for tests that assert on what a service logged rather than merely
/// letting it log. Hand written for the same reason: Castle DynamicProxy cannot proxy a logger whose type
/// parameter is an internal type without an InternalsVisibleTo("DynamicProxyGenAssembly2") entry on the
/// assembly that owns it.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>
    /// The entries recorded at the given level, in the order they were logged.
    /// </summary>
    public IReadOnlyList<LogEntry> EntriesAt(LogEntryLevel level)
    {
        return _entries.Where(entry => entry.Level == level).ToList();
    }

    private void Record(LogEntryLevel level, string? message, object?[] args)
    {
        _entries.Add(new LogEntry(level, message, args));
    }

    public void LogDebug(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Debug, message, args);
    public void LogDebug(string? message, params object?[] args) => Record(LogEntryLevel.Debug, message, args);
    public void LogTrace(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Trace, message, args);
    public void LogTrace(string? message, params object?[] args) => Record(LogEntryLevel.Trace, message, args);
    public void LogInformation(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Information, message, args);
    public void LogInformation(string? message, params object?[] args) => Record(LogEntryLevel.Information, message, args);
    public void LogWarning(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Warning, message, args);
    public void LogWarning(string? message, params object?[] args) => Record(LogEntryLevel.Warning, message, args);
    public void LogWarning(Result result, string? message, params object?[] args) => Record(LogEntryLevel.Warning, message, args);
    public void LogError(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Error, message, args);
    public void LogError(string? message, params object?[] args) => Record(LogEntryLevel.Error, message, args);
    public void LogError(Result result, string? message, params object?[] args) => Record(LogEntryLevel.Error, message, args);
    public void LogCritical(Exception? exception, string? message, params object?[] args) => Record(LogEntryLevel.Critical, message, args);
    public void LogCritical(string? message, params object?[] args) => Record(LogEntryLevel.Critical, message, args);
    public void LogCritical(Result result, string? message, params object?[] args) => Record(LogEntryLevel.Critical, message, args);
    public IDisposable? BeginScope(string messageFormat, params object?[] args) => null;
    public void Shutdown() { }
}
