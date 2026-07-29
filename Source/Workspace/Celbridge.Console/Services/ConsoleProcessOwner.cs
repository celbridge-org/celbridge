using System.Collections.Concurrent;
using System.Diagnostics;
using Celbridge.Console.Platform;
using Celbridge.Logging;

namespace Celbridge.Console.Services;

/// <summary>
/// Workspace-scoped owner of the open consoles' child processes. On Windows it assigns them to a job that
/// kills them when the app process dies (crash safety). On every platform it kills any still-tracked child
/// when the workspace is disposed (project close). A clean tab close is handled by the pty itself.
/// </summary>
public sealed class ConsoleProcessOwner : IConsoleProcessOwner, IDisposable
{
    private readonly ILogger<ConsoleProcessOwner> _logger;
    private readonly ConcurrentDictionary<int, byte> _processIds = new();
    private readonly WindowsJobObject? _jobObject;
    private bool _disposed;

    public ConsoleProcessOwner(ILogger<ConsoleProcessOwner> logger)
    {
        _logger = logger;

        // The Windows job gives crash safety. On other platforms the dispose-time kill covers project close.
        if (OperatingSystem.IsWindows())
        {
            _jobObject = new WindowsJobObject();
        }
    }

    public void Track(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        _processIds[processId] = 0;
        _jobObject?.AssignProcess(processId);
    }

    public void Untrack(int processId)
    {
        _processIds.TryRemove(processId, out _);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var processId in _processIds.Keys)
        {
            TryKill(processId);
        }
        _processIds.Clear();

        _jobObject?.Dispose();
    }

    private void TryKill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // GetProcessById throws when the id is not a running process, i.e. it already exited.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to kill console child process {ProcessId}", processId);
        }
    }
}
