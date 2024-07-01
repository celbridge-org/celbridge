namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Base class for commands that can be executed via the command service.
/// </summary>
public abstract class CommandBase : IExecutableCommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    public CommandId Id { get; } = CommandId.Create();

    /// <summary>
    /// Name of the command stack to add this command to after it executes.
    /// </summary>
    public virtual string StackName => CommandStackNames.None;

    /// <summary>
    /// Execute the command.
    /// </summary>
    public abstract Task<Result> ExecuteAsync();

    /// <summary>
    /// Undo a previously executed command.
    /// </summary>
    public virtual Task<Result> UndoAsync()
    {
        throw new NotImplementedException();
    }
}
