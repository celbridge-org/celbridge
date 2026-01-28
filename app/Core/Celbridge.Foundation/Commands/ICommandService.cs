using System.Runtime.CompilerServices;

namespace Celbridge.Commands;

/// <summary>
/// An asynchronous command queue service.
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
}
