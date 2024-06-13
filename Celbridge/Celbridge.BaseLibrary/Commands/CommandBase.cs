namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Execution state of a command.
/// </summary>
public enum CommandState
{
    NotStarted,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// A command that can be executed and undone.
/// </summary>
public abstract class CommandBase
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    public CommandId Id { get; } = CommandId.Create();

    /// <summary>
    /// Used to cancel the command.
    /// </summary>
    public CancellationTokenSource CancellationToken { get; } = new CancellationTokenSource();

    /// <summary>
    /// The current state of the command.
    /// </summary>
    public CommandState State { get; set; } = CommandState.NotStarted;

    /// <summary>
    /// The progress of the command [0..1].
    /// </summary>
    public float Progress { get; protected set; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    public abstract Task<Result> ExecuteAsync();

    /// <summary>
    /// Undo a previously executed command.
    /// </summary>
    public virtual async Task<Result> Undo()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }
}
