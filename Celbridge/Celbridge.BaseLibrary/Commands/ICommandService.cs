namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// An asynchronous command queue service.
/// </summary>
public interface ICommandService
{
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
    /// Remove a command from the queue.
    /// </summary>
    Result RemoveCommand(ICommand command);
}
