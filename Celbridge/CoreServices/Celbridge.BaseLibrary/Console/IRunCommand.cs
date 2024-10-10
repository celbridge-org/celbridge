using Celbridge.Commands;

namespace Celbridge.Console;

/// <summary>
/// Runs a script defined in a script file resource.
/// </summary>
public interface IRunCommand : IExecutableCommand
{
    /// <summary>
    // The script to run.
    /// </summary>
    public ResourceKey ScriptResource { get; set; }
}
