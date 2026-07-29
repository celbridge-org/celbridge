using Celbridge.Commands;

namespace Celbridge.Console;

/// <summary>
/// Runs a script file in a specific open console by injecting that console's runner command.
/// </summary>
public interface IRunCommand : IExecutableCommand
{
    /// <summary>
    /// The script file to run.
    /// </summary>
    public ResourceKey ScriptResource { get; set; }

    /// <summary>
    /// The session id of the console to run the script in. Empty resolves to the earliest-opened console
    /// that can run the script's file type.
    /// </summary>
    Guid SessionId { get; set; }

    /// <summary>
    /// Argument string appended after the runner's substituted command.
    /// </summary>
    string Arguments { get; set; }
}
