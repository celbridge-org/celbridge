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
    /// Execute the command.
    /// </summary>
    public abstract Task<Result> ExecuteAsync();
}
