namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// A system that can execute commands.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Returns true if this executor can execute the command.
    /// </summary>
    bool CanExecuteCommand(CommandBase command);

    /// <summary>
    /// Execute a command.
    /// </summary>
    Task<Result> ExecuteCommand(CommandBase command);
}
