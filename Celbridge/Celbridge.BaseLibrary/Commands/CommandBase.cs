using Celbridge.Utilities;

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
    /// Optional group identifier for undo/redo.
    /// Commands with the same valid group id will be undone/redone together.
    /// </summary>
    public EntityId UndoGroupId { get; set; }

    /// <summary>
    /// Name of the undo stack to add this command to after it executes.
    /// </summary>
    public virtual string UndoStackName => UndoStackNames.None;

    /// <summary>
    /// Describes where in the source code the command was first executed.
    /// </summary>
    public string ExecutionSource { get; set; } = string.Empty;

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
