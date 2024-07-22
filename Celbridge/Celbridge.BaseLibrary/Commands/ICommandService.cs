namespace Celbridge.Commands;

/// <summary>
/// An asynchronous command queue service with undo/redo support.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Create, configure and enqueue a command in one step.
    /// </summary>
    Result Execute<T>(Action<T> configure) where T : IExecutableCommand;

    /// <summary>
    /// Create, configure and enqueue a command in one step, with a delay.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// </summary>
    Result Execute<T>(Action<T> configure, uint delay) where T : IExecutableCommand;

    /// <summary>
    /// Create and enqueue a command in one step.
    /// Use this for commands that don't need to be configured.
    /// </summary>
    Result Execute<T>() where T : IExecutableCommand;

    /// <summary>
    /// Create and enqueue a command in one step, with a delay.
    /// Use this for commands that don't need to be configured.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// </summary>
    Result Execute<T>(uint delay) where T : IExecutableCommand;

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
    /// Add the command to the queue, with a delay.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// Actual execution might take longer than the delay time, depending on what other commands execute ahead of it in the queue.
    /// </summary>
    Result EnqueueCommand(IExecutableCommand command, uint Delay);

    /// <summary>
    /// Returns true if a command of the given type is in the queue.
    /// </summary>
    bool ContainsCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// Removes all commands of the given type from the queue.
    /// </summary>
    void RemoveCommandsOfType<T>() where T : notnull;

    /// <summary>
    /// The undo stack associated with the most recently focussed UI element.
    /// </summary>
    string ActiveUndoStack { get; set; }

    /// <summary>
    /// Returns true if the specified undo stack is empty.
    /// </summary>
    bool IsUndoStackEmpty(string undoStackName);

    /// <summary>
    /// Returns true if the specified redo stack is empty.
    /// </summary>
    bool IsRedoStackEmpty(string undoStackName);

    /// <summary>
    /// Pop the most recent command from the specified undo stack and execute it.
    /// The call fails if no undo command was found in the redo stack.
    /// </summary>
    Result Undo(string undoStackName);

    /// <summary>
    /// Attempt to pop the most recent undo command from the Active Undo Stack and execute it.
    /// The call will succeed whether an undo is performed or not (e.g. if the undo stack is empty).
    /// The boolean return value indicates if an undo operation was actually performed.
    /// </summary>
    Result<bool> TryUndo();

    /// <summary>
    /// Pop the most recently undone command from the specified undo stack and execute it.
    /// The call fails if no undone command was found in the undo stack.
    /// </summary>
    Result Redo(string undoStackName);

    /// <summary>
    /// Attempt to pop the most recent redo command from the Active Undo Stack and execute it.
    /// The call will succeed whether a redo is performed or not (e.g. if the redo stack is empty).
    /// The boolean return value indicates if a redo operation was actually performed.
    /// </summary>
    Result<bool> TryRedo();
}
