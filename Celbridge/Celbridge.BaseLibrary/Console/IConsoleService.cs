namespace Celbridge.BaseLibrary.Console;

/// <summary>
/// The console service provides functionality to support the console panel in the workspace UI.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Factory method to create the console panel for the Workspace UI.
    /// </summary>
    object CreateConsolePanel();

    /// <summary>
    /// Factory method to create a CommandHistory instance.
    /// </summary>
    ICommandHistory CreateCommandHistory();
}
