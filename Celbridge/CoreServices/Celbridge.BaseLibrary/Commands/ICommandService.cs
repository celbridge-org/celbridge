using System.Runtime.CompilerServices;

namespace Celbridge.Commands;

/// <summary>
/// An asynchronous command queue service with undo/redo support.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Enqueues a command for later execution.
    /// Enqueued commands are executed in sequential order.
    /// Succeeds if the command was enqueued successfully.
    /// </summary>
    Result Execute<T> (
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Executes a command immediately without enqueuing it.
    /// When you use this method, bear in mind that an enqueued command could execute at the same time.
    /// Command flags have no effect when you use this method.
    /// </summary>
    Task<Result> ExecuteImmediate<T>(
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) where T : IExecutableCommand;

    /// <summary>
    /// Enqueue a command for execution, and then wait for it to execute.
    /// Returns the command execution result.
    /// </summary>
    Task<Result> ExecuteAsync<T>(
        Action<T>? configure = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) where T : IExecutableCommand;

    /// <summary>
    /// Returns true if a command of the given type is in the queue.
    /// </summary>
    bool ContainsCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// Removes all commands of the given type from the queue.
    /// </summary>
    void RemoveCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// Returns the number of available undo operations.
    /// </summary>
    int GetUndoCount();

    /// <summary>
    /// Returns the number of available redo operations.
    /// </summary>
    int GetRedoCount();

    /// <summary>
    /// Attempt to pop the most recent undo command from the undo stack and execute it.
    /// </summary>
    Result Undo();

    /// <summary>
    /// Attempts to pop the most recently undone command from the redo stack and execute it.
    /// </summary>
    Result Redo();
}
