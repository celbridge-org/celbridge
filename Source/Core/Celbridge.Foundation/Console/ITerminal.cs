namespace Celbridge.Console;

/// <summary>
/// A terminal window instance used to interact with command line programs.
/// </summary>
public interface ITerminal : IDisposable
{
    /// <summary>
    /// Event fired when the terminal has received output data.
    /// </summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>
    /// Event fired when the terminal process has exited.
    /// </summary>
    event EventHandler? ProcessExited;

    /// <summary>
    /// Starts the terminal session by executing a command line program.
    /// When environmentVariables is provided, those variables are added to the child
    /// process environment (merged with the current process environment).
    /// </summary>
    void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null);

    /// <summary>
    /// Writes input data to the terminal.
    /// </summary>
    void Write(string input);

    /// <summary>
    /// Sets the column and row size of the terminal.
    /// </summary>
    void SetSize(int cols, int rows);
}
