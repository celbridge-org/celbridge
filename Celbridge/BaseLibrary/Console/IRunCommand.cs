using Celbridge.Commands;

namespace Celbridge.Console;

/// <summary>
/// Runs a script defined in a script file resource.
/// </summary>
public interface IRunCommand : IExecutableCommand
{
    /// <summary>
    // The script file to run.
    /// </summary>
    public ResourceKey ScriptResource { get; set; }

    /// <summary>
    /// Argument string to pass to the script file
    /// </summary>
    string Arguments { get; set; }
}
