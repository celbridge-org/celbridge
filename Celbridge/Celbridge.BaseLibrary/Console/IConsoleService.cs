namespace Celbridge.BaseLibrary.Console;

/// <summary>
/// The console service provides functionality used to support the console panel.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Factory method to create a CommandHistory instance.
    /// </summary>
    ICommandHistory CreateCommandHistory();
}
