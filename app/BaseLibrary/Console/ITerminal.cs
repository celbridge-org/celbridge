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
    /// Stores a command temporarily until the system is ready to inject it into the terminal input.
    /// </summary>
    string CommandBuffer { get; set; }

    /// <summary>
    /// Starts the terminal session by executing a command line program.
    /// </summary>
    void Start(string commandLine, string workingDir);

    /// <summary>
    /// Writes input data to the terminal.
    /// </summary>
    void Write(string input);

    /// <summary>
    /// Sets the column and row size of the terminal.
    /// </summary>
    void SetSize(int cols, int rows);
}
