namespace Celbridge.Commands;

/// <summary>
/// A command that can be executed via the command service.
/// </summary>
public interface IExecutableCommand
{
    /// <summary>
    /// Unique identifier for the command.
    /// </summary>
    CommandId Id { get; }

    /// <summary>
    /// Name of the undo stack to add this command to after it executes.
    /// </summary>
    string UndoStackName { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    Task<Result> ExecuteAsync();

    /// <summary>
    /// Undo a previously executed command.
    /// </summary>
    Task<Result> UndoAsync();
}
