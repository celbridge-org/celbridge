using Celbridge.Console.Platform;

namespace Celbridge.Console.Services;

public class Terminal : ITerminal, IDisposable
{
    // Null on a platform with no pty backend. The terminal operations then report it as unsupported.
    private readonly IPtyBackend? _backend;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

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
        GetBackend().Write(input);
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
