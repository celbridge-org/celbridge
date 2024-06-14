namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// A command that can be executed and undone.
/// </summary>
public abstract class CommandBase : ICommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    public CommandId Id { get; } = CommandId.Create();

    /// <summary>
    /// Execute the command.
    /// </summary>
    public abstract Task<Result> ExecuteAsync();
}
