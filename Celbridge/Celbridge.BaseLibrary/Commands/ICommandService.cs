namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// An asynchronous command queue service.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Create, configure and enqueue a command in one step.
    /// </summary>
    Result Execute<T>(Action<T> configure) where T : ICommand;

    /// <summary>
    /// Create, configure and enqueue a command in one step, with a delay.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// </summary>
    Result Execute<T>(Action<T> configure, uint delay) where T : ICommand;

    /// <summary>
    /// Create and enqueue a command in one step.
    /// Use this for commands that don't need to be configured.
    /// </summary>
    Result Execute<T>() where T : ICommand;

    /// <summary>
    /// Create and enqueue a command in one step, with a delay.
    /// Use this for commands that don't need to be configured.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// </summary>
    Result Execute<T>(uint delay) where T : ICommand;

    /// <summary>
    /// Create a new command via the dependency injection system.
    /// </summary>
    T CreateCommand<T>() where T : ICommand;

    /// <summary>
    /// Add the command to the queue.
    /// It will be executed when it reaches the front of the queue.
    /// </summary>
    Result EnqueueCommand(ICommand command);

    /// <summary>
    /// Add the command to the queue, with a delay.
    /// The delay is the minimum time (in milliseconds) before the command will execute. 
    /// Actual execution might take longer than the delay time, depending on what other commands execute ahead of it in the queue.
    /// </summary>
    Result EnqueueCommand(ICommand command, uint Delay);

    /// <summary>
    /// Removes all commands of the given type from the queue.
    /// </summary>
    public void RemoveCommandsOfType<T>() where T : notnull;
}
