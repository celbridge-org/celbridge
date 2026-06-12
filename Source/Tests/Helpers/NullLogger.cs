namespace Celbridge.Tests.Helpers;

/// <summary>
/// No-op logger for constructing services whose logger type parameter is an
/// internal type, which Castle DynamicProxy cannot proxy without an
/// InternalsVisibleTo("DynamicProxyGenAssembly2") entry on the owning assembly.
/// </summary>
public sealed class NullLogger<T> : ILogger<T>
{
    public void LogDebug(Exception? exception, string? message, params object?[] args) {}
    public void LogDebug(string? message, params object?[] args) {}
    public void LogTrace(Exception? exception, string? message, params object?[] args) {}
    public void LogTrace(string? message, params object?[] args) {}
    public void LogInformation(Exception? exception, string? message, params object?[] args) {}
    public void LogInformation(string? message, params object?[] args) {}
    public void LogWarning(Exception? exception, string? message, params object?[] args) {}
    public void LogWarning(string? message, params object?[] args) {}
    public void LogWarning(Result result, string? message, params object?[] args) {}
    public void LogError(Exception? exception, string? message, params object?[] args) {}
    public void LogError(string? message, params object?[] args) {}
    public void LogError(Result result, string? message, params object?[] args) {}
    public void LogCritical(Exception? exception, string? message, params object?[] args) {}
    public void LogCritical(string? message, params object?[] args) {}
    public void LogCritical(Result result, string? message, params object?[] args) {}
    public IDisposable? BeginScope(string messageFormat, params object?[] args) => null;
    public void Shutdown() {}
}
