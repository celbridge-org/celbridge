namespace Celbridge.Console.Services;

public class Terminal : ITerminal, IDisposable
{
    // ConPtyTerminal wraps the Windows pseudo-console API. It is created only on Windows;
    // on other platforms _terminal stays null and the terminal operations report that the
    // platform is not yet supported. A macOS pty backend is a separate workstream.
    private readonly ConPtyTerminal? _terminal;

#pragma warning disable CS0067 // Events are only raised on platforms with a terminal backend
    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;
#pragma warning restore CS0067

    public Terminal()
    {
        if (OperatingSystem.IsWindows())
        {
            _terminal = new ConPtyTerminal();

            _terminal.OutputReceived += (sender, output) =>
            {
                OutputReceived?.Invoke(sender, output);
            };

            _terminal.ProcessExited += (sender, e) =>
            {
                ProcessExited?.Invoke(sender, e);
            };
        }
    }

    public void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null)
    {
        GetTerminal().Start(commandLine, workingDir, environmentVariables);
    }

    public void Write(string input)
    {
        GetTerminal().Write(input);
    }

    public void SetSize(int cols, int rows)
    {
        GetTerminal().SetSize(cols, rows);
    }

    private ConPtyTerminal GetTerminal()
    {
        if (_terminal is null)
        {
            throw new PlatformNotSupportedException("The terminal is not supported on this platform yet.");
        }

        return _terminal;
    }

    public void Dispose()
    {
        _terminal?.Dispose();
    }
}
