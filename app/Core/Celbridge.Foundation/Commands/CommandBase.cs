namespace Celbridge.Commands;

/// <summary>
/// Base class for commands that can be executed via the command service.
/// </summary>
public abstract class CommandBase : IExecutableCommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    public EntityId CommandId { get; } = EntityId.Create();

    /// <summary>
    /// Flags to configure behaviour when executing the command.
    /// </summary>
    public virtual CommandFlags CommandFlags => CommandFlags.None;

    /// <summary>
    /// Describes where in the source code the command was first executed.
    /// </summary>
    public string ExecutionSource { get; set; } = string.Empty;

    /// <summary>
    /// Called when the command is executed.
    /// This is used internally by the ExecuteAsync() method and should not be used directly.
    /// </summary>
    public Action<Result>? OnExecute { get; set; } = null;

    /// <summary>
    /// Execute the command.
    /// </summary>
    public abstract Task<Result> ExecuteAsync();
}
