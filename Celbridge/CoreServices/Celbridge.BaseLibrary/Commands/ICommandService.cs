using System.Runtime.CompilerServices;

namespace Celbridge.Commands;

/// <summary>
/// An asynchronous command queue service with undo/redo support.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Create and enqueue a command that does not require configuration.
    /// </summary>
    Result Execute<T>(
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Create and immediately execute a command that does not require configuration.
    /// Note that immediately executed commands do not support command flags or undo/redo.
    /// </summary>
    Task<Result> ExecuteNow<T>(
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Create, configure and enqueue a command.
    /// </summary>
    Result Execute<T> (
        Action<T> configure,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Create, configure and immediately execute a command.
    /// Note that immediately executed commands do not support command flags or undo/redo.
    /// </summary>
    Task<Result> ExecuteNow<T>(
        Action<T> configure,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Create a new command via the dependency injection system.
    /// </summary>
    T CreateCommand<T>() where T : IExecutableCommand;

    /// <summary>
    /// Add the command to the queue.
    /// It will be executed when it reaches the front of the queue.
    /// </summary>
    Result EnqueueCommand(IExecutableCommand command);

    /// <summary>
    /// Returns true if a command of the given type is in the queue.
    /// </summary>
    bool ContainsCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// Removes all commands of the given type from the queue.
    /// </summary>
    void RemoveCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// Returns true if the undo stack is empty.
    /// </summary>
    bool IsUndoStackEmpty();

    /// <summary>
    /// Returns true if the redo stack is empty.
    /// </summary>
    bool IsRedoStackEmpty();

    /// <summary>
    /// Attempt to pop the most recent undo command from the undo stack and execute it.
    /// The call will succeed whether an undo is performed or not (e.g. if the undo stack is empty).
    /// The boolean return value indicates whether an undo operation was actually performed.
    /// </summary>
    Result<bool> Undo();

    /// <summary>
    /// Attempts to pop the most recently undone command from the redo stack and execute it.
    /// The call will succeed whether a redo is performed or not (e.g. if the redo stack is empty).
    /// The boolean return value indicates whether a redo operation was actually performed.
    /// </summary>
    Result<bool> Redo();
}
