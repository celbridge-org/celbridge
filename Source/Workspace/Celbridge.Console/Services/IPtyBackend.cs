namespace Celbridge.Console.Services;

/// <summary>
/// A platform pseudo-terminal backend that runs a command line in a pty and streams its output.
/// </summary>
internal interface IPtyBackend : IDisposable
{
    event EventHandler<string>? OutputReceived;

    event EventHandler? ProcessExited;

    int? ProcessId { get; }

    void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null);

    void Write(string input);

    void SetSize(int cols, int rows);
}
