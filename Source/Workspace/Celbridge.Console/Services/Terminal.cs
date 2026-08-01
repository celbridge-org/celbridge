using Celbridge.Console.Platform;

namespace Celbridge.Console.Services;

public class Terminal : ITerminal, IDisposable
{
    // Null on a platform with no pty backend. The terminal operations then report it as unsupported.
    private readonly IPtyBackend? _backend;

    // Writes reach the pty from more than one thread: user keystrokes from the UI, and trigger runs from
    // the scheduler's own continuations. Neither backend serializes its write, so an invocation submitted
    // while the user is typing could otherwise interleave with their input part way through the line.
    private readonly object _writeLock = new();

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

    public int? ProcessId => _backend?.ProcessId;

    public Terminal()
    {
        _backend = PtyBackendFactory.Create();

        if (_backend is not null)
        {
            _backend.OutputReceived += (sender, output) =>
            {
                OutputReceived?.Invoke(sender, output);
            };

            _backend.ProcessExited += (sender, e) =>
            {
                ProcessExited?.Invoke(sender, e);
            };
        }
    }

    public void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null)
    {
        GetBackend().Start(commandLine, workingDir, environmentVariables);
    }

    public void Write(string input)
    {
        var backend = GetBackend();

        lock (_writeLock)
        {
            backend.Write(input);
        }
    }

    public void SetSize(int cols, int rows)
    {
        GetBackend().SetSize(cols, rows);
    }

    private IPtyBackend GetBackend()
    {
        if (_backend is null)
        {
            throw new PlatformNotSupportedException("The terminal is not supported on this platform yet.");
        }

        return _backend;
    }

    public void Dispose()
    {
        _backend?.Dispose();
    }
}
