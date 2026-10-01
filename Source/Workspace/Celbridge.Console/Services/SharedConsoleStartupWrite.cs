using Celbridge.Console.Helpers;

namespace Celbridge.Console.Services;

/// <summary>
/// Writes the console start-up files once, and gives every console the same write to wait for. A write that
/// fails is not kept, so the next console tries again.
/// </summary>
public sealed class SharedConsoleStartupWrite
{
    private readonly Func<Task<Result<ConsoleStartupOptions>>> _write;
    private readonly object _writeLock = new();
    private Task<Result<ConsoleStartupOptions>>? _writeTask;

    public SharedConsoleStartupWrite(Func<Task<Result<ConsoleStartupOptions>>> write)
    {
        _write = write;
    }

    /// <summary>
    /// Returns the shared write. Starts it when no console has asked for it yet, or when the last write failed.
    /// </summary>
    public Task<Result<ConsoleStartupOptions>> GetAsync()
    {
        lock (_writeLock)
        {
            var lastWriteFailed = _writeTask is { IsCompleted: true } &&
                (!_writeTask.IsCompletedSuccessfully || _writeTask.Result.IsFailure);

            if (_writeTask is null ||
                lastWriteFailed)
            {
                _writeTask = _write();
            }

            return _writeTask;
        }
    }
}
