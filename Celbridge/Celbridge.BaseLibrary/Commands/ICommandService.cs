namespace Celbridge.BaseLibrary.Commands;

/// <summary>
/// Service for managing a command queue.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Create a new command via the dependency injection system.
    /// </summary>
    T CreateCommand<T>() where T : ICommand;

    /// <summary>
    /// Execute a command.
    /// The command is added to the command queue and executed when it reaches the front of the queue.
    /// </summary>
    Result EnqueueCommand(ICommand command);

    /// <summary>
    /// Execute a command after a delay.
    /// Delay is the minimum time (in milliseconds) before the command should execute. 
    /// Actual execution might take longer than the delay time, depending on what other commands are ahead of it in the queue.
    /// </summary>
    Result EnqueueCommand(ICommand command, uint Delay);

    /// <summary>
    /// Remove a command from the queue.
    /// </summary>
    Result RemoveCommand(ICommand command);

    /// <summary>
    /// Starts the service executing commands.
    /// </summary>
    void StartExecutingCommands();

    /// <summary>
    /// Stop the service executing commands.
    /// </summary>
    void StopExecutingCommands();
}
