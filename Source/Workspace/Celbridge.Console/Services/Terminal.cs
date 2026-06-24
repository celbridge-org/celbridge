namespace Celbridge.Console.Services;

public class Terminal : ITerminal, IDisposable
{
    // The pty backend is selected by platform: ConPtyTerminal wraps the Windows pseudo-console API,
    // UnixPtyTerminal wraps openpty/posix_spawn on the macOS and Linux heads. On an unsupported
    // platform _backend stays null and the terminal operations report that the platform is not yet
    // supported.
    private readonly IPtyBackend? _backend;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

    public Terminal()
    {
        if (OperatingSystem.IsWindows())
        {
            _backend = new ConPtyTerminal();
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            _backend = new UnixPtyTerminal();
        }

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
