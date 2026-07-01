namespace Celbridge.Console.Services;

/// <summary>
/// A platform pseudo-terminal backend that runs a command line in a pty and streams its output.
/// Terminal selects one implementation at runtime: ConPtyTerminal on Windows, UnixPtyTerminal on
/// the macOS and Linux heads. The members mirror the public ITerminal surface.
/// </summary>
internal interface IPtyBackend : IDisposable
{
    event EventHandler<string>? OutputReceived;

    event EventHandler? ProcessExited;

    void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null);

    void Write(string input);

    void SetSize(int cols, int rows);
}
